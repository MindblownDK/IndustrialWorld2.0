// Assets/Scripts/VoxelEngine/UI/HotbarItemNameHud.cs
//
// Brief held-item identification readout. Styled as a compact fitted LCD label
// so scrolling the hotbar feels like reading a tool belt/field console, not a
// floating generic notification.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;

namespace VoxelEngine.UI
{
    public static class HotbarItemNameHud
    {
        private const float ShowSeconds = 2.15f;
        private const float FadeSeconds = 0.38f;

        private static VisualElement _root;
        private static VisualElement _card;
        private static VisualElement _bezel;
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
            _card.style.bottom = 88;
            _card.style.alignItems = Align.Center;
            _card.style.justifyContent = Justify.Center;
            _card.style.opacity = 0f;
            _card.style.display = DisplayStyle.None;
            _card.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_card);

            _bezel = new VisualElement { name = "HeldItemLcdBezel" };
            _bezel.style.flexDirection = FlexDirection.Row;
            _bezel.style.alignItems = Align.Center;
            _bezel.style.paddingLeft = 5;
            _bezel.style.paddingRight = 5;
            _bezel.style.paddingTop = 5;
            _bezel.style.paddingBottom = 5;
            _bezel.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyChassis(_bezel, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.95f), 2f);
            _card.Add(_bezel);

            _slot = new Label("1");
            _slot.style.width = 25;
            _slot.style.height = 30;
            _slot.style.marginRight = 5;
            _slot.style.fontSize = 11;
            _slot.style.letterSpacing = 0.5f;
            _slot.style.unityTextAlign = TextAnchor.MiddleCenter;
            _slot.style.unityFontStyleAndWeight = FontStyle.Bold;
            _slot.style.color = new StyleColor(LcdHudTheme.Phosphor);
            _slot.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
            _slot.pickingMode = PickingMode.Ignore;
            UITheme.Radius(_slot, 1f);
            UITheme.Border(_slot, 1f, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.9f));
            _bezel.Add(_slot);

            var screen = new VisualElement { name = "HeldItemLcdScreen" };
            screen.style.minWidth = 190;
            screen.style.maxWidth = 420;
            screen.style.height = 30;
            screen.style.paddingLeft = 8;
            screen.style.paddingRight = 8;
            screen.style.overflow = Overflow.Hidden;
            screen.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyScreen(screen, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.88f), 1f);
            LcdHudTheme.AddScanlines(screen, 2, top: 8f, spacing: 12f);
            _bezel.Add(screen);

            var caption = LcdHudTheme.CaptionLabel("HELD ITEM");
            caption.style.position = Position.Absolute;
            caption.style.left = 8;
            caption.style.top = 3;
            screen.Add(caption);

            _name = new Label();
            _name.style.position = Position.Absolute;
            _name.style.left = 8;
            _name.style.right = 8;
            _name.style.bottom = 2;
            _name.style.fontSize = 12;
            _name.style.letterSpacing = 0.45f;
            _name.style.unityFontStyleAndWeight = FontStyle.Bold;
            _name.style.color = new StyleColor(LcdHudTheme.Phosphor);
            _name.style.whiteSpace = WhiteSpace.NoWrap;
            _name.style.overflow = Overflow.Hidden;
            _name.style.textOverflow = TextOverflow.Ellipsis;
            _name.pickingMode = PickingMode.Ignore;
            screen.Add(_name);
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
