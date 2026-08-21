// Assets/Scripts/VoxelEngine/UI/CanisterPressureHud.cs
//
// Persistent pocket-strip shown while the player carries pressurized canisters:
// a live field-pressure readout with amber/red warning states. Mounted into the
// persistent HUD layer like the other LCD HUDs.
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.UI
{
    public static class CanisterPressureHud
    {
        private static VisualElement _root;
        private static VisualElement _strip;
        private static Label _label;
        private static VisualElement _fill;
        private static bool _visible;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _strip != null && _strip.parent == uiRoot) return;
            _root = uiRoot;
            if (_strip != null) _strip.RemoveFromHierarchy();

            _strip = new VisualElement { name = "CanisterPressureHud" };
            _strip.style.position = Position.Absolute;
            _strip.style.left = Length.Percent(50f);
            _strip.style.bottom = 96;
            _strip.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f, 0f);
            _strip.style.flexDirection = FlexDirection.Row;
            _strip.style.alignItems = Align.Center;
            _strip.style.paddingTop = 4;
            _strip.style.paddingBottom = 4;
            _strip.style.paddingLeft = 10;
            _strip.style.paddingRight = 10;
            _strip.style.backgroundColor = new StyleColor(new Color(0.06f, 0.05f, 0.09f, 0.85f));
            UITheme.Radius(_strip, 8f);
            UITheme.Border(_strip, 1, new Color(0.58f, 0.34f, 0.95f, 0.55f));
            _strip.style.display = DisplayStyle.None;
            _strip.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_strip);

            _label = new Label();
            _label.style.fontSize = 10;
            _label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _label.style.letterSpacing = 1.2f;
            _label.style.color = new Color(0.72f, 0.9f, 1f);
            _label.style.marginRight = 8;
            _label.pickingMode = PickingMode.Ignore;
            _strip.Add(_label);

            var track = new VisualElement();
            track.style.width = 92;
            track.style.height = 8;
            track.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.065f));
            UITheme.Radius(track, 4f);
            UITheme.Border(track, 1, new Color(0.3f, 0.33f, 0.42f));
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            _strip.Add(track);

            _fill = new VisualElement();
            _fill.style.width = Length.Percent(100f);
            _fill.style.height = Length.Percent(100f);
            _fill.style.backgroundColor = new StyleColor(new Color(0.55f, 0.95f, 1f, 0.9f));
            _fill.pickingMode = PickingMode.Ignore;
            track.Add(_fill);
        }

        /// <summary>Update the strip. pressure01 = lowest carried canister field.</summary>
        public static void Show(float pressure01, string pressureText, int count)
        {
            if (_strip == null) return;
            _strip.style.display = DisplayStyle.Flex;
            _visible = true;

            Color c = pressure01 < 0.15f ? new Color(1f, 0.25f, 0.2f)
                    : pressure01 < 0.30f ? new Color(1f, 0.7f, 0.25f)
                    : new Color(0.55f, 0.95f, 1f);
            _fill.style.width = new StyleLength(new Length(Mathf.Clamp01(pressure01) * 100f, LengthUnit.Percent));
            _fill.style.backgroundColor = new StyleColor(c);
            _label.text = $"◉ CANISTER ×{count}  PRESSURE {pressureText}%";
            _label.style.color = new StyleColor(c);
        }

        public static void Hide()
        {
            if (_strip == null || !_visible) return;
            _visible = false;
            _strip.style.display = DisplayStyle.None;
        }
    }
}
