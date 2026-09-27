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
    ///
    /// The v1 Locomotive and its `RailTrain` entity are RETIRED as of 12.0.0-dev - the
    /// MAJOR bump that the retirement waited for. Train System v2 replaced it with the
    /// Rail Truck (step 92), which turns any player-built grid into a train; this step
    /// now also scrubs the dead script off old locomotive prefabs so no missing-script
    /// component survives in the project.
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

        // ══ THE 11.41.0 FORMATION: three times the width the rail launched with ══
        // Sleeper 4.5 m, gauge 3.15 m, rail heads 0.33 m. The whole permanent way scaled
        // together so the ratio between sleeper length and gauge stays the real one (0.70),
        // and step 92 re-gauges the bogie from the same number so wheels sit on rail heads.
        private const float FormationGauge = 3.15f;
        private const float SleeperLength = 4.5f;
        private const float RailHeadWidth = 0.33f;

        private static readonly Color RailTint = new(0.55f, 0.57f, 0.62f);
        private static readonly Color StationTint = new(0.78f, 0.68f, 0.40f);

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
                // The v1 locomotive is RETIRED (11.39.0). Train System v2 makes a train an
                // ordinary grid with a Rail Truck on it, so a bespoke locomotive entity is
                // now a second way to do the same thing - and the one that cannot carry
                // grid blocks, take damage or be designed by the player.
                any |= RetireLocomotive(registry);

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
                    "\nThe v1 Locomotive is RETIRED (entity removed in 12.0.0-dev, a\n" +
                    "MAJOR: fresh save required). A train is now any grid with a\n" +
                    "Rail Truck on it - see setup step 92.\n\n" +
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

                // Sleepers and rails at a proper gauge.
                //
                // The first pass used a 0.56 m gauge on a 1 m cell, which read as a narrow
                // ladder rather than a railway. 11.39.0 went to 1.05 m across a 1.5 m sleeper,
                // the real 0.70 ratio - and 11.41.0 tripled the whole formation to 3.15 m
                // across a 4.5 m sleeper, because beside a player-built grid the old deck
                // read as a ribbon rather than a railway. The ratio is unchanged by the
                // scaling, so it still reads correctly at standing height.
                //
                // Four sleepers per cell rather than two: at 1 m spacing two sleepers left
                // visible gaps between cells, so a run looked like a dashed line. On curves
                // RailTrack fans these radially and stretches the rails to their arc length.
                for (int i = 0; i < 4; i++)
                {
                    var sleeper = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    sleeper.name = "Sleeper" + i;
                    sleeper.transform.SetParent(root.transform, false);
                    // Evenly spread across the cell so consecutive cells tile without a seam.
                    float z = -0.375f + i * 0.25f;
                    sleeper.transform.localPosition = new Vector3(0f, 0.05f, z);
                    sleeper.transform.localScale = new Vector3(SleeperLength, 0.10f, 0.18f);
                    Paint(sleeper, sleeperMat);
                }
                for (int i = 0; i < 2; i++)
                {
                    var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    rail.name = "Rail" + i;
                    rail.transform.SetParent(root.transform, false);
                    rail.transform.localPosition = new Vector3((i == 0 ? -1f : 1f) * FormationGauge * 0.5f, 0.14f, 0f);
                    // Slightly taller than wide, like a real rail profile, and full cell
                    // length so consecutive cells form one continuous line.
                    rail.transform.localScale = new Vector3(RailHeadWidth, 0.12f, 1.0f);
                    Paint(rail, railMat);
                }

                if (kind == RailPieceKind.Buffer)
                {
                    var stop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stop.name = "BufferStop";
                    stop.transform.SetParent(root.transform, false);
                    stop.transform.localPosition = new Vector3(0f, 0.22f, 0.42f);
                    stop.transform.localScale = new Vector3(2.1f, 0.3f, 0.10f);
                    Paint(stop, MakeColoredMat(PrefabsFolder, "Mat_RailBuffer", new Color(0.72f, 0.24f, 0.18f)));
                }
                else if (kind == RailPieceKind.Switch)
                {
                    var lever = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    lever.name = "PointLever";
                    lever.transform.SetParent(root.transform, false);
                    lever.transform.localPosition = new Vector3(1.32f, 0.22f, 0f);
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

            // The triple-width formation (11.41.0) has to reach prefabs authored before it,
            // or an upgraded project keeps laying ribbon track beside the new bogie.
            // Geometry-only: sleepers, rail heads, buffer and lever; components, materials
            // and every authored tuning value stay exactly as they are.
            repaired |= RegaugeFormation(contents);

            GameObject result = existing;
            if (repaired) result = PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            changed = repaired;
            return result;
        }

        /// <summary>
        /// Brings an existing track prefab to the current formation width. Idempotent - a
        /// prefab already at the gauge returns false and is left byte-identical - and blind
        /// to anything that is not running gear, so re-running setup never flattens a
        /// hand-tweaked prefab beyond the width itself.
        /// </summary>
        private static bool RegaugeFormation(GameObject root)
        {
            var rail0 = root.transform.Find("Rail0");
            if (rail0 == null) return false;
            float current = Mathf.Abs(rail0.localPosition.x) * 2f;
            if (Mathf.Abs(current - FormationGauge) < 0.001f) return false;

            for (int i = 0; i < 4; i++)
            {
                var sleeper = root.transform.Find("Sleeper" + i);
                if (sleeper == null) continue;
                var sc = sleeper.localScale;
                sc.x = SleeperLength;
                sleeper.localScale = sc;
            }

            for (int i = 0; i < 2; i++)
            {
                var rail = root.transform.Find("Rail" + i);
                if (rail == null) continue;
                var pos = rail.localPosition;
                pos.x = (i == 0 ? -1f : 1f) * FormationGauge * 0.5f;
                rail.localPosition = pos;
                var rsc = rail.localScale;
                rsc.x = RailHeadWidth;
                rail.localScale = rsc;
            }

            var stop = root.transform.Find("BufferStop");
            if (stop != null)
            {
                var ssc = stop.localScale;
                ssc.x = 2.1f;
                stop.localScale = ssc;
            }

            var lever = root.transform.Find("PointLever");
            if (lever != null)
            {
                var lpos = lever.localPosition;
                lpos.x = 1.32f;
                lever.localPosition = lpos;
            }

            Debug.Log("[Setup 85] Re-gauged " + root.name + " to the triple-width formation " +
                      "(sleeper " + SleeperLength.ToString("0.0") + " m, gauge " +
                      FormationGauge.ToString("0.00") + " m).");
            return true;
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
        /// <summary>
        /// Removes the v1 locomotive from the game.
        ///
        /// The recipe left the registry in 11.39.0 while the asset stayed, because saves still
        /// held locomotives and deleting the asset would have turned them into missing
        /// references. 12.0.0-dev IS the MAJOR those saves were waiting out: the `RailTrain`
        /// entity is gone from code, so this pass now also scrubs the dead script off the
        /// locomotive prefab. The prefab shell itself stays on disk - a MAJOR demands a fresh
        /// save, not a deleted history - but nothing in it references code that no longer
        /// exists.
        /// </summary>
        private static bool RetireLocomotive(RecipeRegistry registry)
        {
            bool changed = false;

            var realRecipe = FindRecipe("Recipe_Locomotive");
            if (realRecipe != null && registry.recipes.Contains(realRecipe))
            {
                registry.recipes.Remove(realRecipe);
                EditorUtility.SetDirty(registry);
                changed = true;
                Debug.Log("[Setup 85] Retired the v1 Locomotive: recipe removed from the registry. " +
                          "Build a grid and put a Rail Truck on it instead (setup step 92).");
            }

            // Scrub the dead RailTrain script off any locomotive prefab. A missing-script
            // component is a console warning on every load and a trap for the next person
            // to open the prefab; removing it is repair, not deletion.
            var guids = AssetDatabase.FindAssets("Locomotive t:GameObject");
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var contents = PrefabUtility.LoadPrefabContents(path);
                // Singular, per-GameObject API - the same call step 17 of the setup window
                // uses - walked over every transform including inactive children.
                int removed = 0;
                foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    changed = true;
                    Debug.Log("[Setup 85] Scrubbed " + removed + " dead script(s) off " + path +
                              " - the v1 RailTrain entity no longer exists (12.0.0-dev).");
                }
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return changed;
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
