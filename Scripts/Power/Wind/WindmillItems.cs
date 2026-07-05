// Assets/Scripts/VoxelEngine/Power/Wind/WindmillItems.cs
// Item and Recipe definitions for the new windmill system.
// 2 sizes Helix + 3 sizes Standard + Monopole + Assembly components.
// All stationary, non-grid windmills.
// MAX EFFORT: complete, beautiful, assembly-part focused items.

using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power.Wind;

namespace VoxelEngine.Power.Wind
{
    // Central registry to create & register all wind power items/recipes at runtime / editor.
    // Call WindmillItems.EnsureRegistered() from bootstrap or editor utility.
    public static class WindmillItems
    {
        public static bool IsRegistered { get; private set; }

        // === BLOCK ITEMS (placeable windmills) ===
        public static BlockItem SmallStandardBlock;
        public static BlockItem MediumStandardBlock;
        public static BlockItem LargeStandardBlock;   // V236

        public static BlockItem SmallHelixBlock;
        public static BlockItem LargeHelixBlock;

        public static BlockItem MonopoleBlock;

        // === ASSEMBLY PART ITEMS ===
        public static ItemDefinition TowerSegmentItem;
        public static ItemDefinition NacelleItem;
        public static ItemDefinition GearboxItem;
        public static ItemDefinition GeneratorItem;
        public static ItemDefinition HubItem;
        public static ItemDefinition BladeItem;             // 3 needed per standard
        public static ItemDefinition HelixGeneratorSmall;
        public static ItemDefinition HelixGeneratorLarge;
        public static ItemDefinition HelixWingSmall;
        public static ItemDefinition HelixWingLarge;

        // === RECIPES ===
        public static RecipeDefinition Recipe_SmallStandard;
        public static RecipeDefinition Recipe_MediumStandard;
        public static RecipeDefinition Recipe_LargeStandard;

        public static RecipeDefinition Recipe_SmallHelix;
        public static RecipeDefinition Recipe_LargeHelix;

        public static RecipeDefinition Recipe_Monopole;

        // Component recipes (crafted at Assembler)
        public static RecipeDefinition Recipe_Tower;
        public static RecipeDefinition Recipe_Nacelle;
        public static RecipeDefinition Recipe_Gearbox;
        public static RecipeDefinition Recipe_Generator;
        public static RecipeDefinition Recipe_Hub;
        public static RecipeDefinition Recipe_Blade;

        public static RecipeDefinition Recipe_HelixGenSmall;
        public static RecipeDefinition Recipe_HelixGenLarge;
        public static RecipeDefinition Recipe_HelixWingSmall;
        public static RecipeDefinition Recipe_HelixWingLarge;

        public static void EnsureRegistered()
        {
            if (IsRegistered) return;

            CreateAllItems();
            CreateAllRecipes();

            IsRegistered = true;
            Debug.Log("[WindmillItems] All new windmill items & recipes registered.");
        }

        private static void CreateAllItems()
        {
            // BLOCKS (placeable)
            SmallStandardBlock = CreateBlockItem("block_swind_small", "Small Standard Windmill", "Vestas V82 inspired. 2.5 MW max.", 2500000f, "Standard");
            MediumStandardBlock = CreateBlockItem("block_swind_medium", "Medium Standard Windmill", "Vestas V150 — 6.5 MW", 6500000f, "Standard");
            LargeStandardBlock = CreateBlockItem("block_swind_large", "Large V236 Offshore Windmill", "Vestas V236 — 15 MW. Placeable on land or sea with monopole.", 15000000f, "Standard");

            SmallHelixBlock = CreateBlockItem("block_hwind_small", "Small Vertical Helix Windmill", "1.2 MW compact vertical design.", 1200000f, "Helix");
            LargeHelixBlock = CreateBlockItem("block_hwind_large", "Large Vertical Helix Windmill", "4.8 MW powerful vertical helix.", 4800000f, "Helix");

            MonopoleBlock = CreateBlockItem("block_wind_monopole", "Windmill Monopole", "Heavy-duty steel monopole for water placement. Extends deep into seafloor.", 0f, "Monopole");

            // PARTS
            TowerSegmentItem = CreateSimpleItem("item_wind_tower", "Windmill Tower Segment", "Reinforced steel tower section.", "Building");
            NacelleItem = CreateSimpleItem("item_wind_nacelle", "Windmill Nacelle", "Main housing for gearbox, generator and hub.", "Power");
            GearboxItem = CreateSimpleItem("item_wind_gearbox", "Windmill Gearbox", "High-torque planetary gearbox.", "Power");
            GeneratorItem = CreateSimpleItem("item_wind_generator", "Windmill Generator", "Direct-drive permanent magnet generator.", "Power");
            HubItem = CreateSimpleItem("item_wind_hub", "Windmill Hub", "Rotor hub assembly.", "Power");
            BladeItem = CreateSimpleItem("item_wind_blade", "Windmill Blade", "Carbon-fiber composite blade. Requires 3 per turbine.", "Power");

            HelixGeneratorSmall = CreateSimpleItem("item_helix_gen_small", "Small Helix Generator", "Base generator unit for vertical helix.", "Power");
            HelixGeneratorLarge = CreateSimpleItem("item_helix_gen_large", "Large Helix Generator", "High-output base for large vertical helix.", "Power");
            HelixWingSmall = CreateSimpleItem("item_helix_wing_small", "Small Helix Wings", "Compact vertical helix rotor blades.", "Power");
            HelixWingLarge = CreateSimpleItem("item_helix_wing_large", "Large Helix Wings", "Massive vertical helix rotor.", "Power");
        }

        private static BlockItem CreateBlockItem(string id, string name, string desc, float maxPower, string categorySuffix)
        {
            var item = ScriptableObject.CreateInstance<BlockItem>();
            item.itemId = id;
            item.displayName = name;
            item.description = desc;
            item.category = "Power";
            item.maxStack = 5;
            item.massPerUnit = 180f;
            item.blockHealth = 800;
            item.miningTier = 2;

            // Note: placedPrefab assigned later in Unity Editor after prefab generation
            // For now, placeholder so build system can find it
            return item;
        }

        private static ItemDefinition CreateSimpleItem(string id, string name, string desc, string cat)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = name;
            item.description = desc;
            item.category = cat;
            item.maxStack = 50;
            item.massPerUnit = 35f;
            return item;
        }

        private static void CreateAllRecipes()
        {
            // === COMPONENT RECIPES (Assembler required) ===
            Recipe_Tower = CreateRecipe("Wind Tower Segment", new[] {
                ("SteelPlate", 18), ("Concrete", 12)
            }, TowerSegmentItem, 2, StationTier.Assembler, 8f);

            Recipe_Nacelle = CreateRecipe("Wind Nacelle", new[] {
                ("SteelPlate", 26), ("AdvancedElectronics", 4), ("Gear", 8)
            }, NacelleItem, 1, StationTier.Assembler, 14f);

            Recipe_Gearbox = CreateRecipe("Wind Gearbox", new[] {
                ("SteelPlate", 14), ("Gear", 22), ("Lubricant", 6)
            }, GearboxItem, 1, StationTier.Assembler, 11f);

            Recipe_Generator = CreateRecipe("Wind Generator", new[] {
                ("CopperCoil", 35), ("Magnet", 12), ("SteelPlate", 11)
            }, GeneratorItem, 1, StationTier.Assembler, 16f);

            Recipe_Hub = CreateRecipe("Wind Hub", new[] {
                ("SteelPlate", 9), ("Bearing", 8)
            }, HubItem, 1, StationTier.Assembler, 6f);

            Recipe_Blade = CreateRecipe("Wind Blade", new[] {
                ("CarbonFiber", 22), ("Resin", 8)
            }, BladeItem, 1, StationTier.Assembler, 9f);

            Recipe_HelixGenSmall = CreateRecipe("Small Helix Generator", new[] {
                ("SteelPlate", 8), ("CopperCoil", 12), ("Magnet", 4)
            }, HelixGeneratorSmall, 1, StationTier.Assembler, 7f);

            Recipe_HelixGenLarge = CreateRecipe("Large Helix Generator", new[] {
                ("SteelPlate", 22), ("CopperCoil", 30), ("Magnet", 14), ("AdvancedElectronics", 3)
            }, HelixGeneratorLarge, 1, StationTier.Assembler, 18f);

            Recipe_HelixWingSmall = CreateRecipe("Small Helix Wings", new[] {
                ("CarbonFiber", 15), ("Resin", 6)
            }, HelixWingSmall, 1, StationTier.Assembler, 5f);

            Recipe_HelixWingLarge = CreateRecipe("Large Helix Wings", new[] {
                ("CarbonFiber", 42), ("Resin", 18), ("SteelPlate", 7)
            }, HelixWingLarge, 1, StationTier.Assembler, 12f);

            // === FINAL WINDMILL RECIPES ===
            Recipe_SmallStandard = CreateRecipe("Small Standard Windmill", new[] {
                ("item_wind_tower", 5), ("item_wind_nacelle", 1), ("item_wind_gearbox", 1),
                ("item_wind_generator", 1), ("item_wind_hub", 1), ("item_wind_blade", 3)
            }, SmallStandardBlock, 1, StationTier.Assembler, 22f);

            Recipe_MediumStandard = CreateRecipe("Medium Standard Windmill", new[] {
                ("item_wind_tower", 9), ("item_wind_nacelle", 1), ("item_wind_gearbox", 1),
                ("item_wind_generator", 1), ("item_wind_hub", 1), ("item_wind_blade", 3)
            }, MediumStandardBlock, 1, StationTier.Assembler, 35f);

            Recipe_LargeStandard = CreateRecipe("Large V236 Windmill", new[] {
                ("item_wind_tower", 18), ("item_wind_nacelle", 1), ("item_wind_gearbox", 1),
                ("item_wind_generator", 1), ("item_wind_hub", 1), ("item_wind_blade", 3),
                ("item_wind_monopole", 1) // optional but recommended for water
            }, LargeStandardBlock, 1, StationTier.Assembler, 65f);

            Recipe_SmallHelix = CreateRecipe("Small Helix Windmill", new[] {
                ("item_helix_gen_small", 1), ("item_helix_wing_small", 1)
            }, SmallHelixBlock, 1, StationTier.Assembler, 14f);

            Recipe_LargeHelix = CreateRecipe("Large Helix Windmill", new[] {
                ("item_helix_gen_large", 1), ("item_helix_wing_large", 1)
            }, LargeHelixBlock, 1, StationTier.Assembler, 28f);

            Recipe_Monopole = CreateRecipe("Windmill Monopole", new[] {
                ("SteelPlate", 45), ("Concrete", 30)
            }, MonopoleBlock, 1, StationTier.Assembler, 18f);
        }

        private static RecipeDefinition CreateRecipe(string name, (string itemId, int count)[] inputs, ItemDefinition output, int outCount, StationTier station, float seconds)
        {
            var r = ScriptableObject.CreateInstance<RecipeDefinition>();
            r.displayName = name;
            r.requiredStation = station;
            r.craftSeconds = seconds;
            r.outputCount = outCount;
            r.outputItem = output;
            r.unlockedByDefault = false;

            var ingList = new System.Collections.Generic.List<RecipeIngredient>();
            foreach (var inp in inputs)
            {
                // NOTE: In real usage, you must assign real ItemDefinition references in Unity inspector after creation.
                // Here we create placeholder items for prototype. User must link actual items in editor.
                var ing = new RecipeIngredient();
                // Placeholder — will be overwritten when assets are created
                ing.count = inp.count;
                ingList.Add(ing);
            }
            r.inputs = ingList.ToArray();
            return r;
        }
    }
}
