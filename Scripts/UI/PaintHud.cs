// Assets/Scripts/VoxelEngine/UI/PaintHud.cs
//
// Compact finish readout shown while the Paint Tool is held.
// Shows selected finish name + swatch. Cycles are reflected immediately.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class PaintHud
    {
        private static VisualElement _root, _card, _swatch;
        private static Label _title, _detail;
        private static bool _visible;
        private static string _last;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _card != null && _card.parent == uiRoot) return;
            _root = uiRoot;
            if (_card != null) _card.RemoveFromHierarchy();

            _card = new VisualElement { name = "PaintHud" };
            _card.style.position = Position.Absolute;
            _card.style.left = 18;
            _card.style.bottom = 16 + RustStyleHud.TOTAL_HEIGHT + 12;
            _card.style.width = 220;
            _card.style.paddingLeft = 12;
            _card.style.paddingRight = 12;
            _card.style.paddingTop = 10;
            _card.style.paddingBottom = 10;
            _card.style.flexDirection = FlexDirection.Row;
            _card.style.alignItems = Align.Center;
            _card.style.backgroundColor = new StyleColor(new Color(0.03f, 0.04f, 0.06f, 0.92f));
            _card.style.opacity = 0f;
            _card.pickingMode = PickingMode.Ignore;
            T.Radius(_card, 8f);
            T.Border(_card, 1f, new Color(T.BorderBright.r, T.BorderBright.g, T.BorderBright.b, 0.7f));
            uiRoot.Add(_card);

            _swatch = new VisualElement();
            _swatch.style.width = 28;
            _swatch.style.height = 28;
            _swatch.style.marginRight = 10;
            _swatch.pickingMode = PickingMode.Ignore;
            T.Radius(_swatch, 6f);
            T.Border(_swatch, 1f, new Color(1f, 1f, 1f, 0.25f));
            _card.Add(_swatch);

            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.pickingMode = PickingMode.Ignore;
            _card.Add(col);

            _title = new Label("PAINT");
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.fontSize = 10;
            _title.style.color = new StyleColor(T.AccentCyan);
            _title.style.marginBottom = 2;
            _title.pickingMode = PickingMode.Ignore;
            col.Add(_title);

            _detail = new Label("Industrial Grey");
            _detail.style.fontSize = 13;
            _detail.style.color = Color.white;
            _detail.style.whiteSpace = WhiteSpace.Normal;
            _detail.pickingMode = PickingMode.Ignore;
            col.Add(_detail);

            var hint = new Label("LMB paint · RMB cycle finish · Shift+RMB clear");
            hint.style.fontSize = 9;
            hint.style.color = new StyleColor(new Color(0.7f, 0.74f, 0.8f));
            hint.style.marginTop = 3;
            hint.pickingMode = PickingMode.Ignore;
            col.Add(hint);

            _visible = false;
            _last = null;
        }

        public static void Tick(Inventory inventory)
        {
            if (_card == null) return;
            bool holding = inventory != null
                && !inventory.ActiveStack.IsEmpty
                && inventory.ActiveStack.item is PaintToolItem;

            if (!holding)
            {
                if (_visible) SetVisible(false);
                return;
            }

            PaintToolItem.EnsureSelected();
            string key = PaintToolItem.SelectedName + "|" + (int)PaintToolItem.SelectedFinish;
            if (!_visible) SetVisible(true);
            if (key != _last)
            {
                _last = key;
                _detail.text = PaintToolItem.SelectedName;
                _swatch.style.backgroundColor = new StyleColor(PaintToolItem.SelectedColor);
            }
        }

        private static void SetVisible(bool on)
        {
            _visible = on;
            if (_card == null) return;
            _card.style.opacity = on ? 1f : 0f;
            if (!on) _last = null;
        }
    }
}
