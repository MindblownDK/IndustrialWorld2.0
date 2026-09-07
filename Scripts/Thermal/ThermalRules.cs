// Assets/Scripts/VoxelEngine/Thermal/ThermalRules.cs
//
// Central balance surface for block thermal simulation and atmospheric entry.
// Everything that decides "how hot is it here" lives in one file so flight,
// hull damage, HUD and FX can never disagree about the temperature model.

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

        /// <summary>How fast a block slews toward its target temperature (per second).
        /// Deliberately slow: thermal mass is what makes re-entry feel like a commitment.</summary>
        public const float ThermalResponsePerSecond = 0.18f;

        /// <summary>A hull cools this much faster than it heats, so recovery is possible.</summary>
        public const float CoolingRateMultiplier = 1.6f;

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

        /// <summary>Peak °C a running thruster adds to a directly adjacent block.</summary>
        public const float ThrusterNeighbourHeatC = 240f;

        // ── Queries ────────────────────────────────────────────────────────────

        /// <summary>
        /// Ambient temperature at a world position. Inside an atmosphere the planet's
        /// mean surface temperature dominates and thins toward deep space with altitude;
        /// in vacuum the hull sees the cold sink directly.
        /// </summary>
        public static float AmbientTemperatureC(Vector3 worldPosition)
        {
            var body = GravityProvider.ActiveBody;
            float surfaceC = body != null && body.settings != null
                ? body.settings.temperature
                : FallbackAmbientC;

            var sample = AtmosphereManager.Sample(worldPosition);
            if (sample.IsInSpace || !sample.HasAtmosphere || sample.AirDensity <= 0f)
                return DeepSpaceTemperatureC;

            // Air carries the planet's heat; as it thins the hull sees more of the void.
            return Mathf.Lerp(DeepSpaceTemperatureC, surfaceC, Mathf.Clamp01(sample.Density01));
        }

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

        /// <summary>Damage per second a block takes at a given temperature.</summary>
        public static float BlockDamagePerSecond(float temperatureC)
        {
            if (temperatureC <= BlockDamageThresholdC) return 0f;
            float severity = Mathf.Clamp01((temperatureC - BlockDamageThresholdC) / BlockDamageSpanC);
            return MaxBlockDamagePerSecond * severity;
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

        /// <summary>Glow strength 0..1 used to drive emissive hull shading during entry.</summary>
        public static float GlowIntensity01(float temperatureC)
            => Mathf.Clamp01((temperatureC - 320f) / 1100f);
    }
}
