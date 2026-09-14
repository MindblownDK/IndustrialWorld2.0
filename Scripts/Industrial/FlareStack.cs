// Assets/Scripts/VoxelEngine/Industrial/FlareStack.cs
//
// STATIONARY FLARE STACK — tall derrick tower for land refineries and bases.
// Disposes of excess petroleum cuts and combustible liquids, with selectable
// fuel targeting, analog world gauges, real flame & pollution particles,
// and optional waste-heat electrical power recovery.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.Industrial
{
    [RequireComponent(typeof(PowerGenerator))]
    public class FlareStack : MonoBehaviour, IItemPortHost, IMachineProcessState
    {
        [Header("Flare Controls")]
        [Tooltip("When true, the flare is burning incoming fuel.")]
        public bool isOpen = true;

        [Tooltip("When true, converts waste heat from combustion into electrical watts.")]
        public bool wasteHeatRecovery = false;

        [Tooltip("Target fuel fraction to burn.")]
        public LiquidType targetFuel = LiquidType.HeavyFuelOil;

        [Tooltip("When true, automatically flares any overflowing combustible fraction.")]
        public bool autoSelectFuel = true;

        [Header("Fluid Intake")]
        public MachineFluidTank fluidIn = new MachineFluidTank("Flare Feed", 2000f, LiquidType.HeavyFuelOil, autoType: true);
        public IReadOnlyList<MachineFluidTank> FluidTanks => new[] { fluidIn };

        [Header("World Analog Gauges")]
        public Transform inputGaugeNeedle;
        public Transform burnLoadGaugeNeedle;

        [Header("Tuning")]
        [Tooltip("Maximum litres consumed and destroyed per second.")]
        public float maxBurnRateLitresPerSecond = 25f;

        [Tooltip("Efficiency converting thermal burn energy into electrical watts.")]
        [Range(0.05f, 0.35f)] public float recoveryEfficiency = 0.18f;

        [Tooltip("Radius to automatically sip excess fluids from adjacent processing plants.")]
        public float autoDrainRadius = 6.0f;

        // Telemetry
        public float CurrentBurnRate { get; private set; }
        public float TotalBurnedLitres { get; set; }
        public float GeneratedWatts { get; private set; }
        public float BurnLoad01 { get; private set; }
        public string Status { get; private set; } = "Pilot (Idle)";

        private PowerGenerator _generator;
        private Transform _flareTip;
        private Light _flareLight;
        private ParticleSystem _flamePS;
        private ParticleSystem _smokePS;

        public void SelectFuel(LiquidType fuel, bool auto)
        {
            autoSelectFuel = auto;
            targetFuel = fuel;
            if (!auto && fluidIn != null)
            {
                if (fluidIn.stored <= 0.001f) fluidIn.liquid = fuel;
            }
        }

        private void Awake()
        {
            _generator = GetComponent<PowerGenerator>();
            if (_generator == null) _generator = gameObject.AddComponent<PowerGenerator>();
            _generator.isOn = false;

            AutoWireVisuals();
        }

        public void AutoWireVisuals()
        {
            var visuals = transform.Find("Visuals") ?? transform;
            _flareTip = visuals.Find("FlareTip")
                        ?? visuals.Find("Generated_FlareTip")
                        ?? transform.Find("FlareTip");

            if (_flareTip != null)
            {
                _flareLight = _flareTip.GetComponentInChildren<Light>();
                if (Application.isPlaying)
                {
                    EnsureParticleEffects(_flareTip);
                }
            }

            if (inputGaugeNeedle == null)
            {
                var g = visuals.Find("Gauge_Input");
                if (g != null) inputGaugeNeedle = g.Find("NeedlePivot");
            }
            if (burnLoadGaugeNeedle == null)
            {
                var g = visuals.Find("Gauge_BurnLoad");
                if (g != null) burnLoadGaugeNeedle = g.Find("NeedlePivot");
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (!isOpen)
            {
                CurrentBurnRate = 0f;
                GeneratedWatts = 0f;
                BurnLoad01 = 0f;
                Status = "Shut";
                if (_generator != null) { _generator.isOn = false; _generator.wattsPerSecond = 0f; }
                UpdateTipVisuals(0f);
                UpdateGauges();
                return;
            }

            // Auto-drain nearby excess fluids
            DrainNearbyExcess();

            // Burn fluids from internal tank
            float burned = 0f;
            float energyMJ = 0f;

            if (fluidIn != null && fluidIn.stored > 0.001f)
            {
                float want = Mathf.Min(fluidIn.stored, maxBurnRateLitresPerSecond * dt);
                if (want > 0f)
                {
                    fluidIn.stored -= want;
                    burned = want / dt;
                    TotalBurnedLitres += want;
                    energyMJ = fluidIn.liquid.BurnEnergyMJPerL() * want;
                }
            }

            CurrentBurnRate = burned;
            BurnLoad01 = maxBurnRateLitresPerSecond > 0f ? Mathf.Clamp01(burned / maxBurnRateLitresPerSecond) : 0f;

            if (wasteHeatRecovery && energyMJ > 0.0001f && dt > 0.0001f)
            {
                float totalJoules = energyMJ * 1_000_000f;
                GeneratedWatts = (totalJoules / dt) * recoveryEfficiency;
            }
            else
            {
                GeneratedWatts = 0f;
            }

            if (_generator != null)
            {
                _generator.isOn = GeneratedWatts > 0.01f;
                _generator.wattsPerSecond = GeneratedWatts;
            }

            Status = BurnLoad01 > 0.01f
                ? (wasteHeatRecovery ? "Power Recovery" : "Flaring")
                : "Pilot (Idle)";

            UpdateTipVisuals(BurnLoad01);
            UpdateGauges();
        }

        private void UpdateGauges()
        {
            if (inputGaugeNeedle == null || burnLoadGaugeNeedle == null)
                AutoWireVisuals();

            // Input tank needle (-135° empty to +135° full)
            if (inputGaugeNeedle != null && fluidIn != null)
            {
                float target = Mathf.Lerp(-135f, 135f, fluidIn.Fill01);
                float cur = inputGaugeNeedle.localEulerAngles.z;
                if (cur > 180f) cur -= 360f;
                float eased = Mathf.MoveTowardsAngle(cur, target, 160f * Time.deltaTime);
                inputGaugeNeedle.localRotation = Quaternion.Euler(0f, 0f, eased);
            }

            // Burn load needle (-135° at 0% load to +135° at 100% load)
            if (burnLoadGaugeNeedle != null)
            {
                float target = Mathf.Lerp(-135f, 135f, BurnLoad01);
                float cur = burnLoadGaugeNeedle.localEulerAngles.z;
                if (cur > 180f) cur -= 360f;
                float eased = Mathf.MoveTowardsAngle(cur, target, 160f * Time.deltaTime);
                burnLoadGaugeNeedle.localRotation = Quaternion.Euler(0f, 0f, eased);
            }
        }

        private void DrainNearbyExcess()
        {
            if (fluidIn == null || fluidIn.SpaceFor(fluidIn.liquid) < 50f) return;

            var cols = Physics.OverlapSphere(transform.position, autoDrainRadius);
            for (int i = 0; i < cols.Length; i++)
            {
                var plant = cols[i].GetComponentInParent<DistillationPlant>();
                if (plant != null)
                {
                    foreach (var tank in plant.FluidTanks)
                    {
                        if (tank == null || tank.stored <= 20f) continue;
                        // Siphon if tank is full or if player selected this specific fuel
                        bool shouldSiphon = autoSelectFuel ? tank.Fill01 > 0.85f : tank.liquid == targetFuel;
                        if (shouldSiphon)
                        {
                            if (fluidIn.IsEmpty || fluidIn.liquid == tank.liquid)
                            {
                                float siphon = Mathf.Min(25f * Time.deltaTime, tank.stored * 0.15f);
                                tank.stored -= siphon;
                                fluidIn.liquid = tank.liquid;
                                fluidIn.stored += siphon;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void EnsureParticleEffects(Transform tip)
        {
            if (tip == null || !Application.isPlaying) return;
            try
            {
                if (_flamePS == null)
                {
                    var fgo = tip.Find("FlameParticles")?.gameObject;
                    if (fgo == null)
                    {
                        fgo = new GameObject("FlameParticles");
                        fgo.transform.SetParent(tip, false);
                        fgo.transform.localPosition = Vector3.zero;
                    }
                    _flamePS = fgo.GetComponent<ParticleSystem>() ?? fgo.AddComponent<ParticleSystem>();
                    ConfigureFlameSystem(_flamePS);
                }

                if (_smokePS == null)
                {
                    var sgo = tip.Find("SmokeParticles")?.gameObject;
                    if (sgo == null)
                    {
                        sgo = new GameObject("SmokeParticles");
                        sgo.transform.SetParent(tip, false);
                        sgo.transform.localPosition = Vector3.up * 0.5f;
                    }
                    _smokePS = sgo.GetComponent<ParticleSystem>() ?? sgo.AddComponent<ParticleSystem>();
                    ConfigureSmokeSystem(_smokePS);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FlareStack] Particle effect init deferred: {ex.Message}");
            }
        }

        private static Material _particleMat;
        private static Material GetParticleMat()
        {
            if (_particleMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");
                _particleMat = new Material(sh) { color = Color.white };
                if (_particleMat.HasProperty("_Surface")) _particleMat.SetFloat("_Surface", 1f);
                _particleMat.SetOverrideTag("RenderType", "Transparent");
                _particleMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            return _particleMat;
        }

        private void ConfigureFlameSystem(ParticleSystem ps)
        {
            if (ps == null) return;
            try
            {
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
                main.startColor = new Color(1f, 0.55f, 0.12f, 0.9f);
                main.maxParticles = 120;
                main.loop = true;

                var em = ps.emission;
                em.enabled = true;
                em.rateOverTime = 12f;

                var sh = ps.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 8f;
                sh.radius = 0.25f;

                var ren = ps.GetComponent<ParticleSystemRenderer>();
                if (ren != null) ren.sharedMaterial = GetParticleMat();
            }
            catch (System.Exception) { }
        }

        private void ConfigureSmokeSystem(ParticleSystem ps)
        {
            if (ps == null) return;
            try
            {
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3.0f, 5.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 7.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 3.8f);
                main.startColor = new Color(0.12f, 0.12f, 0.14f, 0.65f);
                main.maxParticles = 200;
                main.loop = true;

                var em = ps.emission;
                em.enabled = true;
                em.rateOverTime = 8f;

                var sh = ps.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 14f;
                sh.radius = 0.35f;

                var ren = ps.GetComponent<ParticleSystemRenderer>();
                if (ren != null) ren.sharedMaterial = GetParticleMat();
            }
            catch (System.Exception) { }
        }

        private void UpdateTipVisuals(float load01)
        {
            if (_flareTip == null) return;
            if (Application.isPlaying)
            {
                EnsureParticleEffects(_flareTip);
            }

            if (!isOpen)
            {
                if (_flamePS != null) { var em = _flamePS.emission; em.rateOverTime = 0f; }
                if (_smokePS != null) { var em = _smokePS.emission; em.rateOverTime = 0f; }
                if (_flareLight != null) _flareLight.intensity = 0f;
                return;
            }

            float flicker = 1f + 0.15f * Mathf.Sin(Time.time * 22f) * Mathf.Cos(Time.time * 16f);

            if (_flamePS != null)
            {
                try
                {
                    var em = _flamePS.emission;
                    em.rateOverTime = Mathf.Lerp(6f, 40f, load01);
                    var main = _flamePS.main;
                    main.startColor = wasteHeatRecovery
                        ? new Color(0.4f, 0.75f, 1.0f, 0.9f)
                        : new Color(1.0f, 0.55f, 0.12f, 0.9f);
                }
                catch (System.Exception) { }
            }

            if (_smokePS != null)
            {
                try
                {
                    var em = _smokePS.emission;
                    em.rateOverTime = wasteHeatRecovery ? Mathf.Lerp(2f, 12f, load01) : Mathf.Lerp(6f, 35f, load01);
                    var main = _smokePS.main;
                    // Cleaner lighter smoke when waste heat recovery is active, thick dark soot when flaring raw
                    main.startColor = wasteHeatRecovery
                        ? new Color(0.45f, 0.45f, 0.48f, 0.35f)
                        : new Color(0.10f, 0.10f, 0.12f, 0.75f);
                }
                catch (System.Exception) { }
            }

            if (_flareLight != null)
            {
                float baseIntensity = Mathf.Lerp(0.8f, 5.0f, load01);
                _flareLight.intensity = baseIntensity * flicker;
                _flareLight.color = wasteHeatRecovery ? new Color(0.7f, 0.85f, 1.0f) : new Color(1f, 0.6f, 0.2f);
            }
        }

        // ── Machine process persistence (9.57.0-dev) ────────────────────────
        // The derrick has no recipe worth resuming, but every setting on it came from
        // a player decision taken on the panel — whether it flares at all, whether it
        // recovers waste heat, and what it targets — and its feed tank holds the
        // surplus it siphoned. All of it rides the shared machine payload.

        private const string ExtraOpen = "flare_open";
        private const string ExtraWasteHeatRecovery = "flare_waste_heat_recovery";
        private const string ExtraTargetFuel = "flare_target_fuel";
        private const string ExtraAutoSelectFuel = "flare_auto_select_fuel";
        private const string ExtraTotalBurned = "flare_total_burned_litres";

        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            MachineProcessPersistence.CaptureTanks(state, FluidTanks);
            state.SetExtra(ExtraOpen, isOpen ? 1f : 0f);
            state.SetExtra(ExtraWasteHeatRecovery, wasteHeatRecovery ? 1f : 0f);
            state.SetExtra(ExtraTargetFuel, (int)targetFuel);
            state.SetExtra(ExtraAutoSelectFuel, autoSelectFuel ? 1f : 0f);
            state.SetExtra(ExtraTotalBurned, TotalBurnedLitres);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;

            MachineProcessPersistence.RestoreTanks(state, FluidTanks);

            isOpen = state.GetExtraBool(ExtraOpen, isOpen);
            wasteHeatRecovery = state.GetExtraBool(ExtraWasteHeatRecovery, wasteHeatRecovery);
            autoSelectFuel = state.GetExtraBool(ExtraAutoSelectFuel, autoSelectFuel);

            int fuel = Mathf.RoundToInt(state.GetExtra(ExtraTargetFuel, (int)targetFuel));
            if (System.Enum.IsDefined(typeof(LiquidType), fuel)) targetFuel = (LiquidType)fuel;

            TotalBurnedLitres = Mathf.Max(0f, state.GetExtra(ExtraTotalBurned, TotalBurnedLitres));
        }

        // ── IItemPortHost ───────────────────────────────────────────────────
        private PortConfig _portConfig;
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

        public IReadOnlyList<ItemPortContainer> GetPortContainers() => System.Array.Empty<ItemPortContainer>();
    }
}
