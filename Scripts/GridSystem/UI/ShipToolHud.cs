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
        }

        public static void Tick()
        {
            if (_bar == null) return;
            var seat = GridCockpit.ActivePilotSeat;
            bool show = seat != null && seat.Grid != null && !VoxelEngine.UI.UIState.IsBlocking;
            _bar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            _bar.Clear();
            var grid = seat.Grid;
            var tools = grid.GetFireTools();
            if (tools.Count == 0) return;
            int sel = ((grid.SelectedToolIndex % tools.Count) + tools.Count) % tools.Count;

            for (int i = 0; i < tools.Count; i++)
            {
                bool active = i == sel;
                var slot = new VisualElement();
                slot.style.width = 120; slot.style.height = 34;
                slot.style.marginLeft = 4; slot.style.marginRight = 4;
                slot.style.alignItems = Align.Center; slot.style.justifyContent = Justify.Center;
                slot.style.backgroundColor = new StyleColor(active
                    ? new Color(0.18f, 0.72f, 0.88f, 0.85f) : new Color(0.08f, 0.09f, 0.12f, 0.85f));
                T.Border(slot, active ? 2 : 1, active ? T.AccentCyan : T.BorderDim); T.Radius(slot, 6);
                var lbl = new Label(tools[i] is GridDrill ? "⛏ Drill" : "🔫 Weapon");
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold; lbl.style.fontSize = 12;
                lbl.style.color = new StyleColor(active ? Color.white : new Color(0.7f,0.74f,0.8f));
                slot.Add(lbl);
                _bar.Add(slot);
            }
        }
    }
}
