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
        // Remembers scroll offsets per ScrollView key so the live refresh doesn't
        // jump the list back to the top.
        private static readonly Dictionary<string, float> _scrollY = new();

        // Runtime terminal organisation. This is deliberately save-compatible: groups and
        // hidden-list state are UI state only for now, so no world/save schema changes.
        private static readonly Dictionary<int, TerminalState> _states = new();

        private sealed class TerminalState
        {
            public readonly HashSet<GridBlock> selected = new();
            public readonly HashSet<GridBlock> hiddenBlocks = new();
            public readonly List<BlockGroup> groups = new();
            public string groupNameDraft = "New Group";
            public bool hideOnCreate;
            public bool showHidden;
            public int lastSelectedIndex = -1;
        }

        private sealed class BlockGroup
        {
            public string name;
            public bool hidden;
            public readonly List<GridBlock> blocks = new();
        }

        private static void PersistScroll(ScrollView sv, string key)
        {
            sv.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_scrollY.TryGetValue(key, out var y))
                    sv.verticalScroller.value = y;
            });
            sv.verticalScroller.valueChanged += v => _scrollY[key] = v;
        }

        /// <param name="tab">-1 = All Storage; otherwise index into the sorted terminal block list.</param>
        public static VisualElement Build(GridEntity grid, int tab, Action<int> onSelectTab,
            MachineUIs.SlotBuilder slot, Action onClose)
        {
            var state = GetState(grid);

            var blocks = new List<GridBlock>();
            if (grid != null)
                foreach (var kv in grid.Blocks)
                    if (kv.Value != null && IsTerminalBlock(kv.Value)) blocks.Add(kv.Value);
            blocks.Sort((a, b) => string.CompareOrdinal(a.blockName, b.blockName));
            CleanState(state, blocks);

            // ── Window shell
            var win = new VisualElement();
            win.style.flexGrow = 1;
            win.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            win.style.flexDirection = FlexDirection.Column;
            win.style.backgroundColor = new StyleColor(new Color(0.06f, 0.07f, 0.10f, 1f)); // opaque
            T.Border(win, 1, T.BorderDim);
            T.Radius(win, 8);
            win.style.paddingTop = 12;
            win.style.paddingBottom = 12;
            win.style.paddingLeft = 14;
            win.style.paddingRight = 14;

            // Wide-screen ergonomics: the terminal keeps a readable desktop-app width
            // instead of stretching everything across 4K/ultrawide monitors.
            win.style.maxWidth = 1280;
            win.style.alignSelf = Align.Center;
            win.style.width = new StyleLength(new Length(100, LengthUnit.Percent));

            // ── Title bar ──
            var title = new VisualElement();
            title.style.flexDirection = FlexDirection.Row;
            title.style.alignItems = Align.Center;
            title.style.flexShrink = 0;
            title.style.marginBottom = 6;
            var t = T.Title("SHIP CONTROL TERMINAL");
            t.style.flexGrow = 1;
            title.Add(t);
            title.Add(T.SmallButton("All ON", () => SetAllEnabled(grid, true), T.AccentGreen));
            title.Add(T.SmallButton("All OFF", () => SetAllEnabled(grid, false), T.AccentAmber));
            title.Add(T.SmallButton("Close", () => onClose?.Invoke(), T.AccentRed));
            win.Add(title);

            var div = T.AccentDivider(T.AccentCyan);
            div.style.flexShrink = 0;
            win.Add(div);

            // ── Status strip — compact at-a-glance ship readout ──
            if (grid != null)
            {
                int blockCount = grid.BlockCount;
                float speed = grid.Body != null ? grid.Body.linearVelocity.magnitude : 0f;
                var strip = new VisualElement();
                strip.style.flexDirection = FlexDirection.Row;
                strip.style.flexWrap = Wrap.Wrap;
                strip.style.flexShrink = 0;
                strip.style.marginTop = 6;
                strip.style.marginBottom = 6;
                strip.Add(Stat("MASS", MassFormat.Format(grid.TotalMass), T.AccentCyan));
                strip.Add(Stat("POWER", PowerFormat.Watts(grid.PowerBalance), grid.PowerBalance >= 0 ? T.AccentGreen : T.AccentRed));
                strip.Add(Stat("GEN", PowerFormat.Watts(grid.PowerGenerated), T.AccentGreen));
                strip.Add(Stat("USE", PowerFormat.Watts(grid.PowerConsumed), T.AccentAmber));
                strip.Add(Stat("H2", $"{grid.HydrogenStored:0} L", T.AccentCyan));
                strip.Add(Stat("O2", $"{grid.OxygenStored:0} L", T.AccentGreen));
                strip.Add(Stat("SPEED", $"{speed:0.0} m/s", T.AccentGold));
                strip.Add(Stat("BLOCKS", blockCount.ToString(), new Color(0.8f, 0.84f, 0.9f)));
                win.Add(strip);
                var d2 = T.AccentDivider(T.AccentTeal);
                d2.style.flexShrink = 0;
                win.Add(d2);
            }

            // ── Body: left list + right content (fills remaining height) ──
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            body.style.minHeight = 0; // allow children to scroll instead of overflow

            body.Add(BuildBlockList(grid, state, blocks, tab, onSelectTab));
            body.Add(BuildContent(grid, blocks, tab, slot));
            win.Add(body);
            return win;
        }

        private static TerminalState GetState(GridEntity grid)
        {
            int key = grid != null ? grid.GetEntityId() : 0;
            if (!_states.TryGetValue(key, out var state))
            {
                state = new TerminalState();
                _states[key] = state;
            }
            return state;
        }

        private static void CleanState(TerminalState state, List<GridBlock> blocks)
        {
            if (state == null) return;
            state.selected.RemoveWhere(b => b == null || !blocks.Contains(b));
            state.hiddenBlocks.RemoveWhere(b => b == null || !blocks.Contains(b));
            for (int i = state.groups.Count - 1; i >= 0; i--)
            {
                var g = state.groups[i];
                g.blocks.RemoveAll(b => b == null || !blocks.Contains(b));
                if (g.blocks.Count == 0) state.groups.RemoveAt(i);
            }
        }

        // ── LEFT: block list + groups ─────────────────────────────────────────────
        private static VisualElement BuildBlockList(GridEntity grid, TerminalState state, List<GridBlock> blocks,
            int tab, Action<int> onSelectTab)
        {
            var col = new VisualElement();
            col.style.width = 340;
            col.style.flexShrink = 0;
            col.style.marginRight = 10;
            col.style.backgroundColor = new StyleColor(new Color(0.09f, 0.10f, 0.14f, 1f));
            T.Border(col, 1, T.BorderDim);
            T.Radius(col, 6);
            col.style.paddingTop = 6;
            col.style.paddingBottom = 6;
            col.style.paddingLeft = 6;
            col.style.paddingRight = 6;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4;
            var lbl = new Label($"BLOCKS ({blocks.Count})");
            lbl.style.fontSize = 10;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.color = new StyleColor(new Color(0.55f, 0.6f, 0.68f));
            lbl.style.flexGrow = 1;
            header.Add(lbl);
            header.Add(T.SmallButton(state.showHidden ? "Hide Hidden" : "Show Hidden", () =>
            {
                state.showHidden = !state.showHidden;
                RefreshTerminal();
            }, state.showHidden ? T.AccentCyan : T.AccentDim));
            col.Add(header);

            if (state.selected.Count > 0)
                col.Add(BuildSelectionTools(state));

            var list = new ScrollView();
            list.style.flexGrow = 1;
            list.style.minHeight = 0;
            PersistScroll(list, "blocklist");

            list.Add(TabButton("All Storage", tab == -1, () => onSelectTab(-1), null));

            var grouped = new HashSet<GridBlock>();
            if (state.groups.Count > 0)
            {
                list.Add(SectionLabel("GROUPS"));
                for (int i = 0; i < state.groups.Count; i++)
                {
                    var group = state.groups[i];
                    foreach (var b in group.blocks) grouped.Add(b);
                    list.Add(GroupRow(state, group));

                    if (!group.hidden || state.showHidden)
                    {
                        foreach (var b in group.blocks)
                        {
                            int blockIndex = blocks.IndexOf(b);
                            if (blockIndex < 0) continue;
                            bool hidden = IsHidden(state, b);
                            list.Add(BlockRow(state, blocks, blockIndex, tab, onSelectTab, indent: true, hidden));
                        }
                    }
                }
            }

            list.Add(SectionLabel("UNGROUPED BLOCKS"));
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (grouped.Contains(b)) continue;
                bool hidden = IsHidden(state, b);
                if (hidden && !state.showHidden) continue;
                list.Add(BlockRow(state, blocks, i, tab, onSelectTab, indent: false, hidden));
            }

            col.Add(list);
            return col;
        }

        private static VisualElement BuildSelectionTools(TerminalState state)
        {
            var box = T.Card();
            box.style.marginBottom = 6;
            box.style.flexShrink = 0;

            var title = new Label($"{state.selected.Count} SELECTED");
            title.style.fontSize = 10;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.AccentCyan);
            title.style.marginBottom = 6;
            box.Add(title);

            var name = new TextField { value = state.groupNameDraft };
            name.style.marginBottom = 4;
            name.tooltip = "Name for the group created from the selected blocks.";
            name.RegisterValueChangedCallback(e => state.groupNameDraft = string.IsNullOrWhiteSpace(e.newValue) ? "New Group" : e.newValue.Trim());
            name.RegisterCallback<FocusInEvent>(_ => PortConfigHud.IsAnyDropdownOpen = true);
            name.RegisterCallback<FocusOutEvent>(_ => PortConfigHud.IsAnyDropdownOpen = false);
            box.Add(name);

            var hideToggle = new Toggle("Hide selected blocks on group creation");
            hideToggle.SetValueWithoutNotify(state.hideOnCreate);
            hideToggle.style.marginBottom = 6;
            hideToggle.style.color = new StyleColor(T.TextSecondary);
            hideToggle.RegisterValueChangedCallback(e => state.hideOnCreate = e.newValue);
            box.Add(hideToggle);

            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.flexWrap = Wrap.Wrap;
            row1.Add(T.SmallButton("Create Group", () => CreateGroup(state), T.AccentGreen));
            row1.Add(T.SmallButton("Hide Selected", () => HideSelected(state), T.AccentAmber));
            row1.Add(T.SmallButton("Unhide Selected", () => UnhideSelected(state), T.AccentCyan));
            row1.Add(T.SmallButton("Clear", () => { state.selected.Clear(); RefreshTerminal(); }, T.AccentDim));
            box.Add(row1);

            var hint = T.Muted("Click = select/open. Shift-click selects a range. Ctrl-click adds/removes one block.");
            hint.style.marginTop = 6;
            box.Add(hint);
            return box;
        }

        private static Label SectionLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 9;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 1.2f;
            label.style.color = new StyleColor(T.TextMuted);
            label.style.marginTop = 8;
            label.style.marginBottom = 3;
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private static VisualElement GroupRow(TerminalState state, BlockGroup group)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 6;
            row.style.backgroundColor = new StyleColor(group.hidden
                ? new Color(0.12f, 0.11f, 0.09f, 1f)
                : new Color(0.10f, 0.15f, 0.18f, 1f));
            T.Radius(row, 5);
            T.Border(row, 1, group.hidden ? new Color(T.AccentAmber.r, T.AccentAmber.g, T.AccentAmber.b, 0.35f) : T.BorderDim);

            var name = new Label($"{group.name}  ({group.blocks.Count})");
            name.style.flexGrow = 1;
            name.style.fontSize = 11;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = new StyleColor(group.hidden ? T.AccentAmber : T.TextPrimary);
            row.Add(name);

            row.Add(T.SmallButton("Select", () =>
            {
                state.selected.Clear();
                foreach (var b in group.blocks) state.selected.Add(b);
                RefreshTerminal();
            }, T.AccentCyan));
            row.Add(T.SmallButton(group.hidden ? "Show" : "Hide", () =>
            {
                group.hidden = !group.hidden;
                RefreshTerminal();
            }, group.hidden ? T.AccentGreen : T.AccentAmber));
            return row;
        }

        private static Button BlockRow(TerminalState state, List<GridBlock> blocks, int index,
            int tab, Action<int> onSelectTab, bool indent, bool hidden)
        {
            var block = blocks[index];
            bool selected = state.selected.Contains(block);
            bool active = tab == index;
            bool off = !block.Enabled;
            string statePrefix = selected ? "[x] " : "[ ] ";
            string onlinePrefix = off ? "OFF  " : "ON   ";

            var button = new Button(() =>
            {
                HandleBlockSelection(state, blocks, index);
                onSelectTab(index);
            })
            { text = statePrefix + onlinePrefix + block.blockName };

            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.marginTop = 0;
            button.style.marginBottom = 2;
            button.style.marginLeft = indent ? 16 : 0;
            button.style.marginRight = 0;
            button.style.paddingLeft = 10;
            button.style.height = 30;
            button.style.flexShrink = 0;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.textOverflow = TextOverflow.Ellipsis;
            button.style.overflow = Overflow.Hidden;
            button.style.backgroundColor = new StyleColor(active
                ? new Color(0.18f, 0.72f, 0.88f, 0.30f)
                : selected
                    ? new Color(0.18f, 0.42f, 0.48f, 0.45f)
                    : hidden
                        ? new Color(0.12f, 0.11f, 0.09f, 1f)
                        : new Color(0.13f, 0.15f, 0.20f, 1f));
            button.style.color = new StyleColor(active
                ? T.AccentCyan
                : selected
                    ? Color.white
                    : hidden
                        ? T.AccentAmber
                        : off ? new Color(0.5f, 0.5f, 0.55f) : new Color(0.84f, 0.88f, 0.93f));
            return button;
        }

        private static Button TabButton(string text, bool active, Action onClick, Color? textColor)
        {
            var b = new Button(onClick) { text = text };
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.marginTop = 0;
            b.style.marginBottom = 2;
            b.style.marginLeft = 0;
            b.style.marginRight = 0;
            b.style.paddingLeft = 10;
            b.style.height = 30;
            b.style.flexShrink = 0;
            b.style.whiteSpace = WhiteSpace.NoWrap;
            b.style.textOverflow = TextOverflow.Ellipsis;
            b.style.overflow = Overflow.Hidden;
            b.style.backgroundColor = new StyleColor(active
                ? new Color(0.18f, 0.72f, 0.88f, 0.30f)
                : new Color(0.13f, 0.15f, 0.20f, 1f));
            b.style.color = new StyleColor(active ? T.AccentCyan : (textColor ?? new Color(0.84f, 0.88f, 0.93f)));
            return b;
        }

        private static void HandleBlockSelection(TerminalState state, List<GridBlock> blocks, int index)
        {
            var block = blocks[index];
            bool shift = GridInput.Shift;
            bool ctrl = GridInput.Ctrl;

            if (shift && state.lastSelectedIndex >= 0)
            {
                int a = Mathf.Clamp(state.lastSelectedIndex, 0, blocks.Count - 1);
                int b = Mathf.Clamp(index, 0, blocks.Count - 1);
                int min = Mathf.Min(a, b);
                int max = Mathf.Max(a, b);
                if (!ctrl) state.selected.Clear();
                for (int i = min; i <= max; i++) state.selected.Add(blocks[i]);
            }
            else if (ctrl)
            {
                if (!state.selected.Add(block)) state.selected.Remove(block);
                state.lastSelectedIndex = index;
            }
            else
            {
                state.selected.Clear();
                state.selected.Add(block);
                state.lastSelectedIndex = index;
            }
        }

        private static bool IsHidden(TerminalState state, GridBlock block)
        {
            if (state.hiddenBlocks.Contains(block)) return true;
            foreach (var group in state.groups)
                if (group.hidden && group.blocks.Contains(block)) return true;
            return false;
        }

        private static void CreateGroup(TerminalState state)
        {
            if (state.selected.Count == 0) return;
            var group = new BlockGroup
            {
                name = string.IsNullOrWhiteSpace(state.groupNameDraft) ? $"Group {state.groups.Count + 1}" : state.groupNameDraft.Trim(),
                hidden = state.hideOnCreate
            };
            foreach (var b in state.selected)
                if (b != null && !group.blocks.Contains(b)) group.blocks.Add(b);
            group.blocks.Sort((a, b) => string.CompareOrdinal(a.blockName, b.blockName));
            state.groups.Add(group);
            state.selected.Clear();
            state.groupNameDraft = $"Group {state.groups.Count + 1}";
            RefreshTerminal();
        }

        private static void HideSelected(TerminalState state)
        {
            foreach (var b in state.selected)
                if (b != null) state.hiddenBlocks.Add(b);
            state.selected.Clear();
            RefreshTerminal();
        }

        private static void UnhideSelected(TerminalState state)
        {
            foreach (var b in state.selected)
            {
                if (b == null) continue;
                state.hiddenBlocks.Remove(b);
                foreach (var group in state.groups)
                    if (group.blocks.Contains(b)) group.hidden = false;
            }
            state.selected.Clear();
            state.showHidden = true;
            RefreshTerminal();
        }

        private static void RefreshTerminal()
        {
            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
        }

        // ── RIGHT: content ────────────────────────────────────────────────────────
        private static VisualElement BuildContent(GridEntity grid, List<GridBlock> blocks, int tab, MachineUIs.SlotBuilder slot)
        {
            var wrap = new ScrollView(ScrollViewMode.Vertical);
            wrap.style.flexGrow = 1;
            wrap.style.minHeight = 0;
            wrap.style.minWidth = 0;
            PersistScroll(wrap, "content_" + tab);

            if (tab >= 0 && tab < blocks.Count)
            {
                var block = blocks[tab];
                if (HasToggle(block))
                {
                    var bar = new VisualElement();
                    bar.style.flexDirection = FlexDirection.Row;
                    bar.style.alignItems = Align.Center;
                    bar.style.marginBottom = 6;
                    var toggleBtn = T.SmallButton(block.Enabled ? "Turn OFF" : "Turn ON", () =>
                    {
                        block.Enabled = !block.Enabled;
                        RefreshTerminal();
                    }, block.Enabled ? T.AccentRed : T.AccentGreen);
                    toggleBtn.style.marginRight = 10;
                    bar.Add(toggleBtn);
                    var status = new Label(block.Enabled ? "ONLINE" : "OFFLINE");
                    status.style.flexGrow = 1;
                    status.style.unityFontStyleAndWeight = FontStyle.Bold;
                    status.style.color = new StyleColor(block.Enabled ? T.AccentGreen : new Color(0.6f, 0.6f, 0.65f));
                    bar.Add(status);
                    wrap.Add(bar);
                }

                var panel = GridBlockUI.BuildPanel(block, slot);
                panel.style.position = Position.Relative;
                panel.style.top = StyleKeyword.Auto;
                panel.style.right = StyleKeyword.Auto;
                panel.style.bottom = StyleKeyword.Auto;
                panel.style.width = StyleKeyword.Auto;
                panel.style.maxWidth = 760;
                panel.style.flexGrow = 0;
                panel.style.alignSelf = Align.FlexStart;

                wrap.Add(panel);
                return wrap;
            }

            var storage = AllStoragePanel(grid, slot);
            storage.style.position = Position.Relative;
            storage.style.top = StyleKeyword.Auto;
            storage.style.right = StyleKeyword.Auto;
            storage.style.bottom = StyleKeyword.Auto;
            storage.style.width = StyleKeyword.Auto;
            storage.style.maxWidth = 860;
            storage.style.flexGrow = 0;
            storage.style.alignSelf = Align.FlexStart;

            wrap.Add(storage);
            return wrap;
        }

        private static VisualElement AllStoragePanel(GridEntity grid, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();
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
            head.style.fontSize = 14;
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.color = new StyleColor(T.AccentGold);
            head.style.marginBottom = 2;
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
            RefreshTerminal();
        }

        // Compact labeled stat box for the status strip.
        private static VisualElement Stat(string label, string value, Color c)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Column;
            box.style.marginRight = 16;
            box.style.minWidth = 70;
            var l = new Label(label);
            l.style.fontSize = 8;
            l.style.letterSpacing = 1f;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new StyleColor(new Color(0.5f, 0.55f, 0.62f));
            box.Add(l);
            var v = new Label(value);
            v.style.fontSize = 14;
            v.style.unityFontStyleAndWeight = FontStyle.Bold;
            v.style.color = new StyleColor(c);
            box.Add(v);
            return box;
        }
    }
}
