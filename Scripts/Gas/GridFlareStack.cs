// Assets/Scripts/VoxelEngine/Gas/GridFlareStack.cs
//
// GRID FLARE STACK / FLARE VENT — run terminator for surplus gases and liquids.
//
// A flare stack destroys combustible gases (hydrogen, exhaust, off-gas) and
// excess petroleum cuts (LPG, naphtha, kerosene, diesel, gasoline, heavy fuel oil,
// refined oil, crude oil) so that production lines never choke on unwanted fractions.
//
// Key mechanics:
//   • Destruction: Burns incoming combustible fluid/gas streams up to rated capacity.
//   • Oxygen Consumption: Requires atmospheric or piped oxygen. Inside a sealed room
//     without adequate air supply, the flare chokes (NO COMBUSTION AIR).
//   • Thermal Output (IHeatSourceBlock): Generates significant process heat while burning,
//     shedding temperature into its own casing and adjacent blocks.
//   • Waste-Heat Recovery: Optional generator mode that converts a fraction of the burn
//     energy (approx 20%) into electrical watts onto the grid.
//   • Tip Visuals & Lighting: Procedural flame animation that scales with burn load.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Pressure;
using VoxelEngine.Thermal;

namespace VoxelEngine.Gas
{
    public class GridFlareStack : GridBlock, IGridDataProvider, IHeatSourceBlock, IAirtightBlock
    {
        [Header("Flare Controls")]
        [Tooltip("When true, the flare is ignited and accepts incoming fuel to burn.")]
        public bool open = true;

        [Tooltip("When enabled, recovers waste heat from the flare to produce electrical power.")]
        public bool wasteHeatRecovery = false;

        [Header("Burn Capacities")]
        [Tooltip("Maximum litres of liquid destroyed per second.")]
        [Range(1f, 500f)] public float maxLiquidFlowLitresPerSecond = 30f;

        [Tooltip("Maximum litres of gas destroyed per second.")]
        [Range(10f, 5000f)] public float maxGasFlowLitresPerSecond = 800f;

        [Tooltip("Thermal conversion efficiency for waste-heat power recovery.")]
        [Range(0.05f, 0.40f)] public float recoveryEfficiency = 0.20f;

        [Header("Power & Airtight")]
        public float idleWatts = 5f;
        public bool airtight = true;

        [Header("Thermal")]
        [Tooltip("Base self-heat rise in °C at 100% flare burn.")]
        public float peakSelfHeatC = 110f;
        [Tooltip("Heat conducted into directly adjacent neighbours at 100% flare burn.")]
        public float peakNeighbourHeatC = 35f;

        // Telemetry
        public float CurrentGasFlow { get; private set; }
        public float CurrentLiquidFlow { get; private set; }
        public float TotalGasBurned { get; set; }
        public float TotalLiquidBurned { get; set; }
        public float CurrentWattsGenerated { get; private set; }
        public float BurnLoad01 { get; private set; }
        public string Status { get; private set; } = "Idle";
        public GasType LastGas { get; private set; } = GasType.None;
        public LiquidType LastLiquid { get; private set; } = LiquidType.CrudeOil;
        public bool HasBurnedLiquid { get; private set; }

        public bool SealsAir => airtight;
        public bool IsOpen => Enabled && open;
        public bool OxygenStarved { get; private set; }

        // IHeatSourceBlock implementation
        public float SelfHeatC => (IsOpen && !OxygenStarved) ? peakSelfHeatC * BurnLoad01 : 0f;
        public float NeighbourHeatC => (IsOpen && !OxygenStarved) ? peakNeighbourHeatC * BurnLoad01 : 0f;

        public override float PowerDraw
        {
            get
            {
                if (!Enabled || !IsOpen) return 0f;
                // If generating power, return negative power draw so power network receives watts
                if (wasteHeatRecovery && CurrentWattsGenerated > 0.01f)
                    return -CurrentWattsGenerated;
                return idleWatts;
            }
        }

        public GridRoom RoomSide => GridPressureSystem.ConcealedRoom(this);

        private float _pendingGas;
        private float _pendingLiquid;
        private float _pendingEnergyJoules;
        private const float FlareExhaustTemperatureC = 220f;
        private float _window;
        private Transform _flareTip;
        private Light _flareLight;
        private Vector3 _baseTipScale = Vector3.one;
        private ParticleSystem _flamePS;
        private ParticleSystem _smokePS;

        private void Awake()
        {
            _flareTip = transform.Find("Generated_Visuals/Generated_FlareTip")
                        ?? transform.Find("FlareTip")
                        ?? transform.Find("Generated_FlareTip");
            if (_flareTip != null)
            {
                _baseTipScale = _flareTip.localScale;
                _flareLight = _flareTip.GetComponentInChildren<Light>();
            }
        }

        private void Update()
        {
            if (!Enabled)
            {
                CurrentGasFlow = 0f;
                CurrentLiquidFlow = 0f;
                CurrentWattsGenerated = 0f;
                BurnLoad01 = 0f;
                Status = "Disabled";
                _pendingGas = 0f;
                _pendingLiquid = 0f;
                _pendingEnergyJoules = 0f;
                _window = 0f;
                UpdateTipVisuals(0f);
                return;
            }

            float dt = Time.deltaTime;
            _window += dt;

            // Check combustion air
            var air = CombustionAirRules.Resolve(this, airIndependent: false, pipeConnected: false,
                pipeCanFeed: false, bufferHasOxygen: false, allowFallback: true);
            OxygenStarved = (air.Source == AirSource.None);

            if (_window >= 0.25f)
            {
                CurrentGasFlow = _pendingGas / _window;
                CurrentLiquidFlow = _pendingLiquid / _window;
                float totalJoulesPerSec = _pendingEnergyJoules / _window;

                if (wasteHeatRecovery && !OxygenStarved)
                    CurrentWattsGenerated = totalJoulesPerSec * recoveryEfficiency;
                else
                    CurrentWattsGenerated = 0f;

                float gasFraction = maxGasFlowLitresPerSecond > 0f ? CurrentGasFlow / maxGasFlowLitresPerSecond : 0f;
                float liquidFraction = maxLiquidFlowLitresPerSecond > 0f ? CurrentLiquidFlow / maxLiquidFlowLitresPerSecond : 0f;
                BurnLoad01 = Mathf.Clamp01(Mathf.Max(gasFraction, liquidFraction));

                _pendingGas = 0f;
                _pendingLiquid = 0f;
                _pendingEnergyJoules = 0f;
                _window = 0f;

                Status = !open ? "Shut"
                    : OxygenStarved ? "No Oxygen"
                    : BurnLoad01 > 0.01f ? (wasteHeatRecovery ? "Power Recovery" : "Flaring")
                    : "Pilot (Idle)";
            }

            UpdateTipVisuals(BurnLoad01);
        }

        private void EnsureParticleEffects()
        {
            if (_flareTip == null || !Application.isPlaying) return;
            try
            {
                if (_flamePS == null)
                {
                    var fgo = _flareTip.Find("FlameParticles")?.gameObject;
                    if (fgo == null)
                    {
                        fgo = new GameObject("FlameParticles");
                        fgo.transform.SetParent(_flareTip, false);
                        fgo.transform.localPosition = Vector3.zero;
                    }
                    _flamePS = fgo.GetComponent<ParticleSystem>() ?? fgo.AddComponent<ParticleSystem>();
                    ConfigureFlameSystem(_flamePS);
                }
                if (_smokePS == null)
                {
                    var sgo = _flareTip.Find("SmokeParticles")?.gameObject;
                    if (sgo == null)
                    {
                        sgo = new GameObject("SmokeParticles");
                        sgo.transform.SetParent(_flareTip, false);
                        sgo.transform.localPosition = Vector3.back * 0.4f;
                    }
                    _smokePS = sgo.GetComponent<ParticleSystem>() ?? sgo.AddComponent<ParticleSystem>();
                    ConfigureSmokeSystem(_smokePS);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GridFlareStack] Particle init deferred: {ex.Message}");
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
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
                main.startColor = new Color(1f, 0.55f, 0.12f, 0.9f);
                main.maxParticles = 80;
                main.loop = true;

                var em = ps.emission;
                em.enabled = true;
                em.rateOverTime = 8f;

                var sh = ps.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 8f;
                sh.radius = 0.2f;

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
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
                main.startColor = new Color(0.12f, 0.12f, 0.14f, 0.6f);
                main.maxParticles = 120;
                main.loop = true;

                var em = ps.emission;
                em.enabled = true;
                em.rateOverTime = 6f;

                var sh = ps.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 12f;
                sh.radius = 0.25f;

                var ren = ps.GetComponent<ParticleSystemRenderer>();
                if (ren != null) ren.sharedMaterial = GetParticleMat();
            }
            catch (System.Exception) { }
        }

        private void UpdateTipVisuals(float load01)
        {
            if (_flareTip == null) return;
            EnsureParticleEffects();

            bool active = IsOpen && !OxygenStarved;
            if (!active)
            {
                _flareTip.localScale = Vector3.zero;
                if (_flamePS != null) { var em = _flamePS.emission; em.rateOverTime = 0f; }
                if (_smokePS != null) { var em = _smokePS.emission; em.rateOverTime = 0f; }
                if (_flareLight != null) _flareLight.intensity = 0f;
                return;
            }

            // Animate tip scale + flicker
            float flicker = 1f + 0.15f * Mathf.Sin(Time.time * 24f) * Mathf.Cos(Time.time * 17f);
            float scaleMultiplier = Mathf.Lerp(0.35f, 1.4f, load01) * flicker;
            _flareTip.localScale = _baseTipScale * scaleMultiplier;

            if (_flamePS != null)
            {
                var em = _flamePS.emission;
                em.rateOverTime = Mathf.Lerp(4f, 30f, load01);
                var main = _flamePS.main;
                main.startColor = wasteHeatRecovery
                    ? new Color(0.4f, 0.75f, 1.0f, 0.9f)
                    : new Color(1.0f, 0.55f, 0.12f, 0.9f);
            }

            if (_smokePS != null)
            {
                var em = _smokePS.emission;
                em.rateOverTime = wasteHeatRecovery ? Mathf.Lerp(1f, 8f, load01) : Mathf.Lerp(4f, 25f, load01);
                var main = _smokePS.main;
                main.startColor = wasteHeatRecovery
                    ? new Color(0.45f, 0.45f, 0.48f, 0.35f)
                    : new Color(0.10f, 0.10f, 0.12f, 0.75f);
            }

            if (_flareLight != null)
            {
                float baseIntensity = Mathf.Lerp(0.5f, 3.5f, load01);
                _flareLight.intensity = baseIntensity * flicker;
                _flareLight.color = wasteHeatRecovery ? new Color(0.7f, 0.85f, 1.0f) : new Color(1f, 0.6f, 0.2f);
            }
        }

        // ── Gas Acceptance ──────────────────────────────────────────────────
        public bool AcceptsGas(GasType type) => IsOpen && !OxygenStarved && type != GasType.None;

        public float AcceptGas(GasType type, float litres)
        {
            if (litres <= 0.0001f || !IsOpen || type == GasType.None) return 0f;

            // Check oxygen draw
            var room = GridPressureSystem.ConcealedRoom(this);
            if (room != null && room.IsSealed)
            {
                if (room.CombustionAirAtm < ThermalRules.CombustionAirMinAtm)
                {
                    OxygenStarved = true;
                    return 0f;
                }
                // Draw oxygen for combustion
                float o2Needed = litres * 0.2f;
                room.DrawCombustionOxygen(o2Needed);
                room.ReportExhaust(FlareExhaustTemperatureC);
            }

            float capacity = maxGasFlowLitresPerSecond * Time.deltaTime;
            float taken = Mathf.Min(litres, capacity);
            if (taken <= 0.0001f) return 0f;

            TotalGasBurned += taken;
            LastGas = type;
            _pendingGas += taken;

            // Approximate energy: 10.8 kJ per litre of hydrogen/fuel gas
            float energyMJ = (type == GasType.Hydrogen ? 10.8f : 8.0f) * 0.001f * taken;
            _pendingEnergyJoules += energyMJ * 1_000_000f;

            return taken;
        }

        // ── Liquid Acceptance ───────────────────────────────────────────────
        public bool AcceptsLiquid(LiquidType type) => IsOpen && !OxygenStarved && type.IsCombustible();

        public float AcceptLiquid(LiquidType type, float litres)
        {
            if (litres <= 0.0001f || !IsOpen || !type.IsCombustible()) return 0f;

            var room = GridPressureSystem.ConcealedRoom(this);
            if (room != null && room.IsSealed)
            {
                if (room.CombustionAirAtm < ThermalRules.CombustionAirMinAtm)
                {
                    OxygenStarved = true;
                    return 0f;
                }
                float o2Needed = litres * ThermalRules.CombustionOxygenPerFuelLitre;
                room.DrawCombustionOxygen(o2Needed);
                room.ReportExhaust(FlareExhaustTemperatureC + 40f);
            }

            float capacity = maxLiquidFlowLitresPerSecond * Time.deltaTime;
            float taken = Mathf.Min(litres, capacity);
            if (taken <= 0.0001f) return 0f;

            TotalLiquidBurned += taken;
            LastLiquid = type;
            HasBurnedLiquid = true;
            _pendingLiquid += taken;

            float energyMJ = type.BurnEnergyMJPerL() * taken;
            _pendingEnergyJoules += energyMJ * 1_000_000f;

            return taken;
        }

        // ── IGridDataProvider ───────────────────────────────────────────────
        public string SourceName => "Flare Stack";
        public string DataCategory => "Thermal & Disposal";

        public string GetDisplayData()
        {
            string state = !Enabled ? "DISABLED" : !open ? "SHUT"
                : OxygenStarved ? "NO COMBUSTION AIR"
                : BurnLoad01 > 0.01f ? (wasteHeatRecovery ? "RECOVERING POWER" : "FLARING")
                : "PILOT FLAME";

            return $"FLARE {state}\n" +
                   $"LIQUID OUT {CurrentLiquidFlow:0.0} L/s (∑ {TotalLiquidBurned:0} L)\n" +
                   $"GAS OUT {CurrentGasFlow:0} L/s (∑ {TotalGasBurned:0} L)\n" +
                   (wasteHeatRecovery ? $"POWER RECOVERY {CurrentWattsGenerated:0} W\n" : "POWER RECOVERY OFF\n") +
                   $"HEAT OUTPUT +{SelfHeatC:0}°C casing · +{NeighbourHeatC:0}°C ambient";
        }
    }
}
