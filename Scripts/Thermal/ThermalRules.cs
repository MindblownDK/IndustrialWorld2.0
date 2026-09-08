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

        /// <summary>A hot hull cools at this multiple of its heating rate. Slow on
        /// purpose (0.35x): a ship that survives a scorching entry or a sustained
        /// engine burn stays visibly hot for a long while afterwards — heated
        /// metal radiates its charge away gradually, it doesn't snap back.</summary>
        public const float CoolingRateMultiplier = 0.35f;

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

        // ── Thruster exhaust plume ─────────────────────────────────────────────
        // A running engine is no longer a soft warm glow in every direction: the
        // exhaust is a directed plume that leaves the nozzle, heats what it
        // impinges on FAST, and erodes it. Build in your own exhaust cone and
        // the ship will bite its own hull apart.

        /// <summary>Peak °C the exhaust plume adds to the cell directly behind a
        /// hydrogen thruster at full throttle (before distance falloff).</summary>
        public const float ThrusterPlumePeakC = 1350f;

        /// <summary>How many cells the exhaust plume reaches before dissipating.</summary>
        public const int ThrusterPlumeLength = 4;

        /// <summary>Heat retained per plume cell (index 0 = the impinged cell).</summary>
        public static readonly float[] ThrusterPlumeFalloff = { 1f, 0.6f, 0.35f, 0.2f };

        /// <summary>Peak °C bled into the cells flanking a running nozzle — the
        /// side surfaces of the engine housing. Warm, but never burning.</summary>
        public const float ThrusterSideWashHeatC = 170f;

        /// <summary>Peak °C conducted into every block touching a running engine,
        /// so burying a thruster inside a hull still has a (mild) cost.</summary>
        public const float ThrusterConductionHeatC = 90f;

        /// <summary>Direct flame impingement heats the struck surface much faster
        /// than conduction can soak through a hull (per second slew rate).</summary>
        public const float PlumeResponsePerSecond = 0.5f;

        /// <summary>HP/second of direct erosion on the cell directly behind a
        /// hydrogen thruster at full throttle (before distance falloff). This is
        /// the mechanical sandblasting on top of the heat — sustained blasting
        /// chews through armour in seconds-to-tens-of-seconds, not minutes.</summary>
        public const float ThrusterPlumeErosionPerSecond = 40f;

        /// <summary>Plume effects also apply to blocks on OTHER grids caught in
        /// the exhaust cone (a hovering ship can cook the landing pad under it).</summary>
        public const bool CrossGridPlumeDamage = true;

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

        /// <summary>Plume heat multiplier per thruster type — burning hydrogen runs
        /// far hotter than a fan-driven atmospheric engine; ion is nearly clean.</summary>
        public static float PlumeHeatMultiplier(GridSystem.ThrusterType type) => type switch
        {
            GridSystem.ThrusterType.Hydrogen => 1.00f,
            GridSystem.ThrusterType.Atmospheric => 0.75f,
            GridSystem.ThrusterType.Ion => 0.35f,
            _ => 0.75f,
        };

        /// <summary>Plume erosion multiplier per thruster type.</summary>
        public static float PlumeErosionMultiplier(GridSystem.ThrusterType type) => type switch
        {
            GridSystem.ThrusterType.Hydrogen => 1.00f,
            GridSystem.ThrusterType.Atmospheric => 0.70f,
            GridSystem.ThrusterType.Ion => 0.15f,
            _ => 0.70f,
        };

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

        /// <summary>Temperature at which a block's heat glow shell first appears.</summary>
        public const float GlowVisibleC = 420f;

        /// <summary>
        /// Blackbody glow colour for a block temperature: deep red as the glow
        /// first appears, orange at the burn threshold, yellow-white at re-entry
        /// temperatures. Alpha is 1 — callers scale it by glow intensity.
        /// </summary>
        public static Color GlowColor(float temperatureC)
        {
            float t = Mathf.Clamp01((temperatureC - GlowVisibleC) / 1300f);

            if (t < 0.35f)
                return Color.Lerp(new Color(0.45f, 0.03f, 0.01f), new Color(1f, 0.18f, 0.02f), t / 0.35f);
            if (t < 0.70f)
                return Color.Lerp(new Color(1f, 0.18f, 0.02f), new Color(1f, 0.55f, 0.10f), (t - 0.35f) / 0.35f);
            return Color.Lerp(new Color(1f, 0.55f, 0.10f), new Color(1f, 0.93f, 0.78f), (t - 0.70f) / 0.30f);
        }
    }
}
