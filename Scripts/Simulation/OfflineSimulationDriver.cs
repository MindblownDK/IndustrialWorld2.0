// Assets/Scripts/VoxelEngine/Simulation/OfflineSimulationDriver.cs
//
// The one always-present tick for systems that must run with nothing loaded.
//
// WHY THIS EXISTS
// Interplanetary cargo was driven from `CargoLaunchPad.Update`, which meant flights only
// advanced while at least one pad was loaded. Fly away from BOTH ends of a route and the
// shipment froze in transit forever - the exact failure the feature was built to avoid.
//
// A pad cannot own that tick, because the whole point is that neither pad is present.
// So one tiny driver is bootstrapped automatically and ticks the handful of registries
// that are genuinely world-independent.
//
// WHY AUTOMATIC RATHER THAN A SCENE OBJECT
// `RuntimeInitializeOnLoadMethod` means there is nothing to place, nothing to forget in a
// scene, and no setup step to re-run. A feature that silently stops working because an
// object was missing from one scene is exactly the class of bug this session has already
// spent two releases chasing.
//
// WHAT BELONGS HERE
// Only registries that are (a) world-independent and (b) cheap. Per-machine catch-up does
// NOT belong here - a machine settles its own absence on wake, which costs nothing while
// away. This driver is for state that exists between objects rather than inside one.

using UnityEngine;
using VoxelEngine.Transport;

namespace VoxelEngine.Simulation
{
    [DisallowMultipleComponent]
    public sealed class OfflineSimulationDriver : MonoBehaviour
    {
        private static OfflineSimulationDriver _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;

            var go = new GameObject("OfflineSimulationDriver");
            _instance = go.AddComponent<OfflineSimulationDriver>();
            DontDestroyOnLoad(go);
        }

        private void OnEnable()
        {
            // A fresh load must not treat the gap since the last session as one giant step
            // that teleports every shipment to its destination on frame one.
            CargoFlightRegistry.ResyncClock();
        }

        private void Update()
        {
            // Interplanetary freight: advances on the saved cosmic clock, so a delivery
            // lands whether or not either world is loaded.
            CargoFlightRegistry.TickCosmic();
        }
    }
}
