// Assets/Scripts/VoxelEngine/Nuclear/ReactorCore.cs
//
// Nuclear reactor core. Burns enriched fuel rods to boil water into steam.
// Steam is output via GasPipe network to a SteamTurbine.
// Has internal water and steam tanks.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Gas;
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Nuclear
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class ReactorCore : MonoBehaviour, IItemPortHost
    {
        [Header("Fuel")]
        public ItemDefinition fuelRodItem;
        public ItemDefinition spentFuelRod;
        public float fuelRodBurnTime = 600f;

        [Header("Thermal Output")]
        public float maxThermalKW = 1000f;

        [Header("Control")]
        [Range(0f, 1f)] public float controlRodLevel = 0.5f;
        public float coreTemperature = 300f;
        public float maxSafeTemperature = 800f;
        public float passiveCoolingKW = 50f;

        [Header("Internal Tanks")]
        public float waterTankCapacity = 500f;
        public float steamTankCapacity = 500f;
        public float waterAmount;
        public float steamAmount;
        [Tooltip("Water consumed per kW of thermal output per second.")]
        public float waterPerKW = 0.5f;

        [Header("Containers")]
        public ItemContainer fuelC;
        public ItemContainer spentC;

        public float CurrentThermalKW { get; private set; }
        public float FuelRemaining01 { get; private set; } = 1f;
        public bool IsOnline { get; private set; }
        public bool IsOverheating => coreTemperature > maxSafeTemperature;
        public float WaterFill01 => waterTankCapacity > 0 ? Mathf.Clamp01(waterAmount / waterTankCapacity) : 0;
        public float SteamFill01 => steamTankCapacity > 0 ? Mathf.Clamp01(steamAmount / steamTankCapacity) : 0;

        private float _fuelTimer;
        private float _steamPushTimer;

        private void Awake() => EnsureContainers();

        public void EnsureContainers()
        {
            if (fuelC == null) fuelC = new ItemContainer("Fuel Rods", 4);
            else fuelC.Resize(4);
            if (spentC == null) spentC = new ItemContainer("Spent Rods", 4);
            else spentC.Resize(4);
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
            _portContainers[0] = new ItemPortContainer("Fuel Rods",  fuelC,  canInput: true,  canOutput: false);
            _portContainers[1] = new ItemPortContainer("Spent Rods", spentC, canInput: false, canOutput: true);
            return _portContainers;
        }

        private void Update()
        {
            EnsureContainers();
            float dt = Time.deltaTime;

            bool hasFuel = fuelRodItem != null && fuelC.CountOf(fuelRodItem) > 0;
            IsOnline = hasFuel && controlRodLevel < 0.99f && waterAmount > 1f;

            if (IsOnline)
            {
                float powerFraction = 1f - controlRodLevel;
                CurrentThermalKW = maxThermalKW * powerFraction;

                // Consume water → produce steam.
                float waterNeeded = CurrentThermalKW * waterPerKW * dt;
                float waterUsed = Mathf.Min(waterAmount, waterNeeded);
                waterAmount -= waterUsed;
                float steamProduced = waterUsed * 2f; // water expands to steam
                steamAmount = Mathf.Min(steamTankCapacity, steamAmount + steamProduced);

                // Burn fuel.
                _fuelTimer += (powerFraction / fuelRodBurnTime) * dt;
                FuelRemaining01 = 1f - Mathf.Clamp01(_fuelTimer);
                if (_fuelTimer >= 1f)
                {
                    fuelC.Remove(fuelRodItem, 1);
                    if (spentFuelRod != null) spentC.Insert(new ItemStack(spentFuelRod, 1));
                    _fuelTimer = 0f;
                }

                // Temperature.
                float cooling = passiveCoolingKW + (waterUsed > 0 ? waterUsed * 100f : 0);
                coreTemperature += (CurrentThermalKW - cooling) * dt * 0.1f;
                coreTemperature = Mathf.Max(20f, coreTemperature);

                if (coreTemperature > maxSafeTemperature * 1.5f)
                {
                    controlRodLevel = 1f;
                    Debug.LogWarning("[ReactorCore] SCRAM! Overheated.");
                }
            }
            else
            {
                CurrentThermalKW = 0;
                coreTemperature = Mathf.Max(20f, coreTemperature - passiveCoolingKW * dt * 0.1f);
            }

            // Push steam to gas network.
            _steamPushTimer += dt;
            if (_steamPushTimer >= 0.5f)
            {
                _steamPushTimer = 0;
                PushSteam();
                PullWater();
            }
        }

        private void PushSteam()
        {
            if (steamAmount <= 0) return;
            var tank = GasNetwork.Instance?.FindTankNear(transform.position, GasType.Steam, false);
            if (tank != null)
            {
                float pushed = tank.TryAdd(GasType.Steam, steamAmount);
                steamAmount -= pushed;
            }
        }

        private void PullWater()
        {
            float space = waterTankCapacity - waterAmount;
            if (space <= 0) return;
            // Pull from nearby FluidNode water tanks.
            var hits = Physics.OverlapSphere(transform.position, 3f);
            foreach (var col in hits)
            {
                var wt = col.GetComponent<VoxelEngine.Fluids.WaterTank>();
                if (wt != null && wt.water > 1f)
                {
                    float taken = wt.TakeSome(Mathf.Min(space, 50f));
                    waterAmount += taken;
                    space -= taken;
                    if (space <= 0) break;
                }
            }
        }
    }
}
