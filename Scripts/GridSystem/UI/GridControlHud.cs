// Assets/Scripts/VoxelEngine/GridSystem/UI/GridControlHud.cs
//
// 14.64.0 - GRID CONTROL HUD.
//
// A bottom-screen action toolbar for control seats, with our twist: the nine
// digit keys fire ASSIGNED ACTIONS while piloting. Assignments are authored in
// the GRID CONTROL terminal (EDIT HUD button): drag any controllable block, a
// whole category (e.g. WEAPONS) or a player-made group onto one of the nine
// slots, then pick WHICH action that key fires - on/off toggle, engage or
// disengage landing gear, autolock on/off, battery or tank mode, and so on.
//
// Three deliberate rules:
//   1. The toolbar belongs to the GRID - every control seat of a hull shows
//      the same nine slots, keyed by the grid's entity id.
//   2. Actions resolve their member blocks AT PRESS TIME, so slots follow the
//      live hull: blocks welded into an assigned category or group later are
//      included automatically, destroyed blocks drop out.
//   3. Toggles are majority-off deterministic: if ANY member is on/locked the
//      toggle turns everything off/unlocked, otherwise everything on - the
//      same convergent rule the terminal's ALL ON / ALL OFF buttons use.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Settings;
using T = VoxelEngine.UI.UITheme;
using L = VoxelEngine.UI.LcdHudTheme;

namespace VoxelEngine.GridSystem.UI
{
    public static class GridControlHud
    {
        private const int SlotCount = 9;

        private enum TargetKind { Block, Group, Category }

        private sealed class SlotAssignment
        {
            public TargetKind kind;
            public GridBlock block;    // Block targets
            public string key;         // group name / category label
            public string actionId;
            public string actionLabel;

            public string DisplayName => kind == TargetKind.Block
                ? (block != null ? BlockDisplayName(block) : "MISSING")
                : key;
        }

        private readonly struct ActionDef
        {
            public readonly string id;
            public readonly string label;
            public ActionDef(string id, string label) { this.id = id; this.label = label; }
        }

        // Per-grid toolbars, keyed the same way the terminal keys its state.
        private static readonly Dictionary<string, SlotAssignment[]> _bars = new();

        private static string KeyOf(GridEntity grid) =>
            grid != null ? grid.GetEntityId().ToString() : "null";

        private static SlotAssignment[] SlotsFor(GridEntity grid)
        {
            string key = KeyOf(grid);
            if (!_bars.TryGetValue(key, out var slots))
            {
                slots = new SlotAssignment[SlotCount];
                _bars[key] = slots;
            }
            return slots;
        }

        private static string BlockDisplayName(GridBlock block)
        {
            if (block == null) return "MISSING";
            string name = string.IsNullOrWhiteSpace(block.blockName)
                ? block.GetType().Name
                : block.blockName;
            return name;
        }

        // ─────────────────────────────────────────────────────────────
        //  TOOLBAR (bottom HUD while seated)
        // ─────────────────────────────────────────────────────────────

        private static VisualElement _root, _bar;
        private static string _lastSig = "\u0000";
        private static int _flashSlot = -1;
        private static float _flashUntil;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _bar != null && _bar.parent == uiRoot) return;

            _root = uiRoot;
            if (_bar != null) _bar.RemoveFromHierarchy();

            _bar = new VisualElement { name = "GridControlHudBar" };
            _bar.style.position = Position.Absolute;
            _bar.style.bottom = 24; _bar.style.left = 0; _bar.style.right = 0;
            _bar.style.flexDirection = FlexDirection.Row;
            _bar.style.justifyContent = Justify.Center;
            _bar.style.display = DisplayStyle.None;
            _bar.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_bar);
            _lastSig = "\u0000";
        }

        public static void Tick()
        {
            // Editor housekeeping: if the UI root was rebuilt underneath the open
            // editor, its panel died - drop the stale references.
            if (_editorOverlay != null && _editorOverlay.panel == null)
                CloseEditor();
            // 14.64.1 — the editor closes like any screen: ESC or the inventory key
            // dismisses it, and when the terminal underneath is gone (another UI
            // took over, the seat closed everything) it never lingers on its own.
            if (_editorOverlay != null &&
                (EditorClosePressed() || !VoxelEngine.UI.UIState.IsBlocking))
                CloseEditor();

            if (_bar == null) return;

            var grid = GridCockpit.ActiveControlGrid;
            bool show = grid != null && GridCockpit.AnyPilotSeatActive && !VoxelEngine.UI.UIState.IsBlocking;
            _bar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) { _lastSig = "\u0000"; return; }

            // Digit keys fire the slots while seated (the player hotbar yields them).
            var slots = SlotsFor(grid);
            for (int i = 0; i < SlotCount; i++)
                if (GameSettings.WasPressed(HotbarAction(i)))
                    ExecuteSlot(grid, i);

            if (_flashSlot >= 0 && Time.unscaledTime > _flashUntil)
            {
                _flashSlot = -1;
                _lastSig = "\u0000";
            }

            // Cheap signature; rebuild only when the rendered state actually changed.
            var sb = new System.Text.StringBuilder();
            sb.Append(KeyOf(grid)).Append('|').Append(_flashSlot).Append('|');
            for (int i = 0; i < SlotCount; i++)
            {
                var a = slots[i];
                if (a == null) { sb.Append('-').Append('|'); continue; }
                sb.Append(a.DisplayName).Append('·').Append(a.actionLabel)
                  .Append('·').Append(SlotStateTag(grid, a)).Append('|');
            }
            string sig = sb.ToString();
            if (sig == _lastSig) return;
            _lastSig = sig;

            RebuildBar(grid, slots);
        }

        private static InputAction HotbarAction(int index) => index switch
        {
            0 => InputAction.Hotbar1, 1 => InputAction.Hotbar2, 2 => InputAction.Hotbar3,
            3 => InputAction.Hotbar4, 4 => InputAction.Hotbar5, 5 => InputAction.Hotbar6,
            6 => InputAction.Hotbar7, 7 => InputAction.Hotbar8, _ => InputAction.Hotbar9,
        };

        private static void RebuildBar(GridEntity grid, SlotAssignment[] slots)
        {
            _bar.Clear();

            var strip = new VisualElement();
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.backgroundColor = new StyleColor(new Color(0.045f, 0.055f, 0.045f, 0.92f));
            strip.style.paddingLeft = 3; strip.style.paddingRight = 3;
            strip.style.paddingTop = 3; strip.style.paddingBottom = 3;
            T.Border(strip, 1, T.BorderDim); T.Radius(strip, 8);
            strip.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < SlotCount; i++)
            {
                var a = slots[i];
                bool flash = i == _flashSlot;

                var cell = new VisualElement();
                cell.style.width = 108; cell.style.height = 74;
                cell.style.marginLeft = 2; cell.style.marginRight = 2;
                cell.style.paddingLeft = 6; cell.style.paddingRight = 6;
                cell.style.paddingTop = 4; cell.style.paddingBottom = 4;
                cell.style.justifyContent = Justify.SpaceBetween;
                cell.style.backgroundColor = new StyleColor(a != null ? L.GlassDark : new Color(0f, 0f, 0f, 0.35f));
                T.Border(cell, 1, flash ? L.Phosphor : (a != null ? new Color(L.Phosphor.r, L.Phosphor.g, L.Phosphor.b, 0.35f) : T.BorderDim));
                T.Radius(cell, 6);
                cell.pickingMode = PickingMode.Ignore;

                // Row 1: slot number left, state CHIP right — the chip has its own
                // dark background so the status reads whatever the name text does.
                var top = new VisualElement();
                top.style.flexDirection = FlexDirection.Row;
                top.style.justifyContent = Justify.SpaceBetween;
                top.style.alignItems = Align.Center;
                top.style.minHeight = 14; top.style.flexShrink = 0;
                top.pickingMode = PickingMode.Ignore;

                var num = new Label((i + 1).ToString());
                num.style.fontSize = 9;
                num.style.color = new StyleColor(L.Caption);
                num.style.unityFontStyleAndWeight = FontStyle.Bold;
                top.Add(num);

                if (a != null)
                {
                    string tag = SlotStateTag(grid, a);
                    var state = new Label(tag);
                    state.style.fontSize = 8;
                    state.style.unityFontStyleAndWeight = FontStyle.Bold;
                    state.style.color = new StyleColor(SlotStateColor(tag));
                    state.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
                    state.style.paddingLeft = 4; state.style.paddingRight = 4;
                    state.style.paddingTop = 1; state.style.paddingBottom = 1;
                    T.Radius(state, 3);
                    top.Add(state);
                }
                cell.Add(top);

                // Row 2: the target name — one fixed-height clipped line, never
                // overlapping the chip above or the action below.
                var nameLabel = new Label(a != null ? a.DisplayName.ToUpperInvariant() : "—");
                nameLabel.style.fontSize = 9;
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.style.color = new StyleColor(a != null ? new Color(0.88f, 0.93f, 0.85f) : new Color(0.35f, 0.38f, 0.36f));
                nameLabel.style.overflow = Overflow.Hidden;
                nameLabel.style.textOverflow = TextOverflow.Ellipsis;
                nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
                nameLabel.style.minHeight = 14; nameLabel.style.flexShrink = 0;
                cell.Add(nameLabel);

                // Row 3: the assigned action.
                var action = new Label(a != null ? a.actionLabel : string.Empty);
                action.style.fontSize = 8;
                action.style.color = new StyleColor(L.PhosphorDim);
                action.style.overflow = Overflow.Hidden;
                action.style.textOverflow = TextOverflow.Ellipsis;
                action.style.whiteSpace = WhiteSpace.NoWrap;
                action.style.minHeight = 13; action.style.flexShrink = 0;
                cell.Add(action);

                strip.Add(cell);
            }

            _bar.Add(strip);
        }

        /// <summary>Short live state tag for a slot: ON / OFF / MIXED / LOCKED / FREE / N/A.</summary>
        private static string SlotStateTag(GridEntity grid, SlotAssignment a)
        {
            var members = ResolveMembers(grid, a);
            if (members.Count == 0) return "N/A";

            if (a.actionId.StartsWith("gear", System.StringComparison.Ordinal) || a.actionId == "autolock")
            {
                bool anyLocked = false, anyGear = false;
                foreach (var b in members)
                    if (b is GridLandingGear g) { anyGear = true; if (g.IsLocked) { anyLocked = true; break; } }
                if (!anyGear) return "N/A";
                return anyLocked ? "LOCKED" : "FREE";
            }

            int on = 0;
            foreach (var b in members) if (b.Enabled) on++;
            if (on == 0) return "OFF";
            return on == members.Count ? "ON" : "MIXED";
        }

        private static Color SlotStateColor(string tag) => tag switch
        {
            "ON" => L.Phosphor,
            "LOCKED" => new Color(0.35f, 0.80f, 0.95f),
            "MIXED" => T.AccentAmber,
            "FREE" => T.AccentAmber,
            "OFF" => new Color(0.55f, 0.35f, 0.30f),
            _ => new Color(0.42f, 0.45f, 0.43f),
        };

        // ─────────────────────────────────────────────────────────────
        //  TARGET RESOLUTION + ACTIONS
        // ─────────────────────────────────────────────────────────────

        private static List<GridBlock> ResolveMembers(GridEntity grid, SlotAssignment a)
        {
            var list = new List<GridBlock>();
            if (a == null || grid == null) return list;
            switch (a.kind)
            {
                case TargetKind.Block:
                    if (a.block != null && a.block.Grid == grid) list.Add(a.block);
                    break;
                case TargetKind.Group:
                    foreach (var g in GridMasterTerminal.PlayerGroups(grid))
                        if (g.Key == a.key)
                        {
                            foreach (var b in g.Value)
                                if (b != null) list.Add(b);
                            break;
                        }
                    break;
                case TargetKind.Category:
                    foreach (var b in grid.AllBlocks)
                        if (GridMasterTerminal.IsListedBlock(b) && GridMasterTerminal.CategoryLabel(b) == a.key) list.Add(b);
                    break;
            }
            return list;
        }

        private static List<ActionDef> ActionsFor(List<GridBlock> members)
        {
            var actions = new List<ActionDef>
            {
                new("toggle", "ON / OFF TOGGLE"),
                new("on", "TURN ON"),
                new("off", "TURN OFF"),
            };
            bool gear = false, battery = false, tank = false;
            foreach (var b in members)
            {
                if (b is GridLandingGear) gear = true;
                else if (b is GridBattery) battery = true;
                else if (b is GridLiquidTank || b is GridGasTank) tank = true;
            }
            if (gear)
            {
                actions.Add(new ActionDef("gear_toggle", "ENGAGE / DISENGAGE LOCK"));
                actions.Add(new ActionDef("gear_lock", "ENGAGE LOCK"));
                actions.Add(new ActionDef("gear_unlock", "DISENGAGE LOCK"));
                actions.Add(new ActionDef("autolock", "AUTOLOCK ON / OFF"));
            }
            if (battery) actions.Add(new ActionDef("battery_cycle", "CYCLE BATTERY MODE"));
            if (tank) actions.Add(new ActionDef("tank_cycle", "CYCLE TANK MODE"));
            return actions;
        }

        private static void ExecuteSlot(GridEntity grid, int index)
        {
            var slots = SlotsFor(grid);
            var a = slots[index];
            if (a == null) return;

            _flashSlot = index;
            _flashUntil = Time.unscaledTime + 0.25f;
            _lastSig = "\u0000";

            var members = ResolveMembers(grid, a);
            if (members.Count == 0)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Grid Control",
                    $"{a.DisplayName}: no matching blocks on this grid",
                    null, new Color(1f, 0.70f, 0.25f));
                return;
            }

            string feedback = RunAction(a.actionId, members);
            if (!string.IsNullOrEmpty(feedback))
                VoxelEngine.UI.BuildFeedbackHud.Show("Grid Control",
                    $"{a.DisplayName}: {feedback}", null, new Color(0.35f, 0.90f, 0.80f));
        }

        private static string RunAction(string actionId, List<GridBlock> members)
        {
            switch (actionId)
            {
                case "toggle":
                {
                    bool anyOn = false;
                    foreach (var b in members) if (b.Enabled) { anyOn = true; break; }
                    foreach (var b in members) b.Enabled = !anyOn;
                    return anyOn ? "turned OFF" : "turned ON";
                }
                case "on":
                    foreach (var b in members) b.Enabled = true;
                    return "turned ON";
                case "off":
                    foreach (var b in members) b.Enabled = false;
                    return "turned OFF";
                case "gear_toggle":
                {
                    bool anyLocked = false;
                    int gears = 0;
                    foreach (var b in members)
                        if (b is GridLandingGear g) { gears++; if (g.IsLocked) anyLocked = true; }
                    if (gears == 0) return "no landing gear";
                    foreach (var b in members)
                        if (b is GridLandingGear g) { if (anyLocked) g.Unlock(); else g.TryLock(); }
                    return anyLocked ? "gear disengaged" : "gear engaging";
                }
                case "gear_lock":
                {
                    int gears = 0;
                    foreach (var b in members) if (b is GridLandingGear g) { g.TryLock(); gears++; }
                    return gears > 0 ? "gear engaging" : "no landing gear";
                }
                case "gear_unlock":
                {
                    int gears = 0;
                    foreach (var b in members) if (b is GridLandingGear g) { g.Unlock(); gears++; }
                    return gears > 0 ? "gear disengaged" : "no landing gear";
                }
                case "autolock":
                {
                    bool anyAuto = false;
                    int gears = 0;
                    foreach (var b in members)
                        if (b is GridLandingGear g) { gears++; if (g.autoLock) anyAuto = true; }
                    if (gears == 0) return "no landing gear";
                    foreach (var b in members)
                        if (b is GridLandingGear g) g.autoLock = !anyAuto;
                    return anyAuto ? "autolock OFF" : "autolock ON";
                }
                case "battery_cycle":
                {
                    GridBattery first = null;
                    foreach (var b in members) if (b is GridBattery bat) { first = bat; break; }
                    if (first == null) return "no batteries";
                    var next = first.mode switch
                    {
                        GridBatteryMode.Auto => GridBatteryMode.Recharge,
                        GridBatteryMode.Recharge => GridBatteryMode.Discharge,
                        _ => GridBatteryMode.Auto,
                    };
                    foreach (var b in members) if (b is GridBattery bat) bat.mode = next;
                    return $"batteries → {next}";
                }
                case "tank_cycle":
                {
                    GridTankMode? current = null;
                    foreach (var b in members)
                    {
                        if (b is GridLiquidTank liquid) { current = liquid.mode; break; }
                        if (b is GridGasTank gas) { current = gas.mode; break; }
                    }
                    if (current == null) return "no tanks";
                    var next = current == GridTankMode.Auto ? GridTankMode.Stockpile : GridTankMode.Auto;
                    foreach (var b in members)
                    {
                        if (b is GridLiquidTank liquid) liquid.mode = next;
                        else if (b is GridGasTank gas) gas.mode = next;
                    }
                    return $"tanks → {next}";
                }
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────
        //  EDITOR (opened from the GRID CONTROL terminal's EDIT HUD)
        // ─────────────────────────────────────────────────────────────

        private static VisualElement _editorOverlay;
        private static VisualElement _editorCard;
        private static GridEntity _editorGrid;
        private static int _editorTab;                       // 0 = blocks, 1 = categories, 2 = groups
        private static readonly List<VisualElement> _slotBoxes = new();
        private static SlotAssignment _pending;              // picked-up target awaiting a slot
        private static Label _pendingHint;
        private static Label _dragGhost;

        /// <summary>Open the HUD slot editor over the terminal. <paramref name="context"/>
        /// is any mounted element of the terminal - the overlay climbs to its panel root
        /// so it renders above everything currently on screen.</summary>
        public static void OpenEditor(GridEntity grid, VisualElement context)
        {
            if (grid == null || context == null || context.panel == null) return;
            CloseEditor();

            // 14.64.1 — mount on the persistent fullscreen HUD layer (definite
            // size, drawn above the content layer). Mounting on the climbed panel
            // root gave the overlay no resolved height, so every percent-sized
            // child collapsed — the "whole screen mushed together" report.
            VisualElement root = _root != null && _root.panel != null ? _root : null;
            if (root == null)
            {
                root = context;
                while (root.parent != null) root = root.parent;
            }

            _editorGrid = grid;
            _editorTab = 0;
            _pending = null;

            _editorOverlay = new VisualElement { name = "GridControlHudEditor" };
            _editorOverlay.style.position = Position.Absolute;
            _editorOverlay.style.left = 0; _editorOverlay.style.right = 0;
            _editorOverlay.style.top = 0; _editorOverlay.style.bottom = 0;
            _editorOverlay.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.55f));
            _editorOverlay.style.alignItems = Align.Center;
            _editorOverlay.style.justifyContent = Justify.Center;

            _editorCard = new VisualElement();
            // Absolute insets instead of percent width/height: the card keeps a real
            // rectangle whatever the host's layout state is (no collapse, no cutoff).
            _editorCard.style.position = Position.Absolute;
            _editorCard.style.left = new StyleLength(new Length(14f, LengthUnit.Percent));
            _editorCard.style.right = new StyleLength(new Length(14f, LengthUnit.Percent));
            _editorCard.style.top = new StyleLength(new Length(8f, LengthUnit.Percent));
            _editorCard.style.bottom = new StyleLength(new Length(9f, LengthUnit.Percent));
            _editorCard.style.minWidth = 560;
            _editorCard.style.backgroundColor = new StyleColor(L.Chassis);
            T.Border(_editorCard, 2, L.Bezel); T.Radius(_editorCard, 10);
            _editorCard.style.paddingLeft = 10; _editorCard.style.paddingRight = 10;
            _editorCard.style.paddingTop = 8; _editorCard.style.paddingBottom = 10;
            _editorCard.style.flexDirection = FlexDirection.Column;
            _editorOverlay.Add(_editorCard);

            root.Add(_editorOverlay);
            RebuildEditor();
        }

        private static bool EditorClosePressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
#else
            if (Input.GetKeyDown(KeyCode.Escape)) return true;
#endif
            return GameSettings.WasPressed(InputAction.Inventory);
        }

        public static void CloseEditor()
        {
            _editorOverlay?.RemoveFromHierarchy();
            _editorOverlay = null;
            _editorCard = null;
            _editorGrid = null;
            _pending = null;
            _pendingHint = null;
            _dragGhost = null;
            _slotBoxes.Clear();
        }

        private static void RebuildEditor()
        {
            if (_editorCard == null || _editorGrid == null) return;
            _editorCard.Clear();
            _slotBoxes.Clear();

            _editorCard.Add(L.CreateDisplayHeader("SLOT ASSIGNMENT", "GRID CONTROL HUD", "GCH-01", "EDIT"));

            // Tabs + close.
            var commands = new VisualElement();
            commands.style.flexDirection = FlexDirection.Row;
            commands.style.flexWrap = Wrap.Wrap;
            commands.style.marginBottom = 6;
            // 14.64.3 — the tab strip is now UN-SQUEEZABLE: fixed 40 px height,
            // no wrap, no shrink, keys hard-sized to 26 px and centered. minHeight
            // alone still lost to the surrounding flex math on short screens and
            // the keys overflowed the card's bottom edge.
            commands.style.flexShrink = 0;
            commands.style.height = 40;
            commands.style.minHeight = 40;
            commands.style.flexWrap = Wrap.NoWrap;
            commands.style.alignItems = Align.Center;
            commands.style.paddingTop = 0; commands.style.paddingBottom = 0;
            commands.style.paddingLeft = 6; commands.style.paddingRight = 6;
            L.ApplyDataCard(commands, L.Bezel);
            commands.Add(L.CommandButton("ALL BLOCKS", () => { _editorTab = 0; RebuildEditor(); }, null, _editorTab == 0));
            commands.Add(L.CommandButton("CATEGORIES", () => { _editorTab = 1; RebuildEditor(); }, null, _editorTab == 1));
            commands.Add(L.CommandButton("GROUPS", () => { _editorTab = 2; RebuildEditor(); }, null, _editorTab == 2));
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; commands.Add(spacer);
            commands.Add(L.CommandButton("CLOSE", CloseEditor, T.AccentRed));
            foreach (var child in commands.Children())
            {
                if (child is not Button key) continue;
                key.style.height = 26;
                key.style.minHeight = 26;
                key.style.flexShrink = 0;
                key.style.marginBottom = 0;
            }
            _editorCard.Add(commands);

            _pendingHint = new Label();
            _pendingHint.style.fontSize = 10;
            _pendingHint.style.marginBottom = 4;
            _pendingHint.style.color = new StyleColor(L.Caption);
            _editorCard.Add(_pendingHint);
            UpdatePendingHint();

            // Source list with a live filter (display toggled in place - no rebuild,
            // so the search field keeps focus while typing).
            var search = new TextField { value = string.Empty };
            search.style.marginBottom = 4;
            _editorCard.Add(search);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.flexGrow = 1;
            list.style.marginBottom = 8;
            _editorCard.Add(list);

            var rows = new List<(VisualElement row, string match)>();
            switch (_editorTab)
            {
                case 0:
                {
                    foreach (var block in _editorGrid.AllBlocks)
                    {
                        if (!GridMasterTerminal.IsListedBlock(block)) continue;
                        var cell = block.GridPos;
                        string name = $"{BlockDisplayName(block)}  [{cell.x},{cell.y},{cell.z}]";
                        var captured = block;
                        var row = SourceRow(name, GridMasterTerminal.CategoryLabel(block),
                            () => new SlotAssignment { kind = TargetKind.Block, block = captured });
                        rows.Add((row, name.ToLowerInvariant()));
                        list.Add(row);
                    }
                    break;
                }
                case 1:
                {
                    var counts = new Dictionary<string, int>();
                    foreach (var block in _editorGrid.AllBlocks)
                    {
                        if (!GridMasterTerminal.IsListedBlock(block)) continue;
                        string cat = GridMasterTerminal.CategoryLabel(block);
                        counts[cat] = counts.TryGetValue(cat, out int n) ? n + 1 : 1;
                    }
                    var cats = new List<string>(counts.Keys);
                    cats.Sort(System.StringComparer.OrdinalIgnoreCase);
                    foreach (var cat in cats)
                    {
                        string captured = cat;
                        var row = SourceRow(cat.ToUpperInvariant(), $"{counts[cat]} BLOCKS",
                            () => new SlotAssignment { kind = TargetKind.Category, key = captured });
                        rows.Add((row, cat.ToLowerInvariant()));
                        list.Add(row);
                    }
                    break;
                }
                default:
                {
                    var groups = GridMasterTerminal.PlayerGroups(_editorGrid);
                    if (groups.Count == 0)
                    {
                        var empty = new Label("No player groups yet — select blocks in the terminal list and create one.");
                        empty.style.fontSize = 11;
                        empty.style.color = new StyleColor(T.TextSecondary);
                        empty.style.marginTop = 10;
                        list.Add(empty);
                    }
                    foreach (var group in groups)
                    {
                        string captured = group.Key;
                        var row = SourceRow(group.Key.ToUpperInvariant(), $"GROUP · {group.Value.Count} BLOCKS",
                            () => new SlotAssignment { kind = TargetKind.Group, key = captured });
                        rows.Add((row, group.Key.ToLowerInvariant()));
                        list.Add(row);
                    }
                    break;
                }
            }

            search.RegisterValueChangedCallback(evt =>
            {
                string q = (evt.newValue ?? string.Empty).Trim().ToLowerInvariant();
                foreach (var (row, match) in rows)
                    row.style.display = string.IsNullOrEmpty(q) || match.Contains(q)
                        ? DisplayStyle.Flex : DisplayStyle.None;
            });

            // Slot strip.
            var slotsRow = new VisualElement();
            slotsRow.style.flexDirection = FlexDirection.Row;
            slotsRow.style.justifyContent = Justify.Center;
            var slots = SlotsFor(_editorGrid);
            for (int i = 0; i < SlotCount; i++)
            {
                int index = i;
                var a = slots[i];

                var box = new VisualElement();
                box.style.width = 92; box.style.height = 62;
                box.style.marginLeft = 3; box.style.marginRight = 3;
                box.style.paddingLeft = 5; box.style.paddingRight = 5;
                box.style.paddingTop = 4;
                box.style.backgroundColor = new StyleColor(a != null ? L.GlassDark : L.SegmentOff);
                T.Border(box, 1, a != null ? new Color(L.Phosphor.r, L.Phosphor.g, L.Phosphor.b, 0.45f) : T.BorderDim);
                T.Radius(box, 6);

                var num = new Label((index + 1).ToString());
                num.style.fontSize = 10;
                num.style.unityFontStyleAndWeight = FontStyle.Bold;
                num.style.color = new StyleColor(L.Caption);
                box.Add(num);

                var label = new Label(a != null ? a.DisplayName.ToUpperInvariant() : "EMPTY");
                label.style.fontSize = 9;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = new StyleColor(a != null ? new Color(0.88f, 0.93f, 0.85f) : new Color(0.38f, 0.42f, 0.40f));
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                box.Add(label);

                if (a != null)
                {
                    var act = new Label(a.actionLabel);
                    act.style.fontSize = 8;
                    act.style.color = new StyleColor(L.PhosphorDim);
                    act.style.overflow = Overflow.Hidden;
                    act.style.textOverflow = TextOverflow.Ellipsis;
                    act.style.whiteSpace = WhiteSpace.NoWrap;
                    box.Add(act);
                }

                box.RegisterCallback<ClickEvent>(_ => OnSlotClicked(index));
                _slotBoxes.Add(box);
                slotsRow.Add(box);
            }
            _editorCard.Add(slotsRow);
        }

        private static VisualElement SourceRow(string title, string subtitle,
            System.Func<SlotAssignment> makeTarget)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 26;
            row.style.paddingLeft = 8; row.style.paddingRight = 8;
            row.style.marginBottom = 2;
            row.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.025f));
            T.Radius(row, 4);

            var name = new Label(title);
            name.style.fontSize = 11;
            name.style.color = new StyleColor(new Color(0.85f, 0.90f, 0.84f));
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.flexShrink = 1;
            row.Add(name);

            var gap = new VisualElement(); gap.style.flexGrow = 1; row.Add(gap);

            var sub = new Label(subtitle);
            sub.style.fontSize = 9;
            sub.style.color = new StyleColor(L.Caption);
            row.Add(sub);

            // Drag OR click: pointer-down picks the target up; releasing over a slot
            // assigns it there, releasing anywhere else keeps it armed so a plain
            // click on a slot box finishes the assignment (trackpad-friendly).
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _pending = makeTarget();
                UpdatePendingHint();
                row.CapturePointer(evt.pointerId);
                ShowGhost(title, evt.position);
                evt.StopPropagation();
            });
            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (row.HasPointerCapture(evt.pointerId)) MoveGhost(evt.position);
            });
            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!row.HasPointerCapture(evt.pointerId)) return;
                row.ReleasePointer(evt.pointerId);
                HideGhost();
                int slot = SlotIndexAt(evt.position);
                if (slot >= 0) BeginAssign(slot);
            });

            return row;
        }

        private static void UpdatePendingHint()
        {
            if (_pendingHint == null) return;
            _pendingHint.text = _pending != null
                ? $"PICKED UP: {_pending.DisplayName.ToUpperInvariant()} — drop it on a slot (or click one) to choose its action."
                : "Drag a block, category or group onto a slot 1–9 below — each drop asks which action that key fires.";
            _pendingHint.style.color = new StyleColor(_pending != null ? L.Phosphor : L.Caption);
        }

        private static void ShowGhost(string text, Vector2 panelPos)
        {
            if (_editorOverlay == null) return;
            if (_dragGhost == null)
            {
                _dragGhost = new Label();
                _dragGhost.style.position = Position.Absolute;
                _dragGhost.style.fontSize = 10;
                _dragGhost.style.unityFontStyleAndWeight = FontStyle.Bold;
                _dragGhost.style.color = new StyleColor(Color.white);
                _dragGhost.style.backgroundColor = new StyleColor(new Color(0.10f, 0.14f, 0.11f, 0.95f));
                T.Border(_dragGhost, 1, L.Phosphor); T.Radius(_dragGhost, 4);
                _dragGhost.style.paddingLeft = 8; _dragGhost.style.paddingRight = 8;
                _dragGhost.style.paddingTop = 3; _dragGhost.style.paddingBottom = 3;
                _dragGhost.pickingMode = PickingMode.Ignore;
                _editorOverlay.Add(_dragGhost);
            }
            _dragGhost.text = text;
            MoveGhost(panelPos);
        }

        private static void MoveGhost(Vector2 panelPos)
        {
            if (_dragGhost == null || _editorOverlay == null) return;
            Vector2 local = _editorOverlay.WorldToLocal(panelPos);
            _dragGhost.style.left = local.x + 14;
            _dragGhost.style.top = local.y + 10;
        }

        private static void HideGhost()
        {
            _dragGhost?.RemoveFromHierarchy();
            _dragGhost = null;
        }

        private static int SlotIndexAt(Vector2 panelPos)
        {
            for (int i = 0; i < _slotBoxes.Count; i++)
                if (_slotBoxes[i].worldBound.Contains(panelPos)) return i;
            return -1;
        }

        private static void OnSlotClicked(int index)
        {
            if (_editorGrid == null) return;
            if (_pending != null) { BeginAssign(index); return; }

            var slots = SlotsFor(_editorGrid);
            var a = slots[index];
            if (a == null) return;

            // Filled slot, nothing picked up: offer CHANGE ACTION / CLEAR.
            ShowModal($"SLOT {index + 1} — {a.DisplayName.ToUpperInvariant()}", modal =>
            {
                modal.Add(ModalButton("CHANGE ACTION", () =>
                {
                    CloseModal();
                    _pending = new SlotAssignment { kind = a.kind, block = a.block, key = a.key };
                    BeginAssign(index);
                }));
                modal.Add(ModalButton("CLEAR SLOT", () =>
                {
                    slots[index] = null;
                    _lastSig = "\u0000";
                    CloseModal();
                    RebuildEditor();
                }));
                modal.Add(ModalButton("CANCEL", CloseModal));
            });
        }

        /// <summary>The drop landed on a slot: ask WHICH action the key should fire.</summary>
        private static void BeginAssign(int slotIndex)
        {
            if (_pending == null || _editorGrid == null) return;
            var target = _pending;
            var members = ResolveMembers(_editorGrid, target);
            var actions = ActionsFor(members);

            ShowModal($"SLOT {slotIndex + 1} — {target.DisplayName.ToUpperInvariant()}", modal =>
            {
                var caption = new Label($"WHICH ACTION SHOULD KEY {slotIndex + 1} FIRE?");
                caption.style.fontSize = 10;
                caption.style.color = new StyleColor(L.Caption);
                caption.style.marginBottom = 6;
                modal.Add(caption);

                foreach (var action in actions)
                {
                    var captured = action;
                    modal.Add(ModalButton(captured.label, () =>
                    {
                        var slots = SlotsFor(_editorGrid);
                        slots[slotIndex] = new SlotAssignment
                        {
                            kind = target.kind,
                            block = target.block,
                            key = target.key,
                            actionId = captured.id,
                            actionLabel = captured.label,
                        };
                        _pending = null;
                        _lastSig = "\u0000";
                        CloseModal();
                        RebuildEditor();
                    }));
                }
                modal.Add(ModalButton("CANCEL", () =>
                {
                    _pending = null;
                    CloseModal();
                    UpdatePendingHint();
                }));
            });
        }

        // ── tiny modal inside the editor overlay ──
        private static VisualElement _modal;

        private static void ShowModal(string title, System.Action<VisualElement> fill)
        {
            if (_editorOverlay == null) return;
            CloseModal();

            _modal = new VisualElement();
            _modal.style.position = Position.Absolute;
            _modal.style.left = 0; _modal.style.right = 0;
            _modal.style.top = 0; _modal.style.bottom = 0;
            _modal.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.45f));
            _modal.style.alignItems = Align.Center;
            _modal.style.justifyContent = Justify.Center;

            var box = new VisualElement();
            box.style.minWidth = 320; box.style.maxWidth = 440;
            box.style.backgroundColor = new StyleColor(L.Chassis);
            T.Border(box, 2, L.Bezel); T.Radius(box, 8);
            box.style.paddingLeft = 14; box.style.paddingRight = 14;
            box.style.paddingTop = 10; box.style.paddingBottom = 12;

            var header = new Label(title);
            header.style.fontSize = 12;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.color = new StyleColor(L.Phosphor);
            header.style.marginBottom = 8;
            box.Add(header);

            fill(box);
            _modal.Add(box);
            _editorOverlay.Add(_modal);
        }

        private static void CloseModal()
        {
            _modal?.RemoveFromHierarchy();
            _modal = null;
        }

        private static Button ModalButton(string text, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.height = 26;
            button.style.marginBottom = 3;
            button.style.fontSize = 10;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.backgroundColor = new StyleColor(L.GlassDark);
            button.style.color = new StyleColor(new Color(0.85f, 0.92f, 0.82f));
            T.Border(button, 1, T.BorderDim); T.Radius(button, 4);
            return button;
        }
    }
}
