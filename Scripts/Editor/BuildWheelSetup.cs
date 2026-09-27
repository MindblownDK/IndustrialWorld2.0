#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Items;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Non-destructive Step 100 setup for the Hammer Build Wheel.
    ///
    /// Everything this step touches is additive: a missing wheel object is created,
    /// a missing UIDocument is added, and null references are filled in. Values that
    /// were already authored — sorting order, panel settings, the registry the
    /// designer picked — are read and reported, never overwritten. The step also
    /// audits the tiered registry and names every family that would render as a
    /// locked wedge, so a blank slice on the dial is always explained in the console
    /// instead of being mistaken for a bug.
    /// </summary>
    public static class BuildWheelSetup
    {
        private const string WheelObjectName = "HammerBuildWheel";
        private const string PanelSettingsPath = "Assets/Resources/MenuPanelSettings.asset";
        private const string RegistryPath = "Assets/VoxelEngineAssets/Tiered/TieredBlockRegistry.asset";
        private const int DefaultSortingOrder = 600;

        private static readonly BuildFamily[] StructuralFamilies =
        {
            BuildFamily.Foundation, BuildFamily.Wall, BuildFamily.Floor,
            BuildFamily.Doorway, BuildFamily.Door, BuildFamily.Window,
            BuildFamily.Stairs, BuildFamily.Roof, BuildFamily.Pillar,
            BuildFamily.HalfWall
        };

        private static readonly BuildFamily[] StationFamilies =
        {
            BuildFamily.StationHull, BuildFamily.StationFloor, BuildFamily.StationCorridor,
            BuildFamily.StationJunction, BuildFamily.StationWindow, BuildFamily.StationAirlock,
            BuildFamily.StationDock, BuildFamily.StationDome
        };

        public static void RunStep100()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 100 — Hammer Build Wheel setup started.");
            Debug.Log("[VoxelEngineSetupWindow] Step 100 — Non-destructive: creates what is missing, reconnects what is broken, overwrites nothing that was already authored.");

            int created = 0, linked = 0;

            var player = FindPlayer();
            var wheel = Object.FindAnyObjectByType<HammerBuildWheel>(FindObjectsInactive.Include);

            if (wheel == null)
            {
                if (player == null)
                {
                    Debug.LogWarning("[Step 100] No player with an Inventory was found in the open scene. " +
                                     "Run Step 2 (Spawn Player + UI) first, then re-run this step.");
                    return;
                }

                var host = FindChild(player.transform, WheelObjectName);
                if (host == null)
                {
                    var go = new GameObject(WheelObjectName);
                    Undo.RegisterCreatedObjectUndo(go, "Create Hammer Build Wheel");
                    go.transform.SetParent(player.transform, false);
                    host = go.transform;
                    created++;
                    Debug.Log($"[Step 100] + Created '{WheelObjectName}' under '{player.name}'.");
                }

                wheel = host.GetComponent<HammerBuildWheel>();
                if (wheel == null)
                {
                    wheel = Undo.AddComponent<HammerBuildWheel>(host.gameObject);
                    created++;
                    Debug.Log("[Step 100] + Added the HammerBuildWheel component.");
                }
            }
            else
            {
                Debug.Log($"[Step 100] ✓ HammerBuildWheel already present on '{wheel.gameObject.name}' — kept as authored.");
            }

            if (wheel == null)
            {
                Debug.LogError("[Step 100] Could not resolve a HammerBuildWheel. Nothing was changed.");
                return;
            }

            // ── UIDocument: add when missing, respect it when present ──────────
            var document = wheel.GetComponent<UIDocument>();
            if (document == null)
            {
                document = Undo.AddComponent<UIDocument>(wheel.gameObject);
                created++;
                Debug.Log("[Step 100] + Added the UIDocument the wheel renders into.");
            }

            if (document.sortingOrder == 0)
            {
                Undo.RecordObject(document, "Wheel Sorting Order");
                document.sortingOrder = DefaultSortingOrder;
                linked++;
                Debug.Log($"[Step 100] ✓ Sorting order was unset; raised to {DefaultSortingOrder} so the dial draws over the HUD.");
            }
            else
            {
                Debug.Log($"[Step 100] ✓ Sorting order {document.sortingOrder} was already authored — left alone.");
            }

            if (document.panelSettings == null)
            {
                var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
                if (panel != null)
                {
                    Undo.RecordObject(document, "Wheel Panel Settings");
                    document.panelSettings = panel;
                    linked++;
                    Debug.Log("[Step 100] ✓ Linked the shared MenuPanelSettings.");
                }
                else
                {
                    Debug.LogWarning($"[Step 100] '{PanelSettingsPath}' is missing. Run Step 2 (Spawn Player + UI) to author it.");
                }
            }

            // ── Runtime references ────────────────────────────────────────────
            if (wheel.inventory == null)
            {
                var inventory = Object.FindAnyObjectByType<Inventory>(FindObjectsInactive.Include);
                if (inventory != null)
                {
                    Undo.RecordObject(wheel, "Wheel Inventory Link");
                    wheel.inventory = inventory;
                    linked++;
                    Debug.Log($"[Step 100] ✓ Linked the wheel to the inventory on '{inventory.gameObject.name}'.");
                }
                else
                {
                    Debug.LogWarning("[Step 100] No Inventory in the scene — the wheel will resolve one at runtime instead.");
                }
            }

            if (wheel.registry == null)
            {
                var registry = AssetDatabase.LoadAssetAtPath<TieredBlockRegistry>(RegistryPath)
                               ?? Resources.Load<TieredBlockRegistry>("TieredBlockRegistry");
                if (registry != null)
                {
                    Undo.RecordObject(wheel, "Wheel Registry Link");
                    wheel.registry = registry;
                    linked++;
                    Debug.Log($"[Step 100] ✓ Linked the tiered block registry '{registry.name}'.");
                }
                else
                {
                    Debug.LogWarning("[Step 100] No TieredBlockRegistry found. Run Step 5 (Build Tiered Building Content) first.");
                }
            }

            EditorUtility.SetDirty(wheel);
            if (document != null) EditorUtility.SetDirty(document);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wheel.gameObject.scene);

            AuditRegistry(wheel.registry);

            Debug.Log($"[VoxelEngineSetupWindow] Step 100 complete — {created} object(s)/component(s) created, {linked} reference(s) reconnected. " +
                      "No authored value was replaced.");
            EditorUtility.DisplayDialog("Voxel Engine",
                $"Hammer Build Wheel is wired.\n\n" +
                $"Created: {created}\nReconnected: {linked}\n\n" +
                "Check the Console for any family that will render as a locked wedge.",
                "OK");
        }

        /// <summary>
        /// Reports, without changing anything, which families the dial cannot offer.
        /// A family with no definition or no wood prefab draws as a dark locked wedge.
        /// </summary>
        private static void AuditRegistry(TieredBlockRegistry registry)
        {
            if (registry == null) return;

            var missing = new List<string>();
            var unfinished = new List<string>();

            void Check(BuildFamily family)
            {
                var def = registry.Get(family);
                if (def == null) missing.Add(BuildFamilyInfo.DisplayName(family));
                else if (def.GetPrefab(BuildTier.Wood) == null) unfinished.Add(BuildFamilyInfo.DisplayName(family));
            }

            foreach (var family in StructuralFamilies) Check(family);
            foreach (var family in StationFamilies) Check(family);

            if (missing.Count == 0 && unfinished.Count == 0)
            {
                Debug.Log("[Step 100] ✓ Every build family has a definition and a base-tier prefab — the dial is fully lit.");
                return;
            }

            if (missing.Count > 0)
                Debug.LogWarning($"[Step 100] Locked wedges — no definition in the registry: {string.Join(", ", missing)}. " +
                                 "Run Step 5 (structural) or Step 89 (orbital station) to author them.");
            if (unfinished.Count > 0)
                Debug.LogWarning($"[Step 100] Locked wedges — definition exists but the base-tier prefab is empty: {string.Join(", ", unfinished)}.");
        }


        // ══════════════════════════════════════════════════════════════════
        //  STEP 101 — the rest of the radial wheel family
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Non-destructive Step 101. Every radial selector in the game now runs on the
        /// shared dial, and each one is a plain component on the player: conveyor
        /// shapes, grid armour shapes, energy pipe fittings, road surfaces and the
        /// jump drive destinations. This step adds the ones that are missing and
        /// leaves the ones that are already there untouched.
        /// </summary>
        public static void RunStep101()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 101 — Radial wheel family setup started.");
            Debug.Log("[VoxelEngineSetupWindow] Step 101 — Non-destructive: adds only missing components, changes no authored value.");

            var player = FindPlayer();
            if (player == null)
            {
                Debug.LogWarning("[Step 101] No player with an Inventory was found in the open scene. " +
                                 "Run Step 2 (Spawn Player + UI) first, then re-run this step.");
                return;
            }

            int created = 0, present = 0;

            created += EnsureComponent<VoxelEngine.Simulation.ConveyorShapeWheel>(player, "Conveyor shape wheel", ref present);
            created += EnsureComponent<VoxelEngine.UI.GridShapeWheel>(player, "Grid armour shape wheel", ref present);
            created += EnsureComponent<VoxelEngine.UI.EnergyPipeShapeWheel>(player, "Energy pipe shape wheel", ref present);
            created += EnsureComponent<VoxelEngine.Simulation.RoadSurfaceWheel>(player, "Road surface wheel", ref present);
            created += EnsureComponent<VoxelEngine.GridSystem.JumpDriveWheel>(player, "Jump drive destination wheel", ref present);

            EditorUtility.SetDirty(player);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.scene);

            Debug.Log($"[VoxelEngineSetupWindow] Step 101 complete — {created} wheel(s) added, {present} already present.");
            EditorUtility.DisplayDialog("Voxel Engine",
                $"Radial wheel family is wired on '{player.name}'.\n\n" +
                $"Added: {created}\nAlready present: {present}\n\n" +
                "Conveyor, grid armour, energy pipe and road surface wheels all open on the Build Wheel key " +
                "while the matching item is held. The jump drive wheel opens on the Warp Drive key while piloting.",
                "OK");
        }

        private static int EnsureComponent<TComponent>(GameObject host, string label, ref int present)
            where TComponent : Component
        {
            var existing = host.GetComponent<TComponent>();
            if (existing != null)
            {
                Debug.Log($"[Step 101] ✓ {label} already present — kept as authored.");
                present++;
                return 0;
            }
            Undo.AddComponent<TComponent>(host);
            Debug.Log($"[Step 101] + Added the {label}.");
            return 1;
        }

        private static GameObject FindPlayer()
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) return tagged;
            var inventory = Object.FindAnyObjectByType<Inventory>(FindObjectsInactive.Include);
            return inventory != null ? inventory.gameObject : null;
        }

        private static Transform FindChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == childName) return parent.GetChild(i);
            return null;
        }
    }
}
#endif
