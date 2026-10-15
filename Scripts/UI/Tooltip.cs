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

        // Sticky mode (14.40.0): a panel (e.g. the storage terminal grid) can
        // pin the tooltip itself. While pinned, the per-frame Tick does not
        // hide it just because no inventory slot sits under the cursor.
        private static bool _sticky;

        // ── Mount ──────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _panel != null && _panel.parent == uiRoot) return;
            _root = uiRoot;
            if (_panel != null) _panel.RemoveFromHierarchy();

            _panel = new VisualElement { name = "TooltipPanel" };
            _panel.style.position    = Position.Absolute;
            _panel.style.maxWidth    = 340;
            _panel.style.minWidth    = 190;
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
                if (_sticky) return;   // a panel pinned the tooltip - leave it
                Hide();
                return;
            }
            _sticky = false;           // a real slot hover takes over again

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
            _sticky      = false;
            if (_panel != null) _panel.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 14.40.0: show the tooltip pinned at a fixed panel position for a
        /// synthetic stack (used by the storage terminal grid, whose cells are
        /// not real inventory slots). Optional extra lines are appended to the
        /// stats card. Stays visible until <see cref="HideSticky"/> or a real
        /// slot hover takes over.
        /// </summary>
        public static void ShowStackAt(ItemStack stack, Vector2 panelPos, string extraStats = null)
        {
            if (_panel == null || _root == null) return;
            if (stack == null || stack.IsEmpty || stack.item == null) return;

            FillFor(stack, extraStats);

            float tx   = panelPos.x;
            float ty   = panelPos.y;
            float maxX = _root.layout.width  - 310f;
            float maxY = _root.layout.height - 240f;
            if (maxX > 0) tx = Mathf.Min(tx, maxX);
            if (maxY > 0) ty = Mathf.Min(ty, maxY);

            _panel.style.left    = tx;
            _panel.style.top     = ty;
            _panel.style.display = DisplayStyle.Flex;
            _panel.BringToFront();
            _sticky = true;
        }

        /// <summary>Hide a tooltip that was pinned via <see cref="ShowStackAt"/>.</summary>
        public static void HideSticky()
        {
            if (!_sticky) return;
            _sticky = false;
            if (_panel != null) _panel.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Shows a detailed crafting-focused tooltip for a required ingredient row in the
        /// crafting menu: item name at the top, what machine is needed to make it, the input
        /// items, the output item, craft time, and current have/required counts.
        /// </summary>
        public static void ShowRequiredItemAt(ItemDefinition item, int have, int need, Vector2 panelPos)
        {
            if (_panel == null || _root == null || item == null) return;

            var synthetic = new ItemStack(item, 1);
            string extra = BuildRequiredItemCraftingInfo(item, have, need);
            FillFor(synthetic, extra);
            // Always show ONLY the item name at the top for required-item tooltips.
            _name.text = item.displayName;

            float tx   = panelPos.x + 12f;
            float ty   = panelPos.y + 12f;
            float maxX = _root.layout.width  - 350f;
            float maxY = _root.layout.height - 280f;
            if (maxX > 0) tx = Mathf.Min(tx, maxX);
            if (maxY > 0) ty = Mathf.Min(ty, maxY);

            _panel.style.left    = tx;
            _panel.style.top     = ty;
            _panel.style.display = DisplayStyle.Flex;
            _panel.BringToFront();
            _sticky = true;
        }

        private sealed class CachedProductionMethod
        {
            public string Machine;
            public string Inputs;
            public string Output;
            public float Seconds;
            public string RequiredResearch;
        }

        private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<CachedProductionMethod>> _productionCache;

        private static string ItemLookupKey(ItemDefinition item)
        {
            if (item == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(item.displayName)) return item.displayName.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(item.itemId)) return item.itemId.Trim().ToLowerInvariant();
            return item.name.Trim().ToLowerInvariant();
        }

        private static void EnsureProductionCache()
        {
            if (_productionCache != null) return;
            _productionCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<CachedProductionMethod>>();

            void AddMethod(ItemDefinition output, CachedProductionMethod m)
            {
                string key = ItemLookupKey(output);
                if (string.IsNullOrEmpty(key)) return;
                if (!_productionCache.TryGetValue(key, out var list))
                {
                    list = new System.Collections.Generic.List<CachedProductionMethod>();
                    _productionCache[key] = list;
                }
                foreach (var existing in list)
                {
                    if (existing.Machine == m.Machine && existing.Inputs == m.Inputs && existing.Output == m.Output)
                        return;
                }
                list.Add(m);
            }

            var researchByRecipe = new System.Collections.Generic.Dictionary<VoxelEngine.Crafting.RecipeDefinition, string>();
            foreach (var node in Resources.FindObjectsOfTypeAll<VoxelEngine.Research.ResearchNode>())
            {
                if (node == null || node.unlocksRecipes == null || string.IsNullOrEmpty(node.displayName)) continue;
                foreach (var unlocked in node.unlocksRecipes)
                {
                    if (unlocked != null && !researchByRecipe.ContainsKey(unlocked))
                        researchByRecipe[unlocked] = node.displayName;
                }
            }

            foreach (var r in Resources.FindObjectsOfTypeAll<VoxelEngine.Crafting.RecipeDefinition>())
            {
                if (r == null || r.outputItem == null) continue;
                if (VoxelEngine.Crafting.Crafter.OutputsBuildToken(r)) continue;
                var inSb = new System.Text.StringBuilder();
                if (r.inputs != null)
                {
                    foreach (var ing in r.inputs)
                    {
                        if (ing.item == null || ing.count <= 0) continue;
                        if (inSb.Length > 0) inSb.Append(", ");
                        inSb.Append($"{ing.count}\u00D7 {ing.item.displayName}");
                    }
                }
                string station = r.requiredStation switch
                {
                    VoxelEngine.Crafting.StationTier.None          => "Hand Crafting (Inventory)",
                    VoxelEngine.Crafting.StationTier.CraftingBench => "Crafting Bench",
                    VoxelEngine.Crafting.StationTier.Furnace       => "Furnace",
                    VoxelEngine.Crafting.StationTier.Assembler     => "Assembler Station",
                    VoxelEngine.Crafting.StationTier.ArmorStation  => "Armor Station",
                    _                                              => r.requiredStation.ToString()
                };
                string resName = !r.unlockedByDefault && researchByRecipe.TryGetValue(r, out var rn) ? rn : null;
                AddMethod(r.outputItem, new CachedProductionMethod
                {
                    Machine = station,
                    Inputs  = inSb.Length > 0 ? inSb.ToString() : "None",
                    Output  = $"{Mathf.Max(1, r.outputCount)}\u00D7 {r.outputItem.displayName}",
                    Seconds = r.craftSeconds,
                    RequiredResearch = resName
                });
            }

            foreach (var sr in Resources.FindObjectsOfTypeAll<VoxelEngine.Crafting.SmeltingRecipe>())
            {
                if (sr == null || sr.output == null) continue;
                string inp = sr.input != null ? $"{Mathf.Max(1, sr.inputCount)}\u00D7 {sr.input.displayName}" : "None";
                AddMethod(sr.output, new CachedProductionMethod
                {
                    Machine = "Furnace / Smelter",
                    Inputs  = inp,
                    Output  = $"{Mathf.Max(1, sr.outputCount)}\u00D7 {sr.output.displayName}",
                    Seconds = sr.smeltSeconds
                });
            }

            foreach (var mr in Resources.FindObjectsOfTypeAll<VoxelEngine.Simulation.MachineRecipe>())
            {
                if (mr == null || mr.outputItem == null) continue;
                var inSb = new System.Text.StringBuilder();
                if (mr.inputs != null)
                {
                    foreach (var ing in mr.inputs)
                    {
                        if (ing.item == null || ing.count <= 0) continue;
                        if (inSb.Length > 0) inSb.Append(", ");
                        inSb.Append($"{ing.count}\u00D7 {ing.item.displayName}");
                    }
                }
                string machine = mr.recipeType switch
                {
                    VoxelEngine.Simulation.MachineRecipeType.Smelting   => "Coke Kiln / Arc Foundry",
                    VoxelEngine.Simulation.MachineRecipeType.Crushing   => "Crusher",
                    VoxelEngine.Simulation.MachineRecipeType.Assembling => "Assembler",
                    VoxelEngine.Simulation.MachineRecipeType.Chemical   => "Chemical Plant",
                    VoxelEngine.Simulation.MachineRecipeType.Washing    => "Ore Washer",
                    _                                                   => mr.recipeType.ToString()
                };
                AddMethod(mr.outputItem, new CachedProductionMethod
                {
                    Machine = machine,
                    Inputs  = inSb.Length > 0 ? inSb.ToString() : "None",
                    Output  = $"{Mathf.Max(1, mr.outputCount)}\u00D7 {mr.outputItem.displayName}",
                    Seconds = mr.processSeconds
                });
            }
        }

        private static string BuildRequiredItemCraftingInfo(ItemDefinition item, int have, int need)
        {
            EnsureProductionCache();
            var sb = new System.Text.StringBuilder();
            string status = have >= need ? "Ready" : $"Missing {need - have}";
            sb.Append($"Available:  {have} / {need} required ({status})");

            string key = ItemLookupKey(item);
            if (_productionCache != null && _productionCache.TryGetValue(key, out var methods) && methods.Count > 0)
            {
                int shown = Mathf.Min(methods.Count, 3);
                for (int i = 0; i < shown; i++)
                {
                    var m = methods[i];
                    sb.AppendLine();
                    if (shown > 1)
                        sb.AppendLine($"\u2500\u2500 Method {i + 1} \u2500\u2500");
                    sb.AppendLine($"Machine:    {m.Machine}");
                    sb.AppendLine($"Inputs:     {m.Inputs}");
                    sb.Append($"Output:     {m.Output}");
                    string timeTxt = m.Seconds > 0f ? $"{m.Seconds:0.#}s" : "Instant";
                    sb.Append($"\nCraft Time: {timeTxt}");
                    if (!string.IsNullOrEmpty(m.RequiredResearch))
                        sb.Append($"\nResearch:   {m.RequiredResearch}");
                }
                if (methods.Count > shown)
                    sb.Append($"\n(+{methods.Count - shown} more production method{(methods.Count - shown > 1 ? "s" : "")})");
            }
            else
            {
                sb.Append("\nMachine:    Raw resource (gathered / mined in world)");
            }
            return sb.ToString();
        }

        /// <summary>Legacy no-op stub — kept for compile compatibility.</summary>
        public static void Bind(VisualElement slot, ItemStack stack) { }

        // ── Private Fill ───────────────────────────────────────────
        private static void FillFor(ItemStack stack, string extraStats = null)
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

            // 14.40.0: every item tooltip shows its weight. Heavier items also
            // cost more network storage (1 kg = 1 GB of matter data).
            float unitMass = item.massPerUnit <= 0f ? 1f : item.massPerUnit;
            string weightLine = stack.count > 1
                ? $"Weight:     {MassFormat.Format(unitMass)} / unit  ({MassFormat.Format(unitMass * stack.count)} total)"
                : $"Weight:     {MassFormat.Format(unitMass)}";
            statsText = string.IsNullOrEmpty(statsText) ? weightLine : statsText + "\n" + weightLine;

            if (!string.IsNullOrEmpty(extraStats))
                statsText = string.IsNullOrEmpty(statsText) ? extraStats : statsText + "\n" + extraStats;

            bool hasStats = !string.IsNullOrEmpty(statsText);
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
            if (item is VoxelEngine.GridSystem.GridBlockItem gb)
            {
                // Roadmap 5.1 item 8: every grid block shows its heat tolerance.
                float tolerance = VoxelEngine.Thermal.ThermalRules.ToleranceC(gb);
                return $"Block HP:       {gb.blockHP:0}\n" +
                       $"Mass:           {VoxelEngine.Items.MassFormat.Format(gb.blockMass)}\n" +
                       $"Heat Tolerance: {tolerance:0} °C";
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
            if (item is JetpackItem jp)
            {
                var sb = new System.Text.StringBuilder();
                if (jp.UsesHydrogenEffective)
                    sb.AppendLine($"H₂ Tank:    {JetpackItem.GetH2Ml(stack)} / {jp.HydrogenCapacityMl} ml");
                if (jp.UsesPowerEffective)
                    sb.AppendLine($"Power Cell: {JetpackItem.GetPowerMl(stack)} / {jp.PowerCapacityMl} Wh");
                sb.AppendLine($"Speed ×{jp.flightSpeedMultiplier:0.##}   Boost ×{jp.boostMultiplier:0.##}");
                string env = jp.supportsAtmosphere && jp.supportsVacuum ? "Atmosphere + Vacuum"
                           : jp.supportsVacuum ? "Vacuum only"
                           : "Atmosphere only";
                sb.Append($"Operates:   {env}");
                return sb.ToString();
            }
            if (HydrogenCanisterItem.IsPortableHydrogenTank(item))
            {
                return $"H₂ Stored:  {HydrogenCanisterItem.GetStoredMl(stack)} / {HydrogenCanisterItem.GetCapacityMl(stack)} ml";
            }
            if (PortableBatteryItem.IsPortableBattery(item))
            {
                return $"Charge:     {PortableBatteryItem.GetStoredMl(stack)} / {PortableBatteryItem.GetCapacityMl(stack)} Wh";
            }
            return string.Empty;
        }
    }
}
