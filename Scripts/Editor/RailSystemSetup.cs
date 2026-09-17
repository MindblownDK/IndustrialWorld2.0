#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 85 (11.15.0-dev): author the Rail System.
    ///
    /// Five assets that only make sense together:
    ///
    ///   Rail Track    - one cell of permanent way. Auto-connects to neighbours and
    ///                   refuses a gradient a train could not pull.
    ///   Rail Switch   - a junction cell; holds up to four connections and picks one.
    ///   Rail Buffer   - a line end. Stops a train at the railhead.
    ///   Rail Station  - a named stop with a cargo hold and a load/unload role.
    ///   Locomotive    - the scheduled hauler that walks the graph.
    ///
    /// Non-destructive: missing assets are created, existing ones only have
    /// unresolvable links repaired (null prefab, null output item, missing registry
    /// membership). Authored tuning, craft times, health and icons are never reset.
    /// Safe to re-run. Logs with the [Setup 85] prefix.
    /// </summary>
    public static class RailSystemSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";

        private static readonly Color RailTint = new(0.55f, 0.57f, 0.62f);
        private static readonly Color StationTint = new(0.78f, 0.68f, 0.40f);
        private static readonly Color LocoTint = new(0.36f, 0.44f, 0.56f);

        private const string SteelPath = Root + "/Items/Item_SteelIngot.asset";
        private const string WirePath = Root + "/Industrial/Items/Item_CopperWire.asset";

        public static void RunStep85()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Rail System", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Rail System",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelPath);
                var wire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WirePath);
                if (steel == null || wire == null)
                {
                    Debug.LogError("[Setup 85] An ingredient did not resolve (steel: " + (steel != null) +
                                   ", copper wire: " + (wire != null) + ").");
                    EditorUtility.DisplayDialog("Rail System",
                        "An ingredient is missing.\n\nRun the earlier crafting-content steps first.", "OK");
                    return;
                }

                bool any = false;
                any |= BuildTrackPiece("RailTrack", "Rail Track", RailPieceKind.Straight,
                    "One cell of permanent way. Lay cells end to end and the line forms itself. " +
                    "Refuses a gradient steeper than a train can pull.", steel, 4, wire, 0, registry);

                any |= BuildTrackPiece("RailSwitch", "Rail Switch", RailPieceKind.Switch,
                    "A junction. Holds up to four connections; right-click to set the points.",
                    steel, 6, wire, 2, registry);

                any |= BuildTrackPiece("RailBuffer", "Rail Buffer", RailPieceKind.Buffer,
                    "A line end. Stops a train at the railhead instead of letting it run off.",
                    steel, 3, wire, 0, registry);

                any |= BuildStation(steel, wire, registry);
                any |= BuildLocomotive(steel, wire, registry);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[Setup 85] Rail System setup complete. " +
                          (any ? "Changes were written." : "Everything was already in place."));

                EditorUtility.DisplayDialog("Step 85 - Rail System",
                    "Rail System authored.\n\n" +
                    "  RAIL TRACK    Steel x4        - auto-connecting permanent way\n" +
                    "  RAIL SWITCH   Steel x6 + Wire x2 - junction, right-click to set\n" +
                    "  RAIL BUFFER   Steel x3        - line end\n" +
                    "  RAIL STATION  Steel x20 + Wire x8 - named stop with a cargo hold\n" +
                    "  LOCOMOTIVE    Steel x40 + Wire x20 - scheduled hauler\n\n" +
                    "To run a line:\n" +
                    "  1. Lay track between two sites. Keep the gradient gentle.\n" +
                    "  2. Place a station beside the track at each end and name it.\n" +
                    "  3. Set one station to LOAD and the other to UNLOAD.\n" +
                    "  4. Place a locomotive on the track, add both stops, start the schedule.\n\n" +
                    (any ? "Changes were written. See the Console." : "Everything was already in place."),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 85] Aborted: " + ex);
                EditorUtility.DisplayDialog("Rail System",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        // ============================================================
        //                      Track pieces
        // ============================================================
        private static bool BuildTrackPiece(string asset, string display, RailPieceKind kind,
            string description, ItemDefinition steel, int steelCount,
            ItemDefinition wire, int wireCount, RecipeRegistry registry)
        {
            var prefab = EnsureTrackPrefab(asset, kind, out bool prefabChanged);
            EnsureBlockItem(asset, display, description, prefab, RailTint, 8f, 120, "Rail",
                out bool itemChanged);
            EnsureRecipe(registry, asset, display, steel, steelCount, wire, wireCount,
                StationTier.CraftingBench, 3f, out bool recipeChanged);
            return prefabChanged || itemChanged || recipeChanged;
        }

        private static GameObject EnsureTrackPrefab(string asset, RailPieceKind kind, out bool changed)
        {
            string path = PrefabsFolder + "/" + asset + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject(asset);
                var sleeperMat = MakeColoredMat(PrefabsFolder, "Mat_RailSleeper", new Color(0.32f, 0.26f, 0.20f));
                var railMat = MakeColoredMat(PrefabsFolder, "Mat_RailSteel", RailTint);

                // Two sleepers and two rails: reads as track from above at 1 m per cell.
                for (int i = 0; i < 2; i++)
                {
                    var sleeper = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    sleeper.name = "Sleeper" + i;
                    sleeper.transform.SetParent(root.transform, false);
                    sleeper.transform.localPosition = new Vector3(0f, 0.03f, i == 0 ? -0.26f : 0.26f);
                    sleeper.transform.localScale = new Vector3(0.78f, 0.06f, 0.14f);
                    Paint(sleeper, sleeperMat);
                }
                for (int i = 0; i < 2; i++)
                {
                    var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    rail.name = "Rail" + i;
                    rail.transform.SetParent(root.transform, false);
                    rail.transform.localPosition = new Vector3(i == 0 ? -0.28f : 0.28f, 0.09f, 0f);
                    rail.transform.localScale = new Vector3(0.07f, 0.07f, 1.0f);
                    Paint(rail, railMat);
                }

                if (kind == RailPieceKind.Buffer)
                {
                    var stop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stop.name = "BufferStop";
                    stop.transform.SetParent(root.transform, false);
                    stop.transform.localPosition = new Vector3(0f, 0.22f, 0.42f);
                    stop.transform.localScale = new Vector3(0.7f, 0.3f, 0.10f);
                    Paint(stop, MakeColoredMat(PrefabsFolder, "Mat_RailBuffer", new Color(0.72f, 0.24f, 0.18f)));
                }
                else if (kind == RailPieceKind.Switch)
                {
                    var lever = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    lever.name = "PointLever";
                    lever.transform.SetParent(root.transform, false);
                    lever.transform.localPosition = new Vector3(0.44f, 0.22f, 0f);
                    lever.transform.localScale = new Vector3(0.07f, 0.34f, 0.07f);
                    Paint(lever, MakeColoredMat(PrefabsFolder, "Mat_RailLever", new Color(0.90f, 0.72f, 0.16f)));
                }

                var track = root.AddComponent<RailTrack>();
                track.pieceKind = kind;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 85] Created " + path + ".");
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool repaired = false;
            var existingTrack = contents.GetComponent<RailTrack>();
            if (existingTrack == null)
            {
                existingTrack = contents.AddComponent<RailTrack>();
                existingTrack.pieceKind = kind;
                repaired = true;
                Debug.Log("[Setup 85] " + asset + " had no RailTrack component; added one.");
            }

            GameObject result = existing;
            if (repaired) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = repaired;
            return result;
        }

        // ============================================================
        //                        Station
        // ============================================================
        private static bool BuildStation(ItemDefinition steel, ItemDefinition wire, RecipeRegistry registry)
        {
            string asset = "RailStation";
            string path = PrefabsFolder + "/" + asset + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool prefabChanged = false;
            GameObject prefab = existing;

            if (existing == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject(asset);
                var mat = MakeColoredMat(PrefabsFolder, "Mat_RailStation", StationTint);

                var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
                platform.name = "Platform";
                platform.transform.SetParent(root.transform, false);
                platform.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                platform.transform.localScale = new Vector3(1.6f, 0.5f, 2.4f);
                Paint(platform, mat);

                var hut = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hut.name = "Hut";
                hut.transform.SetParent(root.transform, false);
                hut.transform.localPosition = new Vector3(0.25f, 0.95f, 0f);
                hut.transform.localScale = new Vector3(1.0f, 0.9f, 1.1f);
                Paint(hut, mat);

                var canopy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                canopy.name = "Canopy";
                canopy.transform.SetParent(root.transform, false);
                canopy.transform.localPosition = new Vector3(0.1f, 1.44f, 0f);
                canopy.transform.localScale = new Vector3(1.7f, 0.08f, 2.4f);
                Paint(canopy, mat);

                root.AddComponent<RailStation>();

                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                prefabChanged = true;
                Debug.Log("[Setup 85] Created " + path + ".");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                if (contents.GetComponent<RailStation>() == null)
                {
                    contents.AddComponent<RailStation>();
                    prefab = PrefabUtility.SaveAsPrefabAsset(contents, path);
                    prefabChanged = true;
                    Debug.Log("[Setup 85] RailStation prefab had no component; added one.");
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }

            EnsureBlockItem(asset, "Rail Station",
                "A named stop with a cargo hold. Set it to LOAD or UNLOAD and trains will " +
                "trade with it automatically. Must sit within 4 m of track.",
                prefab, StationTint, 45f, 400, "Rail", out bool itemChanged);

            EnsureRecipe(registry, asset, "Rail Station", steel, 20, wire, 8,
                StationTier.Assembler, 12f, out bool recipeChanged);

            return prefabChanged || itemChanged || recipeChanged;
        }

        // ============================================================
        //                       Locomotive
        // ============================================================
        private static bool BuildLocomotive(ItemDefinition steel, ItemDefinition wire, RecipeRegistry registry)
        {
            string asset = "Locomotive";
            string path = PrefabsFolder + "/" + asset + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool prefabChanged = false;
            GameObject prefab = existing;

            if (existing == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject(asset);
                var mat = MakeColoredMat(PrefabsFolder, "Mat_Locomotive", LocoTint);

                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hull.name = "Hull";
                hull.transform.SetParent(root.transform, false);
                hull.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                hull.transform.localScale = new Vector3(0.78f, 0.72f, 2.3f);
                Paint(hull, mat);

                var cab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cab.name = "Cab";
                cab.transform.SetParent(root.transform, false);
                cab.transform.localPosition = new Vector3(0f, 1.08f, -0.62f);
                cab.transform.localScale = new Vector3(0.74f, 0.52f, 0.9f);
                Paint(cab, mat);

                for (int i = 0; i < 4; i++)
                {
                    var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    wheel.name = "Wheel" + i;
                    wheel.transform.SetParent(root.transform, false);
                    float x = (i % 2 == 0) ? -0.36f : 0.36f;
                    float z = (i < 2) ? -0.72f : 0.72f;
                    wheel.transform.localPosition = new Vector3(x, 0.20f, z);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    wheel.transform.localScale = new Vector3(0.36f, 0.05f, 0.36f);
                    Paint(wheel, MakeColoredMat(PrefabsFolder, "Mat_LocoWheel", new Color(0.18f, 0.19f, 0.22f)));
                }

                root.AddComponent<RailTrain>();

                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                prefabChanged = true;
                Debug.Log("[Setup 85] Created " + path + ".");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                if (contents.GetComponent<RailTrain>() == null)
                {
                    contents.AddComponent<RailTrain>();
                    prefab = PrefabUtility.SaveAsPrefabAsset(contents, path);
                    prefabChanged = true;
                    Debug.Log("[Setup 85] Locomotive prefab had no RailTrain component; added one.");
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }

            EnsureBlockItem(asset, "Locomotive",
                "A scheduled hauler. Place it on track, give it a list of station names, and " +
                "it runs that order on its own - including while its chunks are unloaded.",
                prefab, LocoTint, 220f, 600, "Rail", out bool itemChanged);

            EnsureRecipe(registry, asset, "Locomotive", steel, 40, wire, 20,
                StationTier.Assembler, 25f, out bool recipeChanged);

            return prefabChanged || itemChanged || recipeChanged;
        }

        // ============================================================
        //                     Shared authoring
        // ============================================================
        private static void EnsureBlockItem(string asset, string display, string description,
            GameObject prefab, Color tint, float mass, int health, string category, out bool changed)
        {
            string path = BlocksFolder + "/Block_" + asset + ".asset";
            var b = AssetDatabase.LoadAssetAtPath<BlockItem>(path);
            bool created = false;

            if (b == null)
            {
                EnsureFolder(BlocksFolder);
                b = ScriptableObject.CreateInstance<BlockItem>();
                created = true;
            }
            bool dirty = created;

            string id = asset.ToLowerInvariant();
            if (b.itemId != id) { b.itemId = id; dirty = true; }
            if (b.displayName != display) { b.displayName = display; dirty = true; }
            if (b.maxStack <= 0) { b.maxStack = 99; dirty = true; }
            if (b.massPerUnit <= 0f) { b.massPerUnit = mass; dirty = true; }
            if (b.blockHealth <= 0) { b.blockHealth = health; dirty = true; }
            if (b.miningTier <= 0) { b.miningTier = 1; dirty = true; }
            if (b.category != category) { b.category = category; dirty = true; }
            if (string.IsNullOrEmpty(b.description)) { b.description = description; dirty = true; }
            if (b.icon == null) b.iconTint = tint;

            if (b.placedPrefab == null || b.placedPrefab != prefab)
            {
                b.placedPrefab = prefab;
                dirty = true;
            }

            if (dirty)
            {
                if (!AssetDatabase.Contains(b)) AssetDatabase.CreateAsset(b, path);
                EditorUtility.SetDirty(b);
                Debug.Log("[Setup 85] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            changed = dirty;
        }

        private static void EnsureRecipe(RecipeRegistry registry, string asset, string display,
            ItemDefinition steel, int steelCount, ItemDefinition wire, int wireCount,
            StationTier station, float seconds, out bool changed)
        {
            string stem = "Recipe_" + asset;
            var recipe = FindRecipe(stem);
            var block = AssetDatabase.LoadAssetAtPath<BlockItem>(BlocksFolder + "/Block_" + asset + ".asset");

            RecipeIngredient[] Inputs()
            {
                if (wireCount <= 0)
                    return new[] { new RecipeIngredient { item = steel, count = steelCount } };
                return new[]
                {
                    new RecipeIngredient { item = steel, count = steelCount },
                    new RecipeIngredient { item = wire,  count = wireCount },
                };
            }

            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = display;
                recipe.requiredStation = station;
                recipe.craftSeconds = seconds;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = block;
                recipe.inputs = Inputs();
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
                Debug.Log("[Setup 85] Created " + stem + ".");
            }
            else
            {
                bool dirty = false;
                if (recipe.outputItem == null && block != null)
                {
                    recipe.outputItem = block;
                    recipe.outputCount = 1;
                    dirty = true;
                }
                if (recipe.inputs == null || recipe.inputs.Length == 0) { recipe.inputs = Inputs(); dirty = true; }
                if (dirty) EditorUtility.SetDirty(recipe);
                changed = dirty;
            }

            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                Debug.Log("[Setup 85] Added " + stem + " to RecipeRegistry.");
            }
        }

        // ============================================================
        //                        Helpers
        // ============================================================
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

        private static Material MakeColoredMat(string folder, string name, Color c)
        {
            string path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                Debug.LogError("[Setup 85] Preserved conflicting asset at '" + path + "'.");
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
