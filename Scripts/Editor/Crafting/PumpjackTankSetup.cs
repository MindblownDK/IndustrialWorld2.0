#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power;
using Object = UnityEngine.Object;

namespace IndustrialWorld.EditorTools
{
    /// <summary>
    /// Step 76 (11.0.0-dev): put the Jack Pump onto its TANK-ONLY shape.
    ///
    /// The pumpjack used to consume an Empty Barrel from an input slot and write a
    /// Crude Oil Barrel into an output slot. Crude is a liquid in this game's fluid
    /// chain, so that design made the player hand-carry drums between the well and the
    /// refinery instead of plumbing them together. The component is now a single crude
    /// tank with no item slots at all.
    ///
    /// This step repairs an existing prefab onto that shape. It is non-destructive by
    /// construction: an authored tank capacity, litres-per-batch, power draw, scan
    /// depth or scan radius is never reset — only a value that is missing, zero or
    /// negative is filled in, and only components that belong to the retired barrel
    /// design are removed. Every change is logged.
    /// </summary>
    public static class PumpjackTankSetup
    {
        private const string Root       = "Assets/VoxelEngineAssets";

        /// <summary>
        /// Where the industrial step writes the prefab: <c>ASSET_ROOT + "/Industrial"</c>
        /// plus its own <c>/Prefabs</c>. This is only the SECOND place the step looks —
        /// the authoritative source is the placed-block asset's own reference, because
        /// that is what the game instantiates. A path here can drift; a reference cannot.
        /// </summary>
        private const string PrefabPath = Root + "/Industrial/Prefabs/Pumpjack.prefab";
        private const string BlockPath  = Root + "/Industrial/Blocks/Block_Pumpjack.asset";

        private const float DefaultTankCapacity  = 5000f;
        private const float DefaultLitresPerCycle = 1000f;
        private const float DefaultSecondsPerCycle = 14f;
        private const float DefaultConnectRadius = 2.2f;

        public static void RunStep76()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Jack Pump Tank",
                    "Exit Play Mode before running setup.", "OK");
                return;
            }

            try
            {
                string path = ResolvePrefabPath();
                if (string.IsNullOrEmpty(path))
                {
                    // No stub is written on purpose: a prefab built here would be a grey
                    // box where the industrial step authors a real walking-beam unit, and
                    // a half-built prefab is worse than a clear instruction.
                    Debug.LogError("[Setup 76] No Jack Pump prefab could be found. Looked for the " +
                                   "placed-block reference on " + BlockPath + ", then " + PrefabPath +
                                   ", then any prefab carrying a Pumpjack component under " + Root + ". " +
                                   "Run the industrial content step (Build Industrial Content) first, then run this step again.");
                    EditorUtility.DisplayDialog("Jack Pump Tank",
                        "No Jack Pump prefab could be found.\n\n" +
                        "Run the industrial content step first, then run step 76 again.\n" +
                        "The console lists every place that was searched.", "OK");
                    return;
                }

                Debug.Log("[Setup 76] Jack Pump prefab resolved to " + path);
                Update(path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[Setup 76] Jack Pump is a tank-only crude producer. " +
                          "No input barrels, no output slots — crude goes into the machine's own tank. " +
                          "Existing capacities, batch sizes and power draw were left as authored.");
                EditorUtility.DisplayDialog("Jack Pump Tank",
                    "Jack Pump updated.\n\n" +
                    "It now fills its own crude tank instead of barrel items.\n" +
                    "Authored capacities, batch sizes and power draw were preserved.\n" +
                    "See the console for every change that was made.", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Jack Pump Tank",
                    "Setup stopped: " + ex.Message + "\n\nNothing was written.", "OK");
            }
        }

        /// <summary>
        /// Find the prefab the game actually instantiates, in the order that matters.
        ///
        /// 1. The placed-block asset's <c>placedPrefab</c> reference. This is what a
        ///    player puts in the world, so it is the only answer that cannot be wrong —
        ///    whichever prefab it points at is the one that needs converting.
        /// 2. The path the industrial step writes to, for a project where the block
        ///    asset has not been authored yet.
        /// 3. Any prefab carrying a <see cref="Pumpjack"/> component under the asset
        ///    root, so a prefab that was moved by hand is still found.
        /// </summary>
        private static string ResolvePrefabPath()
        {
            var block = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.BlockItem>(BlockPath);
            if (block != null && block.placedPrefab != null)
            {
                string fromBlock = AssetDatabase.GetAssetPath(block.placedPrefab);
                if (!string.IsNullOrEmpty(fromBlock))
                {
                    Debug.Log("[Setup 76] Resolved from the placed-block reference on " + BlockPath + ".");
                    return fromBlock;
                }
            }
            Debug.Log("[Setup 76] No usable placed-block reference at " + BlockPath + "; trying the authored path.");

            string authored = AssetDatabase.GetAssetPath(AssetDatabase.LoadMainAssetAtPath(PrefabPath));
            if (!string.IsNullOrEmpty(authored)) return authored;
            Debug.Log("[Setup 76] Nothing at " + PrefabPath + "; searching every prefab under " + Root + ".");

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(candidate)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(candidate);
                if (prefab != null && prefab.GetComponent<Pumpjack>() != null)
                {
                    Debug.Log("[Setup 76] Found a prefab carrying a Pumpjack component: " + candidate);
                    return candidate;
                }
            }
            return null;
        }

        private static void Update(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var pump = root.GetComponent<Pumpjack>() ?? root.AddComponent<Pumpjack>();

                EnsureTank(pump);
                EnsureDefaults(pump);
                EnsurePowerConsumer(root, pump);
                RemoveRetiredItemPorts(root);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Make sure the machine has a crude tank that can only ever hold crude. An
        /// existing capacity or stored amount is kept — the tank is only built when it
        /// is missing, and only a capacity that cannot hold anything is replaced.
        /// </summary>
        private static void EnsureTank(Pumpjack pump)
        {
            if (pump.crudeTank == null)
            {
                pump.crudeTank = new MachineFluidTank("Crude Tank", DefaultTankCapacity,
                    LiquidType.CrudeOil, autoType: false);
                Debug.Log("[Setup 76] Jack Pump: added the crude tank (" +
                          DefaultTankCapacity + " L).");
            }
            else if (pump.crudeTank.capacity <= 0f)
            {
                pump.crudeTank.capacity = DefaultTankCapacity;
                Debug.Log("[Setup 76] Jack Pump: the crude tank had no usable capacity; set to " +
                          DefaultTankCapacity + " L. Stored crude was left alone.");
            }
            else
            {
                Debug.Log("[Setup 76] Jack Pump: crude tank kept at its authored " +
                          pump.crudeTank.capacity + " L.");
            }

            // Fixed-type: the tank is a well, so it must not adopt whatever liquid a
            // pipe happens to push at it first.
            if (pump.crudeTank.autoType)
            {
                pump.crudeTank.autoType = false;
                Debug.Log("[Setup 76] Jack Pump: the crude tank was set to a fixed liquid so it cannot adopt another one.");
            }
            if (pump.crudeTank.liquid != LiquidType.CrudeOil)
            {
                Debug.Log("[Setup 76] Jack Pump: the tank was holding " + pump.crudeTank.liquid +
                          "; it is a crude well, so it is now locked to Crude Oil.");
                pump.crudeTank.liquid = LiquidType.CrudeOil;
            }
        }

        /// <summary>Fill in only what was never authored. Nothing tuned is overwritten.</summary>
        private static void EnsureDefaults(Pumpjack pump)
        {
            if (pump.litresPerCycle <= 0f)
            {
                pump.litresPerCycle = DefaultLitresPerCycle;
                Debug.Log("[Setup 76] Jack Pump: litres per batch was unset; set to " +
                          DefaultLitresPerCycle + " L.");
            }
            if (pump.secondsPerCycle <= 0f)
            {
                pump.secondsPerCycle = DefaultSecondsPerCycle;
                Debug.Log("[Setup 76] Jack Pump: seconds per batch was unset; set to " +
                          DefaultSecondsPerCycle + " s.");
            }
            if (pump.baseWattsPerSecond <= 0f)
            {
                pump.baseWattsPerSecond = 4000f;
                Debug.Log("[Setup 76] Jack Pump: pumping power draw was unset; set to 4000 W.");
            }
            if (pump.idleWattsPerSecond < 0f)
            {
                pump.idleWattsPerSecond = 120f;
                Debug.Log("[Setup 76] Jack Pump: idle power draw was negative; set to 120 W.");
            }
            if (pump.scanDepth <= 0)
            {
                pump.scanDepth = 120;
                Debug.Log("[Setup 76] Jack Pump: well scan depth was unset; set to 120 m.");
            }
            if (pump.scanRadius < 0)
            {
                pump.scanRadius = 3;
                Debug.Log("[Setup 76] Jack Pump: well scan radius was negative; set to 3.");
            }
        }

        /// <summary>
        /// The pumpjack must stay a power consumer — without it the machine never comes
        /// online. The authored draw is read from the component, never the other way
        /// round, so a tuned value survives.
        /// </summary>
        private static void EnsurePowerConsumer(GameObject root, Pumpjack pump)
        {
            var power = root.GetComponent<PowerConsumer>() ?? root.AddComponent<PowerConsumer>();
            if (power.connectRadius <= 0f)
            {
                power.connectRadius = DefaultConnectRadius;
                Debug.Log("[Setup 76] Jack Pump: power connect radius was unset; set to " +
                          DefaultConnectRadius + " m.");
            }
            Debug.Log("[Setup 76] Jack Pump: power draw left as authored (pumping " +
                      pump.baseWattsPerSecond + " W, idle " + pump.idleWattsPerSecond + " W).");
        }

        /// <summary>
        /// Strip the item-port plumbing that belonged to the barrel design. The panel
        /// appends an item-port section for any machine that still carries it, so
        /// leaving it behind would show the player two dead slot rows under a tank
        /// gauge. Containers are deliberately NOT deleted: they are created at runtime
        /// and are not part of the prefab.
        /// </summary>
        private static void RemoveRetiredItemPorts(GameObject root)
        {
            var routing = root.GetComponentsInChildren<VoxelEngine.Transport.ItemPortRouting>(true);
            if (routing == null || routing.Length == 0)
            {
                Debug.Log("[Setup 76] Jack Pump: no retired item ports found — nothing to remove.");
                return;
            }

            var removed = new List<string>(routing.Length);
            foreach (var r in routing)
            {
                if (r == null) continue;
                removed.Add(r.gameObject.name);
                Object.DestroyImmediate(r, true);
            }
            Debug.Log("[Setup 76] Jack Pump: removed " + removed.Count +
                      " retired item-port component(s) from the barrel design: " +
                      string.Join(", ", removed) + ".");
        }
    }
}
#endif
