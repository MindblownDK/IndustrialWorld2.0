// Assets/Scripts/VoxelEngine/UI/UpgradePromptHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        UPGRADE PROMPT HUD — Top-centre contextual hint         ║
// ║   Shown when holding Hammer and targeting an upgradable block. ║
// ║   Lists next-tier cost in green (have) or red (missing).       ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class UpgradePromptHud
    {
        // ── State ──────────────────────────────────────────────────────
        private static VisualElement _root, _panel, _card;
        private static Label         _title, _body;

        // ── Mount ──────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _panel != null && _panel.parent == uiRoot) return;
            _root = uiRoot;
            if (_panel != null) _panel.RemoveFromHierarchy();

            _panel = new VisualElement { name = "UpgradePromptHud" };
            _panel.style.position    = Position.Absolute;
            _panel.style.top         = 70;
            _panel.style.left        = 0;
            _panel.style.right       = 0;
            _panel.style.alignItems  = Align.Center;
            _panel.pickingMode       = PickingMode.Ignore;
            _panel.style.display     = DisplayStyle.None;
            uiRoot.Add(_panel);

            _card = new VisualElement();
            _card.style.paddingTop    = 10;
            _card.style.paddingBottom = 10;
            _card.style.paddingLeft   = 18;
            _card.style.paddingRight  = 18;
            _card.style.backgroundColor = new StyleColor(new Color(T.BgPanel.r, T.BgPanel.g, T.BgPanel.b, 0.94f));
            T.Radius(_card, T.PanelRadius);
            T.Border(_card, 1, T.BorderGold);
            _card.style.alignItems = Align.Center;
            _card.pickingMode = PickingMode.Ignore;
            _panel.Add(_card);

            // Gold accent stripe at top.
            var stripe = new VisualElement();
            stripe.style.height          = 2;
            stripe.style.alignSelf       = Align.Stretch;
            stripe.style.backgroundColor = new StyleColor(new Color(T.AccentGold.r, T.AccentGold.g, T.AccentGold.b, 0.45f));
            stripe.style.marginBottom    = 7;
            T.Radius(stripe, 1f);
            stripe.pickingMode = PickingMode.Ignore;
            _card.Add(stripe);

            _title = new Label("");
            _title.style.color                   = new StyleColor(T.TextPrimary);
            _title.style.fontSize                = 12;
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.unityTextAlign          = TextAnchor.MiddleCenter;
            _title.style.letterSpacing           = 0.5f;
            _title.pickingMode = PickingMode.Ignore;
            _card.Add(_title);

            _body = new Label("");
            _body.style.fontSize       = 11;
            _body.style.marginTop      = 4;
            _body.style.unityTextAlign = TextAnchor.MiddleCenter;
            _body.style.whiteSpace     = WhiteSpace.NoWrap;
            _body.enableRichText       = true;
            _body.pickingMode          = PickingMode.Ignore;
            _card.Add(_body);
        }

        // ── Tick ───────────────────────────────────────────────────────
        public static void Tick(PlacedTieredBlock target, Inventory inv, bool holdingHammer)
        {
            if (_panel == null) return;

            if (target == null || target.definition == null || !holdingHammer)
            {
                _panel.style.display = DisplayStyle.None;
                return;
            }

            _panel.style.display = DisplayStyle.Flex;
            var def  = target.definition;
            var tier = target.tier;

            // Already maxed out.
            if (tier == BuildTier.Steel)
            {
                _title.text = $"{def.displayName}  ·  Steel Tier";
                _body.text  = "<color=#66DD88>✓  Maximum tier — nothing to upgrade.</color>";
                T.Border(_card, 1, T.BorderBright);
                return;
            }

            var nextTier = TieredBlockDefinition.NextTier(tier);
            var cost     = def.GetUpgradeCost(tier);

            _title.text = $"{def.displayName}  ·  {tier} → {nextTier}  ·  LMB to upgrade";

            if (cost == null || cost.items == null || cost.items.Length == 0)
            {
                _body.text = "<color=#66DD88>Free upgrade</color>";
                T.Border(_card, 1, T.BorderBright);
                return;
            }

            var sb         = new System.Text.StringBuilder();
            bool anyMissing = false;

            for (int i = 0; i < cost.items.Length; i++)
            {
                var ing  = cost.items[i];
                if (ing.item == null) continue;
                int  have = inv != null && inv.container != null ? inv.container.CountOf(ing.item) : 0;
                bool ok   = have >= ing.count;
                if (!ok) anyMissing = true;

                string col = ok ? "#66DD88" : "#DD5544";
                sb.Append($"<color={col}>{have}/{ing.count}</color>  <color=#8A92A8>{ing.item.displayName}</color>");
                if (i < cost.items.Length - 1) sb.Append("     ");
            }

            _body.text = sb.ToString();
            T.Border(_card, 1, anyMissing ? T.BorderRed : T.BorderGold);
        }
    }
}
