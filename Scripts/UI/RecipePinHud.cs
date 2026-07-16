// Assets/Scripts/VoxelEngine/UI/RecipePinHud.cs

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class RecipePinHud
    {
        public sealed class Pin
        {
            public string Key;
            public string OutputName;
            public Color Tint;
            public int OutputCount;
            public string Method;
            public readonly List<string> Inputs = new();
        }

        private const int MaxPins = 4;
        private static readonly List<Pin> Pins = new();
        private static VisualElement _root;
        private static VisualElement _list;

        public static bool IsPinned(string key) => !string.IsNullOrEmpty(key) && Pins.Any(p => p.Key == key);

        public static void Toggle(Pin pin)
        {
            if (pin == null || string.IsNullOrEmpty(pin.Key)) return;
            var existing = Pins.FirstOrDefault(p => p.Key == pin.Key);
            if (existing != null) Pins.Remove(existing);
            else
            {
                if (Pins.Count >= MaxPins) Pins.RemoveAt(0);
                Pins.Add(pin);
            }
            Rebuild();
        }

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root != null && _root.parent == uiRoot) { Rebuild(); return; }
            _root?.RemoveFromHierarchy();

            _root = new VisualElement { name = "RecipePinHud" };
            _root.style.position = Position.Absolute;
            _root.style.right = 14;
            _root.style.top = new StyleLength(new Length(42f, LengthUnit.Percent));
            _root.style.width = 280;
            _root.style.maxHeight = new StyleLength(new Length(44f, LengthUnit.Percent));
            _root.pickingMode = PickingMode.Position;
            uiRoot.Add(_root);

            _list = new VisualElement();
            _root.Add(_list);
            Rebuild();
        }

        private static void Rebuild()
        {
            if (_root == null || _list == null) return;
            _root.style.display = Pins.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _list.Clear();
            foreach (var pin in Pins.ToList()) _list.Add(Card(pin));
        }

        private static VisualElement Card(Pin pin)
        {
            var card = T.Card();
            card.style.marginBottom = 8;
            card.style.paddingTop = 9;
            card.style.paddingBottom = 9;
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = new StyleColor(pin.Tint);
            card.style.backgroundColor = new StyleColor(new Color(0.045f, 0.052f, 0.072f, 0.96f));

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            card.Add(header);

            var title = new Label($"{pin.OutputCount}x {pin.OutputName}");
            title.style.flexGrow = 1;
            title.style.color = new StyleColor(T.TextPrimary);
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);
            header.Add(T.SmallButton("×", () => Toggle(pin), T.AccentRed));

            var method = new Label(pin.Method);
            method.style.color = new StyleColor(T.AccentGold);
            method.style.fontSize = 9;
            method.style.marginTop = 2;
            card.Add(method);

            if (pin.Inputs.Count > 0)
            {
                var inputs = new Label(string.Join(" + ", pin.Inputs));
                inputs.style.color = new StyleColor(T.TextSecondary);
                inputs.style.fontSize = 10;
                inputs.style.whiteSpace = WhiteSpace.Normal;
                inputs.style.marginTop = 4;
                card.Add(inputs);
            }

            return card;
        }
    }
}
