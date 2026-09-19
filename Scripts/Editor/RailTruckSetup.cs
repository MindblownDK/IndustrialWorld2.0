#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 92 (11.31.0-dev): the Rail Truck — Train System v2, phase 1.
    ///
    /// One grid block. Put it on any construct you have built and that construct can run
    /// on rails: no locomotive entity, no special vehicle type. A wagon carries containers
    /// and tanks because it is a grid and those are grid blocks.
    ///
    /// Non-destructive: an existing prefab, item or recipe keeps every authored value and
    /// only missing links are repaired. Safe to re-run. Logs with the [Setup 92] prefix.
    /// </summary>
    public static class RailTruckSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string GridPrefabsFolder = Root + "/GridSystem/Prefabs";
        private const string GridItemsFolder = Root + "/GridSystem/Items";
        private const string GridRecipesFolder = Root + "/GridSystem/Recipes";

        private static readonly Color TruckTint = new(0.42f, 0.45f, 0.50f);
        private static readonly Color WheelTint = new(0.17f, 0.18f, 0.21f);
        private static readonly Color SpringTint = new(0.62f, 0.64f, 0.68f);
        /// <summary>Safety orange, like the brake gear on the reference bogie.</summary>
        private static readonly Color AccentTint = new(0.88f, 0.48f, 0.13f);

        public static void RunStep92()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Rail Truck", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Rail Truck",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.",
                        "OK");
                    return;
                }

                var steel = FindItem("Item_SteelIngot");
                var wire = FindItem("Item_CopperWire");
                if (steel == null)
                {
                    EditorUtility.DisplayDialog("Rail Truck",
                        "Steel Ingot not found. Run the earlier crafting-content steps first.", "OK");
                    return;
                }

                var prefab = EnsurePrefab(out bool prefabChanged);
                var item = EnsureItem(prefab, out bool itemChanged);
                bool recipeChanged = EnsureRecipe(registry, item, steel, wire);

                var couplerPrefab = EnsureCouplerPrefab(out bool couplerPrefabChanged);
                var couplerItem = EnsureCouplerItem(couplerPrefab, out bool couplerItemChanged);
                bool couplerRecipeChanged = EnsureCouplerRecipe(registry, couplerItem, steel, wire);

                prefabChanged |= couplerPrefabChanged;
                itemChanged |= couplerItemChanged;
                recipeChanged |= couplerRecipeChanged;

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("Step 92 - Rail Truck",
                    "Rail Truck authored.\n\n" +
                    "  RAIL TRUCK      Steel x18" + (wire != null ? " + Wire x10" : "") + "\n" +
                    "  WAGON COUPLER   Steel x8" + (wire != null ? " + Wire x4" : "") + "\n\n" +
                    "Train System v2: a train is now an ordinary construct.\n\n" +
                    "  1. Build any grid you like.\n" +
                    "  2. Place a Rail Truck on it.\n" +
                    "  3. Park it within a few metres of track.\n" +
                    "  4. Open the truck and press SNAP TO RAIL, then DRIVE.\n\n" +
                    "Because it is a grid, it can carry containers, tanks,\n" +
                    "refineries or turrets, and takes damage, paint, power and\n" +
                    "pressurisation exactly like anything else you build.\n\n" +
                    "The 11.15.0 rail network (track, switches, buffers) is\n" +
                    "unchanged and is what these run on.\n\n" +
                    ((prefabChanged || itemChanged || recipeChanged)
                        ? "Changes were written. See the Console."
                        : "Everything was already in place."),
                    "OK");

                Debug.Log("[Setup 92] Rail Truck setup complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 92] Aborted: " + ex);
                EditorUtility.DisplayDialog("Rail Truck",
                    "Setup stopped: " + ex.Message + "\n\nNothing further was written.", "OK");
            }
        }

        private static GameObject EnsurePrefab(out bool changed)
        {
            string path = GridPrefabsFolder + "/RailTruck.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject("RailTruck");
                var body = MakeMat("Mat_RailTruck", TruckTint);
                var wheel = MakeMat("Mat_RailTruckWheel", WheelTint);
                var spring = MakeMat("Mat_RailTruckSpring", SpringTint);
                var accent = MakeMat("Mat_RailTruckAccent", AccentTint);

                // A real bogie, not a plate on wheels.
                //
                // Modelled on the reference: two side frames carrying the axleboxes, a
                // bolster across the middle that the wagon actually rests on, a centre
                // pivot, and visible coil springs over each axlebox. The springs matter
                // more than they look - they are what makes it read as something that
                // CARRIES weight, which is exactly the mechanic the load model implements.
                //
                // Gauge matches the track authored in step 85 (3.15 m between rail centres
                // since the 11.41.0 triple-width formation), so the wheels sit on the rails
                // rather than beside them.
                const float gauge = TrackGauge;
                const float axleZ = 0.62f;      // half the wheelbase
                const float wheelRadius = 0.30f;

                // ── Side frames ──
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? -gauge * 0.5f - 0.10f : gauge * 0.5f + 0.10f;

                    var sideFrame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    sideFrame.name = side == 0 ? "SideFrameL" : "SideFrameR";
                    sideFrame.transform.SetParent(root.transform, false);
                    sideFrame.transform.localPosition = new Vector3(x, wheelRadius + 0.10f, 0f);
                    sideFrame.transform.localScale = new Vector3(0.16f, 0.20f, 1.72f);
                    Paint(sideFrame, body);

                    // Axleboxes: the blocks the springs sit on, one over each wheel.
                    for (int a = 0; a < 2; a++)
                    {
                        float z = a == 0 ? -axleZ : axleZ;

                        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        box.name = "Axlebox" + side + a;
                        box.transform.SetParent(root.transform, false);
                        box.transform.localPosition = new Vector3(x, wheelRadius + 0.02f, z);
                        box.transform.localScale = new Vector3(0.22f, 0.24f, 0.30f);
                        Paint(box, body);

                        // Coil spring, drawn as a short stack of thin discs so it reads as a
                        // spring at a distance without needing a custom mesh.
                        for (int coil = 0; coil < 3; coil++)
                        {
                            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                            ring.name = "Spring" + side + a + coil;
                            ring.transform.SetParent(root.transform, false);
                            ring.transform.localPosition =
                                new Vector3(x, wheelRadius + 0.20f + coil * 0.07f, z);
                            ring.transform.localScale = new Vector3(0.17f, 0.022f, 0.17f);
                            Paint(ring, spring);
                        }
                    }
                }

                // ── Bolster: the cross-beam the wagon body rests on ──
                var bolster = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bolster.name = "Bolster";
                bolster.transform.SetParent(root.transform, false);
                bolster.transform.localPosition = new Vector3(0f, wheelRadius + 0.34f, 0f);
                bolster.transform.localScale = new Vector3(gauge + 0.42f, 0.16f, 0.46f);
                Paint(bolster, body);

                // ── Centre pivot: what the wagon actually turns about ──
                var pivot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pivot.name = "CentrePivot";
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = new Vector3(0f, wheelRadius + 0.46f, 0f);
                pivot.transform.localScale = new Vector3(0.34f, 0.06f, 0.34f);
                Paint(pivot, accent);

                // ── Brake gear, in the accent colour like the reference ──
                for (int a = 0; a < 2; a++)
                {
                    float z = a == 0 ? -axleZ : axleZ;
                    var brake = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    brake.name = "BrakeBeam" + a;
                    brake.transform.SetParent(root.transform, false);
                    brake.transform.localPosition = new Vector3(0f, wheelRadius - 0.04f, z * 0.55f);
                    brake.transform.localScale = new Vector3(gauge + 0.12f, 0.07f, 0.09f);
                    Paint(brake, accent);
                }

                // ── Wheelsets: four flanged wheels on two axles ──
                for (int i = 0; i < 4; i++)
                {
                    float x = (i % 2 == 0) ? -gauge * 0.5f : gauge * 0.5f;
                    float z = (i < 2) ? -axleZ : axleZ;

                    var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    w.name = "Wheel" + i;
                    w.transform.SetParent(root.transform, false);
                    w.transform.localPosition = new Vector3(x, wheelRadius, z);
                    w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    w.transform.localScale = new Vector3(wheelRadius * 2f, 0.06f, wheelRadius * 2f);
                    Paint(w, wheel);

                    // Flange: a slightly larger, thinner disc inboard of the tread, which is
                    // what visually keeps the wheel on the rail.
                    var flange = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    flange.name = "Flange" + i;
                    flange.transform.SetParent(root.transform, false);
                    float inboard = x > 0f ? -0.045f : 0.045f;
                    flange.transform.localPosition = new Vector3(x + inboard, wheelRadius, z);
                    flange.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    flange.transform.localScale = new Vector3(wheelRadius * 2.2f, 0.022f, wheelRadius * 2.2f);
                    Paint(flange, wheel);
                }

                // ── Axles ──
                for (int a = 0; a < 2; a++)
                {
                    float z = a == 0 ? -axleZ : axleZ;
                    var axle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    axle.name = "Axle" + a;
                    axle.transform.SetParent(root.transform, false);
                    axle.transform.localPosition = new Vector3(0f, wheelRadius, z);
                    axle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    axle.transform.localScale = new Vector3(0.10f, gauge * 0.5f, 0.10f);
                    Paint(axle, wheel);
                }

                var truck = root.AddComponent<GridRailTruck>();
                truck.blockName = "Rail Truck";
                truck.BlockMass = 260f;
                truck.maxHP = 420f;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 92] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            if (contents.GetComponent<GridRailTruck>() == null)
            {
                var truck = contents.AddComponent<GridRailTruck>();
                truck.blockName = "Rail Truck";
                dirty = true;
                Debug.Log("[Setup 92] Prefab had no GridRailTruck; added one.");
            }

            // The 11.41.0 formation is three times the width the first bogie was authored
            // against; a bogie left at the old gauge would run its wheels down the middle of
            // the sleepers. Geometry-only pass: nothing else on the prefab is touched.
            dirty |= RegaugeBogie(contents);

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return result;
        }

        /// <summary>Rail centres apart, in metres. Matches step 85's formation exactly - the
        /// wheels and the rail heads are authored from the same number on purpose.</summary>
        private const float TrackGauge = 3.15f;

        /// <summary>
        /// Moves an existing bogie's running gear out to the current gauge. Idempotent: a
        /// prefab already at the gauge is left byte-identical, and only the named running-gear
        /// children move - body, materials and component tuning are never touched.
        /// </summary>
        private static bool RegaugeBogie(GameObject root)
        {
            var wheel0 = root.transform.Find("Wheel0");
            if (wheel0 == null) return false;
            float current = Mathf.Abs(wheel0.localPosition.x) * 2f;
            if (Mathf.Abs(current - TrackGauge) < 0.001f) return false;

            const float axleZ = 0.62f;
            const float wheelRadius = 0.30f;
            float half = TrackGauge * 0.5f;

            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -half - 0.10f : half + 0.10f;

                var frame = root.transform.Find(side == 0 ? "SideFrameL" : "SideFrameR");
                if (frame != null) frame.localPosition = new Vector3(x, wheelRadius + 0.10f, 0f);

                for (int a = 0; a < 2; a++)
                {
                    float z = a == 0 ? -axleZ : axleZ;
                    var box = root.transform.Find("Axlebox" + side + a);
                    if (box != null) box.localPosition = new Vector3(x, wheelRadius + 0.02f, z);

                    for (int coil = 0; coil < 3; coil++)
                    {
                        var ring = root.transform.Find("Spring" + side + a + coil);
                        if (ring != null)
                            ring.localPosition = new Vector3(x, wheelRadius + 0.20f + coil * 0.07f, z);
                    }
                }
            }

            var bolster = root.transform.Find("Bolster");
            if (bolster != null) bolster.localScale = new Vector3(TrackGauge + 0.42f, bolster.localScale.y, bolster.localScale.z);

            for (int a = 0; a < 2; a++)
            {
                var brake = root.transform.Find("BrakeBeam" + a);
                if (brake != null) brake.localScale = new Vector3(TrackGauge + 0.12f, brake.localScale.y, brake.localScale.z);

                var axle = root.transform.Find("Axle" + a);
                if (axle != null) axle.localScale = new Vector3(axle.localScale.x, TrackGauge * 0.5f, axle.localScale.z);
            }

            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0) ? -half : half;
                var w = root.transform.Find("Wheel" + i);
                if (w != null) w.localPosition = new Vector3(x, wheelRadius, w.localPosition.z);

                var flange = root.transform.Find("Flange" + i);
                if (flange != null)
                {
                    float inboard = x > 0f ? -0.045f : 0.045f;
                    flange.localPosition = new Vector3(x + inboard, wheelRadius, flange.localPosition.z);
                }
            }

            Debug.Log("[Setup 92] Re-gauged the Rail Truck bogie to " + TrackGauge.ToString("0.00") + " m.");
            return true;
        }

        private static GridBlockItem EnsureItem(GameObject prefab, out bool changed)
        {
            string path = GridItemsFolder + "/GItem_RailTruck.asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool created = false;

            if (item == null)
            {
                EnsureFolder(GridItemsFolder);
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                created = true;
            }
            bool dirty = created;

            if (item.itemId != "railtruck") { item.itemId = "railtruck"; dirty = true; }
            if (item.displayName != "Rail Truck") { item.displayName = "Rail Truck"; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 20; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = 260f; dirty = true; }
            if (item.category != "Grid Blocks") { item.category = "Grid Blocks"; dirty = true; }
            if (string.IsNullOrEmpty(item.description))
            {
                item.description =
                    "Puts a construct on rails. Any grid with a Rail Truck can run on track, " +
                    "so a train is just something you built - it carries whatever grid blocks " +
                    "you put on it. The slowest truck aboard sets the speed.";
                dirty = true;
            }
            if (item.icon == null) item.iconTint = TruckTint;
            if (created) { item.blockMass = 260f; item.blockHP = 420f; }

            if (item.blockPrefab == null || item.blockPrefab != prefab)
            {
                item.blockPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                Debug.Log("[Setup 92] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return item;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, GridBlockItem item,
            ItemDefinition steel, ItemDefinition wire)
        {
            const string stem = "Recipe_RailTruck";
            var recipe = FindRecipe(stem);
            bool changed = false;

            RecipeIngredient[] Inputs()
            {
                if (wire == null)
                    return new[] { new RecipeIngredient { item = steel, count = 18 } };
                return new[]
                {
                    new RecipeIngredient { item = steel, count = 18 },
                    new RecipeIngredient { item = wire, count = 10 },
                };
            }

            if (recipe == null)
            {
                EnsureFolder(GridRecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Rail Truck";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 14f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = item;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, GridRecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 92] Created " + stem + ".");
            }
            else
            {
                if (recipe.outputItem == null && item != null)
                {
                    recipe.outputItem = item;
                    recipe.outputCount = 1;
                    EditorUtility.SetDirty(recipe);
                    changed = true;
                }
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    recipe.inputs = Inputs();
                    EditorUtility.SetDirty(recipe);
                    changed = true;
                }
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 92] Added " + stem + " to RecipeRegistry.");
                changed = true;
            }

            return changed;
        }

        // ============================================================
        //                      Wagon coupler
        // ============================================================
        private static GameObject EnsureCouplerPrefab(out bool changed)
        {
            string path = GridPrefabsFolder + "/RailCoupler.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject("RailCoupler");
                var body = MakeMat("Mat_RailTruck", TruckTint);
                var accent = MakeMat("Mat_RailTruckAccent", AccentTint);

                // A drawhook on a headstock: reads as the thing between two wagons.
                var headstock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                headstock.name = "Headstock";
                headstock.transform.SetParent(root.transform, false);
                headstock.transform.localScale = new Vector3(1.3f, 0.34f, 0.22f);
                headstock.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                Paint(headstock, body);

                var shank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shank.name = "Shank";
                shank.transform.SetParent(root.transform, false);
                shank.transform.localScale = new Vector3(0.18f, 0.18f, 0.42f);
                shank.transform.localPosition = new Vector3(0f, 0.45f, 0.26f);
                Paint(shank, accent);

                var knuckle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                knuckle.name = "Knuckle";
                knuckle.transform.SetParent(root.transform, false);
                knuckle.transform.localScale = new Vector3(0.30f, 0.26f, 0.20f);
                knuckle.transform.localPosition = new Vector3(0f, 0.45f, 0.50f);
                Paint(knuckle, accent);

                // Buffers either side, which is what actually takes the shove.
                for (int i = 0; i < 2; i++)
                {
                    var buffer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    buffer.name = "Buffer" + i;
                    buffer.transform.SetParent(root.transform, false);
                    buffer.transform.localPosition =
                        new Vector3(i == 0 ? -0.46f : 0.46f, 0.45f, 0.24f);
                    buffer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    buffer.transform.localScale = new Vector3(0.20f, 0.14f, 0.20f);
                    Paint(buffer, body);
                }

                var coupler = root.AddComponent<GridRailCoupler>();
                coupler.blockName = "Wagon Coupler";
                coupler.BlockMass = 90f;
                coupler.maxHP = 260f;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 92] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            if (contents.GetComponent<GridRailCoupler>() == null)
            {
                var c = contents.AddComponent<GridRailCoupler>();
                c.blockName = "Wagon Coupler";
                dirty = true;
                Debug.Log("[Setup 92] Coupler prefab had no GridRailCoupler; added one.");
            }

            GameObject result = existing;
            if (dirty) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = dirty;
            return result;
        }

        private static GridBlockItem EnsureCouplerItem(GameObject prefab, out bool changed)
        {
            string path = GridItemsFolder + "/GItem_RailCoupler.asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool created = false;

            if (item == null)
            {
                EnsureFolder(GridItemsFolder);
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                created = true;
            }
            bool dirty = created;

            if (item.itemId != "railcoupler") { item.itemId = "railcoupler"; dirty = true; }
            if (item.displayName != "Wagon Coupler") { item.displayName = "Wagon Coupler"; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 40; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = 90f; dirty = true; }
            if (item.category != "Grid Blocks") { item.category = "Grid Blocks"; dirty = true; }
            if (string.IsNullOrEmpty(item.description))
            {
                item.description =
                    "Joins one car to the next. Park a railed construct behind another and " +
                    "use the coupler to attach it; use it again to release. The car in front " +
                    "does the pulling, so only the leader needs to be powered.";
                dirty = true;
            }
            if (item.icon == null) item.iconTint = AccentTint;
            if (created) { item.blockMass = 90f; item.blockHP = 260f; }

            if (item.blockPrefab == null || item.blockPrefab != prefab)
            {
                item.blockPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                Debug.Log("[Setup 92] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            changed = dirty;
            return item;
        }

        private static bool EnsureCouplerRecipe(RecipeRegistry registry, GridBlockItem item,
            ItemDefinition steel, ItemDefinition wire)
        {
            const string stem = "Recipe_RailCoupler";
            var recipe = FindRecipe(stem);
            bool changed = false;

            RecipeIngredient[] Inputs()
            {
                if (wire == null)
                    return new[] { new RecipeIngredient { item = steel, count = 8 } };
                return new[]
                {
                    new RecipeIngredient { item = steel, count = 8 },
                    new RecipeIngredient { item = wire, count = 4 },
                };
            }

            if (recipe == null)
            {
                EnsureFolder(GridRecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = "Wagon Coupler";
                recipe.requiredStation = StationTier.Assembler;
                recipe.craftSeconds = 8f;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = item;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, GridRecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 92] Created " + stem + ".");
            }
            else
            {
                if (recipe.outputItem == null && item != null)
                {
                    recipe.outputItem = item;
                    recipe.outputCount = 1;
                    EditorUtility.SetDirty(recipe);
                    changed = true;
                }
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                {
                    recipe.inputs = Inputs();
                    EditorUtility.SetDirty(recipe);
                    changed = true;
                }
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 92] Added " + stem + " to RecipeRegistry.");
                changed = true;
            }

            return changed;
        }

        // ============================================================
        //                        Helpers
        // ============================================================
        private static ItemDefinition FindItem(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:ItemDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            }
            return null;
        }

        private static RecipeDefinition FindRecipe(string stem)
        {
            var guids = AssetDatabase.FindAssets(stem + " t:RecipeDefinition");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == stem)
                    return AssetDatabase.LoadAssetAtPath<RecipeDefinition>(p);
            }
            return null;
        }

        private static void Paint(GameObject go, Material mat)
        {
            if (mat == null) return;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static Material MakeMat(string name, Color c)
        {
            EnsureFolder(GridPrefabsFolder);
            string path = GridPrefabsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 92] Preserved conflicting asset at '" + path + "'.");
                return null;
            }
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(sh) { name = name, color = c };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
#endif
