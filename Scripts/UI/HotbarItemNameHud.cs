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
        private const int LayoutRevision = 3;

        private static VisualElement _root;
        private static VisualElement _card;
        private static VisualElement _bezel;
        private static Label _name;
        private static int _lastIndex = -1;
        private static ItemDefinition _lastItem;
        private static bool _observed;
        private static float _shownAt = -999f;
        private static int _mountedRevision;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _card != null && _card.parent == uiRoot
                && _mountedRevision == LayoutRevision) return;

            _root = uiRoot;
            if (_card != null) _card.RemoveFromHierarchy();
            // Domain-reload-disabled play sessions may retain an older readout under
            // a previous UI layer. Scrub the complete document tree, not only the
            // current HUD layer, before mounting our one authoritative readout.
            RemoveStaleReadouts(uiRoot);
            _mountedRevision = LayoutRevision;

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

            var screen = new VisualElement { name = "HeldItemLcdScreen" };
            screen.style.minWidth = 220;
            screen.style.maxWidth = 440;
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

            _name = new Label { name = "HeldItemLcdName" };
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

        private static void RemoveStaleReadouts(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            var documentRoot = uiRoot;
            while (documentRoot.parent != null) documentRoot = documentRoot.parent;

            // Names used by the old plain readout and the first LCD pass. Removing
            // their parent cards guarantees a legacy label cannot sit behind the new
            // name even when a UI document survives a no-domain-reload Play session.
            string[] staleNames =
            {
                "HotbarItemNameHud",
                "HotbarItemName",
                "HotbarItemReadout",
                "HotbarItemNameReadout",
                "HeldItemNameHud",
                "HeldItemReadout",
                "HeldItemLcdBezel"
            };
            for (int i = 0; i < staleNames.Length; i++)
            {
                VisualElement stale;
                while ((stale = documentRoot.Q<VisualElement>(staleNames[i])) != null)
                    stale.RemoveFromHierarchy();
            }
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
