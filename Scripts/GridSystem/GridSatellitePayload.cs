// Assets/Scripts/VoxelEngine/GridSystem/GridSatellitePayload.cs
//
// Sensor and weather-control payloads for orbiting satellites.
//
// This is what makes a satellite worth the launch. Three tiers, each a separate
// block, sharing one component:
//
//   SensorArray    - reads the season cycle of the body it orbits, planet-wide,
//                    without the player standing on the ground.
//   WeatherRadar   - adds live weather readout and a real forecast.
//   ClimateControl - can INFLUENCE weather: suppress storms or encourage clear
//                    skies over time, at heavy power cost.
//
// Deliberately influence, not command. A button that sets the weather would
// delete the weather system; a payload that shifts the odds and slowly clears a
// storm front leaves the simulation intact while still feeling powerful.
//
// Every tier requires the host construct to be a SATELLITE committed to orbit,
// which is the same rule the orbital research lab uses.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Weather;

namespace VoxelEngine.GridSystem
{
    public enum SatellitePayloadKind
    {
        /// <summary>Season tracking for the orbited body.</summary>
        SensorArray = 0,
        /// <summary>Season tracking plus live weather and forecast.</summary>
        WeatherRadar = 1,
        /// <summary>All of the above, plus weather influence.</summary>
        ClimateControl = 2,
        /// <summary>
        /// Surveys deep ore deposits across the orbited body. Appended, never inserted:
        /// authored prefabs store this as an int.
        /// </summary>
        ResourceScanner = 3,
    }

    /// <summary>What the player has asked a climate-control payload to do.</summary>
    public enum ClimateDirective
    {
        /// <summary>Observe only. The payload draws idle power and nothing else.</summary>
        Monitor = 0,
        /// <summary>Push the weather toward clear skies.</summary>
        Suppress = 1,
        /// <summary>Push the weather toward precipitation and storms.</summary>
        Encourage = 2,
    }

    public class GridSatellitePayload : GridBlock
    {
        [Header("Payload")]
        public SatellitePayloadKind kind = SatellitePayloadKind.SensorArray;

        [Tooltip("Power drawn while the payload is online and observing.")]
        public float idleWatts = 120f;

        [Tooltip("Extra power drawn while actively influencing the weather. " +
                 "Steering a planet's atmosphere should hurt.")]
        public float influenceWatts = 2400f;

        [Header("Influence")]
        [Tooltip("How strongly one satellite shifts the weather odds per cycle, 0..1. " +
                 "Several satellites stack, with diminishing returns.")]
        [Range(0.05f, 0.9f)] public float influenceStrength = 0.35f;

        /// <summary>Player-set directive. Only meaningful for ClimateControl payloads.</summary>
        public ClimateDirective Directive { get; private set; } = ClimateDirective.Monitor;

        public override float PowerDraw
        {
            get
            {
                if (!Enabled) return 0f;
                bool influencing = kind == SatellitePayloadKind.ClimateControl
                    && Directive != ClimateDirective.Monitor;
                return influencing ? idleWatts + influenceWatts : idleWatts;
            }
        }

        // A resource scanner is a survey instrument, not a meteorological one. It reports
        // seasons like every payload does, but it has no weather hardware at all - keeping
        // the tiers distinct stops the top payload from simply being "all of the above".
        public bool CanTrackSeasons => true;
        public bool CanTrackWeather => kind == SatellitePayloadKind.WeatherRadar
                                    || kind == SatellitePayloadKind.ClimateControl;
        public bool CanInfluenceWeather => kind == SatellitePayloadKind.ClimateControl;
        public bool CanSurveyResources => kind == SatellitePayloadKind.ResourceScanner;

        public string KindLabel => kind switch
        {
            SatellitePayloadKind.WeatherRadar => "WEATHER RADAR",
            SatellitePayloadKind.ClimateControl => "CLIMATE CONTROL",
            SatellitePayloadKind.ResourceScanner => "RESOURCE SCANNER",
            _ => "SENSOR ARRAY",
        };

        [Header("Resource Survey")]
        [Tooltip("Ground radius the scanner sweeps, in metres. Far wider than a hand " +
                 "scanner: the whole point of orbit is seeing more at once.")]
        public float surveyRadius = 6000f;

        [Tooltip("Most deposits reported at a time. A list nobody can read is not more " +
                 "useful than a short ranked one.")]
        public int surveyLimit = 16;

        // Per-instance, deliberately NOT a shared static. A static scratch list returned to
        // callers would be silently overwritten the moment a second scanner surveyed, so a
        // caller iterating one scanner's results would start reading another's.
        private readonly List<VoxelEngine.Generation.DeepOreNode> _surveyResults = new(32);

        /// <summary>
        /// Deposits this scanner can currently see, nearest first. Returns an empty list
        /// when the payload is not a scanner or is not operational, so a caller never has
        /// to check the kind before asking.
        /// </summary>
        public IReadOnlyList<VoxelEngine.Generation.DeepOreNode> SurveyDeposits()
        {
            _surveyResults.Clear();
            if (!CanSurveyResources || !IsOperational) return _surveyResults;

            // Surveyed around the SATELLITE's ground track, not the player: the instrument
            // is in orbit, and reporting what is under the player would make the satellite
            // a pointless middleman for a tool they could carry.
            VoxelEngine.Generation.DeepOreField.SurveyArea(
                transform.position, surveyRadius, _surveyResults, Mathf.Max(1, surveyLimit));

            return _surveyResults;
        }

        public void SetDirective(ClimateDirective directive)
        {
            if (!CanInfluenceWeather) directive = ClimateDirective.Monitor;
            Directive = directive;
        }

        // ── Registry ─────────────────────────────────────────────────────────────
        private static readonly List<GridSatellitePayload> s_all = new();
        public static IReadOnlyList<GridSatellitePayload> All => s_all;

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
        private void OnDisable() { s_all.Remove(this); }

        // ── Operational gate ─────────────────────────────────────────────────────

        /// <summary>
        /// Why this payload is not working, or null if it is. Mirrors the satellite lab's
        /// rule so the player only has to learn one requirement for all orbital hardware.
        /// </summary>
        public string BlockedReason()
        {
            if (!Enabled) return "Payload is switched off.";
            if (Grid == null) return "Payload is not attached to a construct.";
            if (!Grid.HasPower) return "Payload has no power.";

            var identity = GridIdentity.Find(Grid);
            if (identity == null || !identity.IsSatellite)
                return "Host construct must be classified as a SATELLITE.";

            var rails = Grid.GetComponent<OrbitalRails>();
            if (rails == null || !rails.IsOnRails)
                return "Host satellite must be committed to a stable orbit.";

            return null;
        }

        public bool IsOperational => BlockedReason() == null;

        /// <summary>The body this payload is observing, or null when it is not operational.</summary>
        public BodyInstance ObservedBody
        {
            get
            {
                if (!IsOperational) return null;
                var rails = Grid.GetComponent<OrbitalRails>();
                return rails != null ? rails.Parent : null;
            }
        }

        // ── Season and weather readouts ──────────────────────────────────────────

        /// <summary>
        /// Season data for the observed body. This is the payload's core value: season
        /// information for a planet the player is not standing on.
        /// </summary>
        public bool TryGetSeason(out PlanetSeasonInfo info)
        {
            info = default;
            var body = ObservedBody;
            if (body == null) return false;
            info = PlanetarySeasons.GetSeasonInfo(body.DisplayName);
            return !string.IsNullOrEmpty(info.bodyName);
        }

        /// <summary>
        /// Live weather for the observed body. Only the radar tier and above, and only for
        /// the body the player is currently at — the weather sim runs for the active body.
        /// </summary>
        public bool TryGetWeather(out WeatherState state, out float intensity, out string forecast)
        {
            state = WeatherState.Clear;
            intensity = 0f;
            forecast = "";

            if (!CanTrackWeather) return false;

            var body = ObservedBody;
            var manager = WeatherManager.Instance;
            if (body == null || manager == null) return false;

            // The weather simulation only exists for the body the player is at. Reporting a
            // remote planet's live sky would be a fabrication, so the payload says so instead.
            var active = GravityProvider.ActiveBody;
            if (active == null || active.DisplayName != body.DisplayName) return false;

            state = manager.CurrentState;
            intensity = manager.Intensity;

            if (TryGetSeason(out var season)) forecast = season.forecastPrecipitation;
            return true;
        }

        // ── Weather influence ────────────────────────────────────────────────────

        /// <summary>
        /// Combined influence over the body the player is currently on, in the range -1..1.
        /// Negative suppresses precipitation, positive encourages it. Zero means no working
        /// climate payload is pointed at this world.
        /// </summary>
        public static float ResolveInfluence()
        {
            var active = GravityProvider.ActiveBody;
            if (active == null) return 0f;

            float suppress = 0f, encourage = 0f;

            for (int i = 0; i < s_all.Count; i++)
            {
                var payload = s_all[i];
                if (payload == null || !payload.CanInfluenceWeather) continue;
                if (payload.Directive == ClimateDirective.Monitor) continue;
                if (!payload.IsOperational) continue;

                var body = payload.ObservedBody;
                if (body == null || body.DisplayName != active.DisplayName) continue;

                float strength = Mathf.Clamp01(payload.influenceStrength);
                if (payload.Directive == ClimateDirective.Suppress) suppress += strength;
                else encourage += strength;
            }

            // Diminishing returns: each extra satellite adds less than the last, and the
            // total can never quite reach 1. A constellation is better than one satellite
            // but can never hard-lock a planet's sky, which keeps weather a real system.
            float net = Saturate(encourage) - Saturate(suppress);
            return Mathf.Clamp(net, -1f, 1f);
        }

        /// <summary>Maps a raw additive total to 0..1 with diminishing returns.</summary>
        private static float Saturate(float total)
        {
            if (total <= 0f) return 0f;
            return 1f - Mathf.Exp(-total);
        }

        /// <summary>True when any working climate payload is steering the active world.</summary>
        public static bool AnyInfluencing() => Mathf.Abs(ResolveInfluence()) > 0.001f;

        /// <summary>Short description of the current influence, for HUDs.</summary>
        public static string InfluenceLabel()
        {
            float influence = ResolveInfluence();
            if (Mathf.Abs(influence) < 0.001f) return "";
            return influence < 0f
                ? $"CLIMATE CONTROL: SUPPRESSING ({-influence * 100f:0}%)"
                : $"CLIMATE CONTROL: ENCOURAGING ({influence * 100f:0}%)";
        }
    }
}
