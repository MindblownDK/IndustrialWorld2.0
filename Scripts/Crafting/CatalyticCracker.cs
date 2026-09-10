// Assets/Scripts/VoxelEngine/Crafting/CatalyticCracker.cs
//
// Fluid Catalytic Cracker & Continuous Catalytic Reformer (Step 71).
// High-temperature catalytic conversion unit:
//   • Downstream conversion of heavy cuts (HFO) -> light fuels (Diesel, Gasoline, LPG)
//   • Reforming of Naphtha -> High-octane Gasoline + LPG / Hydrogen off-gas
//   • High-pressure Hydrocracking of HFO -> Diesel + Kerosene
//   • Petrochemical polymerisation -> Synthetic Resin & Industrial Lubricants
//
// Supports 4 independent fluid tanks (2 inputs, 2 outputs), dual item containers,
// thermochemical reaction kinetics with dynamic reactor bed temperature, catalyst
// bed decay/replenishment, and exterior analog dials.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Crafting
{
    [RequireComponent(typeof(CraftingStation))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class CatalyticCracker : MonoBehaviour, IItemPortHost, IFluidStore
    {
        public const int INPUT_SLOTS  = 2;
        public const int OUTPUT_SLOTS = 2;

        [Header("Recipes")]
        public List<ProcessingRecipe> knownRecipes = new();

        [Header("Containers (auto-created)")]
        public ItemContainer inputC;
        public ItemContainer outputC;

        [Header("Fluid Tanks (Inputs & Outputs)")]
        public MachineFluidTank fluidInA  = new MachineFluidTank("Heavy Feed",      2000f, LiquidType.HeavyFuelOil, autoType: true);
        public MachineFluidTank fluidInB  = new MachineFluidTank("Secondary Feed",  1000f, LiquidType.Water,        autoType: true);
        public MachineFluidTank fluidOutA = new MachineFluidTank("Primary Product", 2000f, LiquidType.Diesel,      autoType: true);
        public MachineFluidTank fluidOutB = new MachineFluidTank("Secondary Cut",   1000f, LiquidType.Gasoline,    autoType: true);

        [Header("Catalyst & Reaction Dynamics")]
        [Tooltip("Current catalyst bed integrity (0..100%). High-temp cracking slowly consumes catalyst activity.")]
        [Range(0f, 100f)] public float catalystBedPercent = 100f;
        [Tooltip("Reaction core operating temperature in °C.")]
        public float reactorTemperatureC = 25f;
        [Tooltip("Target operating cracking temperature in °C.")]
        public float targetOperatingTempC = 520f;
        [Tooltip("Heating rate in °C per second when powered and running.")]
        public float heatingRateCPerSec = 45f;
        [Tooltip("Cooling rate in °C per second when idle.")]
        public float coolingRateCPerSec = 15f;
        [Tooltip("Catalyst decay percent per completed batch.")]
        public float catalystDecayPerBatch = 2.0f;

        [Header("Power & Thermal Tuning")]
        public float baseWattsPerSecond = 3500f;
        public float idleWattsPerSecond = 100f;

        [Header("Visual Dials & Needles")]
        public Transform tempNeedle;
        public Transform feedNeedle;
        public Transform productNeedle;
        public Light reactorGlowLight;

        private ProcessingRecipe _current;
        private float _progress;
        private PowerConsumer _power;
        private MachineFluidStore _store;

        public IReadOnlyList<MachineFluidTank> FluidTanks => new[] { fluidInA, fluidInB, fluidOutA, fluidOutB };
        public float Progress01 => _current == null ? 0f : Mathf.Clamp01(_progress / Mathf.Max(0.1f, _current.secondsPerBatch));
        public ProcessingRecipe Current => _current;
        public bool  IsOnline   => _power != null && _power.IsPowered;
        public float CurrentWattage { get; private set; }

        public float CrackingEfficiency01
        {
            get
            {
                float catFactor = Mathf.Clamp01(catalystBedPercent / 100f);
                float tempFactor = Mathf.Clamp01(Mathf.InverseLerp(100f, targetOperatingTempC, reactorTemperatureC));
                return 0.3f + 0.7f * catFactor * tempFactor;
            }
        }

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.connectRadius = 2.2f;
            _store = new MachineFluidStore(FluidTanks);
            AutoWireVisuals();
        }

        public void EnsureContainers()
        {
            if (inputC  == null) inputC  = new ItemContainer("Inputs",  INPUT_SLOTS);  else inputC.Resize(INPUT_SLOTS);
            if (outputC == null) outputC = new ItemContainer("Outputs", OUTPUT_SLOTS); else outputC.Resize(OUTPUT_SLOTS);
            fluidInA  ??= new MachineFluidTank("Heavy Feed",      2000f, LiquidType.HeavyFuelOil, autoType: true);
            fluidInB  ??= new MachineFluidTank("Secondary Feed",  1000f, LiquidType.Water,        autoType: true);
            fluidOutA ??= new MachineFluidTank("Primary Product", 2000f, LiquidType.Diesel,      autoType: true);
            fluidOutB ??= new MachineFluidTank("Secondary Cut",   1000f, LiquidType.Gasoline,    autoType: true);
            _store ??= new MachineFluidStore(FluidTanks);
        }

        public void AutoWireVisuals()
        {
            var visuals = transform.Find("Visuals") ?? transform;
            if (tempNeedle == null)
            {
                var g = visuals.Find("Gauge_Temp");
                if (g != null) tempNeedle = g.Find("NeedlePivot");
            }
            if (feedNeedle == null)
            {
                var g = visuals.Find("Gauge_Feed");
                if (g != null) feedNeedle = g.Find("NeedlePivot");
            }
            if (productNeedle == null)
            {
                var g = visuals.Find("Gauge_Product");
                if (g != null) productNeedle = g.Find("NeedlePivot");
            }
            if (reactorGlowLight == null)
            {
                reactorGlowLight = visuals.GetComponentInChildren<Light>();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Thermal core kinetics
            bool isProcessing = _current != null && IsOnline;
            if (isProcessing)
            {
                reactorTemperatureC = Mathf.MoveTowards(reactorTemperatureC, targetOperatingTempC, heatingRateCPerSec * dt);
            }
            else
            {
                reactorTemperatureC = Mathf.MoveTowards(reactorTemperatureC, 25f, coolingRateCPerSec * dt);
            }

            // Dial needles animation: -135° (min) to +135° (max)
            AnimateNeedles(dt);

            // Reactor sight glass glow
            if (reactorGlowLight != null)
            {
                float tempRatio = Mathf.Clamp01(reactorTemperatureC / targetOperatingTempC);
                float flicker = 1f + 0.08f * Mathf.Sin(Time.time * 18f);
                reactorGlowLight.intensity = (isProcessing ? 2.5f : 0.4f * tempRatio) * tempRatio * flicker;
                reactorGlowLight.color = Color.Lerp(new Color(0.2f, 0.4f, 0.9f), new Color(1.0f, 0.55f, 0.15f), tempRatio);
            }

            // Check catalyst replenishment from input inventory
            CheckCatalystReplenish();

            // Recipe selection and execution
            TickProcessing(dt);
        }

        private void CheckCatalystReplenish()
        {
            if (catalystBedPercent > 40f || inputC == null) return;

            for (int i = 0; i < inputC.Size; i++)
            {
                var stack = inputC.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                string itemName = stack.item.name.ToLowerInvariant();
                if (itemName.Contains("catalyst") || itemName.Contains("zeolite") || itemName.Contains("platinum"))
                {
                    inputC.Remove(stack.item, 1);
                    catalystBedPercent = Mathf.Min(100f, catalystBedPercent + 50f);
                    break;
                }
            }
        }

        private void TickProcessing(float dt)
        {
            EnsureContainers();

            // Recipe selection
            if (_current == null || !ProcessingExecutor.CanRun(_current, new[] { inputC }, new[] { outputC }, _store))
            {
                _current = PickBestRecipe();
                if (_current == null)
                {
                    _progress = 0f;
                    CurrentWattage = idleWattsPerSecond;
                    if (_power != null) _power.wattsPerSecond = CurrentWattage;
                    return;
                }
            }

            if (!IsOnline)
            {
                CurrentWattage = idleWattsPerSecond;
                if (_power != null) _power.wattsPerSecond = CurrentWattage;
                return;
            }

            CurrentWattage = baseWattsPerSecond * _current.powerDrawMultiplier;
            if (_power != null) _power.wattsPerSecond = CurrentWattage;

            float speedMul = CrackingEfficiency01;
            _progress += dt * speedMul;

            if (_progress >= _current.secondsPerBatch)
            {
                if (ProcessingExecutor.Run(_current, new[] { inputC }, new[] { outputC }, _store))
                {
                    catalystBedPercent = Mathf.Max(5f, catalystBedPercent - catalystDecayPerBatch);
                }
                _progress = 0f;
            }
        }

        private ProcessingRecipe PickBestRecipe()
        {
            if (knownRecipes == null || knownRecipes.Count == 0) return null;
            foreach (var r in knownRecipes)
            {
                if (r != null && ProcessingExecutor.CanRun(r, new[] { inputC }, new[] { outputC }, _store))
                    return r;
            }
            return null;
        }

        public void SelectRecipe(ProcessingRecipe recipe)
        {
            _current = recipe;
            _progress = 0f;
        }

        private void AnimateNeedles(float dt)
        {
            float speed = 160f * dt;

            // Temp: 0..600°C -> -135°..+135°
            if (tempNeedle != null)
            {
                float t01 = Mathf.Clamp01(reactorTemperatureC / 600f);
                float targetAngle = Mathf.Lerp(-135f, 135f, t01);
                float curr = NormalizeAngle(tempNeedle.localEulerAngles.z);
                float next = Mathf.MoveTowardsAngle(curr, targetAngle, speed);
                tempNeedle.localRotation = Quaternion.Euler(0f, 0f, next);
            }

            // Feed: total input fill -> -135°..+135°
            if (feedNeedle != null)
            {
                float fill01 = Mathf.Clamp01((fluidInA.stored + fluidInB.stored) / (fluidInA.capacity + fluidInB.capacity));
                float targetAngle = Mathf.Lerp(-135f, 135f, fill01);
                float curr = NormalizeAngle(feedNeedle.localEulerAngles.z);
                float next = Mathf.MoveTowardsAngle(curr, targetAngle, speed);
                feedNeedle.localRotation = Quaternion.Euler(0f, 0f, next);
            }

            // Product: total output fill -> -135°..+135°
            if (productNeedle != null)
            {
                float fill01 = Mathf.Clamp01((fluidOutA.stored + fluidOutB.stored) / (fluidOutA.capacity + fluidOutB.capacity));
                float targetAngle = Mathf.Lerp(-135f, 135f, fill01);
                float curr = NormalizeAngle(productNeedle.localEulerAngles.z);
                float next = Mathf.MoveTowardsAngle(curr, targetAngle, speed);
                productNeedle.localRotation = Quaternion.Euler(0f, 0f, next);
            }
        }

        private static float NormalizeAngle(float a)
        {
            while (a > 180f)  a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

        // ── IFluidStore Delegation ──────────────────────────────────────────
        public float Available(LiquidType type) => _store?.Available(type) ?? 0f;
        public float SpaceFor(LiquidType type)  => _store?.SpaceFor(type) ?? 0f;
        public float Draw(LiquidType type, float litres) => _store?.Draw(type, litres) ?? 0f;
        public float Fill(LiquidType type, float litres) => _store?.Fill(type, litres) ?? 0f;

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
            _portContainers[0] = new ItemPortContainer("Inputs",  inputC,  canInput: true,  canOutput: false);
            _portContainers[1] = new ItemPortContainer("Outputs", outputC, canInput: false, canOutput: true);
            return _portContainers;
        }
    }
}
