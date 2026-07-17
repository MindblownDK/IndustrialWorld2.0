// Assets/Scripts/VoxelEngine/GridSystem/UI/GridMasterTerminal.cs
//
// grid-terminal ship CONTROL TERMINAL. A right-side configuration screen:
// left column lists every functional block on the grid; clicking one shows its
// full panel (stats, tanks, power, inventory) on the right plus an on/off toggle.
// Includes runtime block groups, selection, hiding, and all-grid storage.

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
        private const int GroupTabBase = -100;

        // Remembers scroll offsets per ScrollView key so live refresh doesn't jump lists.
        private static readonly Dictionary<string, float> _scrollY = new();

        // Runtime terminal organisation. Save-compatible: no world/save schema changes.
        private static readonly Dictionary<string, TerminalState> _states = new();

        private sealed class TerminalState
        {
            public readonly HashSet<GridBlock> selected = new();
            public readonly HashSet<GridBlock> hiddenBlocks = new();
            public readonly List<BlockGroup> groups = new();
            public string groupNameDraft = "New Group";
            public bool hideOnCreate;
            public bool showHidden;
            public bool showPowerUsage;
            public int lastSelectedIndex = -1;
        }

        private sealed class BlockGroup
        {
            public string name;
            public bool hidden;
            public readonly List<GridBlock> blocks = new();
        }

        private readonly struct StorageEntry
        {
            public readonly string label;
            public readonly ItemContainer container;
            public readonly float currentKg;
            public readonly float maxKg;

            public StorageEntry(string label, ItemContainer container, float currentKg, float maxKg = -1f)
            {
                this.label = label;
                this.container = container;
                this.currentKg = currentKg;
                this.maxKg = maxKg;
            }
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

        /// <param name="tab">-1 = All Storage; >=0 = block index; <= GroupTabBase = group page.</param>
        public static VisualElement Build(GridEntity grid, int tab, Action<int> onSelectTab,
            MachineUIs.SlotBuilder slot, Action onClose)
        {
            var state = GetState(grid);
            var blocks = GetSortedTerminalBlocks(grid);
            CleanState(state, blocks);

            var win = new VisualElement();
            win.style.flexGrow = 1;
            win.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            win.style.flexDirection = FlexDirection.Column;
            win.style.backgroundColor = new StyleColor(new Color(0.06f, 0.07f, 0.10f, 1f));
            T.Border(win, 1, T.BorderDim);
            T.Radius(win, 8);
            win.style.paddingTop = 12;
            win.style.paddingBottom = 12;
            win.style.paddingLeft = 14;
            win.style.paddingRight = 14;

            // Right-side terminal. Keeps a readable width on big screens and hugs the right edge.
            win.style.maxWidth = 1280;
            win.style.alignSelf = Align.FlexEnd;
            win.style.width = new StyleLength(new Length(100, LengthUnit.Percent));

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

            if (grid != null)
                win.Add(BuildStatusStrip(grid, state));

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            body.style.minHeight = 0;

            body.Add(BuildBlockList(state, blocks, tab, onSelectTab));
            body.Add(BuildContent(grid, state, blocks, tab, slot));
            win.Add(body);

            if (grid != null && state.showPowerUsage)
                win.Add(BuildPowerUsagePopup(grid, state));

            return win;
        }

        private static List<GridBlock> GetSortedTerminalBlocks(GridEntity grid)
        {
            var blocks = new List<GridBlock>();
            if (grid != null)
                foreach (var kv in grid.Blocks)
                    if (kv.Value != null && IsTerminalBlock(kv.Value)) blocks.Add(kv.Value);
            blocks.Sort((a, b) => string.CompareOrdinal(a.blockName, b.blockName));
            return blocks;
        }

        private static TerminalState GetState(GridEntity grid)
        {
            // EntityId no longer safely casts to int in Unity 6+, so keep the key as text.
            string key = grid != null ? grid.GetEntityId().ToString() : "null";
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
                var group = state.groups[i];
                group.blocks.RemoveAll(b => b == null || !blocks.Contains(b));
                if (group.blocks.Count == 0) state.groups.RemoveAt(i);
            }
        }

        private static VisualElement BuildStatusStrip(GridEntity grid, TerminalState state)
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
            strip.Add(Stat("USE", PowerFormat.Watts(grid.PowerConsumed), T.AccentAmber, () =>
            {
                state.showPowerUsage = true;
                RefreshTerminal();
            }));
            strip.Add(Stat("H2", $"{grid.HydrogenStored:0} L", T.AccentCyan));
            strip.Add(Stat("O2", $"{grid.OxygenStored:0} L", T.AccentGreen));
            strip.Add(Stat("SPEED", $"{speed:0.0} m/s", T.AccentGold));
            strip.Add(Stat("BLOCKS", blockCount.ToString(), new Color(0.8f, 0.84f, 0.9f)));
            return strip;
        }

        private static VisualElement BuildPowerUsagePopup(GridEntity grid, TerminalState state)
        {
            var pop = T.Card();
            pop.style.position = Position.Absolute;
            pop.style.top = 78;
            pop.style.right = 24;
            pop.style.width = 430;
            pop.style.maxHeight = new StyleLength(new Length(60, LengthUnit.Percent));
            pop.style.backgroundColor = new StyleColor(new Color(0.045f, 0.05f, 0.075f, 0.98f));
            pop.style.flexShrink = 0;
            T.Border(pop, 1, T.AccentAmber);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var title = T.Subtitle("CURRENT POWER USAGE");
            title.style.flexGrow = 1;
            header.Add(title);
            header.Add(T.SmallButton("Close", () => { state.showPowerUsage = false; RefreshTerminal(); }, T.AccentRed));
            pop.Add(header);
            pop.Add(T.AccentDivider(T.AccentAmber));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(scroll);   // themed slim scrollbar
            scroll.style.maxHeight = 360;
            float total = 0f;
            if (grid != null)
            {
                foreach (var kv in grid.Blocks)
                {
                    var block = kv.Value;
                    if (block == null) continue;
                    float draw = Mathf.Max(0f, block.PowerDraw);
                    if (draw <= 0.01f) continue;
                    total += draw;
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.marginBottom = 4;
                    var name = new Label(block.blockName);
                    name.style.flexGrow = 1;
                    name.style.color = new StyleColor(T.TextSecondary);
                    name.style.fontSize = 11;
                    row.Add(name);
                    var watts = new Label(PowerFormat.Watts(draw));
                    watts.style.color = new StyleColor(T.AccentGold);
                    watts.style.fontSize = 11;
                    watts.style.unityFontStyleAndWeight = FontStyle.Bold;
                    row.Add(watts);
                    scroll.Add(row);
                }
            }

            if (total <= 0.01f)
            {
                scroll.Add(T.Muted("No blocks are currently drawing power."));
            }
            pop.Add(scroll);
            pop.Add(T.AccentDivider(T.AccentAmber));
            pop.Add(T.StatRow("", "Total Use", PowerFormat.Watts(total), T.AccentGold));
            return pop;
        }

        private static int GroupTab(int groupIndex) => GroupTabBase - groupIndex;
        private static bool IsGroupTab(int tab) => tab <= GroupTabBase;
        private static int GroupIndexFromTab(int tab) => GroupTabBase - tab;

        // ── LEFT: block list + groups ─────────────────────────────────────────────
        private static VisualElement BuildBlockList(TerminalState state, List<GridBlock> blocks,
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
            VoxelEngine.UI.UITheme.StyleScroller(list);   // themed slim scrollbar
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
                    if (group.hidden)
                        foreach (var b in group.blocks) grouped.Add(b);
                    list.Add(GroupRow(state, group, i, tab, onSelectTab));
                }
            }

            list.Add(SectionLabel("BLOCKS"));
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

        private static VisualElement GroupRow(TerminalState state, BlockGroup group, int groupIndex,
            int tab, Action<int> onSelectTab)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;
            row.style.backgroundColor = new StyleColor(group.hidden
                ? new Color(0.12f, 0.11f, 0.09f, 1f)
                : new Color(0.10f, 0.15f, 0.18f, 1f));
            T.Radius(row, 5);
            T.Border(row, 1, tab == GroupTab(groupIndex)
                ? T.AccentCyan
                : group.hidden ? new Color(T.AccentAmber.r, T.AccentAmber.g, T.AccentAmber.b, 0.35f) : T.BorderDim);

            var open = new Button(() => onSelectTab(GroupTab(groupIndex))) { text = $"{group.name} ({group.blocks.Count})" };
            open.style.flexGrow = 1;
            open.style.height = 24;
            open.style.unityTextAlign = TextAnchor.MiddleLeft;
            open.style.color = new StyleColor(group.hidden ? T.AccentAmber : T.TextPrimary);
            open.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            T.Border(open, 0, Color.clear);
            row.Add(open);

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
            row.Add(T.SmallButton("Delete", () =>
            {
                state.groups.Remove(group);
                RefreshTerminal();
            }, T.AccentRed));
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

            var button = new Button { text = statePrefix + onlinePrefix + block.blockName };
            button.RegisterCallback<ClickEvent>(evt =>
            {
                bool shift = evt.shiftKey || GridInput.Shift;
                bool ctrl = evt.ctrlKey || GridInput.Ctrl;
                HandleBlockSelection(state, blocks, index, shift, ctrl);
                onSelectTab(index);
                evt.StopPropagation();
            });

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

        private static void HandleBlockSelection(TerminalState state, List<GridBlock> blocks, int index, bool shift, bool ctrl)
        {
            var block = blocks[index];
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
            {
                if (b == null || group.blocks.Contains(b)) continue;
                group.blocks.Add(b);
                if (group.hidden) state.hiddenBlocks.Add(b);
                else state.hiddenBlocks.Remove(b);
            }
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
        private static VisualElement BuildContent(GridEntity grid, TerminalState state, List<GridBlock> blocks,
            int tab, MachineUIs.SlotBuilder slot)
        {
            var wrap = new ScrollView(ScrollViewMode.Vertical);
            VoxelEngine.UI.UITheme.StyleScroller(wrap);   // themed slim scrollbar
            wrap.style.flexGrow = 1;
            wrap.style.minHeight = 0;
            wrap.style.minWidth = 0;
            PersistScroll(wrap, "content_" + tab);

            if (IsGroupTab(tab))
            {
                int groupIndex = GroupIndexFromTab(tab);
                if (groupIndex >= 0 && groupIndex < state.groups.Count)
                {
                    wrap.Add(BuildGroupPage(state, state.groups[groupIndex]));
                    return wrap;
                }
            }

            if (tab >= 0 && tab < blocks.Count)
            {
                var block = blocks[tab];
                wrap.Add(BuildBlockHeader(block));

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

                // Screen config button
                if (block is VoxelEngine.GridSystem.GridScreenBlock screenBlock)
                {
                    var screenBar = new VisualElement();
                    screenBar.style.flexDirection = FlexDirection.Row;
                    screenBar.style.alignItems = Align.Center;
                    screenBar.style.marginBottom = 8;

                    var configBtn = T.SmallButton("CONFIGURE SCREEN", () =>
                    {
                        VoxelEngine.GridSystem.UI.GridScreenConfigUI.Instance?.OpenForScreen(screenBlock);
                    }, T.AccentCyan);
                    configBtn.style.marginRight = 10;
                    screenBar.Add(configBtn);

                    var srcLbl = new Label("Source: " + (screenBlock.SourceCount > 0 ? screenBlock.SourceCount + " sources" : "none"));
                    srcLbl.style.color = T.TextSecondary;
                    srcLbl.style.fontSize = 11;
                    srcLbl.style.flexGrow = 1;
                    screenBar.Add(srcLbl);

                    wrap.Add(screenBar);
                }

                var panel = GridBlockUI.BuildPanel(block, slot);
                NormalizePanelForTerminal(panel, maxWidth: 760);
                wrap.Add(panel);
                return wrap;
            }

            var storage = AllStoragePanel(grid, slot);
            NormalizePanelForTerminal(storage, maxWidth: 860);
            wrap.Add(storage);
            return wrap;
        }

        private static VisualElement BuildBlockHeader(GridBlock block)
        {
            var box = T.Card();
            box.style.maxWidth = 760;
            box.style.marginBottom = 8;
            box.Add(T.Subtitle("BLOCK NAME"));
            var name = new TextField { value = block.blockName };
            name.tooltip = "Rename this block in the terminal list and groups.";
            name.RegisterValueChangedCallback(e =>
            {
                block.blockName = string.IsNullOrWhiteSpace(e.newValue) ? block.GetType().Name : e.newValue.Trim();
            });
            name.RegisterCallback<FocusInEvent>(_ => PortConfigHud.IsAnyDropdownOpen = true);
            name.RegisterCallback<FocusOutEvent>(_ => { PortConfigHud.IsAnyDropdownOpen = false; RefreshTerminal(); });
            box.Add(name);
            return box;
        }

        private static void NormalizePanelForTerminal(VisualElement panel, float maxWidth)
        {
            panel.style.position = Position.Relative;
            panel.style.top = StyleKeyword.Auto;
            panel.style.right = StyleKeyword.Auto;
            panel.style.bottom = StyleKeyword.Auto;
            panel.style.width = StyleKeyword.Auto;
            panel.style.maxWidth = maxWidth;
            panel.style.flexGrow = 0;
            panel.style.alignSelf = Align.FlexStart;
        }

        private static VisualElement BuildGroupPage(TerminalState state, BlockGroup group)
        {
            var page = T.MachinePanel();
            page.style.position = Position.Relative;
            page.style.top = StyleKeyword.Auto;
            page.style.right = StyleKeyword.Auto;
            page.style.bottom = StyleKeyword.Auto;
            page.style.width = StyleKeyword.Auto;
            page.style.maxWidth = 860;
            page.style.flexGrow = 0;
            page.style.alignSelf = Align.FlexStart;
            page.style.flexShrink = 0;
            page.style.paddingBottom = 40;

            page.Add(T.Subtitle("GROUP"));
            var name = new TextField { value = group.name };
            name.tooltip = "Edit this group name.";
            name.RegisterValueChangedCallback(e => group.name = string.IsNullOrWhiteSpace(e.newValue) ? "Group" : e.newValue.Trim());
            name.RegisterCallback<FocusInEvent>(_ => PortConfigHud.IsAnyDropdownOpen = true);
            name.RegisterCallback<FocusOutEvent>(_ => { PortConfigHud.IsAnyDropdownOpen = false; RefreshTerminal(); });
            page.Add(name);
            page.Add(T.Spacer(8));

            var groupActions = new VisualElement();
            groupActions.style.flexDirection = FlexDirection.Row;
            groupActions.style.flexWrap = Wrap.Wrap;
            groupActions.Add(T.SmallButton(group.hidden ? "Show Group" : "Hide Group", () =>
            {
                group.hidden = !group.hidden;
                RefreshTerminal();
            }, group.hidden ? T.AccentGreen : T.AccentAmber));
            groupActions.Add(T.SmallButton("Delete Group", () =>
            {
                state.groups.Remove(group);
                RefreshTerminal();
            }, T.AccentRed));
            page.Add(groupActions);
            page.Add(T.AccentDivider(T.AccentCyan));

            var byType = new SortedDictionary<string, List<GridBlock>>();
            foreach (var b in group.blocks)
            {
                if (b == null) continue;
                string key = CategoryName(b);
                if (!byType.TryGetValue(key, out var list)) { list = new List<GridBlock>(); byType[key] = list; }
                list.Add(b);
            }

            foreach (var kv in byType)
            {
                var list = kv.Value;
                page.Add(GridUIHelpers.SectionTitle($"{list.Count} x {kv.Key}"));
                page.Add(GroupCategoryControls(list));
                foreach (var b in list)
                {
                    var row = new Label($"  • {b.blockName} ({BlockStateLabel(b)})");
                    row.style.fontSize = 10;
                    row.style.color = new StyleColor(b.Enabled ? T.TextSecondary : T.AccentRed);
                    page.Add(row);
                }
                page.Add(T.Spacer(6));
            }

            return page;
        }

        private static VisualElement GroupCategoryControls(List<GridBlock> blocks)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            if (blocks == null || blocks.Count == 0) return row;

            bool togglable = false;
            foreach (var b in blocks) if (HasToggle(b)) { togglable = true; break; }
            if (togglable)
            {
                row.Add(T.SmallButton("All ON", () => { foreach (var b in blocks) if (HasToggle(b)) b.Enabled = true; RefreshTerminal(); }, T.AccentGreen));
                row.Add(T.SmallButton("All OFF", () => { foreach (var b in blocks) if (HasToggle(b)) b.Enabled = false; RefreshTerminal(); }, T.AccentRed));
            }

            if (blocks[0] is GridBattery)
            {
                row.Add(T.SmallButton("Auto", () => SetBatteryMode(blocks, GridBatteryMode.Auto), T.AccentGreen));
                row.Add(T.SmallButton("Recharge", () => SetBatteryMode(blocks, GridBatteryMode.Recharge), T.AccentCyan));
                row.Add(T.SmallButton("Discharge", () => SetBatteryMode(blocks, GridBatteryMode.Discharge), T.AccentAmber));
            }
            else if (blocks[0] is GridLiquidTank || blocks[0] is GridGasTank)
            {
                row.Add(T.SmallButton("Auto", () => SetTankMode(blocks, GridTankMode.Auto), T.AccentGreen));
                row.Add(T.SmallButton("Stockpile", () => SetTankMode(blocks, GridTankMode.Stockpile), T.AccentAmber));
            }

            return row;
        }

        private static void SetBatteryMode(List<GridBlock> blocks, GridBatteryMode mode)
        {
            foreach (var b in blocks)
                if (b is GridBattery battery) battery.mode = mode;
            RefreshTerminal();
        }

        private static void SetTankMode(List<GridBlock> blocks, GridTankMode mode)
        {
            foreach (var b in blocks)
            {
                if (b is GridLiquidTank liquid) liquid.mode = mode;
                else if (b is GridGasTank gas) gas.mode = mode;
            }
            RefreshTerminal();
        }

        private static string BlockStateLabel(GridBlock block)
        {
            if (block == null) return "Missing";
            if (!block.Enabled) return "Offline";
            if (block is VoxelEngine.Maritime.GridMaritimeEngine eng)
                return eng.IsRunning ? $"{eng.CurrentRPM:0} RPM" : eng.HasExhaust ? "Idle" : "NO EXHAUST";
            if (block is VoxelEngine.Maritime.GridMaritimeGenerator gen)
                return PowerFormat.Watts(gen.GeneratedWatts);
            if (block is VoxelEngine.Maritime.GridGearbox gb)
                return gb.IsOverstressed ? "OVERSTRESSED" : $"{gb.OutputRPM:0} RPM";
            if (block is VoxelEngine.Maritime.GridRotationTransfer rt)
                return rt.CurrentRPM > 1f ? $"{rt.CurrentRPM:0} RPM" : "Stopped";
            if (block is VoxelEngine.Maritime.GridEncasedChainDrive cd)
                return cd.CurrentRPM > 1f ? $"{cd.CurrentRPM:0} RPM" : "Stopped";
            if (block is VoxelEngine.Maritime.GridPropeller prop)
                return prop.CurrentRPM > 1f ? $"{prop.CurrentRPM:0} RPM" : "Stopped";
            if (block is VoxelEngine.Maritime.GridElectricalPropeller ep)
                return ep.CurrentRPM > 1f ? $"{ep.CurrentRPM:0} RPM" : "Stopped";
            if (block is VoxelEngine.Maritime.GridTurbocharger tc)
                return tc.IsConnected ? $"{tc.BoostPressure:0.#} bar" : "Disconnected";
            if (block is VoxelEngine.Maritime.GridBilgePump bp)
                return bp.IsActive ? "Draining" : "Standby";
            if (block is VoxelEngine.Maritime.GridHelm helm)
                return helm.IsActive ? "Manned" : "Unmanned";
            if (block is GridBattery battery) return battery.mode.ToString();
            if (block is GridLiquidTank liquid) return liquid.mode.ToString();
            if (block is GridGasTank gas) return gas.mode.ToString();
            if (block is GridLandingGear gear) return gear.IsLocked ? "Locked" : "Unlocked";
            if (block is GridSolarPanel solar) return PowerFormat.Watts(solar.CurrentOutput);
            if (block is GridBeacon beacon) return beacon.IsActive ? "Active" : "Off";
            if (block is GridOreDetector detector) return $"{detector.DetectedOres.Count} ores";
            if (block.PowerDraw > 0.01f) return PowerFormat.Watts(block.PowerDraw);
            if (block.PowerOutput > 0.01f) return PowerFormat.Watts(block.PowerOutput);
            return "Online";
        }

        private static string CategoryName(GridBlock block)
        {
            if (block is VoxelEngine.Maritime.GridMaritimeEngine) return "Maritime Engines";
            if (block is VoxelEngine.Maritime.GridMaritimeGenerator) return "Maritime Generators";
            if (block is VoxelEngine.Maritime.GridGearbox) return "Gearboxes";
            if (block is VoxelEngine.Maritime.GridPropeller) return "Propellers";
            if (block is VoxelEngine.Maritime.GridElectricalPropeller) return "Electric Propellers";
            if (block is VoxelEngine.Maritime.GridTurbocharger) return "Turbochargers";
            if (block is VoxelEngine.Maritime.GridWaterwheel) return "Waterwheels";
            if (block is VoxelEngine.Maritime.GridRotationTransfer) return "Rotation Transfers";
            if (block is VoxelEngine.Maritime.GridEncasedChainDrive) return "Encased Chain Drives";
            if (block is VoxelEngine.Maritime.GridDriveShaft) return "Drive Shafts";
            if (block is VoxelEngine.Maritime.GridExhaustPipe) return "Exhaust Pipes";
            if (block is VoxelEngine.Maritime.GridBilgePump) return "Bilge Pumps";
            if (block is VoxelEngine.Maritime.GridHelm) return "Helms";
            if (block is VoxelEngine.Maritime.GridHullBlock) return "Hull Blocks";
            if (block is GridBattery) return "Batteries";
            if (block is GridCargoContainer) return "Cargo Containers";
            if (block is GridDrill) return "Mining Drills";
            if (block is GridThruster) return "Thrusters";
            if (block is GridGasTank) return "Gas Tanks";
            if (block is GridLiquidTank) return "Liquid Tanks";
            if (block is GridWeapon) return "Weapons";
            if (block is GridGyroscope) return "Gyroscopes";
            if (block is GridArmorBlock) return "Armor Blocks";
            if (block is GridLandingGear) return "Landing Gear";
            if (block is GridWheel) return "Wheels";
            if (block is GridSolarPanel) return "Solar Panels";
            if (block is GridHydrogenEngine) return "Hydrogen Engines";
            if (block is GridPortableReactor) return "Portable Reactors";
            if (block is GridElectricFurnace) return "Electric Furnaces";
            if (block is GridDockingPort) return "Docking Ports";
            if (block is GridCockpit) return "Cockpits";
            if (block is GridBeacon) return "Beacons";
            if (block is GridOreDetector) return "Ore Detectors";
            if (block is GridScreenBlock) return "Screens";
            return block.GetType().Name;
        }

        private static VisualElement AllStoragePanel(GridEntity grid, MachineUIs.SlotBuilder slot)
        {
            var p = T.MachinePanel();
            p.style.width = StyleKeyword.Auto;
            p.style.flexShrink = 0;
            p.style.paddingBottom = 40;

            var entries = CollectStorageEntries(grid);
            float totalKg = 0f;
            foreach (var e in entries) totalKg += e.currentKg;

            var head = new Label("ALL STORAGE");
            head.style.fontSize = 14;
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.color = new StyleColor(T.AccentGold);
            head.style.marginBottom = 2;
            p.Add(head);
            p.Add(GridUIHelpers.WeightHeader(totalKg, "Total"));
            p.Add(T.AccentDivider(T.AccentGold));

            if (entries.Count == 0)
            {
                p.Add(T.Muted("No item inventories found on this grid."));
                return p;
            }

            foreach (var entry in entries)
            {
                var c = entry.container;
                string massText = entry.maxKg > 0f
                    ? $"{MassFormat.Format(entry.currentKg)} / {MassFormat.Format(entry.maxKg)}"
                    : MassFormat.Format(entry.currentKg);
                p.Add(GridUIHelpers.SectionTitle($"{entry.label}  ·  {massText}"));
                var g = T.SlotGrid(7);
                g.style.width = 7 * 60 + 12;
                g.style.flexShrink = 0;
                g.style.overflow = Overflow.Hidden;
                for (int i = 0; i < c.Size; i++) g.Add(slot(c, i, c.GetSlot(i), false, true));
                p.Add(g);
            }
            return p;
        }

        private static List<StorageEntry> CollectStorageEntries(GridEntity grid)
        {
            var entries = new List<StorageEntry>();
            var seen = new HashSet<ItemContainer>();
            if (grid == null) return entries;

            void Add(string label, ItemContainer container, float maxKg = -1f)
            {
                if (container == null || seen.Contains(container)) return;
                seen.Add(container);
                entries.Add(new StorageEntry(label, container, MassUtil.ContainerMass(container), maxKg));
            }

            foreach (var kv in grid.Blocks)
            {
                var b = kv.Value;
                if (b == null) continue;
                switch (b)
                {
                    case GridCargoContainer cargo:
                        if (cargo.container == null) cargo.OnPlaced();
                        Add($"{cargo.blockName} (Cargo)", cargo.container, cargo.maxMassKg);
                        break;
                    case GridDockingPort dock:
                        if (dock.container == null) dock.OnPlaced();
                        Add($"{dock.blockName} (Dock)", dock.container);
                        break;
                    case GridDrill drill:
                        if (drill.buffer == null) drill.OnPlaced();
                        Add($"{drill.blockName} (Drill Buffer)", drill.buffer);
                        break;
                    case GridWeapon weapon:
                        if (weapon.ammo == null) weapon.OnPlaced();
                        Add($"{weapon.blockName} (Ammo)", weapon.ammo);
                        break;
                    case GridH2O2Generator h2:
                        if (h2.iceInput == null) h2.OnPlaced();
                        Add($"{h2.blockName} (Ice Input)", h2.iceInput);
                        break;
                    case GridElectricFurnace furnace:
                        if (furnace.inputC == null) furnace.OnPlaced();
                        Add($"{furnace.blockName} (Input)", furnace.inputC);
                        Add($"{furnace.blockName} (Output)", furnace.outputC);
                        break;
                    case GridPortableReactor reactor:
                        if (reactor.fuelC == null) reactor.OnPlaced();
                        Add($"{reactor.blockName} (Fuel)", reactor.fuelC);
                        Add($"{reactor.blockName} (Ice)", reactor.iceC);
                        Add($"{reactor.blockName} (Waste)", reactor.wasteC);
                        break;
                }
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            return entries;
        }

        private static bool HasToggle(GridBlock b) => b != null;

        private static bool IsTerminalBlock(GridBlock b) => b != null;

        private static void SetAllEnabled(GridEntity grid, bool on)
        {
            if (grid == null) return;
            foreach (var kv in grid.Blocks)
                if (kv.Value != null && HasToggle(kv.Value)) kv.Value.Enabled = on;
            RefreshTerminal();
        }

        private static VisualElement Stat(string label, string value, Color c, Action onClick = null)
        {
            var box = onClick == null ? new VisualElement() : new Button(() => onClick());
            box.style.flexDirection = FlexDirection.Column;
            box.style.marginRight = 16;
            box.style.minWidth = 70;
            if (onClick != null)
            {
                box.style.paddingLeft = 0;
                box.style.paddingRight = 0;
                box.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                T.Border(box, 0, Color.clear);
            }
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
