// Assets/Scripts/VoxelEngine/GridSystem/UI/GridMasterTerminal.cs
//
// Space-Engineers-style ship CONTROL TERMINAL. A full-screen configuration
// screen: left column lists every functional block on the grid; clicking one
// shows its full panel (stats, tanks, power, inventory) on the right plus an
// on/off toggle. An "All Storage" tab spans every connected container so the
// player can drag items anywhere across the ship's logistics network.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.UI;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.GridSystem.UI
{
    public static class GridMasterTerminal
    {
        // Remembers scroll offsets per ScrollView key so the 4 Hz live refresh doesn't
        // jump the list back to the top.
        private static readonly System.Collections.Generic.Dictionary<string, float> _scrollY = new();

        private static void PersistScroll(ScrollView sv, string key)
        {
            sv.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_scrollY.TryGetValue(key, out var y))
                    sv.verticalScroller.value = y;
            });
            sv.verticalScroller.valueChanged += v => _scrollY[key] = v;
        }

        /// <param name="tab">-1 = All Storage; otherwise index into the block list.</param>
        public static VisualElement Build(GridEntity grid, int tab, Action<int> onSelectTab,
            MachineUIs.SlotBuilder slot, Action onClose)
        {
            // ── Window shell — ABSOLUTELY fills the holder (top/bottom/left/right = 0).
            // Using absolute insets (instead of width/height:100% + flexGrow) guarantees a
            // resolved height so the body + storage grids never collapse to the title bar. ──
            // The window is a flex child of a FULL-SCREEN overlay row (see GameUIController),
            // so flexGrow + 100% height makes it fill that definite space — no absolute insets
            // (which were collapsing under some PanelScaler configs).
            var win = new VisualElement();
            win.style.flexGrow = 1;
            win.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            win.style.flexDirection = FlexDirection.Column;
            win.style.backgroundColor = new StyleColor(new Color(0.06f, 0.07f, 0.10f, 1f)); // opaque
            T.Border(win, 1, T.BorderDim); T.Radius(win, 8);
            win.style.paddingTop = 12; win.style.paddingBottom = 12;
            win.style.paddingLeft = 14; win.style.paddingRight = 14;

            // ── Title bar ──
            var title = new VisualElement();
            title.style.flexDirection = FlexDirection.Row;
            title.style.alignItems = Align.Center;
            title.style.flexShrink = 0;
            title.style.marginBottom = 6;
            var t = T.Title("⬡  SHIP CONTROL TERMINAL");
            t.style.flexGrow = 1;
            title.Add(t);
            // Toggle ALL functional blocks on/off at once.
            title.Add(T.SmallButton("⏼ All ON", () => SetAllEnabled(grid, true), T.AccentGreen));
            title.Add(T.SmallButton("⭘ All OFF", () => SetAllEnabled(grid, false), T.AccentAmber));
            title.Add(T.SmallButton("✕  Close", () => onClose?.Invoke(), T.AccentRed));
            win.Add(title);

            var div = T.AccentDivider(T.AccentCyan); div.style.flexShrink = 0; win.Add(div);

            // ── Status strip — rich at-a-glance ship readout (SE-style) ──
            if (grid != null)
            {
                int blockCount = grid.BlockCount;
                float speed = grid.Body != null ? grid.Body.linearVelocity.magnitude : 0f;
                var strip = new VisualElement();
                strip.style.flexDirection = FlexDirection.Row;
                strip.style.flexWrap = Wrap.Wrap;
                strip.style.flexShrink = 0;
                strip.style.marginTop = 6; strip.style.marginBottom = 6;
                strip.Add(Stat("MASS", MassFormat.Format(grid.TotalMass), T.AccentCyan));
                strip.Add(Stat("POWER",  PowerFormat.Watts(grid.PowerBalance), grid.PowerBalance >= 0 ? T.AccentGreen : T.AccentRed));
                strip.Add(Stat("GEN",    PowerFormat.Watts(grid.PowerGenerated), T.AccentGreen));
                strip.Add(Stat("USE",    PowerFormat.Watts(grid.PowerConsumed), T.AccentAmber));
                strip.Add(Stat("H₂",     $"{grid.HydrogenStored:0} L", T.AccentCyan));
                strip.Add(Stat("O₂",     $"{grid.OxygenStored:0} L", T.AccentGreen));
                strip.Add(Stat("SPEED",  $"{speed:0.0} m/s", T.AccentGold));
                strip.Add(Stat("BLOCKS", blockCount.ToString(), new Color(0.8f,0.84f,0.9f)));
                win.Add(strip);
                var d2 = T.AccentDivider(T.AccentTeal); d2.style.flexShrink = 0; win.Add(d2);
            }

            // ── Body: left list + right content (fills remaining height) ──
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            body.style.minHeight = 0;          // allow children to scroll instead of overflow

            var blocks = new List<GridBlock>();
            if (grid != null)
                foreach (var kv in grid.Blocks)
                    if (kv.Value != null && IsTerminalBlock(kv.Value)) blocks.Add(kv.Value);
            blocks.Sort((a, b) => string.CompareOrdinal(a.blockName, b.blockName));

            body.Add(BuildBlockList(blocks, tab, onSelectTab));
            body.Add(BuildContent(grid, blocks, tab, slot));
            win.Add(body);
            return win;
        }

        // ── LEFT: block list ──────────────────────────────────────────────────────
        private static VisualElement BuildBlockList(List<GridBlock> blocks, int tab, Action<int> onSelectTab)
        {
            var col = new VisualElement();
            col.style.width = 280; col.style.flexShrink = 0;
            col.style.marginRight = 10;
            col.style.backgroundColor = new StyleColor(new Color(0.09f, 0.10f, 0.14f, 1f));
            T.Border(col, 1, T.BorderDim); T.Radius(col, 6);
            col.style.paddingTop = 6; col.style.paddingBottom = 6;
            col.style.paddingLeft = 6; col.style.paddingRight = 6;

            var lbl = new Label($"BLOCKS  ({blocks.Count})");
            lbl.style.fontSize = 10; lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.color = new StyleColor(new Color(0.55f, 0.6f, 0.68f));
            lbl.style.marginBottom = 4; lbl.style.flexShrink = 0;
            col.Add(lbl);

            var list = new ScrollView();
            list.style.flexGrow = 1; list.style.minHeight = 0;
            PersistScroll(list, "blocklist");   // keep scroll position across 4 Hz refreshes

            list.Add(TabButton("📦  All Storage", tab == -1, () => onSelectTab(-1), null));
            for (int i = 0; i < blocks.Count; i++)
            {
                int idx = i;
                var b = blocks[i];
                bool off = !b.Enabled;
                list.Add(TabButton((off ? "○ " : "● ") + b.blockName, tab == idx, () => onSelectTab(idx),
                    off ? new Color(0.5f, 0.5f, 0.55f) : (Color?)null));
            }
            col.Add(list);
            return col;
        }

        private static Button TabButton(string text, bool active, Action onClick, Color? textColor)
        {
            var b = new Button(onClick) { text = text };
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.marginTop = 0; b.style.marginBottom = 2; b.style.marginLeft = 0; b.style.marginRight = 0;
            b.style.paddingLeft = 10; b.style.height = 30; b.style.flexShrink = 0;
            b.style.whiteSpace = WhiteSpace.NoWrap;
            b.style.textOverflow = TextOverflow.Ellipsis;
            b.style.overflow = Overflow.Hidden;
            b.style.backgroundColor = new StyleColor(active
                ? new Color(0.18f, 0.72f, 0.88f, 0.30f)
                : new Color(0.13f, 0.15f, 0.20f, 1f));
            b.style.color = new StyleColor(active ? T.AccentCyan : (textColor ?? new Color(0.84f, 0.88f, 0.93f)));
            return b;
        }

        // ── RIGHT: content ──────────────────────────────────────────────────────────
        private static VisualElement BuildContent(GridEntity grid, List<GridBlock> blocks, int tab, MachineUIs.SlotBuilder slot)
        {
            var wrap = new ScrollView(ScrollViewMode.Vertical);
            wrap.style.flexGrow = 1; wrap.style.minHeight = 0; wrap.style.minWidth = 0;
            PersistScroll(wrap, "content_" + tab);   // keep scroll position across refreshes

            if (tab >= 0 && tab < blocks.Count)
            {
                var block = blocks[tab];
                if (HasToggle(block))
                {
                    var bar = new VisualElement();
                    bar.style.flexDirection = FlexDirection.Row;
                    bar.style.alignItems = Align.Center;
                    bar.style.marginBottom = 6;
                    // Toggle button sits immediately to the LEFT of the status text.
                    var toggleBtn = T.SmallButton(block.Enabled ? "Turn OFF" : "Turn ON", () =>
                    {
                        block.Enabled = !block.Enabled;
                        VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                    }, block.Enabled ? T.AccentRed : T.AccentGreen);
                    toggleBtn.style.marginRight = 10;
                    bar.Add(toggleBtn);
                    var status = new Label(block.Enabled ? "● ONLINE" : "○ OFFLINE");
                    status.style.flexGrow = 1;
                    status.style.unityFontStyleAndWeight = FontStyle.Bold;
                    status.style.color = new StyleColor(block.Enabled ? T.AccentGreen : new Color(0.6f, 0.6f, 0.65f));
                    bar.Add(status);
                    wrap.Add(bar);
                }
                
                var panel = GridBlockUI.BuildPanel(block, slot);
                // Force relative positioning so it works inside the ScrollView
                panel.style.position = Position.Relative;
                panel.style.top = StyleKeyword.Auto;
                panel.style.right = StyleKeyword.Auto;
                panel.style.bottom = StyleKeyword.Auto;
                panel.style.width = StyleKeyword.Auto;
                panel.style.flexGrow = 1;

                wrap.Add(panel);
                return wrap;
            }

            var storage = AllStoragePanel(grid, slot);
            storage.style.position = Position.Relative;
            storage.style.top = StyleKeyword.Auto;
            storage.style.right = StyleKeyword.Auto;
            storage.style.bottom = StyleKeyword.Auto;
            storage.style.width = StyleKeyword.Auto;
            storage.style.flexGrow = 1;
            
            wrap.Add(storage);
            return wrap;
        }

        private static VisualElement AllStoragePanel(GridEntity grid, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();
            // We override these later in BuildContent, but let's set them here too for safety.
            p.style.width = StyleKeyword.Auto;
            p.style.flexShrink = 0;
            p.style.paddingBottom = 40; 
            
            var stores = new List<IGridItemStore>();
            float totalKg = 0f;
            if (grid != null && GridItemNetwork.Instance != null)
                foreach (var s in GridItemNetwork.Instance.GetStores(grid))
                {
                    if (s != null && s.ItemStore != null) 
                    { 
                        stores.Add(s); 
                        totalKg += MassUtil.ContainerMass(s.ItemStore); 
                    }
                }

            var head = new Label("ALL STORAGE");
            head.style.fontSize = 14; head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.color = new StyleColor(T.AccentGold); head.style.marginBottom = 2;
            p.Add(head);
            p.Add(GridUIHelpers.WeightHeader(totalKg, "Total"));
            p.Add(T.AccentDivider(T.AccentGold));

            if (stores.Count == 0)
            {
                p.Add(T.Muted("No cargo containers or docking ports on this grid.\nBuild a Cargo Container (+ Item Pipes) to link storage."));
                return p;
            }

            foreach (var store in stores)
            {
                var c = store.ItemStore;
                p.Add(GridUIHelpers.SectionTitle($"{store.StoreLabel}  ·  {MassFormat.Format(MassUtil.ContainerMass(c))}"));
                var g = T.SlotGrid(7);
                // Fixed width (7 cols × 60px + padding) so slots wrap to new ROWS and the
                // view scrolls DOWN — never overflow the panel sideways.
                g.style.width = 7 * 60 + 12;
                g.style.flexShrink = 0;
                g.style.overflow = Overflow.Hidden;
                for (int i = 0; i < c.Size; i++) g.Add(slot(c, i, c.GetSlot(i), false, true));
                p.Add(g);
            }
            return p;
        }

        private static bool HasToggle(GridBlock b)
            => b is GridThruster || b is GridRefinery || b is GridChemicalPlant
            || b is GridH2O2Generator || b is GridDrill || b is GridGrinder
            || b is GridWeapon || b is GridSolarPanel || b is GridPortableReactor || b is GridElectricFurnace;

        private static bool IsTerminalBlock(GridBlock b)
            => b is GridCargoContainer || b is GridDockingPort
            || b is GridLiquidTank || b is GridGasTank
            || b is GridH2O2Generator || b is GridBattery
            || b is GridRefinery || b is GridChemicalPlant
            || b is GridWeapon || b is GridThruster
            || b is GridSolarPanel || b is GridPortableReactor
            || b is GridDrill || b is GridGrinder || b is GridCockpit || b is GridElectricFurnace
            || b is GridLandingGear;

        private static void SetAllEnabled(GridEntity grid, bool on)
        {
            if (grid == null) return;
            foreach (var kv in grid.Blocks)
                if (kv.Value != null && HasToggle(kv.Value)) kv.Value.Enabled = on;
            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
        }

        // Compact labeled stat box for the status strip.
        private static VisualElement Stat(string label, string value, Color c)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Column;
            box.style.marginRight = 16; box.style.minWidth = 70;
            var l = new Label(label);
            l.style.fontSize = 8; l.style.letterSpacing = 1f;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new StyleColor(new Color(0.5f, 0.55f, 0.62f));
            box.Add(l);
            var v = new Label(value);
            v.style.fontSize = 14; v.style.unityFontStyleAndWeight = FontStyle.Bold;
            v.style.color = new StyleColor(c);
            box.Add(v);
            return box;
        }
    }
}
