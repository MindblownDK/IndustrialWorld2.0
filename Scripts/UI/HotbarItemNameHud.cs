// Assets/Scripts/VoxelEngine/UI/HotbarItemNameHud.cs
//
// Premium Minecraft-style held-item readout. It appears briefly above the hotbar
// whenever the active slot/item changes, making scroll-wheel selection legible
// without opening the inventory.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class HotbarItemNameHud
    {
        private const float ShowSeconds = 2.15f;
        private const float FadeSeconds = 0.38f;

        private static VisualElement _root;
        private static VisualElement _card;
        private static Label _name;
        private static Label _slot;
        private static int _lastIndex = -1;
        private static ItemDefinition _lastItem;
        private static bool _observed;
        private static float _shownAt = -999f;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _card != null && _card.parent == uiRoot) return;

            _root = uiRoot;
            if (_card != null) _card.RemoveFromHierarchy();

            _card = new VisualElement { name = "HotbarItemNameHud" };
            _card.style.position = Position.Absolute;
            _card.style.left = Length.Percent(0);
            _card.style.right = Length.Percent(0);
            _card.style.bottom = 86;
            _card.style.alignItems = Align.Center;
            _card.style.justifyContent = Justify.Center;
            _card.style.opacity = 0f;
            _card.style.display = DisplayStyle.None;
            _card.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_card);

            var plate = new VisualElement();
            plate.style.flexDirection = FlexDirection.Row;
            plate.style.alignItems = Align.Center;
            plate.style.backgroundColor = new StyleColor(new Color(0.025f, 0.032f, 0.05f, 0.92f));
            plate.style.paddingLeft = 12;
            plate.style.paddingRight = 14;
            plate.style.paddingTop = 7;
            plate.style.paddingBottom = 7;
            T.Radius(plate, 7f);
            T.Border(plate, 1f, new Color(T.BorderBright.r, T.BorderBright.g, T.BorderBright.b, 0.72f));
            plate.pickingMode = PickingMode.Ignore;
            _card.Add(plate);

            _slot = new Label("1");
            _slot.style.minWidth = 17;
            _slot.style.height = 17;
            _slot.style.marginRight = 8;
            _slot.style.fontSize = 9;
            _slot.style.unityTextAlign = TextAnchor.MiddleCenter;
            _slot.style.unityFontStyleAndWeight = FontStyle.Bold;
            _slot.style.color = new StyleColor(T.BgPanel);
            _slot.style.backgroundColor = new StyleColor(T.AccentAmber);
            T.Radius(_slot, 4f);
            _slot.pickingMode = PickingMode.Ignore;
            plate.Add(_slot);

            _name = new Label();
            _name.style.fontSize = 14;
            _name.style.unityFontStyleAndWeight = FontStyle.Bold;
            _name.style.color = new StyleColor(T.TextPrimary);
            _name.style.maxWidth = 460;
            _name.style.whiteSpace = WhiteSpace.NoWrap;
            _name.style.overflow = Overflow.Hidden;
            _name.style.textOverflow = TextOverflow.Ellipsis;
            _name.pickingMode = PickingMode.Ignore;
            plate.Add(_name);
        }

        public static void Tick(Inventory inventory)
        {
            if (_card == null) return;
            if (VoxelEngine.GridSystem.GridCockpit.AnyPilotSeatActive)
            {
                Hide();
                return;
            }
            if (inventory == null) return;

            int index = inventory.activeHotbarIndex;
            var stack = inventory.ActiveStack;
            ItemDefinition item = stack != null && !stack.IsEmpty ? stack.item : null;

            if (!_observed)
            {
                _observed = true;
                _lastIndex = index;
                _lastItem = item;
            }
            else if (index != _lastIndex || item != _lastItem)
            {
                _lastIndex = index;
                _lastItem = item;
                if (item != null)
                {
                    _slot.text = index == 9 ? "0" : (index + 1).ToString();
                    _name.text = item.displayName;
                    _shownAt = Time.unscaledTime;
                    _card.style.display = DisplayStyle.Flex;
                    _card.style.opacity = 0f;
                }
                else
                {
                    Hide();
                }
            }

            if (_card.style.display == DisplayStyle.None) return;
            float age = Time.unscaledTime - _shownAt;
            if (age >= ShowSeconds)
            {
                Hide();
                return;
            }

            float opacity = age < 0.14f
                ? Mathf.Clamp01(age / 0.14f)
                : age > ShowSeconds - FadeSeconds
                    ? 1f - Mathf.Clamp01((age - (ShowSeconds - FadeSeconds)) / FadeSeconds)
                    : 1f;
            _card.style.opacity = opacity;
        }

        private static void Hide()
        {
            if (_card == null) return;
            _card.style.display = DisplayStyle.None;
            _card.style.opacity = 0f;
        }
    }
}
