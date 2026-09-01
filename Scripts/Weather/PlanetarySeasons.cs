// Assets/Scripts/VoxelEngine/Weather/PlanetarySeasons.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                    PLANETARY SEASONS SYSTEM                          ║
// ║                                                                      ║
// ║  Deterministic orbital calendar & seasonal climate simulator.       ║
// ║  Tracks seasons (Spring, Summer, Autumn, Winter) for every planet    ║
// ║  and moon in the cosmic registry based on Keplerian orbit            ║
// ║  propagation and elapsed simulation time.                           ║
// ║                                                                      ║
// ║   • True annual temperature oscillations (+14°C in summer, -18°C    ║
// ║     in winter for temperate worlds).                                 ║
// ║   • Winter brings freezing subzero temperatures, transforming rain   ║
// ║     into realistic snow and blizzards.                               ║
// ║   • Solar irradiance changes (+22% in summer, -22% in winter).       ║
// ║   • Autumn and Spring gales boost wind turbine power and ambient     ║
// ║     wind strength.                                                   ║
// ║   • Screen data queries allow grid screens and HUDs to track seasons ║
// ║     of ANY specified planet or the local world.                      ║
// ╚══════════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Weather
{
    /// <summary>The four astronomical planetary seasons.</summary>
    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3
    }

    /// <summary>
    /// Comprehensive snapshot of a celestial body's current seasonal state and climate telemetry.
    /// </summary>
    [Serializable]
    public struct PlanetSeasonInfo
    {
        public string bodyName;
        public Season currentSeason;
        public float seasonProgress;          // 0.0 to 1.0 within the current season
        public int seasonDay;                 // Current day within the season (1..90)
        public int daysInSeason;              // Total days in this season (90)
        public int dayOfYear;                 // Current day of the planetary year (1..360)
        public int totalDaysInYear;           // Total days in a planetary year (360)
        public int daysRemainingInSeason;     // Days left until next season transition
        public int currentYear;               // Planetary year number (1-based)
        public float orbitalPhaseDegrees;     // Orbit angle around the star (0..360°)
        public float baseTemperature;         // Authored baseline surface temperature (°C)
        public float seasonalTemperatureOffset;// Current temperature shift from season (°C)
        public float effectiveTemperature;    // Final surface temperature: base + offset (°C)
        public float solarMultiplier;         // Solar irradiance multiplier (0.75..1.25)
        public float windMultiplier;          // Seasonal wind multiplier (1.0..1.35)
        public float stormChanceModifier;     // Additive modifier to storm chance
        public bool isFreezing;               // True when effective temperature <= 0°C
        public string forecastPrecipitation;  // Expected precipitation: "Clear", "Rain", "Snow", "Blizzard", "None"

        public string SeasonName => currentSeason switch
        {
            Season.Spring => "Spring",
            Season.Summer => "Summer",
            Season.Autumn => "Autumn",
            Season.Winter => "Winter",
            _ => "Standard"
        };

        public string SeasonIcon => currentSeason switch
        {
            Season.Spring => "🌱",
            Season.Summer => "☀",
            Season.Autumn => "🍂",
            Season.Winter => "❄",
            _ => "🌍"
        };

        public string NextSeasonName => currentSeason switch
        {
            Season.Spring => "Summer",
            Season.Summer => "Autumn",
            Season.Autumn => "Winter",
            Season.Winter => "Spring",
            _ => "Spring"
        };

        public string FormattedSummary()
        {
            int pct = Mathf.Clamp(Mathf.RoundToInt(seasonProgress * 100f), 0, 100);
            string tempSign = effectiveTemperature >= 0f ? "+" : "";
            string precipIcon = forecastPrecipitation switch
            {
                "Snow" or "Blizzard" => "❄",
                "Rain" or "Heavy Rain" or "Storm" => "🌧",
                _ => "☀"
            };
            return $"{bodyName.ToUpperInvariant()} {SeasonIcon} {SeasonName.ToUpperInvariant()}\n" +
                   $"Day {seasonDay}/{daysInSeason} ({pct}%) • {daysRemainingInSeason}d left\n" +
                   $"Temp: {tempSign}{effectiveTemperature:F1}°C ({precipIcon} {forecastPrecipitation})\n" +
                   $"Solar: {Mathf.RoundToInt(solarMultiplier * 100f)}% | Wind: {Mathf.RoundToInt(windMultiplier * 100f)}%";
        }

        public string FormattedBars()
        {
            int progBars = Mathf.Clamp(Mathf.RoundToInt(seasonProgress * 10f), 0, 10);
            string progBarStr = new string('█', progBars) + new string('░', 10 - progBars);

            int solarBars = Mathf.Clamp(Mathf.RoundToInt((solarMultiplier - 0.7f) / 0.6f * 10f), 0, 10);
            string solarBarStr = new string('█', solarBars) + new string('░', 10 - solarBars);

            int windBars = Mathf.Clamp(Mathf.RoundToInt((windMultiplier - 0.9f) / 0.5f * 10f), 0, 10);
            string windBarStr = new string('█', windBars) + new string('░', 10 - windBars);

            return $"[{bodyName.ToUpperInvariant()} {SeasonName.ToUpperInvariant()}]\n" +
                   $"Progress [{progBarStr}] {Mathf.RoundToInt(seasonProgress * 100f)}%\n" +
                   $"Solar    [{solarBarStr}] {Mathf.RoundToInt(solarMultiplier * 100f)}%\n" +
                   $"Wind     [{windBarStr}] {Mathf.RoundToInt(windMultiplier * 100f)}%";
        }

        public string FormattedDetailed()
        {
            string tempSign = effectiveTemperature >= 0f ? "+" : "";
            string offsetSign = seasonalTemperatureOffset >= 0f ? "+" : "";
            return $"PLANETARY SEASONS TELEMETRY\n" +
                   $"Target: {bodyName}\n" +
                   $"Season: {SeasonIcon} {SeasonName} (Year {currentYear}, Day {dayOfYear}/{totalDaysInYear})\n" +
                   $"Progress: {seasonDay}/{daysInSeason} days ({Mathf.RoundToInt(seasonProgress * 100f)}%)\n" +
                   $"Next: {NextSeasonName} in {daysRemainingInSeason} days\n" +
                   $"Surface Temp: {tempSign}{effectiveTemperature:F1}°C (Base {baseTemperature:F0}°C, Shift {offsetSign}{seasonalTemperatureOffset:F1}°C)\n" +
                   $"Solar Efficiency: {Mathf.RoundToInt(solarMultiplier * 100f)}%\n" +
                   $"Wind Factor: {Mathf.RoundToInt(windMultiplier * 100f)}%\n" +
                   $"Precipitation: {forecastPrecipitation} (Orbit: {orbitalPhaseDegrees:F0}°)";
        }
    }

    /// <summary>
    /// Static query engine for planetary seasons across all celestial bodies.
    /// Fully deterministic from CosmicRegistry simulation time and orbital math.
    /// </summary>
    public static class PlanetarySeasons
    {
        public const int DaysPerSeason = 90;
        public const int DaysPerYear = 360;
        public const double DefaultYearDurationSeconds = 1800.0; // 30 minutes nominal real-time solar year

        private static readonly List<PlanetSeasonInfo> _cachedInfoList = new List<PlanetSeasonInfo>();
        private static readonly List<string> _cachedNameList = new List<string>();

        /// <summary>
        /// Query the current seasonal climate state for a specific body settings asset.
        /// </summary>
        public static PlanetSeasonInfo GetSeasonInfo(BodySettings settings)
        {
            if (settings == null)
                return GetDefaultInfo("Unknown");

            return ComputeSeasonInfo(settings.bodyName, settings);
        }

        /// <summary>
        /// Query the current seasonal climate state for a scene CelestialBody.
        /// </summary>
        public static PlanetSeasonInfo GetSeasonInfo(CelestialBody body)
        {
            if (body == null || body.settings == null)
                return GetDefaultInfo("Unknown");

            return ComputeSeasonInfo(body.DisplayName, body.settings);
        }

        /// <summary>
        /// Query the current seasonal climate state for a body by its display name.
        /// </summary>
        public static PlanetSeasonInfo GetSeasonInfo(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName))
                return GetCurrentSeasonInfo();

            var registry = CosmicRegistry.Instance;
            if (registry != null && registry.IsReady && registry.Bodies != null)
            {
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    var b = registry.Bodies[i];
                    if (b != null && string.Equals(b.DisplayName, bodyName, StringComparison.OrdinalIgnoreCase))
                        return ComputeSeasonInfo(b.DisplayName, b.settings, b);
                }
            }

            // Check active scene body
            var active = GravityProvider.ActiveBody;
            if (active != null && string.Equals(active.DisplayName, bodyName, StringComparison.OrdinalIgnoreCase))
                return ComputeSeasonInfo(active.DisplayName, active.settings);

            return GetDefaultInfo(bodyName);
        }

        /// <summary>
        /// Query the season state for the player's current/local world.
        /// </summary>
        public static PlanetSeasonInfo GetCurrentSeasonInfo()
        {
            var active = GravityProvider.ActiveBody;
            if (active != null && active.settings != null)
                return GetSeasonInfo(active);

            var registry = CosmicRegistry.Instance;
            if (registry != null && registry.IsReady && registry.Bodies != null && registry.Bodies.Count > 0)
                return GetSeasonInfo(registry.Bodies[0].settings);

            return GetDefaultInfo("Home Planet");
        }

        /// <summary>
        /// Query the current ambient surface temperature of the local body in °C.
        /// </summary>
        public static float GetCurrentTemperature()
        {
            return GetCurrentSeasonInfo().effectiveTemperature;
        }

        /// <summary>
        /// Get a list of all monitored celestial body names in the solar system.
        /// </summary>
        public static IReadOnlyList<string> GetAllMonitoredBodyNames()
        {
            _cachedNameList.Clear();
            var registry = CosmicRegistry.Instance;
            if (registry != null && registry.IsReady && registry.Bodies != null)
            {
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    var b = registry.Bodies[i];
                    if (b != null && !string.IsNullOrEmpty(b.DisplayName))
                        _cachedNameList.Add(b.DisplayName);
                }
            }

            if (_cachedNameList.Count == 0)
            {
                var active = GravityProvider.ActiveBody;
                if (active != null && !string.IsNullOrEmpty(active.DisplayName))
                    _cachedNameList.Add(active.DisplayName);
                else
                    _cachedNameList.Add("Home Planet");
            }

            return _cachedNameList;
        }

        /// <summary>
        /// Query seasonal states for all bodies in the system.
        /// </summary>
        public static IReadOnlyList<PlanetSeasonInfo> GetAllBodiesSeasonInfo()
        {
            _cachedInfoList.Clear();
            var registry = CosmicRegistry.Instance;
            if (registry != null && registry.IsReady && registry.Bodies != null)
            {
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    var b = registry.Bodies[i];
                    if (b != null)
                        _cachedInfoList.Add(ComputeSeasonInfo(b.DisplayName, b.settings, b));
                }
            }

            if (_cachedInfoList.Count == 0)
            {
                _cachedInfoList.Add(GetCurrentSeasonInfo());
            }

            return _cachedInfoList;
        }

        // ── Core Seasonal Simulation Math ────────────────────────────

        private static PlanetSeasonInfo ComputeSeasonInfo(string name, BodySettings settings, BodyInstance instance = null)
        {
            double simSeconds = 0d;
            var registry = CosmicRegistry.Instance;
            if (registry != null && registry.IsReady)
                simSeconds = registry.SimulationSeconds;
            else
                simSeconds = Time.timeAsDouble;

            double yearDuration = DefaultYearDurationSeconds;

            // Try resolving orbital period from Keplerian elements
            if (instance == null && registry != null && registry.IsReady && registry.Bodies != null)
            {
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    if (registry.Bodies[i] != null && registry.Bodies[i].settings == settings)
                    {
                        instance = registry.Bodies[i];
                        break;
                    }
                }
            }

            if (instance != null && instance.orbit.IsValid && instance.orbit.PeriodSeconds > 30.0)
            {
                // Keplerian period scaled to game year duration so seasons are observable in play
                yearDuration = Math.Max(300.0, instance.orbit.PeriodSeconds);
            }

            double totalYears = simSeconds / yearDuration;
            int currentYear = (int)totalYears + 1;
            double yearProgress = totalYears % 1.0;
            if (yearProgress < 0d) yearProgress += 1.0;

            float orbitPhase = (float)(yearProgress * 360.0);

            // 4 equal astronomical quadrants:
            // Spring (0..0.25), Summer (0.25..0.5), Autumn (0.5..0.75), Winter (0.75..1.0)
            int seasonIdx = Mathf.Clamp((int)(yearProgress * 4.0), 0, 3);
            Season season = (Season)seasonIdx;
            float seasonProgress = Mathf.Clamp01((float)((yearProgress * 4.0) - seasonIdx));

            int dayOfYear = Mathf.Clamp((int)(yearProgress * DaysPerYear) + 1, 1, DaysPerYear);
            int seasonDay = Mathf.Clamp((int)(seasonProgress * DaysPerSeason) + 1, 1, DaysPerSeason);
            int daysRemaining = Mathf.Max(0, DaysPerSeason - seasonDay);

            float baseTemp = settings != null ? settings.temperature : 15f;

            // Annual temperature sine wave:
            // • Spring (mid 0.125): mild warming (+0 offset)
            // • Summer (mid 0.375): peak warmth (+1.0 offset)
            // • Autumn (mid 0.625): crisp cooling (+0 offset)
            // • Winter (mid 0.875): deep freeze (-1.0 offset)
            float tempAngle = (float)((yearProgress - 0.125) * Math.PI * 2.0);
            float maxTempSwing = settings != null && !settings.HasAtmosphere ? 45f : Mathf.Clamp(Mathf.Abs(baseTemp) * 0.45f + 12f, 8f, 22f);
            float tempOffset = Mathf.Sin(tempAngle) * maxTempSwing;
            float effectiveTemp = baseTemp + tempOffset;

            // Solar irradiance multiplier (1.0 nominal):
            // Summer receives peak irradiance (+22%), Winter receives lowest irradiance (-22%).
            float solarMultiplier = Mathf.Clamp(1.0f + 0.22f * Mathf.Sin(tempAngle), 0.5f, 1.5f);

            // Seasonal wind multiplier (1.0 nominal):
            // Autumn and Spring experience equinoctial storm gales (+25% to +35% wind power).
            float windAngle = (float)(yearProgress * Math.PI * 2.0);
            float windMultiplier = 1.0f + 0.28f * Mathf.Abs(Mathf.Sin(windAngle));

            // Storm modifier:
            float stormModifier = season switch
            {
                Season.Autumn => 0.20f,
                Season.Winter => 0.15f,
                Season.Summer => 0.10f,
                _ => 0.0f
            };

            bool isFreezing = effectiveTemp <= 0.05f;

            // Forecast precipitation:
            // Explicit rain settings remain rain across all seasons.
            // Auto settings dynamically shift between rain and snow based on sub-zero temperatures.
            string forecast = "Clear";
            if (settings != null && !settings.WeatherAllowed)
            {
                forecast = "None";
            }
            else if (settings != null && settings.weather != null)
            {
                var p = settings.weather.precipitation;
                if (p == WeatherClimateProfile.Precipitation.None) forecast = "Overcast";
                else if (p == WeatherClimateProfile.Precipitation.Snow) forecast = season == Season.Winter ? "Blizzard" : "Snow";
                else if (p == WeatherClimateProfile.Precipitation.Rain) forecast = season == Season.Autumn ? "Heavy Rain" : "Rain";
                else // Auto
                {
                    if (isFreezing) forecast = season == Season.Winter ? "Blizzard" : "Snow";
                    else forecast = season == Season.Autumn ? "Heavy Rain" : "Rain";
                }
            }

            return new PlanetSeasonInfo
            {
                bodyName = string.IsNullOrEmpty(name) ? "Planet" : name,
                currentSeason = season,
                seasonProgress = seasonProgress,
                seasonDay = seasonDay,
                daysInSeason = DaysPerSeason,
                dayOfYear = dayOfYear,
                totalDaysInYear = DaysPerYear,
                daysRemainingInSeason = daysRemaining,
                currentYear = currentYear,
                orbitalPhaseDegrees = orbitPhase,
                baseTemperature = baseTemp,
                seasonalTemperatureOffset = tempOffset,
                effectiveTemperature = effectiveTemp,
                solarMultiplier = solarMultiplier,
                windMultiplier = windMultiplier,
                stormChanceModifier = stormModifier,
                isFreezing = isFreezing,
                forecastPrecipitation = forecast
            };
        }

        private static PlanetSeasonInfo GetDefaultInfo(string name) => new PlanetSeasonInfo
        {
            bodyName = name,
            currentSeason = Season.Spring,
            seasonProgress = 0.5f,
            seasonDay = 45,
            daysInSeason = DaysPerSeason,
            dayOfYear = 45,
            totalDaysInYear = DaysPerYear,
            daysRemainingInSeason = 45,
            currentYear = 1,
            orbitalPhaseDegrees = 45f,
            baseTemperature = 15f,
            seasonalTemperatureOffset = 0f,
            effectiveTemperature = 15f,
            solarMultiplier = 1.0f,
            windMultiplier = 1.0f,
            stormChanceModifier = 0f,
            isFreezing = false,
            forecastPrecipitation = "Rain"
        };
    }
}
