// Assets/Scripts/VoxelEngine/Crafting/Pumpjack.cs
//
// Powered surface-mounted oil extractor.
//
// Behaviour:
//   * Scans the FluidGrid below itself (in a small column) for CrudeOil voxels.
//   * Every cycle, "lifts" one voxel of oil and produces a Crude Oil Barrel
//     item into its output slot, consuming one Empty Barrel from its input.
//   * Stops cycling when there is no nearby crude-oil voxel left, no Empty
//     Barrel input, or the output slot is full.
//   * Pulls baseWattsPerSecond while pumping, idleWattsPerSecond otherwise.
//
// Designed to be lightweight: no fluid pipes required for crude oil — the
// Refinery consumes Crude Oil BARRELS, so item pipes / chests / manual
// shuttling are the only logistics needed in early-mid game.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Crafting
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class Pumpjack : MonoBehaviour, IItemPortHost
    {
        [Header("Fuel / Output")]
        [Tooltip("Empty Barrel item consumed each cycle.")]
        public ItemDefinition emptyBarrel;
        [Tooltip("Crude Oil Barrel item produced each cycle.")]
        public ItemDefinition crudeOilBarrel;

        [Header("Tuning")]
        [Tooltip("Seconds per pump cycle at 1x speed.")]
        public float secondsPerCycle = 8f;
        [Tooltip("Watts/s drawn while pumping.")]
        public float baseWattsPerSecond = 250f;
        [Tooltip("Watts/s drawn while idle.")]
        public float idleWattsPerSecond = 10f;
        [Tooltip("How far down to scan for crude-oil voxels.")]
        public int   scanDepth = 24;
        [Tooltip("Horizontal scan radius (in voxels).")]
        public int   scanRadius = 2;

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;  // 1 slot — Empty Barrel
        public ItemContainer outputC; // 1 slot — Crude Oil Barrel

        // Runtime
        private PowerConsumer _power;
        private float _progress;

        public float Progress01     => Mathf.Clamp01(_progress / Mathf.Max(0.1f, secondsPerCycle));
        public bool  IsOnline       => _power != null && _power.IsPowered;
        public bool  HasReservoir   { get; private set; }
        public float CurrentWattage { get; private set; }

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 1.8f;
        }

        public void EnsureContainers()
        {
            if (inputC  == null) inputC  = new ItemContainer("Empty Barrels",     1); else inputC.Resize(1);
            if (outputC == null) outputC = new ItemContainer("Crude Oil Barrels", 1); else outputC.Resize(1);
        }

        // ── IItemPortHost ───────────────────────────────────────────────────
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

        public PortConfig PortConfig
        {
            get
            {
                if (_portConfig == null)
                {
                    _portConfig = GetComponent<PortConfig>();
                    if (_portConfig == null) _portConfig = gameObject.AddComponent<PortConfig>();
                    _portConfig.EnsureAllFaces();
                }
                return _portConfig;
            }
        }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureContainers();
            _portContainers ??= new ItemPortContainer[2];
            _portContainers[0] = new ItemPortContainer("Empty Barrels",     inputC,  canInput: true,  canOutput: false);
            _portContainers[1] = new ItemPortContainer("Crude Oil Barrels", outputC, canInput: false, canOutput: true);
            return _portContainers;
        }

        private void Update()
        {
            EnsureContainers();

            bool active = CanRun();
            CurrentWattage = active ? baseWattsPerSecond : idleWattsPerSecond;
            if (_power != null) _power.wattsPerSecond = CurrentWattage;

            if (!IsOnline || !active)
            {
                _progress = 0f;
                return;
            }

            _progress += Time.deltaTime;
            if (_progress >= secondsPerCycle)
            {
                _progress = 0f;
                PumpOneBarrel();
            }
        }

        private bool CanRun()
        {
            if (emptyBarrel == null || crudeOilBarrel == null) return false;
            if (inputC.CountOf(emptyBarrel) <= 0) return false;
            if (!outputC.HasSpace(crudeOilBarrel, 1)) return false;
            HasReservoir = FindOilVoxel(out _);
            return HasReservoir;
        }

        private bool FindOilVoxel(out Vector3Int worldPos)
        {
            worldPos = default;
            var world = VoxelWorld.Instance;
            if (world == null) return false;

            Vector3Int origin = Vector3Int.FloorToInt(transform.position);
            for (int dy = 1; dy <= scanDepth; dy++)
            for (int dx = -scanRadius; dx <= scanRadius; dx++)
            for (int dz = -scanRadius; dz <= scanRadius; dz++)
            {
                var p = new Vector3Int(origin.x + dx, origin.y - dy, origin.z + dz);
                var v = world.GetVoxelWorld(p);
                if (v.material == (byte)MaterialId.CrudeOil)
                {
                    worldPos = p;
                    return true;
                }
            }
            return false;
        }

        private void PumpOneBarrel()
        {
            if (!FindOilVoxel(out var oilPos)) return;

            // Replace the voxel with air so the reservoir actually depletes.
            var world = VoxelWorld.Instance;
            if (world != null)
            {
                try
                {
                    var v = world.GetVoxelWorld(oilPos);
                    v.material   = (byte)MaterialId.Air;
                    v.density    = -127;     // mark as empty so meshing skips it
                    v.waterLevel = 0;
                    world.SetVoxelWorld(oilPos, v, remesh: true);
                }
                catch { /* best-effort drain — never break the pump on exceptions */ }
            }

            inputC.Remove(emptyBarrel, 1);
            outputC.Insert(new ItemStack(crudeOilBarrel, 1));
        }
    }
}
