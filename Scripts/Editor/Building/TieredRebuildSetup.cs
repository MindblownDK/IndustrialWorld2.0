#if UNITY_EDITOR
// Assets/Scripts/VoxelEngine/Editor/TieredRebuildSetup.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   INDUSTRIAL WORLD — STEP 102: SIZE-V6 CONSTRUCTION REBUILD       ║
// ║                                                                  ║
// ║  Rebuilds every hammer-placed piece at the new module and with   ║
// ║  the new tier surfaces, and authors the three opening pieces      ║
// ║  that were missing: Wall Frame, Garage Door and Floor Hatch.      ║
// ║                                                                  ║
// ║  WHAT IT REPLACES. Only geometry this tool generated. A prefab   ║
// ║  is rebuilt when every renderer under it is Setup-authored;       ║
// ║  the moment a hand-made child or a custom material is found the  ║
// ║  prefab is left exactly as it is and the console says so.         ║
// ║                                                                  ║
// ║  WHAT IT NEVER TOUCHES. Costs. An existing TieredBlockDefinition ║
// ║  keeps every tuned place and upgrade price; only definitions      ║
// ║  that do not exist yet are given a starting cost.                 ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    public static class TieredRebuildSetup
    {
        private const string Root = "Assets/VoxelEngineAssets/Tiered";
        private const string Prefabs = Root + "/Prefabs";
        private const string Defs = Root + "/Definitions";
        private const string Meshes = Root + "/Meshes";
        private const string Tokens = Root + "/Tokens";
        private const string ItemsFolder = "Assets/VoxelEngineAssets/Items";
        private const string SizeMarker = "Generated_SizeV6";

        private static readonly BuildFamily[] Structural =
        {
            BuildFamily.Foundation, BuildFamily.Floor, BuildFamily.Wall,
            BuildFamily.HalfWall, BuildFamily.Pillar, BuildFamily.Roof,
            BuildFamily.Stairs, BuildFamily.Railing,
            BuildFamily.Doorway, BuildFamily.Door,
            BuildFamily.Window, BuildFamily.WindowPane,
            BuildFamily.WallFrame, BuildFamily.GarageDoor,
            BuildFamily.FloorHatch, BuildFamily.HatchLid
        };

        private static readonly BuildFamily[] Station =
        {
            BuildFamily.StationHull, BuildFamily.StationFloor, BuildFamily.StationCorridor,
            BuildFamily.StationJunction, BuildFamily.StationWindow, BuildFamily.StationAirlock,
            BuildFamily.StationDock, BuildFamily.StationDome
        };

        public static void RunStep102()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 102 — Size-V6 construction rebuild started.");
            Debug.Log($"[VoxelEngineSetupWindow] Step 102 — Module {TieredPieceFactory.Module} m, storey {TieredPieceFactory.Storey} m. " +
                      "Only Setup-generated geometry is replaced; authored costs and custom prefabs are preserved.");

            EnsureFolder("Assets/VoxelEngineAssets", "Tiered");
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root, "Definitions");
            EnsureFolder(Root, "Meshes");
            EnsureFolder(Root, "Tokens");
            EnsureFolder(Root, "Materials");

            // Author every surface before any prefab asks for one: creating a
            // material mid-run and loading it back in the same frame is exactly
            // how an AssetDatabase batch ends up with duplicates.
            TieredSurfaces.Prewarm();

            var registry = LoadOrCreateRegistry();
            int rebuilt = 0, created = 0, skipped = 0;

            try
            {
                int total = Structural.Length + Station.Length, done = 0;
                foreach (var family in Structural)
                {
                    Progress(family, ++done, total);
                    Author(family, registry, false, ref rebuilt, ref created, ref skipped);
                }
                foreach (var family in Station)
                {
                    Progress(family, ++done, total);
                    Author(family, registry, true, ref rebuilt, ref created, ref skipped);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            RepairBuildSystemGrid();

            registry.definitions.RemoveAll(d => d == null);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[VoxelEngineSetupWindow] Step 102 complete — {created} definition(s) authored, " +
                      $"{rebuilt} prefab(s) rebuilt at Size-V6, {skipped} left untouched because they carry custom work.");
            EditorUtility.DisplayDialog("Voxel Engine — Step 102",
                $"Construction rebuilt at {TieredPieceFactory.Module} m modules and {TieredPieceFactory.Storey} m storeys.\n\n" +
                $"Definitions authored: {created}\nPrefabs rebuilt: {rebuilt}\nLeft untouched (custom work): {skipped}\n\n" +
                "Openings, separate fittings and the four-tier Railing are on the build wheel.\n\n" +
                "Existing bases keep their saved family and tier, but pieces placed before this step were " +
                "authored at the old module and will not line up with new ones.",
                "OK");
        }

        private static void Progress(BuildFamily family, int done, int total)
            => EditorUtility.DisplayProgressBar("Step 102 — Size-V6 rebuild",
                   $"Authoring {family} ({done}/{total})", done / (float)total);

        /// <summary>
        /// The placement grid has to match the module or nothing lines up. Repaired
        /// only when it still holds the old default, so a tuned value is respected.
        /// </summary>
        private static void RepairBuildSystemGrid()
        {
            var system = Object.FindAnyObjectByType<BuildSystemV2>(FindObjectsInactive.Include);
            if (system == null) return;

            bool changed = false;
            if (Mathf.Approximately(system.gridSize, 3.75f))
            {
                Undo.RecordObject(system, "Build Grid Size");
                system.gridSize = TieredPieceFactory.Module;
                changed = true;
            }
            // A neighbour socket is one complete 7.5 m module from the host's
            // centre. The former 7.25 m search still missed every neighbour while
            // aiming near the middle of a foundation, so cover the whole module.
            if (Mathf.Approximately(system.socketSnapRadius, 3.25f)
                || Mathf.Approximately(system.socketSnapRadius, 5.5f)
                || Mathf.Approximately(system.socketSnapRadius, 7.25f))
            {
                Undo.RecordObject(system, "Socket Snap Radius");
                system.socketSnapRadius = 8f;
                changed = true;
            }
            if (Mathf.Approximately(system.reach, 8f))
            {
                Undo.RecordObject(system, "Build Reach");
                system.reach = 12f;
                changed = true;
            }

            if (!changed)
            {
                Debug.Log("[Step 102] ✓ BuildSystemV2 already carries authored tuning — left alone.");
                return;
            }
            EditorUtility.SetDirty(system);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(system.gameObject.scene);
            Debug.Log($"[Step 102] ✓ BuildSystemV2 grid raised to {TieredPieceFactory.Module} m with a matching snap radius and reach.");
        }

        // ══════════════════════════════════════════════════════════════════
        //  ONE FAMILY
        // ══════════════════════════════════════════════════════════════════

        private static void Author(BuildFamily family, TieredBlockRegistry registry, bool station,
                                   ref int rebuilt, ref int created, ref int skipped)
        {
            string display = family.ToString();
            string defPath = $"{Defs}/TBlock_{display}.asset";
            var def = AssetDatabase.LoadAssetAtPath<TieredBlockDefinition>(defPath);

            if (def == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(defPath) != null)
                {
                    Debug.LogError($"[Step 102] '{defPath}' exists but is not a TieredBlockDefinition. Preserved, family skipped.");
                    return;
                }
                def = ScriptableObject.CreateInstance<TieredBlockDefinition>();
                def.family = family;
                def.displayName = display;
                ApplyStartingCosts(def, family);
                AssetDatabase.CreateAsset(def, defPath);
                created++;
                Debug.Log($"[Step 102] + Authored definition '{display}' with a starting cost.");
            }
            else
            {
                // A definition that already exists owns its balance. Only the family
                // link is repaired, because a broken link makes the piece unbuildable.
                if (def.family != family)
                {
                    def.family = family;
                    EditorUtility.SetDirty(def);
                }
            }

            for (int t = 0; t < 4; t++)
            {
                var tier = (BuildTier)t;
                string name = $"{display}_{tier}";
                string prefabPath = $"{Prefabs}/{name}.prefab";
                var prefab = BuildPrefab(prefabPath, name, family, tier, station, ref rebuilt, ref skipped);
                if (prefab == null) continue;

                switch (tier)
                {
                    case BuildTier.Wood: def.woodPrefab = prefab; break;
                    case BuildTier.Stone: def.stonePrefab = prefab; break;
                    case BuildTier.Iron: def.ironPrefab = prefab; break;
                    default: def.steelPrefab = prefab; break;
                }
            }

            EditorUtility.SetDirty(def);
            if (!registry.definitions.Contains(def)) registry.definitions.Add(def);
            EnsureToken(family, display);
        }

        private static GameObject BuildPrefab(string path, string name, BuildFamily family, BuildTier tier,
                                              bool station, ref int rebuilt, ref int skipped)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject root;
            bool isNew = existing == null;

            if (isNew)
            {
                root = new GameObject(name);
            }
            else
            {
                root = PrefabUtility.LoadPrefabContents(path);
                if (!IsSetupAuthored(root))
                {
                    PrefabUtility.UnloadPrefabContents(root);
                    skipped++;
                    Debug.Log($"[Step 102] ✓ '{name}' carries custom work — left exactly as it is.");
                    return existing;
                }
                // Strip only what this tool generated.
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                foreach (var collider in root.GetComponents<Collider>())
                    Object.DestroyImmediate(collider);
                foreach (var filter in root.GetComponents<MeshFilter>())
                    Object.DestroyImmediate(filter);
                foreach (var renderer in root.GetComponents<MeshRenderer>())
                    Object.DestroyImmediate(renderer);
            }

            string meshPath = $"{Meshes}/{name}.asset";
            AssetDatabase.DeleteAsset(meshPath);

            if (station) TieredPieceFactory.BuildStation(root, family, tier, meshPath);
            else TieredPieceFactory.Build(root, family, tier, meshPath);

            AddSockets(root, family);
            new GameObject(SizeMarker).transform.SetParent(root.transform, false);

            if (!root.TryGetComponent<PlacedTieredBlock>(out _)) root.AddComponent<PlacedTieredBlock>();
            if (family == BuildFamily.Foundation) EnsureFoundationLegs(root);
            if (family == BuildFamily.Pillar && root.GetComponent<AdjustablePillar>() == null)
                root.AddComponent<AdjustablePillar>();
            if (family == BuildFamily.Roof || family == BuildFamily.Floor
                || family == BuildFamily.FloorHatch || family == BuildFamily.Stairs)
            {
                var loadState = root.GetComponent<StructuralLoadState>();
                if (loadState == null) loadState = root.AddComponent<StructuralLoadState>();
                loadState.loadFamily = family;
            }
            if (family == BuildFamily.Door || family == BuildFamily.GarageDoor) EnsureDoorPivot(root, family);
            if (family == BuildFamily.HatchLid) EnsureHatch(root, tier, name);
            if (family == BuildFamily.Railing && root.GetComponent<TieredRailing>() == null)
                root.AddComponent<TieredRailing>();

            GameObject saved;
            if (isNew)
            {
                saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
            }
            else
            {
                saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }

            rebuilt++;
            return saved;
        }

        /// <summary>
        /// True when every renderer under the prefab is one this tool can own.
        ///
        /// Judged by the MESH, not by the object's name. The first pass matched
        /// names, which quietly excluded every orbital station prefab - their
        /// parts are called Panel, Deck, Collar and so on - so the whole station
        /// family was reported as "custom work" and never rebuilt at all.
        /// A mesh qualifies if it is procedural, lives in our generated Meshes
        /// folder, or is a Unity built-in primitive. Anything imported is a
        /// modeller's work and the prefab is left alone.
        /// </summary>
        private static bool IsSetupAuthored(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;                       // nothing to lose

                string path = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(path)) continue;         // procedural, ours
                if (path.StartsWith(Meshes, System.StringComparison.Ordinal)) continue;
                if (path.StartsWith("Library/", System.StringComparison.Ordinal)) continue;
                if (path.EndsWith("unity default resources", System.StringComparison.Ordinal)) continue;
                if (path.EndsWith("unity_builtin_extra", System.StringComparison.Ordinal)) continue;

                Debug.Log($"[Step 102] '{root.name}' uses the imported mesh '{path}' — left exactly as it is.");
                return false;
            }
            return true;
        }

        private static void EnsureFoundationLegs(GameObject root)
        {
            var support = root.GetComponent<FoundationSupportLegs>();
            if (support == null) support = root.AddComponent<FoundationSupportLegs>();

            Material material = null;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.StartsWith("Mesh_Frame", System.StringComparison.Ordinal))
                {
                    material = renderer.sharedMaterial;
                    break;
                }
            }

            support.legs = new Transform[4];
            int index = 0;
            const float inset = 3.18f;
            foreach (float x in new[] { -inset, inset })
            foreach (float z in new[] { -inset, inset })
            {
                var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leg.name = $"Generated_FoundationLeg_{index + 1}";
                leg.transform.SetParent(root.transform, false);
                leg.transform.localPosition = new Vector3(x, -0.05f, z);
                leg.transform.localScale = new Vector3(0.34f, 0.1f, 0.34f);
                Object.DestroyImmediate(leg.GetComponent<Collider>());
                var renderer = leg.GetComponent<MeshRenderer>();
                if (renderer != null && material != null) renderer.sharedMaterial = material;
                support.legs[index++] = leg.transform;
            }
        }

        /// <summary>
        /// A door leaf needs a pivot at its hinge edge for TieredDoor to swing it.
        /// The welded meshes are re-parented under that pivot.
        /// </summary>
        private static void EnsureDoorPivot(GameObject root, BuildFamily family)
        {
            bool garage = family == BuildFamily.GarageDoor;
            var pivot = new GameObject("Generated_DoorHinge");
            pivot.transform.SetParent(root.transform, false);
            float hinge = garage ? 0f : -1.22f;
            pivot.transform.localPosition = garage
                ? new Vector3(0f, TieredPieceFactory.GarageH, 0f)
                : new Vector3(hinge, 0f, 0f);

            var moved = new List<Transform>();
            foreach (Transform child in root.transform)
                if (child.name.StartsWith("Mesh_", System.StringComparison.Ordinal)) moved.Add(child);
            foreach (var child in moved)
            {
                child.SetParent(pivot.transform, true);
                child.localPosition = garage
                    ? new Vector3(0f, -TieredPieceFactory.GarageH, 0f)
                    : new Vector3(-hinge, 0f, 0f);
            }

            if (garage)
            {
                // The blocking shutter collider is disabled when open, so the
                // header needs a permanent non-blocking target for closing it.
                var interaction = new GameObject("Generated_GarageInteraction");
                interaction.transform.SetParent(root.transform, false);
                interaction.transform.localPosition = new Vector3(0f, TieredPieceFactory.GarageH + 0.30f, 0f);
                var trigger = interaction.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(TieredPieceFactory.GarageW + 0.3f, 0.85f, 0.85f);
            }

            var door = root.GetComponent<TieredDoor>();
            if (door == null) door = root.AddComponent<TieredDoor>();
            door.doorPivot = pivot.transform;
            door.opensUp = garage;
        }

        /// <summary>
        /// Assembles the working hatch: the lid goes under a hinge at its rear
        /// edge, the ladder is built as its own child hanging from that edge with
        /// a climbable volume in front of it, and the runtime component is handed
        /// both. Built closed and furled — opening is the player's business.
        /// </summary>
        private static void EnsureHatch(GameObject root, BuildTier tier, string name)
        {
            float rear = -(TieredPieceFactory.HatchW - 0.14f) * 0.5f;

            var pivot = new GameObject("Generated_HatchPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, rear);

            var moved = new List<Transform>();
            foreach (Transform child in root.transform)
                if (child.name.StartsWith("Mesh_", System.StringComparison.Ordinal)) moved.Add(child);
            foreach (var child in moved)
            {
                child.SetParent(pivot.transform, true);
                child.localPosition = new Vector3(0f, 0f, -rear);
            }

            var lidCollider = pivot.AddComponent<BoxCollider>();
            lidCollider.center = new Vector3(0f, 0.08f, -rear);
            lidCollider.size = new Vector3(TieredPieceFactory.HatchW - 0.14f, 0.18f, TieredPieceFactory.HatchW - 0.14f);

            // The ladder is authored one metre tall hanging from its own origin, so
            // the origin must sit IN the hatch plane and hard against the rear jamb.
            // Its scale is the deployed length: exactly one storey, which is the
            // distance to the floor it drops to.
            var ladder = new GameObject("Generated_Ladder");
            ladder.transform.SetParent(root.transform, false);
            ladder.transform.localPosition = new Vector3(0f, -0.06f, rear + 0.20f);
            ladder.transform.localScale = new Vector3(1f, TieredPieceFactory.Storey, 1f);
            // The lid's own mesh is cleared by BuildPrefab, but the ladder is a
            // second asset built from here and has to be cleared too, or the
            // second run of an explicitly re-runnable step writes over an asset
            // that is already loaded.
            string ladderMesh = $"{Meshes}/{name}_Ladder.asset";
            AssetDatabase.DeleteAsset(ladderMesh);
            TieredPieceFactory.BuildLadder(ladder, tier, ladderMesh);

            // Climb volume: spans the ladder exactly, so its top edge is the hatch
            // lip and the climber is handed back to the floor on arrival.
            var volume = new GameObject("Generated_ClimbVolume");
            volume.transform.SetParent(ladder.transform, false);
            volume.transform.localPosition = new Vector3(0f, -0.5f, 0.34f);
            var trigger = volume.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.4f, 1f, 1.0f);
            volume.AddComponent<ClimbableLadder>();

            var hatch = root.GetComponent<TieredHatch>();
            if (hatch == null) hatch = root.AddComponent<TieredHatch>();
            hatch.lidPivot = pivot.transform;
            hatch.ladder = ladder.transform;
        }

        // ══════════════════════════════════════════════════════════════════
        //  SOCKETS
        // ══════════════════════════════════════════════════════════════════

        private static void AddSockets(GameObject root, BuildFamily family)
        {
            const float m = TieredPieceFactory.Module;
            const float storey = TieredPieceFactory.Storey;
            const float half = TieredPieceFactory.HalfModule;
            const float deck = TieredPieceFactory.DeckTop;

            void Socket(SocketSide side, Vector3 pos, float yaw = 0f)
            {
                var go = new GameObject($"Socket_{side}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                var s = go.AddComponent<BuildSocket>();
                s.side = side;
                s.family = family;
            }

            void Neighbours(float y)
            {
                Socket(SocketSide.North, new Vector3(0f, y, m));
                Socket(SocketSide.South, new Vector3(0f, y, -m));
                Socket(SocketSide.East, new Vector3(m, y, 0f));
                Socket(SocketSide.West, new Vector3(-m, y, 0f));
            }

            void Perimeter(float top)
            {
                Socket(SocketSide.TopNorth, new Vector3(0f, top, half), 0f);
                Socket(SocketSide.TopSouth, new Vector3(0f, top, -half), 180f);
                Socket(SocketSide.TopEast, new Vector3(half, top, 0f), 90f);
                Socket(SocketSide.TopWest, new Vector3(-half, top, 0f), -90f);
            }

            switch (family)
            {
                case BuildFamily.Foundation:
                    Socket(SocketSide.Top, new Vector3(0f, deck, 0f));
                    Neighbours(0f);
                    Perimeter(deck);
                    break;

                case BuildFamily.Floor:
                    Socket(SocketSide.Top, new Vector3(0f, 0.42f, 0f));
                    Neighbours(0f);
                    Perimeter(0.42f);
                    break;

                case BuildFamily.StationFloor:
                    Socket(SocketSide.Top, new Vector3(0f, 0.38f, 0f));
                    Neighbours(0f);
                    Perimeter(0.38f);
                    break;

                case BuildFamily.Window:
                    Socket(SocketSide.Top, new Vector3(0f, storey, 0f));
                    Socket(SocketSide.East, new Vector3(m, 0f, 0f));
                    Socket(SocketSide.West, new Vector3(-m, 0f, 0f));
                    Socket(SocketSide.Center, Vector3.zero);   // takes a Window Pane
                    break;

                case BuildFamily.FloorHatch:
                    Socket(SocketSide.Top, new Vector3(0f, 0.42f, 0f));
                    Neighbours(0f);
                    Perimeter(0.42f);
                    Socket(SocketSide.Center, Vector3.zero);   // takes a Hatch Lid
                    break;

                case BuildFamily.Wall:
                case BuildFamily.HalfWall:
                case BuildFamily.StationHull:
                case BuildFamily.StationWindow:
                {
                    float head = family == BuildFamily.HalfWall ? 2.8f : storey;
                    Socket(SocketSide.Top, new Vector3(0f, head, 0f));          // stack another wall
                    Socket(SocketSide.East, new Vector3(m, 0f, 0f));
                    Socket(SocketSide.West, new Vector3(-m, 0f, 0f));
                    // A floor rests BESIDE a wall, not centred on it: its own edge
                    // has to land on the wall line, so the anchor sits half a module
                    // to either side. Without these a storey never closes.
                    Socket(SocketSide.TopNorth, new Vector3(0f, head, half), 0f);
                    Socket(SocketSide.TopSouth, new Vector3(0f, head, -half), 180f);
                    break;
                }

                case BuildFamily.Doorway:
                case BuildFamily.WallFrame:
                case BuildFamily.StationAirlock:
                    Socket(SocketSide.Top, new Vector3(0f, storey, 0f));
                    Socket(SocketSide.East, new Vector3(m, 0f, 0f));
                    Socket(SocketSide.West, new Vector3(-m, 0f, 0f));
                    Socket(SocketSide.Center, Vector3.zero);
                    Socket(SocketSide.TopNorth, new Vector3(0f, storey, half), 0f);
                    Socket(SocketSide.TopSouth, new Vector3(0f, storey, -half), 180f);
                    // Threshold anchor: stairs snap here and descend a full storey.
                    Socket(SocketSide.Bottom, Vector3.zero);
                    break;

                case BuildFamily.Roof:
                    Neighbours(0f);
                    Socket(SocketSide.Top, new Vector3(0f, storey, 0f));
                    break;

                case BuildFamily.Stairs:
                    // The head of the flight: a floor or landing continues from here.
                    Socket(SocketSide.Top, new Vector3(0f, storey, m));
                    Socket(SocketSide.Bottom, new Vector3(0f, 0f, -m));
                    break;

                case BuildFamily.Pillar:
                    Socket(SocketSide.Top, new Vector3(0f, storey, 0f));
                    Socket(SocketSide.North, new Vector3(0f, 0f, half));
                    Socket(SocketSide.South, new Vector3(0f, 0f, -half));
                    Socket(SocketSide.East, new Vector3(half, 0f, 0f));
                    Socket(SocketSide.West, new Vector3(-half, 0f, 0f));
                    break;

                case BuildFamily.StationCorridor:
                case BuildFamily.StationJunction:
                    Socket(SocketSide.Top, new Vector3(0f, storey * 0.82f, 0f));
                    Neighbours(0f);
                    break;

                case BuildFamily.StationDock:
                case BuildFamily.StationDome:
                    Socket(SocketSide.East, new Vector3(m, 0f, 0f));
                    Socket(SocketSide.West, new Vector3(-m, 0f, 0f));
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  COSTS AND TOKENS
        // ══════════════════════════════════════════════════════════════════

        private static void ApplyStartingCosts(TieredBlockDefinition def, BuildFamily family)
        {
            var wood = Item("wood_log", $"{ItemsFolder}/Item_WoodLog.asset");
            var plank = Item("wooden_plank", $"{ItemsFolder}/Item_WoodenPlank.asset");
            var stone = Item("stone", $"{ItemsFolder}/Item_Stone.asset");
            var iron = Item("iron_ingot", $"{ItemsFolder}/Item_IronIngot.asset");
            var steel = Item("steel_ingot", $"{ItemsFolder}/Item_SteelIngot.asset");

            // Only the three new pieces ever reach this path in an existing project,
            // so these are opening prices, not a rebalance of anything shipped.
            (int w, int p, int s, int i, int st) price = family switch
            {
                BuildFamily.WallFrame => (4, 4, 7, 4, 4),
                BuildFamily.GarageDoor => (2, 4, 0, 6, 5),
                BuildFamily.FloorHatch => (2, 4, 0, 4, 4),
                BuildFamily.WindowPane => (0, 2, 0, 2, 2),
                BuildFamily.HatchLid   => (1, 3, 0, 3, 3),
                BuildFamily.Railing    => (2, 2, 2, 3, 3),
                _ => (3, 3, 5, 3, 3),
            };

            def.placeCost = Cost((wood, price.w), (plank, price.p));
            def.woodToStone = Cost((stone, Mathf.Max(1, price.s)));
            def.stoneToIron = Cost((iron, price.i));
            def.ironToSteel = Cost((steel, price.st));
        }

        private static TierCost Cost(params (ItemDefinition item, int n)[] items)
        {
            var list = new List<Ingredient>();
            foreach (var entry in items)
                if (entry.item != null && entry.n > 0)
                    list.Add(new Ingredient { item = entry.item, count = entry.n });
            return new TierCost { items = list.ToArray() };
        }

        private static ItemDefinition Item(string itemId, string canonicalPath)
        {
            var direct = AssetDatabase.LoadAssetAtPath<ItemDefinition>(canonicalPath);
            if (direct != null) return direct;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/VoxelEngineAssets" }))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.itemId == itemId) return candidate;
            }
            Debug.LogWarning($"[Step 102] Item '{itemId}' was not found. Run Step 5 first if a new piece has no cost.");
            return null;
        }

        private static void EnsureToken(BuildFamily family, string display)
        {
            string path = $"{Tokens}/Token_{display}.asset";
            var token = AssetDatabase.LoadAssetAtPath<BuildToken>(path);
            if (token == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) return;
                token = ScriptableObject.CreateInstance<BuildToken>();
                token.itemId = "build_" + display.ToLowerInvariant();
                token.displayName = display + " (Build)";
                token.description = BuildFamilyInfo.Description(family) +
                                    ". Select it on the hammer wheel and place with the build key.";
                token.iconTint = new Color(0.55f, 0.40f, 0.25f);
                token.maxStack = 99;
                AssetDatabase.CreateAsset(token, path);
            }
            token.family = family;
            EditorUtility.SetDirty(token);
        }

        private static TieredBlockRegistry LoadOrCreateRegistry()
        {
            string path = $"{Root}/TieredBlockRegistry.asset";
            var registry = AssetDatabase.LoadAssetAtPath<TieredBlockRegistry>(path);
            if (registry != null) return registry;
            registry = ScriptableObject.CreateInstance<TieredBlockRegistry>();
            AssetDatabase.CreateAsset(registry, path);
            return registry;
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                int slash = parent.LastIndexOf('/');
                if (slash > 0) EnsureFolder(parent.Substring(0, slash), parent.Substring(slash + 1));
            }
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
