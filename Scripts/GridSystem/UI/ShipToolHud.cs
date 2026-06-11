// Assets/Scripts/VoxelEngine/GridSystem/UI/ShipToolHud.cs
//
// Bottom-centre tool selector shown while piloting — like the Space Engineers
// toolbar. Highlights the currently-selected fire tool (Drill / Weapon N) that
// left-click activates; scroll cycles between them.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.GridSystem.UI
{
    public static class ShipToolHud
    {
        private static VisualElement _root, _bar;
        // Cache the last-rendered signature so we only rebuild the toolbar when the tool
        // set or the selection actually changes — rebuilding every frame caused a flicker.
        private static string _lastSig = "\u0000";

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _bar != null && _bar.parent == uiRoot) return;
            _root = uiRoot;
            if (_bar != null) _bar.RemoveFromHierarchy();

            _bar = new VisualElement { name = "ShipToolHud" };
            _bar.style.position = Position.Absolute;
            _bar.style.bottom = 24; _bar.style.left = 0; _bar.style.right = 0;
            _bar.style.flexDirection = FlexDirection.Row;
            _bar.style.justifyContent = Justify.Center;
            _bar.style.display = DisplayStyle.None;
            _bar.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_bar);
            _lastSig = "\u0000"; // force a rebuild on the next Tick after (re)mount
        }

        public static void Tick()
        {
            if (_bar == null) return;
            var seat = GridCockpit.ActivePilotSeat;
            bool show = seat != null && seat.Grid != null && !VoxelEngine.UI.UIState.IsBlocking;
            _bar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) { _lastSig = "\u0000"; return; }

            var grid = seat.Grid;
            // GROUPED toolbar: one entry per tool TYPE (all drills = one "Drill" group, all
            // weapons = one "Weapon" group), Space-Engineers style.
            var groups = grid.GetToolGroups();
            if (groups.Count == 0) { if (_bar.childCount > 0) _bar.Clear(); _lastSig = "empty"; return; }
            int sel = ((grid.SelectedToolIndex % groups.Count) + groups.Count) % groups.Count;

            // Build a cheap signature; bail out (no rebuild → no flicker) if nothing changed.
            var sb = new System.Text.StringBuilder();
            sb.Append(sel).Append('|');
            foreach (var g in groups) sb.Append((int)g);
            string sig = sb.ToString();
            if (sig == _lastSig) return;
            _lastSig = sig;

            _bar.Clear();

            for (int i = 0; i < groups.Count; i++)
            {
                bool active = i == sel;
                var slot = new VisualElement();
                slot.style.width = 150; slot.style.height = 40;
                slot.style.marginLeft = 4; slot.style.marginRight = 4;
                slot.style.flexDirection = FlexDirection.Column;
                slot.style.alignItems = Align.Center; slot.style.justifyContent = Justify.Center;
                slot.style.backgroundColor = new StyleColor(active
                    ? new Color(0.18f, 0.72f, 0.88f, 0.85f) : new Color(0.08f, 0.09f, 0.12f, 0.85f));
                T.Border(slot, active ? 2 : 1, active ? T.AccentCyan : T.BorderDim); T.Radius(slot, 6);

                var lbl = new Label(groups[i] == GridEntity.ToolGroup.Drill ? "⛏ Drill" : "🔫 Weapon");
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold; lbl.style.fontSize = 13;
                lbl.style.color = new StyleColor(active ? Color.white : new Color(0.7f,0.74f,0.8f));
                slot.Add(lbl);

                // Show the LMB/RMB hint for the drill group when it's selected.
                if (active && groups[i] == GridEntity.ToolGroup.Drill)
                {
                    var hint = new Label("LMB mine · RMB void");
                    hint.style.fontSize = 9;
                    hint.style.color = new StyleColor(new Color(0.92f, 0.96f, 1f, 0.9f));
                    slot.Add(hint);
                }
                _bar.Add(slot);
            }
        }
    }
}
