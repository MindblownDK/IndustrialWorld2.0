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
            // Window shell.
            var win = new VisualElement();
            win.style.flexDirection = FlexDirection.Column;
            win.style.width = 920; win.style.height = 600;
            win.style.backgroundColor = new StyleColor(new Color(0.07f, 0.08f, 0.11f, 0.98f));
            T.Border(win, 1, T.BorderDim); T.Radius(win, 8);
            win.style.paddingTop = 10; win.style.paddingBottom = 10;
            win.style.paddingLeft = 12; win.style.paddingRight = 12;

            // Title bar.
            var title = new VisualElement();
            title.style.flexDirection = FlexDirection.Row;
            title.style.alignItems = Align.Center;
            title.style.marginBottom = 8;
            var t = T.Title("🛰  SHIP CONTROL TERMINAL");
            t.style.flexGrow = 1;
            title.Add(t);
            if (grid != null)
            {
                title.Add(Chip($"⚖ {MassFormat.Format(grid.TotalMass)}", T.AccentCyan));
                title.Add(Chip($"⚡ {PowerFormat.Watts(grid.PowerBalance)}",
                    grid.PowerBalance >= 0 ? T.AccentGreen : T.AccentRed));
            }
            var close = T.SmallButton("✕  Close", () => onClose?.Invoke(), T.AccentRed);
            title.Add(close);
            win.Add(title);
            win.Add(T.AccentDivider(T.AccentCyan));

            // Body: left list + right content.
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;

            var blocks = new List<GridBlock>();
            if (grid != null)
                foreach (var kv in grid.Blocks)
                    if (kv.Value != null && IsTerminalBlock(kv.Value)) blocks.Add(kv.Value);
            blocks.Sort((a, b) => string.CompareOrdinal(a.blockName, b.blockName));

            body.Add(BuildBlockList(grid, blocks, tab, onSelectTab));
            body.Add(BuildContent(grid, blocks, tab, slot));
            win.Add(body);
            return win;
        }

        // ── LEFT: block list ──────────────────────────────────────────────────────
        private static VisualElement BuildBlockList(GridEntity grid, List<GridBlock> blocks, int tab, Action<int> onSelectTab)
        {
            var col = new VisualElement();
            col.style.width = 250; col.style.marginRight = 10;
            col.style.flexShrink = 0;

            var list = new ScrollView();
            list.style.flexGrow = 1;

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
            b.style.marginBottom = 2; b.style.marginLeft = 0; b.style.marginRight = 0;
            b.style.paddingLeft = 10; b.style.height = 30;
            b.style.backgroundColor = new StyleColor(active
                ? new Color(0.18f, 0.72f, 0.88f, 0.28f)
                : new Color(0.12f, 0.14f, 0.18f, 0.95f));
            b.style.color = new StyleColor(active ? T.AccentCyan : (textColor ?? new Color(0.82f, 0.86f, 0.92f)));
            return b;
        }

        // ── RIGHT: content ──────────────────────────────────────────────────────────
        private static VisualElement BuildContent(GridEntity grid, List<GridBlock> blocks, int tab, MachineUIs.SlotBuilder slot)
        {
            var wrap = new ScrollView();
            wrap.style.flexGrow = 1;

            if (tab == -1) { wrap.Add(AllStoragePanel(grid, slot)); return wrap; }

            if (tab >= 0 && tab < blocks.Count)
            {
                var block = blocks[tab];

                // On/off toggle bar for functional blocks.
                if (HasToggle(block))
                {
                    var bar = new VisualElement();
                    bar.style.flexDirection = FlexDirection.Row;
                    bar.style.alignItems = Align.Center;
                    bar.style.marginBottom = 6;
                    var status = new Label(block.Enabled ? "● ONLINE" : "○ OFFLINE");
                    status.style.flexGrow = 1;
                    status.style.unityFontStyleAndWeight = FontStyle.Bold;
                    status.style.color = new StyleColor(block.Enabled ? T.AccentGreen : new Color(0.6f,0.6f,0.65f));
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

        // Unified view of every item store on the grid — drag items between any of them.
        private static VisualElement AllStoragePanel(GridEntity grid, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();
            p.style.width = 600;
            var stores = new List<IGridItemStore>();
            float totalKg = 0f;
            if (grid != null && GridItemNetwork.Instance != null)
                foreach (var s in GridItemNetwork.Instance.GetStores(grid))
                    if (s != null && s.ItemStore != null) { stores.Add(s); totalKg += MassUtil.ContainerMass(s.ItemStore); }

            var (hdr, _, _, _) = T.HeaderRow("📦 All Storage", $"{stores.Count} containers", T.AccentGold);
            p.Add(hdr);
            p.Add(GridUIHelpers.WeightHeader(totalKg, "Total"));
            p.Add(T.AccentDivider(T.AccentGold));

            if (stores.Count == 0)
            {
                p.Add(T.Muted("No cargo containers or docking ports on this grid. Build storage + item pipes to link them."));
                return p;
            }

            foreach (var store in stores)
            {
                var c = store.ItemStore;
                p.Add(GridUIHelpers.SectionTitle($"{store.StoreLabel}  ({MassFormat.Format(MassUtil.ContainerMass(c))})"));
                var grid6 = T.SlotGrid(8);
                for (int i = 0; i < c.Size; i++) grid6.Add(slot(c, i, c.GetSlot(i), false, true));
                p.Add(grid6);
            }
            return p;
        }

        private static bool HasToggle(GridBlock b)
        {
            return b is GridThruster || b is GridRefinery || b is GridChemicalPlant
                || b is GridH2O2Generator || b is GridDrill || b is GridGrinder
                || b is GridWeapon || b is GridSolarPanel || b is GridPortableReactor;
        }

        private static bool IsTerminalBlock(GridBlock b)
        {
            return b is GridCargoContainer || b is GridDockingPort
                || b is GridLiquidTank || b is GridGasTank
                || b is GridH2O2Generator || b is GridBattery
                || b is GridRefinery || b is GridChemicalPlant
                || b is GridWeapon || b is GridThruster
                || b is GridSolarPanel || b is GridPortableReactor
                || b is GridDrill || b is GridGrinder || b is GridCockpit;
        }

        private static VisualElement Chip(string text, Color c)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize = 11;
            l.style.color = new StyleColor(c);
            l.style.marginRight = 10;
            return l;
        }
    }
}
