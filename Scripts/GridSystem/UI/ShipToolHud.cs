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

            // One rounded PILL bar holding every tool group, segmented; the active group is
            // filled cyan. Scroll cycles between them (handled in the cockpit).
            var pill = new VisualElement();
            pill.style.flexDirection = FlexDirection.Row;
            pill.style.alignItems = Align.Center;
            pill.style.backgroundColor = new StyleColor(new Color(0.06f, 0.07f, 0.10f, 0.92f));
            pill.style.paddingLeft = 5; pill.style.paddingRight = 5;
            pill.style.paddingTop = 5; pill.style.paddingBottom = 5;
            T.Border(pill, 1, T.BorderDim); T.Radius(pill, 22);   // big radius = pill shape
            pill.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < groups.Count; i++)
            {
                bool active = i == sel;
                var seg = new VisualElement();
                seg.style.flexDirection = FlexDirection.Column;
                seg.style.alignItems = Align.Center; seg.style.justifyContent = Justify.Center;
                seg.style.height = 34; seg.style.minWidth = 120;
                seg.style.paddingLeft = 14; seg.style.paddingRight = 14;
                seg.style.marginLeft = 2; seg.style.marginRight = 2;
                seg.style.backgroundColor = new StyleColor(active
                    ? new Color(0.18f, 0.72f, 0.88f, 0.95f) : new Color(0f, 0f, 0f, 0f));
                T.Radius(seg, 18);   // each segment is also pill-rounded

                var lbl = new Label(groups[i] == GridEntity.ToolGroup.Drill ? "⛏ Drill" : "🔫 Weapon");
                lbl.style.unityFontStyleAndWeight = FontStyle.Bold; lbl.style.fontSize = 13;
                lbl.style.color = new StyleColor(active ? Color.white : new Color(0.62f, 0.67f, 0.74f));
                seg.Add(lbl);

                if (active && groups[i] == GridEntity.ToolGroup.Drill)
                {
                    var hint = new Label("LMB mine · RMB void");
                    hint.style.fontSize = 9;
                    hint.style.color = new StyleColor(new Color(0.95f, 0.98f, 1f, 0.95f));
                    seg.Add(hint);
                }
                pill.Add(seg);
            }

            // Scroll hint shown to the right of the pill (only when there's more than one group).
            if (groups.Count > 1)
            {
                var scrollHint = new Label("scroll ↕");
                scrollHint.style.fontSize = 10; scrollHint.style.marginLeft = 8;
                scrollHint.style.color = new StyleColor(new Color(0.55f, 0.6f, 0.68f));
                scrollHint.style.unityTextAlign = TextAnchor.MiddleCenter;
                pill.Add(scrollHint);
            }

            _bar.Add(pill);
        }
    }
}
