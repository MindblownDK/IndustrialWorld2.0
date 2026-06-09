// Assets/Scripts/VoxelEngine/UI/Tooltip.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║           ITEM TOOLTIP — Polling-based hover card              ║
// ║   Appears after 0.25s hover. Follows cursor with clamped edge. ║
// ║   Shows: name · category · description · tool/block stats.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Farming;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class Tooltip
    {
        // ── State ──────────────────────────────────────────────────
        private static VisualElement _root, _panel;
        private static VisualElement _categoryDot;
        private static Label         _name, _category, _desc, _stats;
        private static VisualElement _statsCard;

        private static VisualElement _lastHovered;
        private static float         _hoverStart;
        private const  float         HOVER_DELAY = 0.25f;

        // ── Mount ──────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _panel != null && _panel.parent == uiRoot) return;
            _root = uiRoot;
            if (_panel != null) _panel.RemoveFromHierarchy();

            _panel = new VisualElement { name = "TooltipPanel" };
            _panel.style.position    = Position.Absolute;
            _panel.style.maxWidth    = 290;
            _panel.style.minWidth    = 160;
            _panel.style.paddingTop  = 10;
            _panel.style.paddingBottom = 10;
            _panel.style.paddingLeft   = 12;
            _panel.style.paddingRight  = 12;
            _panel.style.backgroundColor = new StyleColor(new Color(T.BgDark.r, T.BgDark.g, T.BgDark.b, 0.97f));
            T.Radius(_panel, T.CardRadius);
            T.Border(_panel, 1, T.BorderGold);
            _panel.pickingMode = PickingMode.Ignore;
            _panel.style.display = DisplayStyle.None;
            uiRoot.Add(_panel);

            // Top accent stripe.
            var stripe = new VisualElement();
            stripe.style.height          = 2;
            stripe.style.alignSelf       = Align.Stretch;
            stripe.style.backgroundColor = new StyleColor(new Color(T.AccentGold.r, T.AccentGold.g, T.AccentGold.b, 0.40f));
            stripe.style.marginBottom    = 8;
            T.Radius(stripe, 1f);
            stripe.pickingMode = PickingMode.Ignore;
            _panel.Add(stripe);

            // Name row: dot + name.
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems    = Align.Center;
            nameRow.style.marginBottom  = 3;
            nameRow.pickingMode = PickingMode.Ignore;

            _categoryDot = new VisualElement();
            _categoryDot.style.width           = 7;
            _categoryDot.style.height          = 7;
            _categoryDot.style.backgroundColor = new StyleColor(T.AccentGold);
            T.Radius(_categoryDot, 3.5f);
            _categoryDot.style.marginRight     = 7;
            _categoryDot.style.flexShrink      = 0;
            _categoryDot.pickingMode = PickingMode.Ignore;
            nameRow.Add(_categoryDot);

            _name = new Label("");
            _name.style.color                   = new StyleColor(T.TextPrimary);
            _name.style.fontSize                = 14;
            _name.style.unityFontStyleAndWeight = FontStyle.Bold;
            _name.style.flexGrow                = 1;
            _name.style.whiteSpace              = WhiteSpace.Normal;
            _name.pickingMode = PickingMode.Ignore;
            nameRow.Add(_name);
            _panel.Add(nameRow);

            // Category badge.
            _category = new Label("");
            _category.style.color                   = new StyleColor(T.AccentGold);
            _category.style.fontSize                = 9;
            _category.style.unityFontStyleAndWeight = FontStyle.Bold;
            _category.style.letterSpacing           = 1.5f;
            _category.style.marginBottom            = 5;
            _category.pickingMode = PickingMode.Ignore;
            _panel.Add(_category);

            // Separator.
            _panel.Add(T.Divider());

            // Description.
            _desc = new Label("");
            _desc.style.color      = new StyleColor(T.TextSecondary);
            _desc.style.fontSize   = 11;
            _desc.style.whiteSpace = WhiteSpace.Normal;
            _desc.style.marginBottom = 6;
            _desc.pickingMode = PickingMode.Ignore;
            _panel.Add(_desc);

            // Stats card — only shown when item has stat data.
            _statsCard = new VisualElement();
            _statsCard.style.paddingTop    = 6;
            _statsCard.style.paddingBottom = 6;
            _statsCard.style.paddingLeft   = 8;
            _statsCard.style.paddingRight  = 8;
            _statsCard.style.backgroundColor = new StyleColor(T.BgCard);
            T.Radius(_statsCard, 5f);
            T.Border(_statsCard, 1, T.BorderSubtle);
            _statsCard.pickingMode = PickingMode.Ignore;

            _stats = new Label("");
            _stats.style.color    = new StyleColor(T.AccentGreen);
            _stats.style.fontSize = 10;
            _stats.style.whiteSpace = WhiteSpace.Normal;
            _stats.pickingMode = PickingMode.Ignore;
            _statsCard.Add(_stats);
            _panel.Add(_statsCard);
        }

        // ── Tick ───────────────────────────────────────────────────
        /// <summary>
        /// Called every frame from GameUIController.Update().
        /// Probes the slot under the cursor and shows the tooltip if hovered long enough.
        /// </summary>
        public static void Tick(
            Vector2 mousePosScreen, int screenH,
            Func<Vector2, ItemStack> slotProbe)
        {
            if (_panel == null || _root == null) return;

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
                _root.panel, new Vector2(mousePosScreen.x, screenH - mousePosScreen.y));

            // Walk up the element tree to find a slot-tagged element.
            var element = _root.panel.Pick(panelPos);
            VisualElement walked = element;
            while (walked != null)
            {
                if (walked.userData != null) break;
                walked = walked.parent;
            }

            ItemStack stack = slotProbe?.Invoke(panelPos);
            if (stack == null || stack.IsEmpty)
            {
                Hide();
                return;
            }

            // Hover timer — only show after HOVER_DELAY.
            if (walked != _lastHovered)
            {
                _lastHovered = walked;
                _hoverStart  = Time.unscaledTime;
            }
            if (Time.unscaledTime - _hoverStart < HOVER_DELAY)
            {
                _panel.style.display = DisplayStyle.None;
                return;
            }

            FillFor(stack);

            // Position tooltip near cursor, clamped to screen edges.
            float tx    = panelPos.x + 8f;
            float ty    = panelPos.y + 8f;
            float maxX  = _root.layout.width  - 310f;
            float maxY  = _root.layout.height - 220f;
            if (maxX > 0) tx = Mathf.Min(tx, maxX);
            if (maxY > 0) ty = Mathf.Min(ty, maxY);

            _panel.style.left    = tx;
            _panel.style.top     = ty;
            _panel.style.display = DisplayStyle.Flex;
            _panel.BringToFront();
        }

        public static void Hide()
        {
            _lastHovered = null;
            if (_panel != null) _panel.style.display = DisplayStyle.None;
        }

        /// <summary>Legacy no-op stub — kept for compile compatibility.</summary>
        public static void Bind(VisualElement slot, ItemStack stack) { }

        // ── Private Fill ───────────────────────────────────────────
        private static void FillFor(ItemStack stack)
        {
            var item = stack.item;

            // Name (with stack count if > 1).
            _name.text = stack.count > 1 ? $"{item.displayName}  ×{stack.count}" : item.displayName;

            // Category + dot colour.
            string cat   = "Item";
            Color  dotC  = T.AccentGold;

            if (item is ResourceItem ri)
            {
                cat  = ri.subcategory.ToString();
                dotC = ri.fuelSeconds > 0f ? T.AccentAmber : T.AccentCyan;
            }
            else if (item is ToolItem)   { cat = "Tool";  dotC = T.AccentGreen;  }
            else if (item is BlockItem)  { cat = "Block"; dotC = T.AccentTeal;   }
            else if (item is FoodItem)   { cat = "Food";  dotC = T.AccentOrange; }

            _category.text = cat.ToUpper();
            _categoryDot.style.backgroundColor = new StyleColor(dotC);
            _category.style.color              = new StyleColor(dotC);

            // Description.
            bool hasDesc = !string.IsNullOrWhiteSpace(item.description);
            _desc.text             = hasDesc ? item.description : "";
            _desc.style.display    = hasDesc ? DisplayStyle.Flex : DisplayStyle.None;

            // Stats card.
            string statsText = BuildStats(item, stack);
            bool   hasStats  = !string.IsNullOrEmpty(statsText);
            _statsCard.style.display = hasStats ? DisplayStyle.Flex : DisplayStyle.None;
            _stats.text              = statsText;
        }

        private static string BuildStats(ItemDefinition item, ItemStack stack)
        {
            if (item is ToolItem t)
            {
                return $"Mining Tier:  {t.miningTier}\n" +
                       $"Strength:     {t.strength}\n" +
                       $"Brush Range:  {t.brushRadius:0.0}\n" +
                       $"Durability:   {stack.durability} / {t.maxDurability}";
            }
            if (item is BlockItem b)
            {
                return $"Block HP:       {b.blockHealth}\n" +
                       $"Mining Tier:    {b.miningTier}";
            }
            if (item is ResourceItem r && r.fuelSeconds > 0f)
            {
                return $"Burns for:  {r.fuelSeconds:0.0}s";
            }
            return string.Empty;
        }
    }
}
