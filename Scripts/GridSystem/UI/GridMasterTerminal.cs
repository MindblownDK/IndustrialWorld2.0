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
        /// <param name="tab">-1 = All Storage; otherwise index into the block list.</param>
        public static VisualElement Build(GridEntity grid, int tab, Action<int> onSelectTab,
            MachineUIs.SlotBuilder slot, Action onClose)
        {
            // ── Window shell (fixed size, near-opaque) ──
            var win = new VisualElement();
            win.style.width = 960; win.style.height = 620;
            win.style.flexDirection = FlexDirection.Column;
            win.style.backgroundColor = new StyleColor(new Color(0.06f, 0.07f, 0.10f, 1f)); // opaque
            T.Border(win, 1, T.BorderDim); T.Radius(win, 8);
            win.style.paddingTop = 10; win.style.paddingBottom = 10;
            win.style.paddingLeft = 12; win.style.paddingRight = 12;

            // ── Title bar ──
            var title = new VisualElement();
            title.style.flexDirection = FlexDirection.Row;
            title.style.alignItems = Align.Center;
            title.style.flexShrink = 0;
            title.style.marginBottom = 6;
            var t = T.Title("SHIP CONTROL TERMINAL");
            t.style.flexGrow = 1;
            title.Add(t);
            if (grid != null)
            {
                title.Add(Chip($"⚖ {MassFormat.Format(grid.TotalMass)}", T.AccentCyan));
                title.Add(Chip($"⚡ {PowerFormat.Watts(grid.PowerBalance)}",
                    grid.PowerBalance >= 0 ? T.AccentGreen : T.AccentRed));
            }
            title.Add(T.SmallButton("✕  Close", () => onClose?.Invoke(), T.AccentRed));
            win.Add(title);

            var div = T.AccentDivider(T.AccentCyan); div.style.flexShrink = 0; win.Add(div);

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
            col.style.width = 250; col.style.flexShrink = 0;
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
            b.style.backgroundColor = new StyleColor(active
                ? new Color(0.18f, 0.72f, 0.88f, 0.30f)
                : new Color(0.13f, 0.15f, 0.20f, 1f));
            b.style.color = new StyleColor(active ? T.AccentCyan : (textColor ?? new Color(0.84f, 0.88f, 0.93f)));
            return b;
        }

        // ── RIGHT: content ──────────────────────────────────────────────────────────
        private static VisualElement BuildContent(GridEntity grid, List<GridBlock> blocks, int tab, MachineUIs.SlotBuilder slot)
        {
            var wrap = new ScrollView();
            wrap.style.flexGrow = 1; wrap.style.minHeight = 0;

            if (tab >= 0 && tab < blocks.Count)
            {
                var block = blocks[tab];
                if (HasToggle(block))
                {
                    var bar = new VisualElement();
                    bar.style.flexDirection = FlexDirection.Row;
                    bar.style.alignItems = Align.Center;
                    bar.style.marginBottom = 6;
                    var status = new Label(block.Enabled ? "● ONLINE" : "○ OFFLINE");
                    status.style.flexGrow = 1;
                    status.style.unityFontStyleAndWeight = FontStyle.Bold;
                    status.style.color = new StyleColor(block.Enabled ? T.AccentGreen : new Color(0.6f, 0.6f, 0.65f));
                    bar.Add(status);
                    bar.Add(T.SmallButton(block.Enabled ? "Turn OFF" : "Turn ON", () =>
                    {
                        block.Enabled = !block.Enabled;
                        VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                    }, block.Enabled ? T.AccentRed : T.AccentGreen));
                    wrap.Add(bar);
                }
                wrap.Add(GridBlockUI.BuildPanel(block, slot));
                return wrap;
            }

            wrap.Add(AllStoragePanel(grid, slot));
            return wrap;
        }

        private static VisualElement AllStoragePanel(GridEntity grid, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();
            p.style.width = 640;
            var stores = new List<IGridItemStore>();
            float totalKg = 0f;
            if (grid != null && GridItemNetwork.Instance != null)
                foreach (var s in GridItemNetwork.Instance.GetStores(grid))
                    if (s != null && s.ItemStore != null) { stores.Add(s); totalKg += MassUtil.ContainerMass(s.ItemStore); }

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
                var g = T.SlotGrid(8);
                for (int i = 0; i < c.Size; i++) g.Add(slot(c, i, c.GetSlot(i), false, true));
                p.Add(g);
            }
            return p;
        }

        private static bool HasToggle(GridBlock b)
            => b is GridThruster || b is GridRefinery || b is GridChemicalPlant
            || b is GridH2O2Generator || b is GridDrill || b is GridGrinder
            || b is GridWeapon || b is GridSolarPanel || b is GridPortableReactor;

        private static bool IsTerminalBlock(GridBlock b)
            => b is GridCargoContainer || b is GridDockingPort
            || b is GridLiquidTank || b is GridGasTank
            || b is GridH2O2Generator || b is GridBattery
            || b is GridRefinery || b is GridChemicalPlant
            || b is GridWeapon || b is GridThruster
            || b is GridSolarPanel || b is GridPortableReactor
            || b is GridDrill || b is GridGrinder || b is GridCockpit;

        private static VisualElement Chip(string text, Color c)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize = 11;
            l.style.color = new StyleColor(c);
            l.style.marginRight = 12;
            return l;
        }
    }
}
