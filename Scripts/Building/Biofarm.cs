// Assets/Scripts/VoxelEngine/Building/Biofarm.cs
//
// Passive oxygen garden / biofarm. Slow, renewable O2 for offline survival.
// Requires power + water (fluid network) + biomass (farming products).
// Produces oxygen into nearby gas pipes / tanks.
//
// Design goals for 11.5:
//   • Expensive to build (steel, glass, circuits, biomass need)
//   • Needs physical space (large-ish prefab, prevents spam)
//   • Power + water + biomass = slow but reliable O2
//   • Slower than industrial electrolyser but renewable and offline-friendly
//   • Feeds gas tanks, cryobeds, life-support rooms via GasNetwork

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Farming;
using VoxelEngine.Fluids;
using VoxelEngine.Gas;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Building
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class Biofarm : MonoBehaviour, IItemPortHost
    {
        [Header("Biofarm Tuning — expensive but reliable")]
        [Tooltip("Watts drawn while producing")]
        public float powerDraw = 65f;
        [Tooltip("Water litres consumed per second while producing")]
        public float waterConsumptionLps = 0.18f;
        [Tooltip("Oxygen units produced per second (slower than electrolyser)")]
        public float oxygenPerSecond = 0.55f;
        [Tooltip("Seconds of production per 1 biomass item")]
        public float secondsPerBiomass = 45f;
        [Tooltip("Internal oxygen buffer capacity")]
        public float bufferCapacity = 260f;

        [Header("Biomass Container")]
        public ItemContainer biomassInput;

        // Runtime state
        public float OxygenBuffer { get; private set; }
        public float BiomassTimeRemaining { get; private set; } // seconds of fuel left from current biomass item
        public bool IsRunning { get; private set; }
        public string Status { get; private set; } = "Idle";
        public float Buffer01 => bufferCapacity > 0 ? Mathf.Clamp01(OxygenBuffer / bufferCapacity) : 0f;

        private PowerConsumer _power;
        private float _pushTimer;
        private float _waterCheckTimer;
        private bool _hasWaterConnection;

        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
        }

        public void EnsureContainers()
        {
            if (biomassInput == null) biomassInput = new ItemContainer("Biomass Input", 4);
            else biomassInput.Resize(4);
            biomassInput.AcceptFilter = (item, wanted) => IsBiomass(item) ? wanted : 0;
        }

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
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Biomass Input", biomassInput, canInput: true, canOutput: false);
            return _portContainers;
        }

        private void Update()
        {
            EnsureContainers();
            float dt = Time.deltaTime;

            // Power check
            bool hasPower = _power == null || _power.IsPowered;
            if (!hasPower)
            {
                IsRunning = false;
                Status = "No Power";
                return;
            }

            // Water check (every 1.5s to avoid per-frame sphere casts)
            _waterCheckTimer += dt;
            if (_waterCheckTimer >= 1.5f)
            {
                _waterCheckTimer = 0f;
                _hasWaterConnection = CheckWaterSource();
            }

            // Need water if required
            if (!_hasWaterConnection)
            {
                IsRunning = false;
                Status = "No Water";
                return;
            }

            // Need biomass fuel
            if (BiomassTimeRemaining <= 0f)
            {
                if (!TryConsumeBiomassItem())
                {
                    IsRunning = false;
                    Status = "No Biomass";
                    return;
                }
            }

            // Produce
            if (OxygenBuffer >= bufferCapacity - 0.01f)
            {
                IsRunning = false;
                Status = "Oxygen Full";
                // Still push
            }
            else
            {
                IsRunning = true;
                Status = "Producing";

                // Consume water
                float needWater = waterConsumptionLps * dt;
                if (!ConsumeWater(needWater))
                {
                    // Ran out mid-tick
                    IsRunning = false;
                    Status = "No Water";
                    _hasWaterConnection = false;
                    return;
                }

                // Burn biomass time
                BiomassTimeRemaining -= dt;

                // Produce oxygen
                float produced = oxygenPerSecond * dt;
                OxygenBuffer = Mathf.Min(bufferCapacity, OxygenBuffer + produced);
            }

            // Push to gas tanks via pipes every 0.5s
            _pushTimer += dt;
            if (_pushTimer >= 0.5f)
            {
                _pushTimer = 0f;
                PushOxygen();
            }
        }

        private bool TryConsumeBiomassItem()
        {
            if (biomassInput == null) return false;
            for (int i = 0; i < biomassInput.Size; i++)
            {
                var slot = biomassInput.GetSlot(i);
                if (slot == null || slot.IsEmpty) continue;
                if (!IsBiomass(slot.item)) continue;
                if (biomassInput.Remove(slot.item, 1) > 0)
                {
                    BiomassTimeRemaining += secondsPerBiomass;
                    return true;
                }
            }
            return false;
        }

        private bool IsBiomass(ItemDefinition item)
        {
            if (item == null) return false;
            if (item is FoodItem) return true;
            if (item is SeedItem) return true;
            // Allow generic biomass / organic resource items
            string id = item.itemId?.ToLowerInvariant() ?? "";
            if (id.Contains("biomass") || id.Contains("organic") || id.Contains("wheat") || id.Contains("corn") || id.Contains("carrot") || id.Contains("fiber") || id.Contains("algae") || id.Contains("compost"))
                return true;
            // Also accept items in Farming category
            if (!string.IsNullOrEmpty(item.category) && item.category.ToLowerInvariant().Contains("farm"))
                return true;
            return false;
        }

        private bool CheckWaterSource()
        {
            var hits = Physics.OverlapSphere(transform.position, 3.2f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var node = col.GetComponent<FluidNode>();
                if (node != null && node.network != null)
                {
                    foreach (var n in node.network.nodes)
                        if (n is WaterTank t && t.liquidType == LiquidType.Water && t.water > 1f)
                            return true;
                }
            }
            // Accept voxel water nearby as emergency source (like sprinkler)
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world != null)
            {
                var pos = world.WorldToVoxel(transform.position);
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    var v = world.GetVoxelWorld(new Vector3Int(pos.x + dx, pos.y - 1, pos.z + dz));
                    if (v.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel)
                        return true;
                }
            }
            return false;
        }

        private bool ConsumeWater(float litres)
        {
            var hits = Physics.OverlapSphere(transform.position, 3.2f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var node = col.GetComponent<FluidNode>();
                if (node == null || node.network == null) continue;
                foreach (var n in node.network.nodes)
                {
                    if (n is WaterTank t && t.liquidType == LiquidType.Water && t.water >= litres)
                    {
                        t.TakeSome(litres);
                        return true;
                    }
                }
            }
            // Voxel water is free but not consumed via tank; allow production
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world != null)
            {
                var pos = world.WorldToVoxel(transform.position);
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    var v = world.GetVoxelWorld(new Vector3Int(pos.x + dx, pos.y - 1, pos.z + dz));
                    if (v.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel)
                        return true;
                }
            }
            return false;
        }

        private void PushOxygen()
        {
            if (OxygenBuffer <= 0.01f) return;
            var tank = GasNetwork.Instance?.FindTankNear(transform.position, GasType.Oxygen, false);
            if (tank != null)
            {
                float pushed = tank.TryAdd(GasType.Oxygen, OxygenBuffer);
                OxygenBuffer -= pushed;
            }
        }

        public void EnsureContainersPublic() => EnsureContainers();
    }
}
