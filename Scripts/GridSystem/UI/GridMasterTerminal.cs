// Assets/Scripts/VoxelEngine/GridSystem/UI/GridMasterTerminal.cs
//
// Space-Engineers-style master control terminal for a whole grid. Left column is
// a tab list (All Storage + every station/store on the ship); the right panel
// shows the selected block's UI or a unified storage view spanning every
// container so the player can drag items anywhere across the ship.

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
        public static VisualElement Build(GridEntity grid, int tab, Action<int> onSelectTab, MachineUIs.SlotBuilder slot)
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.FlexStart;

            // Collect the interactable blocks on the grid.
            var blocks = new List<GridBlock>();
            if (grid != null)
                foreach (var kv in grid.Blocks)
                    if (kv.Value != null && IsTerminalBlock(kv.Value)) blocks.Add(kv.Value);
            blocks.Sort((a, b) => string.CompareOrdinal(a.blockName, b.blockName));

            root.Add(BuildTabList(grid, blocks, tab, onSelectTab));
            root.Add(BuildContent(grid, blocks, tab, slot));
            return root;
        }

        // ── LEFT: tab list ──────────────────────────────────────────────────────
        private static VisualElement BuildTabList(GridEntity grid, List<GridBlock> blocks, int tab, Action<int> onSelectTab)
        {
            var col = T.MachinePanel();
            col.style.width = 220;
            col.style.marginRight = 8;

            var (hdr, _, _, _) = T.HeaderRow("🛰 Ship Terminal", $"{blocks.Count} blocks", T.AccentCyan);
            col.Add(hdr);

            // Grid summary.
            if (grid != null)
            {
                col.Add(T.StatRow("⚖", "Mass", MassFormat.Format(grid.TotalMass), T.AccentCyan));
                col.Add(T.StatRow("⚡", "Power", PowerFormat.Watts(grid.PowerBalance),
                    grid.PowerBalance >= 0 ? T.AccentGreen : T.AccentRed));
                col.Add(T.StatRow("🟦", "H₂", $"{grid.HydrogenStored:0} L", T.AccentCyan));
            }
            col.Add(T.AccentDivider(T.AccentCyan));

            var list = new ScrollView();
            list.style.maxHeight = 360;

            list.Add(TabButton("📦  All Storage", tab == -1, () => onSelectTab(-1)));
            for (int i = 0; i < blocks.Count; i++)
            {
                int idx = i;
                list.Add(TabButton("•  " + blocks[i].blockName, tab == idx, () => onSelectTab(idx)));
            }
            col.Add(list);
            return col;
        }

        private static Button TabButton(string text, bool active, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.marginBottom = 2;
            b.style.paddingLeft = 8; b.style.height = 26;
            b.style.backgroundColor = new StyleColor(active
                ? new Color(0.18f, 0.72f, 0.88f, 0.30f)
                : new Color(0.13f, 0.15f, 0.19f, 0.95f));
            b.style.color = new StyleColor(active ? T.AccentCyan : new Color(0.8f, 0.84f, 0.9f));
            return b;
        }

        // ── RIGHT: content ──────────────────────────────────────────────────────
        private static VisualElement BuildContent(GridEntity grid, List<GridBlock> blocks, int tab, MachineUIs.SlotBuilder slot)
        {
            if (tab == -1) return AllStoragePanel(grid, slot);
            if (tab >= 0 && tab < blocks.Count) return GridBlockUI.BuildPanel(blocks[tab], slot);
            return AllStoragePanel(grid, slot);
        }

        // Unified view of every item store on the grid — drag items between any of them.
        private static VisualElement AllStoragePanel(GridEntity grid, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();
            p.style.width = 520;
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
                p.Add(T.Muted("No cargo containers or docking ports on this grid."));
                return p;
            }

            var scroll = new ScrollView();
            scroll.style.maxHeight = 420;
            foreach (var store in stores)
            {
                var c = store.ItemStore;
                scroll.Add(GridUIHelpers.SectionTitle(store.StoreLabel));
                var grid6 = T.SlotGrid(6);
                for (int i = 0; i < c.Size; i++) grid6.Add(slot(c, i, c.GetSlot(i), false, true));
                scroll.Add(grid6);
            }
            p.Add(scroll);
            return p;
        }

        // Blocks worth showing as terminal tabs (have a UI / inventory / function).
        private static bool IsTerminalBlock(GridBlock b)
        {
            return b is GridCargoContainer || b is GridDockingPort
                || b is GridLiquidTank || b is GridGasTank
                || b is GridH2O2Generator || b is GridBattery
                || b is GridRefinery || b is GridChemicalPlant
                || b is GridWeapon;
        }
    }
}
