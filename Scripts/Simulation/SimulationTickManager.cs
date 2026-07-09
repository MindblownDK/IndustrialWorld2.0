// Assets/Scripts/VoxelEngine/Simulation/SimulationTickManager.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — CENTRALIZED SIMULATION TICK MANAGER         ║
// ║  Drives all IMachine blocks on a fixed-interval tick instead    ║
// ║  of per-frame Update(). Configurable tick rate, auto-register   ║
// ║  on enable/disable, distance-based culling for performance.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Centralized tick driver for all <see cref="IMachine"/> implementations.
    /// Machines register on enable and unregister on disable. Each frame the
    /// manager accumulates time and fires <see cref="IMachine.SimulationTick"/>
    /// at a fixed interval, avoiding Update() spam across hundreds of blocks.
    ///
    /// Optional: machines beyond <see cref="simulationRadius"/> meters from the
    /// player are skipped (sleep) for performance.
    /// </summary>
    public class SimulationTickManager : MonoBehaviour
    {
        public static SimulationTickManager Instance { get; private set; }

        [Header("Tick Rate")]
        [Tooltip("How many simulation ticks per second. 10 = one tick every 100ms.")]
        public float tickRate = 10f;

        [Header("Performance")]
        [Tooltip("Machines beyond this distance from the player are not ticked. 0 = no culling.")]
        public float simulationRadius = 120f;

        [Tooltip("Minimum ticks before a sleeping machine is checked for range again.")]
        public int sleepCheckInterval = 50;

        // Registered machines keyed by their MonoBehaviour for distance checks.
        private readonly List<IMachine> _machines = new(256);
        private readonly Dictionary<IMachine, MonoBehaviour> _machineOwners = new(256);
        private float _tickAccum;
        private int _tickCount;

        // ── Lifecycle ─────────────────────────────────────────────────

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("SimulationTickManager");
            Instance = go.AddComponent<SimulationTickManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Registration ──────────────────────────────────────────────

        /// <summary>
        /// Register a machine for simulation ticks. Call from OnEnable().
        /// </summary>
        public void Register(IMachine machine, MonoBehaviour owner)
        {
            if (machine == null || owner == null) return;
            if (!_machines.Contains(machine))
            {
                _machines.Add(machine);
                _machineOwners[machine] = owner;
            }
        }

        /// <summary>
        /// Unregister a machine. Call from OnDisable().
        /// </summary>
        public void Unregister(IMachine machine)
        {
            _machines.Remove(machine);
            _machineOwners.Remove(machine);
        }

        // ── Tick Loop ─────────────────────────────────────────────────

        private void Update()
        {
            if (tickRate <= 0f) return;

            _tickAccum += Time.deltaTime;
            float interval = 1f / tickRate;

            while (_tickAccum >= interval)
            {
                _tickAccum -= interval;
                TickAll(interval);
                _tickCount++;
            }
        }

        private void TickAll(float dt)
        {
            // Player position for distance culling. Null-safe — if no player
            // exists (main menu, loading), all machines tick regardless.
            Vector3 playerPos = GetPlayerPosition();
            bool hasPlayer = playerPos != Vector3.zero || Camera.main != null;
            float radiusSqr = simulationRadius > 0f ? simulationRadius * simulationRadius : float.MaxValue;

            for (int i = _machines.Count - 1; i >= 0; i--)
            {
                var machine = _machines[i];
                if (machine == null)
                {
                    // Stale reference — clean up.
                    _machines.RemoveAt(i);
                    continue;
                }

                // Distance culling: skip machines too far from the player.
                if (hasPlayer && simulationRadius > 0f)
                {
                    if (_machineOwners.TryGetValue(machine, out var owner) && owner != null)
                    {
                        float distSqr = (owner.transform.position - playerPos).sqrMagnitude;
                        if (distSqr > radiusSqr) continue;
                    }
                }

                machine.SimulationTick(dt);
            }
        }

        private static Vector3 GetPlayerPosition()
        {
            // Try to find the player via tag — cheap enough at 10 Hz.
            var player = GameObject.FindGameObjectWithTag("Player");
            return player != null ? player.transform.position : Vector3.zero;
        }

        // ── Diagnostics ───────────────────────────────────────────────

        /// <summary>Total registered machines (including sleeping ones).</summary>
        public int MachineCount => _machines.Count;

        /// <summary>Total ticks elapsed since scene load.</summary>
        public int TickCount => _tickCount;
    }
}
