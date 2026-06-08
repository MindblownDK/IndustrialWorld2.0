// Assets/Scripts/VoxelEngine/UI/InteractionHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          INTERACTION HUD — Center-screen context prompt        ║
// ║   Visible when looking at interactive objects (cockpits, etc). ║
// ║   Displays "Press [Key] to [Action]" with premium styling.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class InteractionHud
    {
        private static VisualElement _root, _box;
        private static Label         _keyLabel, _actionLabel;
        private static bool          _isVisible;
        private static int           _lastFrame;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _box != null && _box.parent == uiRoot) return;
            _root = uiRoot;
            if (_box != null) _box.RemoveFromHierarchy();

            // Container — centered horizontally, slightly below screen center.
            _box = new VisualElement { name = "InteractionHud" };
            _box.style.position      = Position.Absolute;
            _box.style.left          = Length.Percent(0);
            _box.style.right         = Length.Percent(0);
            _box.style.bottom        = Length.Percent(38);
            _box.style.flexDirection = FlexDirection.Row;
            _box.style.justifyContent = Justify.Center;
            _box.style.alignItems    = Align.Center;
            _box.pickingMode         = PickingMode.Ignore;
            _box.style.display       = DisplayStyle.None;
            uiRoot.Add(_box);

            // The prompt "card".
            var card = new VisualElement();
            card.style.flexDirection   = FlexDirection.Row;
            card.style.alignItems      = Align.Center;
            card.style.backgroundColor = new StyleColor(new Color(0.02f, 0.03f, 0.05f, 0.82f));
            card.style.paddingLeft     = 10;
            card.style.paddingRight    = 14;
            card.style.paddingTop      = 6;
            card.style.paddingBottom   = 6;
            T.Radius(card, 6);
            T.Border(card, 1, new Color(1, 1, 1, 0.15f));
            _box.Add(card);

            // Key cap.
            _keyLabel = new Label("E");
            _keyLabel.style.color                   = Color.white;
            _keyLabel.style.fontSize                = 13;
            _keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _keyLabel.style.backgroundColor         = new StyleColor(new Color(0.20f, 0.55f, 0.95f, 0.90f));
            _keyLabel.style.paddingLeft             = 8;
            _keyLabel.style.paddingRight            = 8;
            _keyLabel.style.paddingTop              = 2;
            _keyLabel.style.paddingBottom           = 2;
            _keyLabel.style.marginRight             = 10;
            T.Radius(_keyLabel, 4);
            card.Add(_keyLabel);

            // Action text.
            _actionLabel = new Label("INTERACT");
            _actionLabel.style.color                   = new StyleColor(T.TextPrimary);
            _actionLabel.style.fontSize                = 12;
            _actionLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _actionLabel.style.letterSpacing           = 1.1f;
            card.Add(_actionLabel);
        }

        public static void Show(string key, string action)
        {
            if (_box == null) return;
            _lastFrame = Time.frameCount;
            
            // Format key string (e.g. "Digit1" -> "1").
            string displayKey = key;
            if (displayKey.StartsWith("Digit")) displayKey = displayKey.Substring(5);
            else if (displayKey.StartsWith("Alpha")) displayKey = displayKey.Substring(5);
            
            _keyLabel.text = displayKey.ToUpper();
            _actionLabel.text = action.ToUpper();
            
            if (!_isVisible)
            {
                _box.style.display = DisplayStyle.Flex;
                _isVisible = true;
                
                // Subtle entrance animation.
                _box.style.opacity = 0f;
                _box.style.scale   = new StyleScale(new Scale(new Vector3(0.9f, 0.9f, 1f)));
                _box.schedule.Execute(() => {
                    _box.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "opacity", "scale" };
                    _box.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new TimeValue(0.12f, TimeUnit.Second) };
                    _box.style.opacity = 1f;
                    _box.style.scale   = new StyleScale(new Scale(Vector3.one));
                }).ExecuteLater(10);
            }
        }

        public static void Tick()
        {
            if (_isVisible && Time.frameCount > _lastFrame + 1)
            {
                Hide();
            }
        }

        public static void Hide()
        {
            if (_box == null || !_isVisible) return;
            _box.style.display = DisplayStyle.None;
            _isVisible = false;
        }
    }
}
