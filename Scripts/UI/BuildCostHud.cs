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
        private static Label         _nameLabel, _planLabel, _costLabel;
        private static bool _isVisible;
        private static int _visibilityToken;

        // ── Mount ──────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            _visibilityToken++;
            _isVisible = false;
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
            _container.style.opacity     = 0f;
            _container.style.scale       = new StyleScale(new Scale(new Vector3(0.96f, 0.96f, 1f)));
            _container.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName>
                { "opacity", "scale" };
            _container.style.transitionDuration = new System.Collections.Generic.List<TimeValue>
                { new TimeValue(0.13f, TimeUnit.Second), new TimeValue(0.13f, TimeUnit.Second) };
            _container.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction>
                { new EasingFunction(EasingMode.EaseOutCubic), new EasingFunction(EasingMode.EaseOutCubic) };
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

            _planLabel = new Label("");
            _planLabel.style.color                   = new StyleColor(T.TextSecondary);
            _planLabel.style.fontSize                = 10;
            _planLabel.style.marginTop               = 3;
            _planLabel.style.unityTextAlign           = TextAnchor.MiddleCenter;
            _planLabel.style.whiteSpace              = WhiteSpace.Normal;
            _planLabel.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName>
                { "color" };
            _planLabel.style.transitionDuration = new System.Collections.Generic.List<TimeValue>
                { new TimeValue(0.10f, TimeUnit.Second) };
            _planLabel.style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction>
                { new EasingFunction(EasingMode.EaseOutCubic) };
            _planLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_planLabel);

            _costLabel = new Label("");
            _costLabel.style.fontSize        = 11;
            _costLabel.style.marginTop       = 3;
            _costLabel.style.unityTextAlign  = TextAnchor.MiddleCenter;
            _costLabel.style.whiteSpace      = WhiteSpace.Normal;
            _costLabel.enableRichText        = true;
            _costLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_costLabel);
        }

        private static void ShowContainer()
        {
            if (_container == null) return;
            if (_isVisible)
            {
                _container.style.display = DisplayStyle.Flex;
                return;
            }

            _isVisible = true;
            int token = ++_visibilityToken;
            VisualElement container = _container;
            container.style.display = DisplayStyle.Flex;
            container.style.opacity = 0f;
            container.style.scale = new StyleScale(new Scale(new Vector3(0.96f, 0.96f, 1f)));
            container.schedule.Execute(() =>
            {
                if (_container != container || !_isVisible || token != _visibilityToken) return;
                container.style.opacity = 1f;
                container.style.scale = new StyleScale(new Scale(Vector3.one));
            }).ExecuteLater(10);
        }

        private static void HideContainer()
        {
            if (_container == null || !_isVisible) return;
            _isVisible = false;
            int token = ++_visibilityToken;
            VisualElement container = _container;
            container.style.opacity = 0f;
            container.style.scale = new StyleScale(new Scale(new Vector3(0.96f, 0.96f, 1f)));
            container.schedule.Execute(() =>
            {
                if (_container != container || _isVisible || token != _visibilityToken) return;
                container.style.display = DisplayStyle.None;
            }).ExecuteLater(140);
        }

        // ── Tick ───────────────────────────────────────────────────────
        public static void Tick()
        {
            if (_container == null) return;

            var wheel = HammerBuildWheel.Instance;
            if (wheel == null || !wheel.ActiveFamily.HasValue)
            { HideContainer(); return; }

            var inv = UnityEngine.Object.FindAnyObjectByType<Inventory>();
            if (inv == null)
            { HideContainer(); return; }

            var stack = inv.ActiveStack;
            if (stack.IsEmpty || !(stack.item is Hammer))
            { HideContainer(); return; }

            var buildSystem = BuildSystemV2.Instance;
            var registry = buildSystem != null ? buildSystem.registry : null;
            if (registry == null) { HideContainer(); return; }

            BuildFamily family = wheel.ActiveFamily.Value;
            var def = registry.Get(family);
            if (def == null) { HideContainer(); return; }

            ShowContainer();
            _nameLabel.text = def.displayName;

            var pattern = VoxelEngine.Building.PlacementOptions.Pattern;
            bool supported = BuildSystemV2.SupportsHammerPattern(family, pattern);
            bool activeRun = buildSystem.HammerPatternActive;
            string planText;
            Color planTint;
            if (pattern == VoxelEngine.Building.PlacementPattern.Single)
            {
                planText = "SINGLE · CLICK TO BUILD";
                planTint = T.TextSecondary;
            }
            else if (!supported)
            {
                planText = "SINGLE FALLBACK · "
                    + BuildSystemV2.HammerPatternFallbackReason(family, pattern);
                planTint = T.AccentAmber;
            }
            else if (activeRun)
            {
                int planned = buildSystem.HammerPatternPreviewCount;
                int valid = buildSystem.HammerPatternValidCount;
                string size = pattern == VoxelEngine.Building.PlacementPattern.Deck
                    || pattern == VoxelEngine.Building.PlacementPattern.Wall
                    ? buildSystem.HammerPatternPlanSize.x + " × " + buildSystem.HammerPatternPlanSize.y
                    : planned + " CELLS";
                planText = VoxelEngine.Building.PlacementOptions.PatternName(pattern) + "  " + size
                    + " · " + buildSystem.HammerPatternPlaneLabel + " · "
                    + valid + "/" + planned + " CLEAR";
                planTint = valid == planned && planned > 0 ? T.AccentGreen
                    : valid > 0 ? T.AccentAmber : T.AccentRed;
            }
            else
            {
                planText = VoxelEngine.Building.PlacementOptions.PatternName(pattern)
                    + " · PRESS + DRAG · RELEASE TO BUILD";
                planTint = T.AccentCyan;
            }
            _planLabel.text = planText;
            _planLabel.style.color = new StyleColor(planTint);

            int pieces = activeRun ? buildSystem.HammerPatternValidCount : 1;
            int multiplier = activeRun ? BuildSystemV2.HammerPatternCostMultiplier : 1;
            if (activeRun && pieces <= 0)
            {
                _costLabel.text = buildSystem.HammerPatternAffordableCount <= 0
                    ? "NO AFFORDABLE CELLS · CARRY MATERIALS"
                    : "NO VALID CELLS · CHECK THE RED PREVIEWS";
                _costLabel.style.color = new StyleColor(T.AccentRed);
                T.Border(_card, 1, T.BorderRed);
                return;
            }

            if (def.placeCost?.items == null || def.placeCost.items.Length == 0)
            {
                _costLabel.text = activeRun
                    ? "<color=#55CC77>Free · " + pieces + " pieces</color>"
                    : "<color=#55CC77>Free</color>";
                _costLabel.style.color = new StyleColor(T.AccentGreen);
                T.Border(_card, 1, T.BorderBright);
                return;
            }

            var sb = new System.Text.StringBuilder(192);
            bool allAfford = true;
            bool first = true;
            var costs = def.placeCost.items;
            for (int i = 0; i < costs.Length; i++)
            {
                ItemDefinition item = costs[i].item;
                if (item == null || costs[i].count <= 0) continue;
                bool alreadyCounted = false;
                for (int prior = 0; prior < i; prior++)
                {
                    if (costs[prior].item != item) continue;
                    alreadyCounted = true;
                    break;
                }
                if (alreadyCounted) continue;

                long basePerPiece = 0L;
                for (int duplicate = i; duplicate < costs.Length; duplicate++)
                    if (costs[duplicate].item == item && costs[duplicate].count > 0)
                        basePerPiece += costs[duplicate].count;
                long perPieceLong = basePerPiece * multiplier;
                long totalLong = perPieceLong * pieces;
                int perPiece = perPieceLong >= int.MaxValue ? int.MaxValue : (int)perPieceLong;
                int total = totalLong >= int.MaxValue ? int.MaxValue : (int)totalLong;
                int available = buildSystem != null
                    ? buildSystem.GetAvailableBuildCount(item)
                    : inv.container != null ? inv.container.CountOf(item) : 0;
                bool enough = available >= total;
                if (!enough) allAfford = false;
                string hex = enough ? "#66DD88" : "#DD5544";
                if (!first) sb.Append("   ");
                sb.Append("<color=").Append(hex).Append(">")
                    .Append(total).Append("</color> <color=#A0A8B8>")
                    .Append(item.displayName).Append("</color> <color=")
                    .Append(hex).Append(">( ");
                if (activeRun) sb.Append(perPiece).Append(" × ").Append(pieces).Append(" · ");
                sb.Append(available).Append(" available )</color>");
                first = false;
            }

            _costLabel.enableRichText = true;
            _costLabel.text = sb.ToString();
            _costLabel.style.color = new StyleColor(allAfford ? T.AccentGreen : T.AccentRed);
            T.Border(_card, 1, allAfford ? T.BorderBright : T.BorderRed);
        }
    }
}
