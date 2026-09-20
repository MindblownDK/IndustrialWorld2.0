#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 96 (12.4.0-dev): author the steam railway - locomotive and water tower.
    /// 12.5.0-dev rebuilds the tower as a grand six-legged steel water tower (the
    /// V2 pass in BuildWaterTower): geometry is re-authored, tuning on existing
    /// towers is kept, and the E-console reads its level.
    ///
    ///   Steam Engine      - grid block; piston gear, flywheel, ROTATIONAL POWER only.
    ///   Water Tower       - stationary; platform-side water for berthed locomotives.
    ///
    /// Non-destructive: missing assets are created, existing ones only have
    /// unresolvable links repaired. Authored tuning is never reset. Safe to re-run.
    /// Logs with the [Setup 96] prefix.
    /// </summary>
    public static class RailSteamSetup
    {
        private const string Root = "Assets/VoxelEngineAssets";
        private const string BlocksFolder = Root + "/Blocks";
        private const string RecipesFolder = Root + "/Recipes";
        private const string PrefabsFolder = Root + "/StationPrefabs";
        private const string GridItemsFolder = Root + "/GridSystem/Items";
        private const string GridPrefabsFolder = Root + "/GridSystem/Prefabs";

        private static readonly Color BoilerBlack = new(0.09f, 0.09f, 0.10f);
        private static readonly Color Brass = new(0.72f, 0.51f, 0.22f);
        private static readonly Color Oxide = new(0.35f, 0.16f, 0.10f);
        private static readonly Color GalvSteel = new(0.62f, 0.64f, 0.66f);
        private static readonly Color RoofRust = new(0.48f, 0.22f, 0.13f);
        private static readonly Color TowerIron = new(0.15f, 0.16f, 0.18f);

        private const string SteelPath = Root + "/Items/Item_SteelIngot.asset";
        private const string BrassPath = Root + "/Items/Item_BrassIngot.asset";
        private const string WirePath = Root + "/Industrial/Items/Item_CopperWire.asset";

        public static void RunStep96()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Steam Railway", "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                var registry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(Root + "/RecipeRegistry.asset");
                if (registry == null)
                {
                    EditorUtility.DisplayDialog("Steam Railway",
                        "Run step 4 (Build Crafting Content) first - RecipeRegistry.asset doesn't exist yet.", "OK");
                    return;
                }

                var steel = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SteelPath);
                var brass = AssetDatabase.LoadAssetAtPath<ItemDefinition>(BrassPath);
                var wire = AssetDatabase.LoadAssetAtPath<ItemDefinition>(WirePath);
                if (steel == null)
                {
                    EditorUtility.DisplayDialog("Steam Railway",
                        "Steel ingot is missing.\n\nRun the earlier crafting-content steps first.", "OK");
                    return;
                }
                if (brass == null)
                {
                    EditorUtility.DisplayDialog("Steam Railway",
                        "Brass ingot is missing.\n\nRun step 95 first - it authors the brass smelt.", "OK");
                    return;
                }

                bool any = false;
                any |= BuildSteamEngine(steel, brass, wire, registry);
                any |= BuildWaterTower(steel, brass, registry);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[Setup 96] Steam railway complete. " +
                          (any ? "Changes were written." : "Everything was already in place."));

                EditorUtility.DisplayDialog("Step 96 - Steam Railway",
                    "Authored:\n\n" +
                    "  STEAM ENGINE      grid block  - steel x24 + brass x6 + wire x4\n" +
                    "  WATER TOWER       grand steel tower, 12 000 L - steel x10 + brass x2\n\n" +
                    "The engine produces ROTATIONAL POWER only: a turning\n" +
                    "flywheel drives the train mechanically when the grid has\n" +
                    "no electric power, and feeds the brass screens' shaft tap.\n" +
                    "It shovels coal (or wood) from any cargo container aboard,\n" +
                    "drinks from tank wagons moving and water towers berthed.\n" +
                    "E opens the footplate: pressure, water, fire, whistle.\n" +
                    "E on the tower shows its level gauge.\n\n" +
                    (any ? "Changes were written. See the Console." : "Everything was already in place."),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Setup 96] Aborted: " + ex);
                EditorUtility.DisplayDialog("Steam Railway",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        // ============================================================
        //  Steam Engine - grid block, piston gear and all
        // ============================================================
        private static bool BuildSteamEngine(ItemDefinition steel, ItemDefinition brass,
            ItemDefinition wire, RecipeRegistry registry)
        {
            const string path = GridPrefabsFolder + "/SteamEngine.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool changed = false;

            if (prefab == null)
            {
                EnsureFolder(GridPrefabsFolder);
                var root = new GameObject("SteamEngine");

                var black = Mat("Mat_EngineBlack", BoilerBlack);
                var brassM = Mat("Mat_EngineBrass", Brass);
                var oxide = Mat("Mat_EngineOxide", Oxide);

                // Bed and frames: everything else bolts to these.
                Cube(root, "Bed", new Vector3(0f, 0.06f, 0f), new Vector3(1.10f, 0.12f, 2.40f), black);
                for (int i = 0; i < 2; i++)
                    Cube(root, "Frame" + i, new Vector3(i == 0 ? -0.42f : 0.42f, 0.21f, 0f),
                        new Vector3(0.08f, 0.30f, 2.40f), oxide);

                // Steam cylinder forward, with a brass gland the piston rod runs through.
                var cyl = Cylinder(root, "SteamCylinder", new Vector3(0f, 0.35f, 0.95f),
                    new Vector3(0.34f, 0.45f, 0.34f), black);
                cyl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var gland = Cylinder(root, "Gland", new Vector3(0f, 0.35f, 0.50f),
                    new Vector3(0.16f, 0.06f, 0.16f), brassM);
                gland.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // Slide bars the crosshead rides on.
                for (int i = 0; i < 2; i++)
                    Cube(root, "SlideBar" + i, new Vector3(i == 0 ? -0.10f : 0.10f, 0.35f, 0.35f),
                        new Vector3(0.04f, 0.05f, 1.00f), black);

                // Crosshead + piston rod. The rod is a child so it slides with it and
                // disappears into the gland exactly like the real thing.
                var cross = Cube(root, "Crosshead", new Vector3(0f, 0.35f, 0.40f),
                    new Vector3(0.16f, 0.16f, 0.24f), brassM);
                var prod = Cylinder(cross, "PistonRod", new Vector3(0f, 0f, 0.45f),
                    new Vector3(0.05f, 0.45f, 0.05f), black);
                prod.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // Connecting rod: an empty pivot at the crank-pin end, body stretched
                // along its local Y. GridSteamEngine lays it between pin and crosshead
                // at its true angle every frame.
                var conRod = new GameObject("ConRod");
                conRod.transform.SetParent(root.transform, false);
                conRod.transform.localPosition = new Vector3(0f, 0.35f, -0.15f);
                var conBody = Cube(conRod, "ConRodBody", new Vector3(0f, 0.425f, 0f),
                    new Vector3(0.05f, 0.85f, 0.05f), oxide);

                // Flywheel: mount carries the placement rotation, spin carries the
                // animation and the crank pin, so the two never fight.
                var mount = new GameObject("FlywheelMount");
                mount.transform.SetParent(root.transform, false);
                mount.transform.localPosition = new Vector3(0f, 0.35f, -0.45f);
                mount.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var spin = new GameObject("FlywheelSpin");
                spin.transform.SetParent(mount.transform, false);
                var rim = Cylinder(spin, "Flywheel", new Vector3(0f, 0f, 0f),
                    new Vector3(0.90f, 0.35f, 0.90f), black);
                var hub = Cylinder(spin, "Hub", new Vector3(0f, 0f, 0f),
                    new Vector3(0.14f, 0.40f, 0.14f), brassM);
                for (int i = 0; i < 4; i++)
                {
                    var spoke = Cube(spin, "Spoke" + i, new Vector3(0f, 0f, 0f),
                        new Vector3(0.80f, 0.30f, 0.06f), oxide);
                    spoke.transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
                }
                var pin = Cylinder(spin, "CrankPin", new Vector3(0.30f, 0f, 0f),
                    new Vector3(0.05f, 0.42f, 0.05f), brassM);

                // Vertical boiler behind, brass-banded, with the chimney the white
                // smoke comes out of.
                var boiler = Cylinder(root, "Boiler", new Vector3(0f, 0.62f, -1.15f),
                    new Vector3(0.55f, 0.60f, 0.55f), oxide);
                for (int i = 0; i < 2; i++)
                {
                    var band = Cylinder(root, "BoilerBand" + i, new Vector3(0f, 0.45f + i * 0.40f, -1.15f),
                        new Vector3(0.58f, 0.03f, 0.58f), brassM);
                }
                Cylinder(root, "Chimney", new Vector3(0f, 1.42f, -1.15f),
                    new Vector3(0.14f, 0.22f, 0.14f), black);
                var gauge = Cylinder(root, "Gauge", new Vector3(0f, 0.95f, -0.86f),
                    new Vector3(0.12f, 0.02f, 0.12f), brassM);
                gauge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var engine = root.AddComponent<GridSteamEngine>();
                engine.blockName = "Steam Engine";
                engine.BlockMass = 900f;
                engine.maxHP = 600f;

                ScrubMissingScripts(root);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 96] Created " + path + ".");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                ScrubMissingScripts(contents);
                bool repaired = false;
                if (contents.GetComponent<GridSteamEngine>() == null)
                {
                    var e = contents.AddComponent<GridSteamEngine>();
                    e.blockName = "Steam Engine";
                    repaired = true;
                }
                if (repaired) prefab = PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                changed = repaired;
            }

            changed |= EnsureGridItem("GItem_SteamEngine", "steamengine", "Steam Engine",
                "A piston steam engine: vertical boiler, horizontal cylinder, crosshead, " +
                "connecting rod and a flywheel across the frames. It produces ROTATIONAL " +
                "POWER and nothing else - no electricity, no traction of its own. The " +
                "mechanical drive takes the flywheel's turn to move a train, and the " +
                "brass screens tap the same shaft. Shovels coal (or wood) from any cargo " +
                "container aboard; drinks from tank wagons moving and water towers " +
                "berthed. Open it with E for the footplate.",
                prefab, Brass, 900f, 600f);

            changed |= EnsureRecipe(registry, "Recipe_SteamEngine", "Steam Engine",
                new[] { (steel, 24), (brass, 6), (wire, 4) }, StationTier.Assembler, 12f,
                AssetDatabase.LoadAssetAtPath<GridBlockItem>(GridItemsFolder + "/GItem_SteamEngine.asset"));

            return changed;
        }

        // ============================================================
        //  Water Tower - stationary
        // ============================================================
        //
        // THE GRAND TOWER (12.5.0): a classic six-legged steel water tower, ~10 m
        // to the finial. Galvanized tank with iron bands and rivets, rust-red
        // conical roof, railed balcony walkway, ladders, a central riser pipe and
        // a spout arm over the platform side. Legs lean inward (1.75 m spread at
        // the ground, 1.15 m under the tank) with girt rings and X-bracing.
        private const int TowerLegs = 6;
        private const float TowerGroundR = 1.75f;
        private const float TowerTopR = 1.15f;
        private const float TowerLegH = 5.9f;
        private const float TankR = 1.70f;
        private const float TankBottom = 5.9f;
        private const float TankH = 2.6f;
        private const float RoofBaseR = 1.90f;
        private const float RoofBaseY = 8.5f;
        private const float RoofApexY = 9.7f;

        private static bool BuildWaterTower(ItemDefinition steel, ItemDefinition brass, RecipeRegistry registry)
        {
            string path = PrefabsFolder + "/WaterTower.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool changed = false;

            if (prefab == null)
            {
                EnsureFolder(PrefabsFolder);
                var root = new GameObject("WaterTower");
                AuthorTowerGeometry(root);
                // Fresh tuning comes from the component defaults (12 000 L).
                root.AddComponent<VoxelEngine.Building.WaterTower>();

                ScrubMissingScripts(root);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                changed = true;
                Debug.Log("[Setup 96] Created " + path + ".");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                ScrubMissingScripts(contents);
                bool repaired = false;
                if (contents.transform.Find("TowerV2") == null)
                {
                    // The 12.5.0 classic rebuild: children are deleted and the grand
                    // tower is re-authored. The WaterTower tuning on the root is
                    // preserved by EnsureTowerTuning - geometry only, never values.
                    for (int i = contents.transform.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.DestroyImmediate(contents.transform.GetChild(i).gameObject);
                    AuthorTowerGeometry(contents);
                    repaired = true;
                    Debug.Log("[Setup 96] Rebuilt " + path + " as the grand steel tower.");
                }
                repaired |= EnsureTowerTuning(contents);
                if (repaired) prefab = PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                changed = repaired;
            }

            changed |= EnsureBlockItem("WaterTower", "Water Tower",
                "A riveted steel water tower on six braced legs, with a railed " +
                "balcony, a ladder and a spout over the platform side. Fills itself " +
                "from a water network it stands beside, or slowly from open water - " +
                "a tower by a pond seeps full the way real ones were pumped. " +
                "Berthed locomotives take their water from the spout. Open it " +
                "with E for the level gauge.",
                prefab, GalvSteel, 60f, 220, registry, "Recipe_WaterTower",
                new[] { (steel, 10), (brass, 2) }, StationTier.CraftingBench, 6f);

            return changed;
        }

        /// <summary>12.5.0 tuning migration: towers still on the untouched 12.4.0
        /// defaults (4 000 L / 8 L/s) grow to the grand-tower values. Anything the
        /// player or an earlier pass already tuned is left alone.</summary>
        private static bool EnsureTowerTuning(GameObject root)
        {
            var tower = root.GetComponent<VoxelEngine.Building.WaterTower>();
            if (tower == null)
            {
                root.AddComponent<VoxelEngine.Building.WaterTower>();
                return true;
            }
            if (!Mathf.Approximately(tower.capacity, 4000f)) return false;
            tower.capacity = 12000f;
            if (Mathf.Approximately(tower.fillRate, 8f)) tower.fillRate = 24f;
            if (tower.seepRate <= 0f) tower.seepRate = 6f;
            Debug.Log("[Setup 96] Migrated a 12.4.0 water tower to 12 000 L.");
            return true;
        }

        /// <summary>A point on leg i at height y. Legs are offset 30 degrees so the
        /// gaps face +X (the spout side) and -X (the ladder side).</summary>
        private static Vector3 LegPoint(int i, float y)
        {
            float a = Mathf.Deg2Rad * (30f + i * 60f);
            float r = TowerGroundR + (TowerTopR - TowerGroundR) * Mathf.Clamp01(y / TowerLegH);
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        private static void AuthorTowerGeometry(GameObject root)
        {
            var steel = Mat("Mat_TowerSteel", GalvSteel, 0.55f, 0.45f);
            var roofM = Mat("Mat_TowerRoof", RoofRust, 0.15f, 0.50f);
            var iron = Mat("Mat_TowerIron", TowerIron, 0.40f, 0.50f);
            var brassM = Mat("Mat_TowerBrass", Brass);

            // Legs with foot plates. The legs keep their colliders: they are what
            // the player walks into and what the E-raycast usually hits low down.
            for (int i = 0; i < TowerLegs; i++)
            {
                BeamBetween(root, "Leg" + i, LegPoint(i, 0.05f), LegPoint(i, TowerLegH), 0.16f, iron, true);
                Vector3 foot = LegPoint(i, 0f);
                Cube(root, "Foot" + i, new Vector3(foot.x, 0.04f, foot.z),
                    new Vector3(0.36f, 0.08f, 0.36f), iron);
            }

            // Girt rings tying the legs together.
            foreach (float gy in new[] { 2.1f, 4.1f })
                for (int i = 0; i < TowerLegs; i++)
                    BeamBetween(root, "Girt" + gy + "_" + i,
                        LegPoint(i, gy), LegPoint((i + 1) % TowerLegs, gy), 0.09f, iron);

            // X-bracing in the lower two bays.
            Vector2[] bays = { new Vector2(0.35f, 1.95f), new Vector2(2.25f, 3.95f) };
            for (int b = 0; b < bays.Length; b++)
                for (int i = 0; i < TowerLegs; i++)
                {
                    int j = (i + 1) % TowerLegs;
                    BeamBetween(root, "Brace" + b + "_" + i + "a",
                        LegPoint(i, bays[b].x), LegPoint(j, bays[b].y), 0.06f, iron);
                    BeamBetween(root, "Brace" + b + "_" + i + "b",
                        LegPoint(j, bays[b].x), LegPoint(i, bays[b].y), 0.06f, iron);
                }

            // Top support ring and radial joists the tank sits on.
            for (int i = 0; i < TowerLegs; i++)
            {
                BeamBetween(root, "TopRing" + i,
                    LegPoint(i, TowerLegH), LegPoint((i + 1) % TowerLegs, TowerLegH), 0.12f, iron);
                Vector3 top = LegPoint(i, TowerLegH);
                Vector3 inner = new Vector3(top.x, 0f, top.z).normalized * 0.35f;
                inner.y = TowerLegH;
                BeamBetween(root, "Joist" + i, top, inner, 0.10f, iron);
            }

            // Central riser pipe with a valve house and a brass valve wheel.
            Cylinder(root, "Riser", new Vector3(0f, TowerLegH / 2f, 0f),
                new Vector3(0.38f, TowerLegH, 0.38f), steel);
            Cube(root, "ValveHouse", new Vector3(0f, 0.50f, 0f),
                new Vector3(0.62f, 1.00f, 0.62f), iron);
            var valve = Cylinder(root, "ValveWheel", new Vector3(0f, 1.12f, 0.36f),
                new Vector3(0.26f, 0.05f, 0.26f), brassM, false);
            valve.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Cube(root, "ValveStem", new Vector3(0f, 1.12f, 0.22f),
                new Vector3(0.06f, 0.06f, 0.24f), brassM, false);

            // The tank: galvanized shell, iron seam bands, two rivet rows.
            float tankMid = TankBottom + TankH / 2f;
            Cylinder(root, "Tank", new Vector3(0f, tankMid, 0f),
                new Vector3(TankR * 2f, TankH, TankR * 2f), steel);
            float[] bands = { TankBottom + 0.35f, tankMid, TankBottom + TankH - 0.35f };
            foreach (float by in bands)
                Cylinder(root, "TankBand" + by, new Vector3(0f, by, 0f),
                    new Vector3(TankR * 2f + 0.06f, 0.08f, TankR * 2f + 0.06f), iron, false);
            foreach (float by in new[] { bands[0], bands[2] })
                for (int k = 0; k < 12; k++)
                {
                    float a = Mathf.PI * 2f * k / 12f;
                    Ball(root, "Rivet" + by + "_" + k,
                        new Vector3(Mathf.Cos(a) * (TankR + 0.02f), by, Mathf.Sin(a) * (TankR + 0.02f)),
                        0.07f, iron);
                }

            // Balcony walkway with a railed rim. Post 6 (the -X face) is left out:
            // that is the boarding gap where the ladder arrives.
            float walkR = TankR + 0.55f;
            Cylinder(root, "Walkway", new Vector3(0f, TankBottom + 0.04f, 0f),
                new Vector3(walkR * 2f, 0.08f, walkR * 2f), iron);
            const int posts = 12;
            for (int i = 0; i < posts; i++)
            {
                if (i == 6) continue;
                float a = Mathf.PI * 2f * i / posts;
                Vector3 bp = new Vector3(Mathf.Cos(a) * (walkR - 0.10f),
                    TankBottom + 0.58f, Mathf.Sin(a) * (walkR - 0.10f));
                Cube(root, "RailPost" + i, bp, new Vector3(0.05f, 1.00f, 0.05f), iron, false);
            }
            foreach (float ry in new[] { TankBottom + 0.60f, TankBottom + 1.06f })
                for (int i = 0; i < posts; i++)
                {
                    float a0 = Mathf.PI * 2f * i / posts;
                    float a1 = Mathf.PI * 2f * (i + 1) / posts;
                    BeamBetween(root, "Rail" + ry + "_" + i,
                        new Vector3(Mathf.Cos(a0) * (walkR - 0.10f), ry, Mathf.Sin(a0) * (walkR - 0.10f)),
                        new Vector3(Mathf.Cos(a1) * (walkR - 0.10f), ry, Mathf.Sin(a1) * (walkR - 0.10f)),
                        0.045f, iron);
                }

            // Ground ladder up the -X face to the balcony, roof ladder up the tank.
            float ladderX = -(walkR + 0.10f);
            foreach (float rz in new[] { -0.20f, 0.20f })
                Cube(root, "LadderRail" + rz, new Vector3(ladderX, 3.22f, rz),
                    new Vector3(0.06f, 6.15f, 0.06f), iron, false);
            for (int r = 0; r < 10; r++)
                Cube(root, "LadderRung" + r, new Vector3(ladderX, 0.65f + r * 0.578f, 0f),
                    new Vector3(0.05f, 0.05f, 0.40f), iron, false);
            float roofLadderX = -(TankR + 0.12f);
            foreach (float rz in new[] { -0.20f, 0.20f })
                Cube(root, "RoofLadderRail" + rz, new Vector3(roofLadderX, 7.40f, rz),
                    new Vector3(0.06f, 2.70f, 0.06f), iron, false);
            for (int r = 0; r < 5; r++)
                Cube(root, "RoofLadderRung" + r, new Vector3(roofLadderX, 6.50f + r * 0.475f, 0f),
                    new Vector3(0.05f, 0.05f, 0.40f), iron, false);

            // Rust-red conical roof with a rim, a vent cap and a brass finial.
            Cylinder(root, "RoofSoffit", new Vector3(0f, RoofBaseY - 0.03f, 0f),
                new Vector3(RoofBaseR * 2f, 0.06f, RoofBaseR * 2f), roofM, false);
            Cone(root, "Roof", new Vector3(0f, RoofBaseY, 0f), EnsureTowerRoofMesh(), roofM);
            Cylinder(root, "RoofRim", new Vector3(0f, RoofBaseY + 0.02f, 0f),
                new Vector3(RoofBaseR * 2f + 0.04f, 0.07f, RoofBaseR * 2f + 0.04f), roofM, false);
            Cylinder(root, "VentCap", new Vector3(0f, RoofApexY + 0.06f, 0f),
                new Vector3(0.16f, 0.12f, 0.16f), iron, false);
            Ball(root, "Finial", new Vector3(0f, RoofApexY + 0.20f, 0f), 0.12f, brassM);

            // The spout: a steel arm out over the platform side with a brass tip
            // and a stay rod up to the tank. Locomotives drink from its reach.
            var arm = Cylinder(root, "SpoutArm", new Vector3(1.10f, 4.40f, 0f),
                new Vector3(0.15f, 1.90f, 0.15f), steel);
            arm.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Cylinder(root, "SpoutTip", new Vector3(1.99f, 4.20f, 0f),
                new Vector3(0.15f, 0.45f, 0.15f), brassM);
            BeamBetween(root, "SpoutStay", new Vector3(0.20f, 5.75f, 0f),
                new Vector3(1.95f, 4.55f, 0f), 0.035f, iron);

            // V2 marker: re-runs leave a grand tower alone.
            var marker = new GameObject("TowerV2");
            marker.transform.SetParent(root.transform, false);
        }

        // ============================================================
        //  Shared authoring
        // ============================================================
        private static void ScrubMissingScripts(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }

        private static GameObject Cube(GameObject parent, string name, Vector3 pos, Vector3 scale,
            Material mat, bool collide = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Paint(go, mat);
            if (!collide) StripCollider(go);
            return go;
        }

        private static GameObject Cylinder(GameObject parent, string name, Vector3 pos, Vector3 scale,
            Material mat, bool collide = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Paint(go, mat);
            if (!collide) StripCollider(go);
            return go;
        }

        private static GameObject Ball(GameObject parent, string name, Vector3 pos, float diameter, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(diameter, diameter, diameter);
            Paint(go, mat);
            StripCollider(go);
            return go;
        }

        /// <summary>A square beam stretched between two points - legs, braces,
        /// girts, rails. Colliders are stripped unless the beam is structural.</summary>
        private static GameObject BeamBetween(GameObject parent, string name, Vector3 a, Vector3 b,
            float thickness, Material mat, bool collide = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = (a + b) * 0.5f;
            Vector3 d = b - a;
            if (d.sqrMagnitude > 0.000001f)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            go.transform.localScale = new Vector3(thickness, d.magnitude, thickness);
            Paint(go, mat);
            if (!collide) StripCollider(go);
            return go;
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }

        /// <summary>A cone from the shared saved mesh. No collider: the tank below
        /// covers interaction and the roof is out of reach.</summary>
        private static GameObject Cone(GameObject parent, string name, Vector3 pos, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        /// <summary>The tower roof cone, saved as an asset so the prefab reference
        /// survives. Created once, reused by every rebuild.</summary>
        private static Mesh EnsureTowerRoofMesh()
        {
            string path = PrefabsFolder + "/Mesh_TowerRoof.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            EnsureFolder(PrefabsFolder);
            var mesh = ConeMesh(RoofBaseR, RoofApexY - RoofBaseY, 24);
            AssetDatabase.CreateAsset(mesh, path);
            Debug.Log("[Setup 96] Created " + path + ".");
            return mesh;
        }

        private static Mesh ConeMesh(float radius, float height, int segments)
        {
            segments = Mathf.Max(3, segments);
            var mesh = new Mesh { name = "TowerRoofCone" };
            var verts = new List<Vector3>(segments + 2) { new Vector3(0f, height, 0f) };
            var uvs = new List<Vector2>(segments + 2) { new Vector2(0.5f, 1f) };
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * 2f * i / segments;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                uvs.Add(new Vector2((float)i / segments, 0f));
            }
            var tris = new List<int>(segments * 3);
            for (int i = 0; i < segments; i++)
            {
                tris.Add(0);
                tris.Add(2 + i);
                tris.Add(1 + i);
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Paint(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
        }

        private static bool EnsureGridItem(string asset, string id, string display, string description,
            GameObject prefab, Color tint, float mass, float hp)
        {
            string path = GridItemsFolder + "/" + asset + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<GridBlockItem>(path);
            bool created = false;
            if (item == null)
            {
                EnsureFolder(GridItemsFolder);
                item = ScriptableObject.CreateInstance<GridBlockItem>();
                created = true;
            }
            bool dirty = created;

            if (item.itemId != id) { item.itemId = id; dirty = true; }
            if (item.displayName != display) { item.displayName = display; dirty = true; }
            if (item.maxStack <= 0) { item.maxStack = 5; dirty = true; }
            if (item.massPerUnit <= 0f) { item.massPerUnit = mass; dirty = true; }
            if (item.category != "Grid Blocks") { item.category = "Grid Blocks"; dirty = true; }
            if (string.IsNullOrEmpty(item.description)) { item.description = description; dirty = true; }
            if (item.icon == null) item.iconTint = tint;
            if (created) { item.blockMass = mass; item.blockHP = hp; }
            if (item.blockPrefab == null || item.blockPrefab != prefab) { item.blockPrefab = prefab; dirty = true; }

            if (dirty)
            {
                if (!AssetDatabase.Contains(item)) AssetDatabase.CreateAsset(item, path);
                EditorUtility.SetDirty(item);
                Debug.Log("[Setup 96] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }
            return dirty;
        }

        private static bool EnsureBlockItem(string asset, string display, string description,
            GameObject prefab, Color tint, float mass, int health, RecipeRegistry registry,
            string recipeStem, (ItemDefinition item, int count)[] inputs, StationTier tier, float seconds)
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
            if (b.category != "Rail") { b.category = "Rail"; dirty = true; }
            if (string.IsNullOrEmpty(b.description)) { b.description = description; dirty = true; }
            if (b.icon == null) b.iconTint = tint;
            if (b.placedPrefab == null || b.placedPrefab != prefab) { b.placedPrefab = prefab; dirty = true; }

            if (dirty)
            {
                if (!AssetDatabase.Contains(b)) AssetDatabase.CreateAsset(b, path);
                EditorUtility.SetDirty(b);
                Debug.Log("[Setup 96] " + (created ? "Created" : "Repaired") + " " + path + ".");
            }

            var recipe = FindRecipe(recipeStem);
            bool recipeChanged = false;
            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = display;
                recipe.requiredStation = tier;
                recipe.craftSeconds = seconds;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = b;
                recipe.inputs = new RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    recipe.inputs[i] = new RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + recipeStem + ".asset");
                EditorUtility.SetDirty(recipe);
                recipeChanged = true;
            }
            else if (recipe.outputItem == null)
            {
                recipe.outputItem = b;
                EditorUtility.SetDirty(recipe);
                recipeChanged = true;
            }
            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                recipeChanged = true;
            }
            return dirty || recipeChanged;
        }

        private static bool EnsureRecipe(RecipeRegistry registry, string stem, string display,
            (ItemDefinition item, int count)[] inputs, StationTier tier, float seconds, ItemDefinition output)
        {
            var recipe = FindRecipe(stem);
            bool changed = false;
            if (recipe == null)
            {
                EnsureFolder(RecipesFolder);
                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.displayName = display;
                recipe.requiredStation = tier;
                recipe.craftSeconds = seconds;
                recipe.unlockedByDefault = true;
                recipe.outputCount = 1;
                recipe.outputItem = output;
                recipe.inputs = new RecipeIngredient[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                    recipe.inputs[i] = new RecipeIngredient { item = inputs[i].item, count = inputs[i].count };
                AssetDatabase.CreateAsset(recipe, RecipesFolder + "/" + stem + ".asset");
                EditorUtility.SetDirty(recipe);
                changed = true;
            }
            else if (recipe.outputItem == null && output != null)
            {
                recipe.outputItem = output;
                EditorUtility.SetDirty(recipe);
                changed = true;
            }
            if (!registry.recipes.Contains(recipe))
            {
                registry.recipes.Add(recipe);
                EditorUtility.SetDirty(registry);
                changed = true;
            }
            return changed;
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

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static Material Mat(string name, Color c, float metallic = 0f, float smoothness = 0.5f)
        {
            string path = PrefabsFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) return null;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(sh) { name = name, color = c };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            // Finish is applied on creation only: an existing material keeps the
            // look the player (or an earlier pass) already gave it.
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            else if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
#endif
