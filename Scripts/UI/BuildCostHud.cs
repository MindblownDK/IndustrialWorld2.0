// Assets/Scripts/VoxelEngine/UI/BuildCostHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║           BUILD COST HUD — Bottom-Centre overlay               ║
// ║   Shown while holding Hammer with a family selected.           ║
// ║   Displays piece name and per-ingredient cost in rich colour.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class BuildCostHud
    {
        // ── State ──────────────────────────────────────────────────────
        private static VisualElement _root, _container, _card;
        private static Label         _nameLabel, _costLabel;

        // ── Mount ──────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            // Centre-bottom anchor.
            _container = new VisualElement { name = "BuildCostHud" };
            _container.style.position    = Position.Absolute;
            _container.style.bottom      = 90;
            _container.style.left        = 0;
            _container.style.right       = 0;
            _container.style.alignItems  = Align.Center;
            _container.pickingMode       = PickingMode.Ignore;
            _container.style.display     = DisplayStyle.None;
            uiRoot.Add(_container);

            // Card.
            _card = new VisualElement();
            _card.style.paddingTop    = 8;
            _card.style.paddingBottom = 8;
            _card.style.paddingLeft   = 20;
            _card.style.paddingRight  = 20;
            _card.style.backgroundColor = new StyleColor(new Color(T.BgDark.r, T.BgDark.g, T.BgDark.b, 0.92f));
            _card.style.alignItems    = Align.Center;
            T.Radius(_card, 8f);
            T.Border(_card, 1, T.BorderBright);
            _card.pickingMode = PickingMode.Ignore;
            _container.Add(_card);

            // Thin top accent stripe.
            var stripe = new VisualElement();
            stripe.style.height          = 2;
            stripe.style.alignSelf       = Align.Stretch;
            stripe.style.backgroundColor = new StyleColor(new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.40f));
            stripe.style.marginBottom    = 6;
            T.Radius(stripe, 1f);
            stripe.pickingMode = PickingMode.Ignore;
            _card.Add(stripe);

            _nameLabel = new Label("");
            _nameLabel.style.color                   = new StyleColor(T.TextPrimary);
            _nameLabel.style.fontSize                = 13;
            _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nameLabel.style.letterSpacing           = 0.8f;
            _nameLabel.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _nameLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_nameLabel);

            _costLabel = new Label("");
            _costLabel.style.fontSize        = 11;
            _costLabel.style.marginTop       = 3;
            _costLabel.style.unityTextAlign  = TextAnchor.MiddleCenter;
            _costLabel.enableRichText        = true;
            _costLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_costLabel);
        }

        // ── Tick ───────────────────────────────────────────────────────
        public static void Tick()
        {
            if (_container == null) return;

            var wheel = HammerBuildWheel.Instance;
            if (wheel == null || !wheel.ActiveFamily.HasValue)
            { _container.style.display = DisplayStyle.None; return; }

            var inv = UnityEngine.Object.FindAnyObjectByType<Inventory>();
            if (inv == null)
            { _container.style.display = DisplayStyle.None; return; }

            var stack = inv.ActiveStack;
            if (stack.IsEmpty || !(stack.item is Hammer))
            { _container.style.display = DisplayStyle.None; return; }

            var registry = BuildSystemV2.Instance?.registry;
            if (registry == null) { _container.style.display = DisplayStyle.None; return; }

            var def = registry.Get(wheel.ActiveFamily.Value);
            if (def == null) { _container.style.display = DisplayStyle.None; return; }

            _container.style.display = DisplayStyle.Flex;
            _nameLabel.text = def.displayName;

            // Build rich-text cost string.
            if (def.placeCost == null || def.placeCost.items == null || def.placeCost.items.Length == 0)
            {
                _costLabel.text              = "<color=#55CC77>Free</color>";
                _costLabel.style.color       = new StyleColor(T.AccentGreen);
                return;
            }

            var sb = new System.Text.StringBuilder();
            bool allAfford = true;
            bool first     = true;

            foreach (var ing in def.placeCost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (!first) sb.Append("   ");
                int  have    = inv.container.CountOf(ing.item);
                bool enough  = have >= ing.count;
                if (!enough) allAfford = false;
                string hex = enough ? "#66DD88" : "#DD5544";
                sb.Append($"<color={hex}>{ing.count}</color> <color=#A0A8B8>{ing.item.displayName}</color> <color={hex}>({have})</color>");
                first = false;
            }

            _costLabel.enableRichText = true;
            _costLabel.text           = sb.ToString();
            // Pulse the card border if we can/can't afford.
            T.Border(_card, 1, allAfford ? T.BorderBright : T.BorderRed);
        }
    }
}
