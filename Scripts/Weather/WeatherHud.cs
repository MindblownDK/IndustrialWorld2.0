// Assets/Scripts/VoxelEngine/Weather/WeatherHud.cs
//
// Small weather indicator in the top-left corner showing current weather state.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.Weather
{
    public static class WeatherHud
    {
        private static VisualElement _root;
        private static VisualElement _container;
        private static Label _iconLabel;
        private static Label _textLabel;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            _container = new VisualElement { name = "WeatherHud" };
            _container.style.position = Position.Absolute;
            _container.style.top = 16;
            _container.style.left = 16;
            _container.style.flexDirection = FlexDirection.Row;
            _container.style.alignItems = Align.Center;
            _container.style.paddingLeft = 8; _container.style.paddingRight = 10;
            _container.style.paddingTop = 4; _container.style.paddingBottom = 4;
            _container.style.backgroundColor = new StyleColor(new Color(0.06f, 0.07f, 0.09f, 0.70f));
            R(_container, 12);
            _container.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_container);

            _iconLabel = new Label("☀");
            _iconLabel.style.fontSize = 16;
            _iconLabel.style.color = Color.white;
            _iconLabel.style.marginRight = 6;
            _iconLabel.pickingMode = PickingMode.Ignore;
            _container.Add(_iconLabel);

            _textLabel = new Label("Clear");
            _textLabel.style.fontSize = 11;
            _textLabel.style.color = new StyleColor(new Color(0.80f, 0.82f, 0.88f));
            _textLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _textLabel.pickingMode = PickingMode.Ignore;
            _container.Add(_textLabel);
        }

        public static void Tick()
        {
            var wm = WeatherManager.Instance;
            if (wm == null || _iconLabel == null) return;

            var state = wm.TransitionProgress >= 0.5f ? wm.TargetState : wm.CurrentState;

            (_iconLabel.text, _textLabel.text) = state switch
            {
                WeatherState.Clear     => ("☀", "Clear"),
                WeatherState.Overcast  => ("☁", "Overcast"),
                WeatherState.LightRain => ("🌧", "Light Rain"),
                WeatherState.HeavyRain => ("⛈", "Heavy Rain"),
                WeatherState.Snow      => ("❄", "Snow"),
                WeatherState.Blizzard  => ("🌨", "Blizzard"),
                _ => ("☀", "Clear")
            };
        }

        private static void R(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = r; v.style.borderTopRightRadius = r;
            v.style.borderBottomLeftRadius = r; v.style.borderBottomRightRadius = r;
        }
    }
}
