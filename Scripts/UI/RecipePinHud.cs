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
        private const string PrefKey = "IndustrialWorld.RecipePins";
        private static readonly List<Pin> Pins = new();
        private static VisualElement _root;
        private static VisualElement _list;
        private static bool _loaded;

        [System.Serializable]
        private sealed class PinSaveList { public List<PinSave> pins = new(); }

        [System.Serializable]
        private sealed class PinSave
        {
            public string key;
            public string outputName;
            public float r, g, b, a;
            public int outputCount;
            public string method;
            public List<string> inputs = new();
        }

        public static bool IsPinned(string key) => !string.IsNullOrEmpty(key) && Pins.Any(p => p.Key == key);

        public static void Toggle(Pin pin)
        {
            if (pin == null || string.IsNullOrEmpty(pin.Key)) return;
            LoadPins();
            var existing = Pins.FirstOrDefault(p => p.Key == pin.Key);
            if (existing != null) Pins.Remove(existing);
            else
            {
                if (Pins.Count >= MaxPins) Pins.RemoveAt(0);
                Pins.Add(pin);
            }
            SavePins();
            Rebuild();
        }

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            LoadPins();
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

        private static void LoadPins()
        {
            if (_loaded) return;
            _loaded = true;
            Pins.Clear();
            string json = PlayerPrefs.GetString(PrefKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                var saved = JsonUtility.FromJson<PinSaveList>(json);
                if (saved?.pins == null) return;
                foreach (var pin in saved.pins.Take(MaxPins))
                {
                    if (string.IsNullOrWhiteSpace(pin.key)) continue;
                    var restored = new Pin
                    {
                        Key = pin.key,
                        OutputName = pin.outputName,
                        Tint = new Color(pin.r, pin.g, pin.b, pin.a),
                        OutputCount = Mathf.Max(1, pin.outputCount),
                        Method = pin.method ?? string.Empty
                    };
                    restored.Inputs.AddRange(pin.inputs ?? new List<string>());
                    Pins.Add(restored);
                }
            }
            catch
            {
                Pins.Clear();
                PlayerPrefs.DeleteKey(PrefKey);
            }
        }

        private static void SavePins()
        {
            var list = new PinSaveList();
            foreach (var pin in Pins.Take(MaxPins))
            {
                list.pins.Add(new PinSave
                {
                    key = pin.Key,
                    outputName = pin.OutputName,
                    r = pin.Tint.r,
                    g = pin.Tint.g,
                    b = pin.Tint.b,
                    a = pin.Tint.a,
                    outputCount = pin.OutputCount,
                    method = pin.Method,
                    inputs = pin.Inputs.ToList()
                });
            }
            PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(list));
            PlayerPrefs.Save();
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
