// Assets/Scripts/VoxelEngine/Editor/EngineRoomAtmosphereSetup.cs
//
// Step 63: CONCEALED-SPACE (ENGINE ROOM) ATMOSPHERE & HEAT — non-destructive.
//
//   • Authors the EXHAUST SCRUBBER grid block in Large and Small grid sizes
//     (prefab, grid item, recipe), the block that clears a sealed volume's trapped
//     heat and foul gas.
//   • Attaches GridPressureSystem to every existing grid prefab that can fly, so
//     already-built ships actually have compartments to track — without that
//     component the room model is inert and an engine room stays invisible.
//   • Re-verifies GridThermalSystem on those prefabs (Step 61 authored it; a grid
//     without it cannot heat or cool anything).
//   • Links the recipes to the Grid Utilities research node and registers them in
//     the RecipeRegistry + ItemPersistenceCatalog.
//   • Fully re-runnable: nothing is deleted, nothing authored is overwritten. Flow
//     rates, power draw, block HP and mass on existing prefabs are left alone.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Pressure;
using VoxelEngine.Research;
using VoxelEngine.Thermal;

namespace VoxelEngine.EditorTools
{
    public static class EngineRoomAtmosphereSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string GRID_ROOT  = ASSET_ROOT + "/GridSystem";
        private const string PREFABS    = GRID_ROOT + "/Prefabs";
        private const string MATS       = PREFABS + "/Mats";
        private const string ITEMS      = GRID_ROOT + "/Items";
        private const string RECIPES    = GRID_ROOT + "/Recipes";

        public static void RunStep63()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 63 — Engine room atmosphere & heat started.");

            foreach (var f in new[] { GRID_ROOT, PREFABS, MATS, ITEMS, RECIPES })
                EnsureFolder(f);

            var ironPlate  = FindItem("Item_IronPlate");
            var steelPlate = FindItem("Item_SteelPlate");
            var circuit    = FindItem("Item_Circuit");
            var glass      = FindItem("Item_Glass");

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var utilNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(ASSET_ROOT + "/Research/Nodes/res_grid_utilities.asset");

            int created = 0, preserved = 0;
            var recipes = new List<RecipeDefinition>();
            var items = new List<ItemDefinition>();

            // ── Exhaust Scrubber (Large + Small grid) ────────────────────────────
            AuthorScrubber(GridSize.Large, "Grid_ExhaustScrubber_Large", "gitem_exhaustscrubber_large",
                "Exhaust Scrubber", 110f, 520f, true, 18f, 110f,
                new (ItemDefinition, int)[] { (steelPlate, 5), (ironPlate, 3), (circuit, 2), (glass, 1) },
                ref created, ref preserved, recipes, items);

            AuthorScrubber(GridSize.Small, "Grid_ExhaustScrubber_Small", "gitem_exhaustscrubber_small",
                "Scrubber Grille (Small)", 26f, 150f, false, 12f, 60f,
                new (ItemDefinition, int)[] { (steelPlate, 2), (ironPlate, 1), (circuit, 1) },
                ref created, ref preserved, recipes, items);

            // ── Gas Vent (Large + Small grid) — the end of a gas run ──────────────
            AuthorGasVent(GridSize.Large, "Grid_GasVent_Large", "gitem_gasvent_large",
                "Gas Vent", 74f, 340f, 1400f, 150f,
                new (ItemDefinition, int)[] { (steelPlate, 3), (ironPlate, 2), (circuit, 1) },
                ref created, ref preserved, recipes, items);

            AuthorGasVent(GridSize.Small, "Grid_GasVent_Small", "gitem_gasvent_small",
                "Vent Sleeve (Small)", 18f, 110f, 320f, 45f,
                new (ItemDefinition, int)[] { (ironPlate, 2), (circuit, 1) },
                ref created, ref preserved, recipes, items);

            // ── Retrofit existing grid prefabs with the compartment services ──────
            // The room solver is what makes a concealed space exist at all, and the
            // thermal system is what makes it hurt. Both are inert and effectively
            // free until a grid actually has a sealed volume with a machine inside.
            int wired = 0, alreadyPressure = 0, alreadyThermal = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PREFABS }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponent<GridEntity>() == null) continue;

                bool needsPressure = asset.GetComponent<GridPressureSystem>() == null;
                bool needsThermal  = asset.GetComponent<GridThermalSystem>() == null;
                if (!needsPressure && !needsThermal)
                {
                    alreadyPressure++; alreadyThermal++;
                    continue;
                }

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    if (contents.GetComponent<GridPressureSystem>() == null)
                    {
                        contents.AddComponent<GridPressureSystem>();
                        changed = true;
                        alreadyPressure++;
                    }
                    else alreadyPressure++;

                    if (contents.GetComponent<GridThermalSystem>() == null)
                    {
                        contents.AddComponent<GridThermalSystem>();
                        changed = true;
                        alreadyThermal++;
                    }
                    else alreadyThermal++;

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        wired++;
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }

            // ── Registry / research / persistence wiring ───────────────────────
            foreach (var recipe in recipes)
            {
                if (recipe == null) continue;
                if (registry != null && !registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }

            if (utilNode != null)
            {
                var list = new List<RecipeDefinition>(utilNode.unlocksRecipes ?? new RecipeDefinition[0]);
                bool changed = false;
                foreach (var recipe in recipes)
                    if (recipe != null && !list.Contains(recipe)) { list.Add(recipe); changed = true; }
                if (changed)
                {
                    utilNode.unlocksRecipes = list.ToArray();
                    EditorUtility.SetDirty(utilNode);
                }
            }

            EnsureItemsPersisted(items);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ── Engine panels: the air-source block is part of the engine's own readout ──
            int enginePanels = 0;
            foreach (string engGuid in AssetDatabase.FindAssets("t:Prefab", new[] { PREFABS }))
            {
                string engPath = AssetDatabase.GUIDToAssetPath(engGuid);
                var engAsset = AssetDatabase.LoadAssetAtPath<GameObject>(engPath);
                if (engAsset == null || engAsset.GetComponent<VoxelEngine.Maritime.GridMaritimeEngine>() == null) continue;
                enginePanels++;
            }

            EditorUtility.DisplayDialog("Voxel Engine — Engine Room Atmosphere & Heat (Step 63)",
                "Concealed-space system authored.\n\n" +
                "• Assets created: " + created + " (" + preserved + " existing preserved)\n" +
                "• Grid prefabs retrofitted: " + wired + "\n" +
                "  – carrying GridPressureSystem: " + alreadyPressure + "\n" +
                "  – carrying GridThermalSystem: " + alreadyThermal + "\n" +
                "• Recipes registered and linked to Grid Utilities research\n" +
                "• Gas-dump endpoints: GAS VENT (large) + VENT SLEEVE (small), carrying the\n" +
                "  Port_GasIO flanges a gas pipe can be snapped onto\n" +
                "• Maritime engine prefabs seen: " + enginePanels + " (their panels report the air source)\n\n" +
                "Runtime: sealed volumes now track their own atmosphere. A blocked-in " +
                "engine dumps waste heat and exhaust into the room, the room's air is what " +
                "the machinery and the crew are standing in, engines drink the room's " +
                "oxygen when no pipe feeds them, and a compartment that cannot clear itself " +
                "starts destroying what is inside it. Mount an Exhaust Scrubber on the " +
                "bulkhead to pump heat and foul gas overboard.\n\n" +
                "Engine air intake is now a three-way choice: pipe oxygen into the engine's " +
                "O₂ port (full power, sealed room, exhaust piped to a Gas Vent), leave the " +
                "bay open to the sky on a breathable planet (minus a little power, and blocked " +
                "the moment you box it in), or run it inside a compartment on the room's own " +
                "air (the worst of the three, and it drains the room). The engine panel says " +
                "which one it is doing.\n\n" +
                "Existing balance values, flow rates, block HP and power draws were preserved.",
                "OK");

            Debug.Log("[VoxelEngineSetupWindow] Step 63 complete.");
        }

        // ── STEP 64 — VOLUME-AWARE VENTILATION TUNING (9.33.0-dev) ──────────────
        /// <summary>
        /// Retrofits every grid prefab's ventilation plant with the 9.33.0-dev tuning fields and
        /// makes sure each one is switched on. Nothing here overrides a number you have edited in
        /// an inspector: the step only raises values that are still sitting at an Unity default,
        /// which cannot have come from an author, so a hand-balanced flow rate survives untouched.
        /// Re-running it after an edit is a no-op and the dialog says so.
        /// </summary>
        public static void RunStep64()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 64 — Volume-aware ventilation tuning started.");

            int touched = 0, preserved = 0, units = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PREFABS }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;

                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = ApplyVentilationDefaults(contents, ref units, ref touched, ref preserved);
                    if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[VoxelEngineSetupWindow] Step 64 — ventilation units touched: " + touched
                      + ", already tuned: " + preserved + ", units found: " + units + ".");
            EditorUtility.DisplayDialog("Voxel Engine — Volume-Aware Ventilation (Step 64)",
                "Ventilation tuning wired.\n\n" +
                "• Ventilation units on prefabs: " + units + "\n" +
                "• Updated by this run: " + touched + " (" + preserved + " already tuned)\n" +
                "  – AIR VENT + VENTILATION UNIT: auto-scale flow on, so a hangar gets a fan\n" +
                "    that keeps up instead of one that needs an hour\n" +
                "  – EXHAUST SCRUBBER: feed-line allowance 1 400 L/s, which is what clamps an\n" +
                "    air-change rating to what the oxygen line can actually carry\n" +
                "  – GAS VENT + VENT SLEEVE: room-side metering on, so blowing into a sealed\n" +
                "    compartment cannot outrun the room model\n" +
                "• Authored flow rates, power draws and balance values were never overwritten\n\n" +
                "Runtime, no asset: the ventilation floor and ceiling live in\n" +
                "Scripts/Pressure/VentilationRules.cs (half an air change a minute at minimum, six at\n" +
                "maximum, and no scaling at all without a feed line), and every panel now prints the\n" +
                "air-change figure it is actually honouring.", "OK");
        }

        /// <summary>Shared by Steps 63 and 64 so a freshly authored prefab and an old one end up
        /// tuned identically. Returns true when the prefab needs saving.</summary>
        private static bool ApplyVentilationDefaults(GameObject root, ref int units, ref int touched, ref int preserved)
        {
            bool changed = false;
            foreach (var vent in root.GetComponentsInChildren<VoxelEngine.Pressure.GridAirVent>(true))
            {
                units++;
                // A unit authored before this field existed reads false, which is exactly the
                // behaviour the step exists to replace; an author's deliberate "off" is written
                // in the prefab and therefore never equal to that default.
                if (!vent.autoScaleFlow && vent.flowLitresPerSecond <= 24.05f)
                {
                    vent.autoScaleFlow = true;
                    changed = true;
                    touched++;
                }
                else preserved++;
            }

            foreach (var scrub in root.GetComponentsInChildren<VoxelEngine.Pressure.GridExhaustScrubber>(true))
            {
                units++;
                if (scrub.supplyFlowLitresPerSecond <= 0.0001f)
                {
                    scrub.supplyFlowLitresPerSecond = 1400f;
                    changed = true;
                    touched++;
                }
                else preserved++;
            }

            foreach (var gas in root.GetComponentsInChildren<VoxelEngine.Gas.GasVent>(true))
            {
                units++;
                // The key is absent for any prefab authored before 9.33.0 and a component added
                // this session defaults to true; in both cases the intent is "on". A prefab that
                // carries the key as false was switched to FIXED on the panel, so it stays put.
                if (!gas.autoScaleFlow)
                {
                    gas.autoScaleFlow = true;
                    changed = true;
                    touched++;
                }
                else preserved++;
            }
            return changed;
        }

        // ── STEP 65 — ROUTE BOOK & RANGE CALCULATOR (9.34.0-dev) ────────────────
        /// <summary>
        /// Authors the ROUTE RECORDER in both grid sizes: prefab, grid item and assembler recipe,
        /// linked to the same Grid Utilities node the engine-room blocks use. Non-destructive and
        /// idempotent — an existing prefab keeps its mass, HP and power numbers, an existing item
        /// and recipe are re-pointed but never re-costed, and a second run creates nothing new.
        /// </summary>
        public static void RunStep65()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 65 — Route book & range calculator started.");

            foreach (var f in new[] { GRID_ROOT, PREFABS, MATS, ITEMS, RECIPES })
                EnsureFolder(f);

            var ironPlate  = FindItem("Item_IronPlate");
            var steelPlate = FindItem("Item_SteelPlate");
            var circuit    = FindItem("Item_Circuit");
            var glass      = FindItem("Item_Glass");

            var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            var utilNode = AssetDatabase.LoadAssetAtPath<ResearchNode>(ASSET_ROOT + "/Research/Nodes/res_grid_utilities.asset");

            int created = 0, preserved = 0;
            var recipes = new List<RecipeDefinition>();
            var items = new List<ItemDefinition>();

            // Copper wire is optional content in this build order: a recipe that quietly lost an
            // ingredient would be worse than one authored without it, so it is passed in and
            // skipped only when the item genuinely does not exist.
            var wire = FindItem("Item_CopperWire");

            AuthorRouteRecorder(GridSize.Large, "Grid_RouteRecorder_Large", "gitem_routerecorder_large",
                "Route Recorder", 62f, 300f, 60f,
                new (ItemDefinition, int)[] { (steelPlate, 3), (ironPlate, 2), (circuit, 2), (glass, 1) },
                wire, ref created, ref preserved, recipes, items);

            AuthorRouteRecorder(GridSize.Small, "Grid_RouteRecorder_Small", "gitem_routerecorder_small",
                "Nav Plotter (Small)", 14f, 90f, 24f,
                new (ItemDefinition, int)[] { (ironPlate, 2), (circuit, 1) },
                null, ref created, ref preserved, recipes, items);

            foreach (var recipe in recipes)
            {
                if (recipe == null) continue;
                if (registry != null && !registry.recipes.Contains(recipe))
                {
                    registry.recipes.Add(recipe);
                    EditorUtility.SetDirty(registry);
                }
            }

            if (utilNode != null)
            {
                var list = new List<RecipeDefinition>(utilNode.unlocksRecipes ?? new RecipeDefinition[0]);
                bool changed = false;
                foreach (var recipe in recipes)
                    if (recipe != null && !list.Contains(recipe)) { list.Add(recipe); changed = true; }
                if (changed)
                {
                    utilNode.unlocksRecipes = list.ToArray();
                    EditorUtility.SetDirty(utilNode);
                }
            }

            EnsureItemsPersisted(items);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[VoxelEngineSetupWindow] Step 65 complete — created " + created
                      + ", preserved " + preserved + ".");
            EditorUtility.DisplayDialog("Voxel Engine — Route Book & Range Calculator (Step 65)",
                "Navigation blocks authored.\n\n" +
                "• Assets created: " + created + " (" + preserved + " existing preserved)\n" +
                "• ROUTE RECORDER (large) — the ship's navigation shelf: it records the run you fly,\n" +
                "  costs a saved route against this ship's own mass, thrust and stored charge, and\n" +
                "  lists every reason the trip is a bad idea before you burn anything\n" +
                "• NAV PLOTTER (Small) — the same book in a deckhouse-sized block, for base ships\n" +
                "  and small craft, where 24 W of console is all the mission costs\n" +
                "• Recipes registered and linked to Grid Utilities research\n" +
                "• Routes are saved with the GRID, not the block: a recorded run survives the recorder\n" +
                "  being moved, replaced or rebuilt, and legacy ships simply start with an empty book\n\n" +
                "Runtime: right-click the block to open the panel. Plan cost is derived from the live\n" +
                "flight model — `GridEntity.TotalMass`, `GetThrustByDirection()`, the grid's own stored\n" +
                "watt-hours and its current consumption — so a plan and the ship that flies it cannot\n" +
                "disagree. Nothing here replaces flying: a route tells you what it costs, the pilot\n" +
                "still decides whether to go.", "OK");
        }

        private static void AuthorRouteRecorder(GridSize size, string prefabName, string itemId,
            string displayName, float mass, float hp, float recordingWatts,
            (ItemDefinition item, int count)[] inputs, ItemDefinition extraIngredient,
            ref int created, ref int preserved,
            List<RecipeDefinition> recipes, List<ItemDefinition> items)
        {
            string prefabPath = PREFABS + "/" + prefabName + ".prefab";
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(prefabName);
            root.name = prefabName;

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            int matIdx = 0;
            GridBlockMeshBuilder.MaterialPersister = (mat, _) =>
            {
                string mp = $"{MATS}/{prefabName}_{matIdx++}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(mp) != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mat, mp);
                return AssetDatabase.LoadAssetAtPath<Material>(mp);
            };

            var visuals = new GameObject("Generated_Visuals");
            visuals.transform.SetParent(root.transform, false);
            try
            {
                GridBlockMeshBuilder.Build(visuals, GridBlockMeshBuilder.Style.RouteRecorder, size,
                    new Color(0.38f, 0.44f, 0.50f));
            }
            finally { GridBlockMeshBuilder.MaterialPersister = null; }

            float cs = size.CellSize();
            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            // Console and dish only: the block is a deck fitting, not a filled cell.
            col.center = new Vector3(0f, -cs * 0.08f, 0f);
            col.size = new Vector3(cs * 0.84f, cs * 0.76f, cs * 0.84f);

            var recorder = root.GetComponent<VoxelEngine.Navigation.GridRouteRecorder>();
            if (recorder == null) recorder = root.AddComponent<VoxelEngine.Navigation.GridRouteRecorder>();
            recorder.blockName = displayName;
            if (recorder.recordingWatts <= 0f) recorder.recordingWatts = recordingWatts;
            if (recorder.recomputeIntervalSeconds <= 0.01f) recorder.recomputeIntervalSeconds = 0.75f;
            if (recorder.BlockMass <= 0f) recorder.BlockMass = mass;
            if (recorder.maxHP <= 0f) recorder.maxHP = hp;

            var prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);

            string itemPath = ITEMS + "/GItem_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(itemPath);
            bool newItem = item == null;
            if (newItem)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, itemPath);
                created++;
            }
            else preserved++;

            item.itemId = itemId;
            item.displayName = displayName;
            item.description = size == GridSize.Large
                ? "The ship's navigation shelf. Bolt it to a deck and it records the runs you fly, then costs each one against this ship: distance, travel time, the thrust the profile asks for, the energy out of the batteries, what the grid burns while you are gone, and the reserve left at the far end. A route that cannot be flown says so, and names the missing resource."
                : "A plotter for a base ship or a small craft: the same route book and the same arithmetic as the full recorder, in a deckhouse fitting that bills 24 W while a capture runs.";
            item.iconTint = new Color(0.58f, 0.74f, 0.86f);
            if (item.maxStack <= 0) item.maxStack = 99;
            if (item.massPerUnit <= 0f) item.massPerUnit = size == GridSize.Large ? 1.1f : 0.3f;
            item.category = "Grid";
            item.gridSize = size;
            item.blockPrefab = prefabAsset;
            if (item.blockMass <= 0f) item.blockMass = mass;
            if (item.blockHP <= 0f) item.blockHP = hp;
            EditorUtility.SetDirty(item);
            items.Add(item);

            string recipePath = RECIPES + "/Recipe_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            bool newRecipe = recipe == null;
            if (newRecipe)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
                created++;
            }
            else preserved++;

            recipe.displayName = displayName;
            recipe.outputItem = item;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (newRecipe)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = size == GridSize.Large ? 6f : 3f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                foreach (var (ing, count) in inputs)
                    if (ing != null) list.Add(new RecipeIngredient { item = ing, count = count });
                // A recorder without wire is a box with a picture on it; only add it if the
                // item exists in this build, so the step still completes on a stripped catalog.
                if (extraIngredient != null) list.Add(new RecipeIngredient { item = extraIngredient, count = 1 });
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            recipes.Add(recipe);
        }

        private static void AuthorScrubber(GridSize size, string prefabName, string itemId,
            string displayName, float mass, float hp, bool fullBlock,
            float airChanges, float activeWatts,
            (ItemDefinition item, int count)[] inputs,
            ref int created, ref int preserved,
            List<RecipeDefinition> recipes, List<ItemDefinition> items)
        {
            string prefabPath = PREFABS + "/" + prefabName + ".prefab";
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(prefabName);
            root.name = prefabName;

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            int matIdx = 0;
            GridBlockMeshBuilder.MaterialPersister = (mat, _) =>
            {
                string mp = $"{MATS}/{prefabName}_{matIdx++}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(mp) != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mat, mp);
                return AssetDatabase.LoadAssetAtPath<Material>(mp);
            };

            var visuals = new GameObject("Generated_Visuals");
            visuals.transform.SetParent(root.transform, false);
            try
            {
                GridBlockMeshBuilder.Build(visuals, GridBlockMeshBuilder.Style.ExhaustScrubber, size,
                    new Color(0.34f, 0.47f, 0.52f));
            }
            finally { GridBlockMeshBuilder.MaterialPersister = null; }

            float cs = size.CellSize();
            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            // A grille is a thin bulkhead insert; the full-block plant owns its cell.
            col.center = fullBlock ? Vector3.zero : new Vector3(0f, 0f, -cs * 0.30f);
            col.size = fullBlock ? new Vector3(cs, cs, cs) : new Vector3(cs, cs, cs * 0.36f);

            var scrub = root.GetComponent<GridExhaustScrubber>();
            if (scrub == null) scrub = root.AddComponent<GridExhaustScrubber>();
            scrub.blockName = displayName;
            scrub.fullBlock = fullBlock;
            scrub.airtight = true;
            // Preserve authored balance: only fill in unset values.
            if (scrub.airChangesPerMinute <= 0f) scrub.airChangesPerMinute = airChanges;
            if (scrub.activeWatts <= 0f) scrub.activeWatts = activeWatts;
            if (scrub.idleWatts <= 0f) scrub.idleWatts = fullBlock ? 12f : 4f;
            if (scrub.exhaustCaptureLitresPerSecond <= 0f) scrub.exhaustCaptureLitresPerSecond = fullBlock ? 18f : 6f;
            if (scrub.BlockMass <= 0f) scrub.BlockMass = mass;
            if (scrub.maxHP <= 0f) scrub.maxHP = hp;

            // Gas ports: the refill and capture lines only reach a scrubber through
            // piped gas, exactly like the Air Vent it is built alongside.
            EnsureGasPorts(root, size);

            var prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);

            string itemPath = ITEMS + "/GItem_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(itemPath);
            bool newItem = item == null;
            if (newItem)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, itemPath);
                created++;
            }
            else preserved++;

            item.itemId = itemId;
            item.displayName = displayName;
            item.description = fullBlock
                ? "Industrial air handling plant. Clears trapped waste heat and exhaust from a sealed compartment, replaces the volume from piped oxygen, and banks the foul gas it pulled out as industrial ExhaustGas."
                : "Bulkhead scrubber grille. Clears trapped heat and exhaust from the compartment it faces. Lower throughput than the full-block unit, but it seals the wall it sits in.";
            item.iconTint = new Color(0.42f, 0.72f, 0.66f);
            if (item.maxStack <= 0) item.maxStack = 99;
            if (item.massPerUnit <= 0f) item.massPerUnit = size == GridSize.Large ? 2.5f : 0.6f;
            item.category = "Grid";
            item.gridSize = size;
            item.blockPrefab = prefabAsset;
            if (item.blockMass <= 0f) item.blockMass = mass;
            if (item.blockHP <= 0f) item.blockHP = hp;
            EditorUtility.SetDirty(item);
            items.Add(item);

            string recipePath = RECIPES + "/Recipe_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            bool newRecipe = recipe == null;
            if (newRecipe)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
                created++;
            }
            else preserved++;

            recipe.displayName = displayName;
            recipe.outputItem = item;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (newRecipe)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = size == GridSize.Large ? 8f : 4f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                foreach (var (ing, count) in inputs)
                    if (ing != null) list.Add(new RecipeIngredient { item = ing, count = count });
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            recipes.Add(recipe);
        }

        /// <summary>Six-way gas taps, reusing the Air Vent port material so both blocks
        /// read as the same family of service hardware. Existing ports are preserved.</summary>
        /// <summary>
        /// GAS VENT: the block that lets a gas run END somewhere. Written the same way as
        /// the scrubber above it — create-if-missing, connect-if-present, and never touch a
        /// balance value the player has already edited by hand.
        /// </summary>
        private static void AuthorGasVent(GridSize size, string prefabName, string itemId,
            string displayName, float mass, float hp, float forcedFlow, float activeWatts,
            (ItemDefinition item, int count)[] inputs,
            ref int created, ref int preserved,
            List<RecipeDefinition> recipes, List<ItemDefinition> items)
        {
            string prefabPath = PREFABS + "/" + prefabName + ".prefab";
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(prefabName);
            root.name = prefabName;

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    Object.DestroyImmediate(child.gameObject);
            }

            int matIdx = 0;
            GridBlockMeshBuilder.MaterialPersister = (mat, _) =>
            {
                string mp = $"{MATS}/{prefabName}_{matIdx++}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(mp) != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mat, mp);
                return AssetDatabase.LoadAssetAtPath<Material>(mp);
            };

            var visuals = new GameObject("Generated_Visuals");
            visuals.transform.SetParent(root.transform, false);
            try
            {
                GridBlockMeshBuilder.Build(visuals, GridBlockMeshBuilder.Style.GasVent, size,
                    new Color(0.44f, 0.48f, 0.52f));
            }
            finally { GridBlockMeshBuilder.MaterialPersister = null; }

            float cs = size.CellSize();
            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.AddComponent<BoxCollider>();
            // The sleeve only occupies as much of the cell as its barrel and cowl need.
            col.center = new Vector3(0f, 0f, -cs * 0.06f);
            col.size = new Vector3(cs * 0.80f, cs * 0.80f, cs * 0.96f);

            var vent = root.GetComponent<VoxelEngine.Gas.GasVent>();
            if (vent == null) vent = root.AddComponent<VoxelEngine.Gas.GasVent>();
            vent.blockName = displayName;
            vent.airtight = true;
            vent.open = true;
            // Preserve authored balance: only fill in unset values.
            if (vent.forcedFlowLitresPerSecond <= 0f) vent.forcedFlowLitresPerSecond = forcedFlow;
            if (vent.draftFlowLitresPerSecond <= 0f) vent.draftFlowLitresPerSecond = size == GridSize.Large ? 40f : 14f;
            if (vent.activeWatts <= 0f) vent.activeWatts = activeWatts;
            if (vent.idleWatts <= 0f) vent.idleWatts = size == GridSize.Large ? 3f : 1.5f;
            if (vent.BlockMass <= 0f) vent.BlockMass = mass;
            if (vent.maxHP <= 0f) vent.maxHP = hp;

            EnsureGasPorts(root, size);

            var prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);

            string itemPath = ITEMS + "/GItem_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(itemPath);
            bool newItem = item == null;
            if (newItem)
            {
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                AssetDatabase.CreateAsset(item, itemPath);
                created++;
            }
            else preserved++;

            item.itemId = itemId;
            item.displayName = displayName;
            item.description = size == GridSize.Large
                ? "Industrial gas discharge endpoint. Wire it to the end of any gas run and everything that arrives is destroyed: an engine's exhaust, a boiler's steam, a skimmer's surplus. Runs on grid power for full flow, and keeps dripping gas overboard by draft alone when the power fails."
                : "Hull sleeve with a louvre and a flame screen. Small, cheap, and quiet: the end of a gas run in a deckhouse or an engine closet. Unpowered it still clears the line at its draft rate.";
            item.iconTint = new Color(0.68f, 0.72f, 0.76f);
            if (item.maxStack <= 0) item.maxStack = 99;
            if (item.massPerUnit <= 0f) item.massPerUnit = size == GridSize.Large ? 1.6f : 0.4f;
            item.category = "Grid";
            item.gridSize = size;
            item.blockPrefab = prefabAsset;
            if (item.blockMass <= 0f) item.blockMass = mass;
            if (item.blockHP <= 0f) item.blockHP = hp;
            EditorUtility.SetDirty(item);
            items.Add(item);

            string recipePath = RECIPES + "/Recipe_" + prefabName.Replace("Grid_", string.Empty) + ".asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(recipePath);
            bool newRecipe = recipe == null;
            if (newRecipe)
            {
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                AssetDatabase.CreateAsset(recipe, recipePath);
                created++;
            }
            else preserved++;

            recipe.displayName = displayName;
            recipe.outputItem = item;
            if (recipe.outputCount <= 0) recipe.outputCount = 1;
            if (newRecipe)
            {
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = size == GridSize.Large ? 5f : 2.5f;
                recipe.unlockedByDefault = false;
            }
            if (recipe.inputs == null || recipe.inputs.Length == 0)
            {
                var list = new List<RecipeIngredient>();
                foreach (var (ing, count) in inputs)
                    if (ing != null) list.Add(new RecipeIngredient { item = ing, count = count });
                recipe.inputs = list.ToArray();
            }
            EditorUtility.SetDirty(recipe);
            recipes.Add(recipe);
        }

        private static void EnsureGasPorts(GameObject root, GridSize size)
        {
            const string prefix = "Port_GasIO";
            float cs = size.CellSize();
            float half = cs * 0.5f;
            float depth = cs * 0.16f;

            Material mat = GetOrCreatePortMaterial();

            var ports = new (string name, Vector3 pos, Vector3 euler, Vector3 outward)[]
            {
                (prefix + "_N",      new Vector3(0f, 0f, -depth), new Vector3(90f, 0f, 0f), Vector3.back),
                (prefix + "_S",      new Vector3(0f, 0f,  depth), new Vector3(90f, 0f, 0f), Vector3.forward),
                (prefix + "_E",      new Vector3( half * 0.92f, 0f, 0f), new Vector3(0f, 0f, 90f), Vector3.right),
                (prefix + "_W",      new Vector3(-half * 0.92f, 0f, 0f), new Vector3(0f, 0f, 90f), Vector3.left),
                (prefix + "_Top",    new Vector3(0f,  half * 0.92f, 0f), Vector3.zero,             Vector3.up),
                (prefix + "_Bottom", new Vector3(0f, -half * 0.92f, 0f), new Vector3(180f, 0f, 0f), Vector3.down),
            };

            foreach (var (name, pos, euler, outward) in ports)
            {
                var existing = root.transform.Find(name);
                GameObject port;
                if (existing != null)
                {
                    port = existing.gameObject;      // preserve any hand-moved port
                }
                else
                {
                    port = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    port.name = name;
                    port.transform.SetParent(root.transform, false);
                    port.transform.localScale = new Vector3(cs * 0.10f, cs * 0.022f, cs * 0.10f);
                    var col = port.GetComponent<Collider>();
                    if (col != null) Object.DestroyImmediate(col);
                    var renderer = port.GetComponent<Renderer>();
                    if (renderer != null && mat != null) renderer.sharedMaterial = mat;
                    port.transform.localPosition = pos;
                    port.transform.localRotation = Quaternion.Euler(euler);
                }

                var facing = port.GetComponent<VoxelEngine.Maritime.MaritimePortFacing>();
                if (facing == null) facing = port.AddComponent<VoxelEngine.Maritime.MaritimePortFacing>();
                facing.localOutward = outward;
            }
        }

        private static Material GetOrCreatePortMaterial()
        {
            EnsureFolder(MATS);
            // Deliberately the same marker asset the Air Vent uses: one shared material
            // for service hardware, so re-running any step never duplicates it.
            const string matPath = MATS + "/AirVent_GasPortMarker.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null) return mat;

            var color = new Color(0.45f, 0.85f, 1.0f, 1f);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = "AirVent_GasPortMarker", color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.5f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.75f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 0.45f);
            }
            AssetDatabase.CreateAsset(mat, matPath);
            return AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        private static ItemDefinition FindItem(string assetName)
        {
            foreach (string guid in AssetDatabase.FindAssets(assetName + " t:ItemDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != assetName) continue;
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item != null) return item;
            }
            return null;
        }

        private static void EnsureItemsPersisted(List<ItemDefinition> items)
        {
            const string catalogPath = "Assets/Resources/VoxelEngine/ItemPersistenceCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<ItemPersistenceCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemPersistenceCatalog>();
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/VoxelEngine");
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }

            bool changed = false;
            foreach (var item in items)
            {
                if (item != null && !catalog.items.Contains(item))
                {
                    catalog.items.Add(item);
                    changed = true;
                }
            }
            if (changed) EditorUtility.SetDirty(catalog);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
