// Assets/Scripts/VoxelEngine/Thermal/ThermalRules.cs
//
// Central balance surface for block thermal simulation, atmospheric entry, thruster
// plumes, visible damage and suit temperature. Everything that decides "how hot is
// it here" and "what does hot look like" lives in one file so flight, hull damage,
// HUD, FX and the player hazard model can never disagree about the temperature model.

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Thermal
{
    /// <summary>Coarse thermal state used for HUD copy, colours and FX gating.</summary>
    public enum ThermalBand
    {
        Nominal = 0,
        Warm = 1,
        Hot = 2,
        Critical = 3,
    }

    public static class ThermalRules
    {
        // ── Ambient ────────────────────────────────────────────────────────────

        /// <summary>Temperature of deep space in °C. Hulls radiate toward this.</summary>
        public const float DeepSpaceTemperatureC = -270f;

        /// <summary>Ambient a body defaults to when no celestial profile is active.</summary>
        public const float FallbackAmbientC = 15f;

        /// <summary>A pressurised, powered room is held at this shirt-sleeve temperature.</summary>
        public const float CabinTemperatureC = 21f;

        // ── Block thermal simulation ───────────────────────────────────────────

        /// <summary>Above this a block begins taking thermal damage.</summary>
        public const float BlockDamageThresholdC = 800f;

        /// <summary>Temperature span above the threshold that reaches maximum burn rate.</summary>
        public const float BlockDamageSpanC = 900f;

        /// <summary>Peak HP/second a block loses while glowing white-hot.</summary>
        public const float MaxBlockDamagePerSecond = 26f;

        /// <summary>How fast a block slews toward a HOTTER target (per second).
        /// Deliberately slow: thermal mass is what makes re-entry feel like a commitment.</summary>
        public const float ThermalResponsePerSecond = 0.18f;

        /// <summary>
        /// Cooling rate relative to heating. Steel radiates slowly: a hull that came in
        /// glowing should still be too hot to touch minutes later, not seconds. 9.30.0
        /// lowered this from 1.6x (which emptied a 1500 °C hull in ~20 s) to 0.55x.
        /// </summary>
        public const float CoolingRateMultiplier = 0.55f;

        /// <summary>
        /// Extra cooling applied while a block is only mildly warm. Radiative loss falls
        /// with temperature, so the last hundred degrees would otherwise linger for ages.
        /// </summary>
        public const float WarmBlockCoolingBoost = 1.8f;

        /// <summary>Below this temperature above ambient the extra cooling boost applies.</summary>
        public const float WarmBlockCoolingBandC = 160f;

        // ── Atmospheric entry ──────────────────────────────────────────────────

        /// <summary>Below this speed (m/s) entry heating is not modelled at all.</summary>
        public const float EntryHeatingMinSpeed = 220f;

        /// <summary>Speed (m/s) that produces full-intensity entry heating.</summary>
        public const float EntryHeatingMaxSpeed = 2600f;

        /// <summary>Stagnation temperature (°C) added at maximum speed in full density.</summary>
        public const float PeakEntryTemperatureC = 1750f;

        /// <summary>Heat-shielded blocks let through only this fraction of entry heat.</summary>
        public const float HeatshieldTransmission = 0.12f;

        /// <summary>A block sheltered behind the grid's leading face receives this much entry heat.</summary>
        public const float ShelteredTransmission = 0.35f;

        // ── Engine heat ────────────────────────────────────────────────────────

        /// <summary>Peak °C a thruster adds to itself while running at full throttle.</summary>
        public const float ThrusterPeakSelfHeatC = 620f;

        /// <summary>Peak °C a running thruster adds to a directly adjacent block (conduction).</summary>
        public const float ThrusterNeighbourHeatC = 240f;

        // ── Thruster exhaust plume ─────────────────────────────────────────────
        // The plume is the column of hot gas leaving the nozzle. Anything standing in
        // it — the ship's own hull, a parked grid, a landing pad, the player — is
        // heated by radiation and convection, falling off with distance and off-axis.

        /// <summary>Stagnation temperature (°C) of the exhaust at the nozzle exit, full throttle.</summary>
        public const float PlumeCoreTemperatureC = 1450f;

        /// <summary>Plume reach in cells along the nozzle axis at full throttle.</summary>
        public const float PlumeLengthCells = 6f;

        /// <summary>Half-angle of the plume cone in degrees.</summary>
        public const float PlumeHalfAngleDeg = 16f;

        /// <summary>Distance (m) between the nozzle and a target below which the plume is at full core temperature.</summary>
        public const float PlumeCoreLengthFraction = 0.22f;

        /// <summary>
        /// Plume heat fraction reaching a target that is a static world block (concrete
        /// pads, hangar walls). Static blocks are heavier than hull plate, so the same
        /// plume takes longer to bite.
        /// </summary>
        public const float PlumeStaticBlockTransmission = 0.85f;

        /// <summary>Fire damage per second dealt to a creature standing in a full-power plume core.</summary>
        public const float PlumeCreatureDamagePerSecond = 9f;

        /// <summary>Relative plume temperature per engine family. Ion exhaust is fast but thin.</summary>
        public static float PlumeScale(ThrusterType type) => type switch
        {
            ThrusterType.Ion => 0.45f,
            ThrusterType.Hydrogen => 1.10f,
            _ => 1f,
        };

        // ── Player / suit ─────────────────────────────────────────────────────

        /// <summary>Resting suit temperature (°C) the body regulates toward.</summary>
        public const float SuitRestingTemperatureC = 37f;

        /// <summary>Suit temperature above which the crew starts taking heat damage.</summary>
        public const float SuitDamageThresholdC = 46f;

        /// <summary>Span above the threshold that reaches maximum suit heat damage.</summary>
        public const float SuitDamageSpanC = 34f;

        /// <summary>Peak HP/second lost at the top of the suit-heat ramp (before armor).</summary>
        public const float SuitMaxHeatDamagePerSecond = 6f;

        /// <summary>Degrees of extra safe headroom every Heat Tolerance module tier buys.</summary>
        public const float SuitToleranceHeadroomPerTierC = 4f;

        /// <summary>Suit temperature below which cold starts to bite.</summary>
        public const float SuitColdThresholdC = 28f;

        /// <summary>Span below the cold threshold that reaches maximum cold damage.</summary>
        public const float SuitColdSpanC = 20f;

        /// <summary>Peak HP/second lost when the suit is frozen through.</summary>
        public const float SuitMaxColdDamagePerSecond = 2.5f;

        /// <summary>
        /// How fast the suit heats toward a hotter environment (per second). Slow: a suit
        /// is an insulated pressure vessel, and heating takes tens of seconds, not frames.
        /// </summary>
        public const float SuitHeatingRatePerSecond = 0.045f;

        /// <summary>
        /// How fast the suit sheds heat once out of the hazard (per second). Deliberately
        /// SLOWER than heating: the crew was cooked and the suit holds it. 9.30.0 replaces
        /// the previous instant-reset behaviour the team flagged as "cools too fast".
        /// </summary>
        public const float SuitCoolingRatePerSecond = 0.018f;

        /// <summary>Extra cooling multiplier while the suit is in an active life-support room.</summary>
        public const float SuitCabinCoolingBoost = 2.2f;

        /// <summary>Extra cooling multiplier while submerged in liquid.</summary>
        public const float SuitSubmergedCoolingBoost = 3.5f;

        /// <summary>
        /// Fraction of the surrounding hull's temperature excess that reaches a player
        /// standing on it. Hull plate radiates onto the crew but a suit is not welded to it.
        /// </summary>
        public const float SuitHullCoupling = 0.06f;

        /// <summary>Fraction of a thruster plume's temperature felt by a player standing in it.</summary>
        public const float SuitPlumeCoupling = 0.14f;

        // ── Queries ────────────────────────────────────────────────────────────

        /// <summary>
        /// Ambient temperature at a world position. Inside an atmosphere the planet's
        /// seasonal surface temperature dominates and thins toward deep space with
        /// altitude; in vacuum the hull sees the cold sink directly.
        /// </summary>
        public static float AmbientTemperatureC(Vector3 worldPosition)
        {
            var body = GravityProvider.ActiveBody;
            float surfaceC = SurfaceTemperatureC(body);

            var sample = AtmosphereManager.Sample(worldPosition);
            if (sample.IsInSpace || !sample.HasAtmosphere || sample.AirDensity <= 0f)
                return DeepSpaceTemperatureC;

            // Air carries the planet's heat; as it thins the hull sees more of the void.
            return Mathf.Lerp(DeepSpaceTemperatureC, surfaceC, Mathf.Clamp01(sample.Density01));
        }

        /// <summary>
        /// Current surface temperature of a body including the seasonal swing, so a
        /// winter night on an ice moon really is colder than its mean.
        /// </summary>
        public static float SurfaceTemperatureC(CelestialBody body)
        {
            if (body == null || body.settings == null) return FallbackAmbientC;

            // Seasons change over minutes, but this is asked by every grid, every hot
            // base block and the suit several times a second, so cache for one second.
            float now = Time.unscaledTime;
            if (ReferenceEquals(body, s_surfaceCacheBody) && now - s_surfaceCacheTime < 1f)
                return s_surfaceCacheC;

            float result;
            try
            {
                result = VoxelEngine.Weather.PlanetarySeasons.GetSeasonInfo(body).effectiveTemperature;
            }
            catch
            {
                result = body.settings.temperature;
            }

            s_surfaceCacheBody = body;
            s_surfaceCacheTime = now;
            s_surfaceCacheC = result;
            return result;
        }

        private static CelestialBody s_surfaceCacheBody;
        private static float s_surfaceCacheTime = -10f;
        private static float s_surfaceCacheC = FallbackAmbientC;

        /// <summary>
        /// Stagnation temperature (°C) added by ploughing through air at speed. Scales
        /// with local air density and roughly with the square of speed, so a shallow
        /// entry through thin air is survivable and a steep fast one is not.
        /// </summary>
        public static float EntryHeatingC(Vector3 worldPosition, float speedMps)
        {
            if (speedMps <= EntryHeatingMinSpeed) return 0f;

            var sample = AtmosphereManager.Sample(worldPosition);
            if (sample.IsInSpace || sample.AirDensity <= 0f) return 0f;

            float t = Mathf.Clamp01((speedMps - EntryHeatingMinSpeed)
                                    / Mathf.Max(1f, EntryHeatingMaxSpeed - EntryHeatingMinSpeed));
            return PeakEntryTemperatureC * t * t * Mathf.Clamp01(sample.Density01);
        }

        /// <summary>
        /// Temperature (°C above ambient) the exhaust plume delivers at a point, given the
        /// nozzle exit position, the direction the exhaust travels, the cell size of the
        /// engine and its 0..1 load. Zero when the point is outside the cone.
        /// </summary>
        public static float PlumeTemperatureAt(Vector3 nozzle, Vector3 exhaustDir, float cellSize,
            float load01, Vector3 point)
        {
            if (load01 <= 0.001f) return 0f;

            Vector3 offset = point - nozzle;
            float along = Vector3.Dot(offset, exhaustDir);
            if (along <= 0f) return 0f;

            float reach = PlumeLengthCells * cellSize * Mathf.Lerp(0.45f, 1f, load01);
            if (along >= reach) return 0f;

            // Off-axis falloff: full heat on the axis, zero at the cone edge. The cone
            // starts with the nozzle radius so a block flush against the exit is inside.
            float radiusAt = cellSize * 0.35f + along * Mathf.Tan(PlumeHalfAngleDeg * Mathf.Deg2Rad);
            float lateral = (offset - exhaustDir * along).magnitude;
            if (lateral >= radiusAt) return 0f;
            float radial = 1f - (lateral / radiusAt);
            radial = radial * radial * (3f - 2f * radial);   // smoothstep

            // Axial falloff: a hot core, then a smooth decay to the plume tip.
            float core = reach * PlumeCoreLengthFraction;
            float axial = along <= core
                ? 1f
                : 1f - Mathf.Clamp01((along - core) / Mathf.Max(0.01f, reach - core));
            axial *= axial;

            return PlumeCoreTemperatureC * load01 * radial * axial;
        }

        /// <summary>Damage per second a block takes at a given temperature.</summary>
        public static float BlockDamagePerSecond(float temperatureC)
        {
            if (temperatureC <= BlockDamageThresholdC) return 0f;
            float severity = Mathf.Clamp01((temperatureC - BlockDamageThresholdC) / BlockDamageSpanC);
            return MaxBlockDamagePerSecond * severity;
        }

        /// <summary>
        /// Effective rate at which a block moves toward its target temperature this tick.
        /// Heating is quick relative to cooling; the last stretch of cooling is boosted
        /// so a hull settles to ambient instead of hovering warm forever.
        /// </summary>
        public static float SlewRate(float current, float target, float ambient)
        {
            if (target >= current) return ThermalResponsePerSecond;

            float rate = ThermalResponsePerSecond * CoolingRateMultiplier;
            float excess = current - ambient;
            if (excess < WarmBlockCoolingBandC)
                rate *= Mathf.Lerp(WarmBlockCoolingBoost, 1f, Mathf.Clamp01(excess / WarmBlockCoolingBandC));
            return rate;
        }

        public static ThermalBand Band(float temperatureC)
        {
            if (temperatureC >= BlockDamageThresholdC) return ThermalBand.Critical;
            if (temperatureC >= 450f) return ThermalBand.Hot;
            if (temperatureC >= 120f) return ThermalBand.Warm;
            return ThermalBand.Nominal;
        }

        public static string BandLabel(ThermalBand band) => band switch
        {
            ThermalBand.Critical => "CRITICAL",
            ThermalBand.Hot => "HOT",
            ThermalBand.Warm => "WARM",
            _ => "NOMINAL",
        };

        public static Color BandColor(ThermalBand band) => band switch
        {
            ThermalBand.Critical => new Color(1.00f, 0.28f, 0.20f),
            ThermalBand.Hot => new Color(1.00f, 0.55f, 0.18f),
            ThermalBand.Warm => new Color(0.95f, 0.82f, 0.35f),
            _ => new Color(0.55f, 0.85f, 0.95f),
        };

        /// <summary>Glow strength 0..1 used to drive emissive hull shading. Starts as a
        /// dull red at ~450 °C and saturates to white heat around 1550 °C.</summary>
        public static float GlowIntensity01(float temperatureC)
            => Mathf.Clamp01((temperatureC - 450f) / 1100f);

        /// <summary>
        /// Incandescent colour of hot steel: dull red, through orange and yellow, to a
        /// white-blue at the top of the range. Used by the hull glow overlay and HUD.
        /// </summary>
        public static Color IncandescentColor(float glow01)
        {
            glow01 = Mathf.Clamp01(glow01);
            if (glow01 < 0.35f)
                return Color.Lerp(new Color(0.55f, 0.03f, 0.01f), new Color(1.00f, 0.18f, 0.02f), glow01 / 0.35f);
            if (glow01 < 0.75f)
                return Color.Lerp(new Color(1.00f, 0.18f, 0.02f), new Color(1.00f, 0.62f, 0.12f), (glow01 - 0.35f) / 0.40f);
            return Color.Lerp(new Color(1.00f, 0.62f, 0.12f), new Color(1.00f, 0.95f, 0.85f), (glow01 - 0.75f) / 0.25f);
        }

        // ── Suit queries ──────────────────────────────────────────────────────

        /// <summary>Heat damage per second for a suit temperature, before armor multipliers.</summary>
        public static float SuitHeatDamagePerSecond(float suitTemperatureC, int heatToleranceTier)
        {
            float threshold = SuitDamageThresholdC + SuitToleranceHeadroomPerTierC * Mathf.Max(0, heatToleranceTier);
            if (suitTemperatureC <= threshold) return 0f;
            float severity = Mathf.Clamp01((suitTemperatureC - threshold) / SuitDamageSpanC);
            return SuitMaxHeatDamagePerSecond * severity * severity;
        }

        /// <summary>Cold damage per second for a suit temperature.</summary>
        public static float SuitColdDamagePerSecond(float suitTemperatureC)
        {
            if (suitTemperatureC >= SuitColdThresholdC) return 0f;
            float severity = Mathf.Clamp01((SuitColdThresholdC - suitTemperatureC) / SuitColdSpanC);
            return SuitMaxColdDamagePerSecond * severity;
        }

        /// <summary>Coarse band for the suit strip: cold, nominal, warm, hot, critical.</summary>
        public static ThermalBand SuitBand(float suitTemperatureC, int heatToleranceTier)
        {
            float threshold = SuitDamageThresholdC + SuitToleranceHeadroomPerTierC * Mathf.Max(0, heatToleranceTier);
            if (suitTemperatureC >= threshold) return ThermalBand.Critical;
            if (suitTemperatureC >= threshold - 4f) return ThermalBand.Hot;
            if (suitTemperatureC >= SuitRestingTemperatureC + 2.5f) return ThermalBand.Warm;
            return ThermalBand.Nominal;
        }
    }
}
