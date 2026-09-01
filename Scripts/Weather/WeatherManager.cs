// Assets/Scripts/VoxelEngine/Weather/WeatherManager.cs
//
// Central weather controller. Drives rain/snow particles, ambient audio, surface-hit
// sounds, fog, sun darkening, lightning, and planetary seasons through its sub-systems.
//
// Weather is per-planet: each celestial body's BodySettings.weather (a WeatherClimateProfile)
// decides whether weather is active at all (airless bodies are always calm), what falls from
// the sky, how stormy it is, and how hard storms hit. ApplyBody() is called by the cosmic
// bootstrap whenever a body becomes the player's home world.
//
// Weather cycles through Clear → Overcast → Rain → HeavyRain → Clear (or the snow/blizzard
// equivalents in cold biomes / during winter seasons / on frozen worlds). Thunder is unified here:
// a single scheduler fires OnThunder, which the audio and lightning sub-systems both honour so
// the flash and the rumble are always in sync.

using UnityEngine;
using UnityEngine.InputSystem;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Weather
{
    public enum WeatherState { Clear, Overcast, LightRain, HeavyRain, Snow, Blizzard }

    public class WeatherManager : MonoBehaviour
    {
        public static WeatherManager Instance { get; private set; }

        [Header("Timing")]
        [Tooltip("Minimum seconds a weather state lasts (after the first cycle).")]
        public float minStateDuration = 45f;
        [Tooltip("Maximum seconds a weather state lasts (after the first cycle).")]
        public float maxStateDuration = 150f;
        [Tooltip("Seconds to blend between weather states.")]
        public float transitionDuration = 12f;

        [Header("First Cycle")]
        [Tooltip("On a weather-allowed body the FIRST weather change happens within this window, " +
                 "so you actually see weather soon after arriving instead of waiting minutes for a roll.")]
        public float firstChangeDelayMin = 10f;
        [Tooltip("Max seconds before the first weather change on arrival.")]
        public float firstChangeDelayMax = 22f;

        [Header("References")]
        [Tooltip("The player's camera transform (particles follow this). Auto-found if null.")]
        public Transform playerCamera;

        // Current state
        public WeatherState CurrentState { get; private set; } = WeatherState.Clear;
        public WeatherState TargetState  { get; private set; } = WeatherState.Clear;

        /// <summary>0 = fully previous state, 1 = fully target state.</summary>
        public float TransitionProgress { get; private set; } = 1f;

        /// <summary>Current ambient surface temperature of the world in °C.</summary>
        public float CurrentTemperature => PlanetarySeasons.GetCurrentTemperature();

        /// <summary>Current planetary seasons telemetry snapshot.</summary>
        public PlanetSeasonInfo CurrentSeasonInfo => PlanetarySeasons.GetCurrentSeasonInfo();

        /// <summary>
        /// Planet-wide precipitation intensity (0 = none, 1 = max). This is the WEATHER of the
        /// world and stays valid no matter where the player is — the cloud shells use it so a
        /// storm still rages on the planet while you watch it from orbit.
        /// </summary>
        public float Intensity { get; private set; }

        /// <summary>
        /// How much of the weather reaches the PLAYER: 1 under the cloud deck, easing to 0 as
        /// you climb through it and 0 above it. Rain, splashes, weather audio and storm fog all
        /// ride this, so flying to space leaves the weather behind on the planet where it belongs.
        /// </summary>
        public float SurfaceProximity { get; private set; } = 1f;

        /// <summary>Precipitation actually falling on the player = Intensity × SurfaceProximity.</summary>
        public float LocalIntensity { get; private set; }

        /// <summary>
        /// Live multiplier weather applies to wind (1 = calm). Storms and seasonal gales drive it up.
        /// Consumed by <see cref="VoxelEngine.Cosmos.WindField"/> (grass / particles / audio)
        /// and by <see cref="VoxelEngine.Power.Wind.WindSystem"/> (wind turbines).
        /// </summary>
        public static float WindMultiplier { get; private set; } = 1f;

        /// <summary>True if the current biome / season is cold/snowy (or the body forces snow).</summary>
        public bool IsSnowBiome { get; private set; }

        /// <summary>True if precipitation is currently reaching the player (rain or snow).</summary>
        public bool IsPrecipitating => IsWeatherActive && LocalIntensity > 0.05f;

        /// <summary>True if the planet is precipitating, whether or not the player is under it.</summary>
        public bool IsPlanetPrecipitating => IsWeatherActive && Intensity > 0.05f;

        /// <summary>
        /// True only while standing on a body that is allowed to have weather (designer toggle on
        /// AND it has an atmosphere). On airless moons / in deep space this is false and weather
        /// collapses to Clear so nothing renders or modulates the scene.
        /// </summary>
        public bool IsWeatherActive { get; private set; }

        /// <summary>
        /// True while a state was forced by hand (debug hotkey / console). The random state
        /// timer is paused so the forced sky is not rolled away a few seconds later.
        /// </summary>
        public bool IsManuallyHeld => _manualHold;

        /// <summary>The active body's climate profile (null → safe Earth-like defaults).</summary>
        public WeatherClimateProfile Profile { get; private set; }

        /// <summary>
        /// Fired on every thunder strike during a storm. The audio sub-system plays the rumble and
        /// the lightning sub-system flashes the sky, both from this single source so they stay synced.
        /// </summary>
        public event System.Action<Vector3> OnThunder;

        private float _stateTimer;
        private float _nextStateChange;
        private float _biomeCheckTimer;
        private float _thunderTimer;
        private float _nextThunder = 25f;
        private float _heartbeatTimer;
        private bool _manualHold;
        private BodySettings _lastAppliedSettings;
        private bool _pendingFirstCycle;

        // Sub-systems (created as children)
        private WeatherParticles _particles;
        private WeatherAudio _audio;
        private WeatherLighting _lighting;
        private WeatherClouds _clouds;
        private WeatherSeaState _seaState;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (playerCamera == null)
            {
                var cam = Camera.main;
                if (cam != null) playerCamera = cam.transform;
            }

            // Create sub-systems (idempotent: reused if the setup step pre-added them for
            // inspector tuning, so we never stack duplicate particle/audio/lighting comps).
            _particles = GetComponent<WeatherParticles>();
            if (_particles == null) _particles = gameObject.AddComponent<WeatherParticles>();
            _audio = GetComponent<WeatherAudio>();
            if (_audio == null) _audio = gameObject.AddComponent<WeatherAudio>();
            _lighting = GetComponent<WeatherLighting>();
            if (_lighting == null) _lighting = gameObject.AddComponent<WeatherLighting>();
            _clouds = GetComponent<WeatherClouds>();
            if (_clouds == null) _clouds = gameObject.AddComponent<WeatherClouds>();
            _seaState = GetComponent<WeatherSeaState>();
            if (_seaState == null) _seaState = gameObject.AddComponent<WeatherSeaState>();

            _nextStateChange = Random.Range(minStateDuration, maxStateDuration);
        }

        /// <summary>
        /// Apply the active body's climate personality. Called by the cosmic bootstrap when a body
        /// becomes the player's home world (and on every planet transition). Mirrors WindField.ApplyBody.
        /// </summary>
        public void ApplyBody(BodySettings body)
        {
            _lastAppliedSettings = body;
            _manualHold = false;   // a forced test sky never survives a planet change

            if (body == null)
            {
                Profile = null;
                IsWeatherActive = false;
                _pendingFirstCycle = false;
                ForceWeather(WeatherState.Clear);
                Debug.Log("[Weather] ApplyBody: no active body — weather off.");
                return;
            }

            Profile = body.weather ?? WeatherClimateProfile.Default();
            IsWeatherActive = body.WeatherAllowed;

            // A body that forces a precipitation type wins over biome sampling.
            if (Profile.precipitation == WeatherClimateProfile.Precipitation.Snow) IsSnowBiome = true;
            else if (Profile.precipitation == WeatherClimateProfile.Precipitation.Rain) IsSnowBiome = false;

            if (!IsWeatherActive)
            {
                // Collapsing to Clear on a calm body keeps deep space / airless moons pristine.
                _pendingFirstCycle = false;
                ForceWeather(WeatherState.Clear);
            }
            else
            {
                // Kick off a SHORT first cycle so weather is actually visible soon after arrival.
                _pendingFirstCycle = true;
                _stateTimer = 0f;
                _nextStateChange = Random.Range(firstChangeDelayMin, firstChangeDelayMax);
            }

            Debug.Log($"[Weather] ApplyBody '{body.bodyName}': active={IsWeatherActive} " +
                      $"atmosphere={body.HasAtmosphere} weatherEnabled={Profile.weatherEnabled} " +
                      $"precip={Profile.precipitation} snowBiome={IsSnowBiome}" +
                      (IsWeatherActive ? $" firstChangeIn~{_nextStateChange:F0}s" : ""));
        }

        private void Update()
        {
            // Re-resolve the active body each frame so leaving a body (or loading one without an
            // explicit ApplyBody call) still gates weather correctly.
            ResolveActiveBody();

            // Follow camera (re-resolve lazily in case it was not available at Start).
            if (playerCamera == null)
            {
                var cam = Camera.main;
                if (cam != null) playerCamera = cam.transform;
            }
            if (playerCamera != null)
                transform.position = playerCamera.position;

            // Orient the weather frame to the body's RADIAL up. On spherical worlds "down" is
            // toward the planet core, not world -Y — without this, the rain slab is only truly
            // "above" you near one spot on the sphere and rain falls sideways everywhere else.
            var activeBody = GravityProvider.ActiveBody;
            if (activeBody != null)
            {
                Vector3 up = activeBody.UpAt(transform.position);
                Vector3 fwd = playerCamera != null ? playerCamera.forward : transform.forward;
                if (Mathf.Abs(Vector3.Dot(fwd.normalized, up.normalized)) > 0.985f)
                    fwd = playerCamera != null ? playerCamera.up : Vector3.Cross(up, Vector3.right);
                Vector3.OrthoNormalize(ref up, ref fwd);
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.Cross(up, Vector3.right).normalized;
                transform.rotation = Quaternion.LookRotation(fwd, up);
            }

            HandleDebugHotkeys();

            // Check biome and seasonal climate every 2 seconds.
            _biomeCheckTimer += Time.deltaTime;
            if (_biomeCheckTimer >= 2f)
            {
                _biomeCheckTimer = 0f;
                CheckBiome();
            }

            // Transition between states.
            if (TransitionProgress < 1f)
            {
                TransitionProgress += Time.deltaTime / Mathf.Max(0.1f, transitionDuration);
                if (TransitionProgress >= 1f)
                {
                    TransitionProgress = 1f;
                    CurrentState = TargetState;
                }
            }

            // Diagnostic heartbeat (throttled).
            _heartbeatTimer += Time.deltaTime;
            if (_heartbeatTimer >= 10f)
            {
                _heartbeatTimer = 0f;
                var body = GravityProvider.ActiveBody;
                var settings = body != null ? body.settings : null;
                var season = PlanetarySeasons.GetCurrentSeasonInfo();
                Debug.Log($"[Weather] Heartbeat: active={IsWeatherActive} " +
                          $"state={CurrentState}->{TargetState} intensity={Intensity:F2} " +
                          $"snow={IsSnowBiome} season={season.SeasonName} ({season.effectiveTemperature:F1}°C) " +
                          $"body={(body != null ? body.DisplayName : "none")}");
            }

            // Update intensity based on current/target blend.
            float fromIntensity = GetIntensity(CurrentState);
            float toIntensity = GetIntensity(TargetState);
            Intensity = IsWeatherActive ? Mathf.Lerp(fromIntensity, toIntensity, TransitionProgress) : 0f;

            UpdateSurfaceProximity();
            LocalIntensity = Intensity * SurfaceProximity;

            UpdateWindMultiplier();

            // State timer — pick next weather (only when weather is actually active and the
            // sky is not being held by hand for testing).
            if (IsWeatherActive && !_manualHold)
            {
                _stateTimer += Time.deltaTime;
                if (_stateTimer >= _nextStateChange)
                {
                    _stateTimer = 0f;
                    _nextStateChange = Random.Range(minStateDuration, maxStateDuration);
                    PickNextState();
                }

                ScheduleThunder();
            }
            else if (!IsWeatherActive && TargetState != WeatherState.Clear)
            {
                ForceWeather(WeatherState.Clear);
            }
            else if (IsWeatherActive && _manualHold)
            {
                ScheduleThunder();   // a held storm still thunders
            }
        }

        /// <summary>
        /// Developer hotkeys (Ctrl+Alt): W steps through every weather state and then back to
        /// the automatic cycle, R jumps straight to a full storm.
        /// </summary>
        private void HandleDebugHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool chord = (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)
                      && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
            if (!chord) return;

            if (kb.rKey.wasPressedThisFrame)
            {
                _manualHold = true;
                ForceWeather(IsSnowBiome ? WeatherState.Blizzard : WeatherState.HeavyRain);
                Debug.Log($"[Weather] DEBUG: forced {TargetState} (Ctrl+Alt+R). " +
                          $"active={IsWeatherActive} intensity={Intensity:F2}");
                return;
            }

            if (!kb.wKey.wasPressedThisFrame) return;

            // Clear → Overcast → LightRain → HeavyRain → Snow → Blizzard → automatic.
            if (!_manualHold)
            {
                _manualHold = true;
                ForceWeather(WeatherState.Clear);
            }
            else
            {
                switch (TargetState)
                {
                    case WeatherState.Clear:     ForceWeather(WeatherState.Overcast); break;
                    case WeatherState.Overcast:  ForceWeather(WeatherState.LightRain); break;
                    case WeatherState.LightRain: ForceWeather(WeatherState.HeavyRain); break;
                    case WeatherState.HeavyRain: ForceWeather(WeatherState.Snow); break;
                    case WeatherState.Snow:      ForceWeather(WeatherState.Blizzard); break;
                    default:
                        _manualHold = false;
                        _stateTimer = 0f;
                        _nextStateChange = Random.Range(firstChangeDelayMin, firstChangeDelayMax);
                        Debug.Log("[Weather] DEBUG: released to the automatic cycle (Ctrl+Alt+W).");
                        return;
                }
            }

            Debug.Log($"[Weather] DEBUG: held {TargetState} (Ctrl+Alt+W). " +
                      $"active={IsWeatherActive} intensity={Intensity:F2} snow={IsSnowBiome}");
        }

        /// <summary>
        /// Fades the weather out as the player climbs through the cloud deck. Above the deck
        /// there is nothing left to fall on you.
        /// </summary>
        private void UpdateSurfaceProximity()
        {
            var body = GravityProvider.ActiveBody;
            if (!IsWeatherActive || body == null || playerCamera == null)
            {
                SurfaceProximity = Mathf.MoveTowards(SurfaceProximity, IsWeatherActive ? 1f : 0f,
                                                     Time.deltaTime * 2f);
                return;
            }

            float surface = Mathf.Max(50f, body.SurfaceRadius);
            float altitude = Vector3.Distance(playerCamera.position, body.transform.position) - surface;
            float deck = PlanetCloudLayer.CloudAltitudeFor(surface);

            // Full weather up to the cloud base, gone once you are clearly above the deck.
            float target = 1f - Mathf.Clamp01((altitude - deck * 0.85f) / Mathf.Max(1f, deck * 0.45f));
            SurfaceProximity = Mathf.MoveTowards(SurfaceProximity, Mathf.Clamp01(target),
                                                 Time.deltaTime * 1.5f);
        }

        private void ResolveActiveBody()
        {
            var body = GravityProvider.ActiveBody;
            var settings = body != null ? body.settings : null;
            if (ReferenceEquals(settings, _lastAppliedSettings)) return;
            _lastAppliedSettings = settings;
            ApplyBody(settings);
        }

        private void CheckBiome()
        {
            var profile = Profile ?? WeatherClimateProfile.Default();

            // A forced precipitation type always wins.
            if (profile.precipitation == WeatherClimateProfile.Precipitation.Snow) { IsSnowBiome = true; return; }
            if (profile.precipitation == WeatherClimateProfile.Precipitation.Rain) { IsSnowBiome = false; return; }
            if (profile.precipitation == WeatherClimateProfile.Precipitation.None) { IsSnowBiome = false; return; }

            // Check planetary seasonal temperature: freezing temperature enforces snow
            var seasonInfo = PlanetarySeasons.GetCurrentSeasonInfo();
            if (seasonInfo.isFreezing)
            {
                IsSnowBiome = true;
                return;
            }

            // Auto: sample temperature from biome noise, modulated by seasonal temperature offset.
            var world = ActiveWorld.Current;
            if (world == null || world.Viewer == null)
            {
                IsSnowBiome = seasonInfo.effectiveTemperature < 2f;
                return;
            }

            var pos = world.Viewer.position;
            int wx = Mathf.FloorToInt(pos.x);
            int wz = Mathf.FloorToInt(pos.z);
            var climate = BiomePicker.SampleClimate(world.Seed, wx, wz);
            float seasonalTempShift = seasonInfo.seasonalTemperatureOffset / 50f;
            float effectiveTempFraction = Mathf.Clamp01(climate.x + seasonalTempShift);
            IsSnowBiome = effectiveTempFraction < 0.28f; // cold biomes / cold season
        }

        private void PickNextState()
        {
            CurrentState = TargetState;
            TransitionProgress = 0f;

            var profile = Profile ?? WeatherClimateProfile.Default();
            var seasonInfo = PlanetarySeasons.GetCurrentSeasonInfo();
            float overcast = profile.overcastBias;
            float storm = Mathf.Clamp01(profile.stormChance + seasonInfo.stormChanceModifier);
            float roll = Random.value;

            bool noPrecip = profile.precipitation == WeatherClimateProfile.Precipitation.None;
            bool snow = IsSnowBiome || profile.precipitation == WeatherClimateProfile.Precipitation.Snow || seasonInfo.isFreezing;

            // First cycle on arrival: guarantee a VISIBLE weather move so a freshly entered
            // world does not read as "clear forever".
            if (_pendingFirstCycle)
            {
                _pendingFirstCycle = false;
                if (noPrecip)
                    TargetState = WeatherState.Overcast;
                else if (snow)
                    TargetState = roll < 0.6f ? WeatherState.Snow : WeatherState.Overcast;
                else
                    TargetState = roll < 0.65f ? WeatherState.LightRain : WeatherState.Overcast;
                LogStateChange("first cycle");
                return;
            }

            // Desert / ash worlds: wind & overcast only — NEVER rain or snow.
            if (noPrecip)
            {
                TargetState = roll < Mathf.Clamp01(overcast + 0.3f) ? WeatherState.Overcast : WeatherState.Clear;
                LogStateChange();
                return;
            }

            if (snow)
            {
                // Snow / winter: clear/overcast -> snow -> blizzard, scaled by storm chance.
                if (CurrentState == WeatherState.Clear || CurrentState == WeatherState.Overcast)
                    TargetState = roll < 0.55f ? WeatherState.Snow
                                : (roll < Mathf.Clamp01(overcast + 0.25f) ? WeatherState.Overcast : WeatherState.Clear);
                else if (CurrentState == WeatherState.Snow)
                    TargetState = roll < storm ? WeatherState.Blizzard
                                : (roll < 0.70f ? WeatherState.Snow : WeatherState.Clear);
                else // Blizzard
                    TargetState = roll < 0.5f ? WeatherState.Blizzard : WeatherState.Snow;
            }
            else
            {
                // Temperate biomes: clear -> overcast -> rain -> heavy rain.
                if (CurrentState == WeatherState.Clear)
                    TargetState = roll < 0.40f ? WeatherState.Overcast
                                : (roll < 0.70f ? WeatherState.LightRain : WeatherState.Clear);
                else if (CurrentState == WeatherState.Overcast)
                    TargetState = roll < 0.55f ? WeatherState.LightRain
                                : (roll < 0.80f ? WeatherState.Overcast : WeatherState.Clear);
                else if (CurrentState == WeatherState.LightRain)
                    TargetState = roll < storm ? WeatherState.HeavyRain
                                : (roll < 0.75f ? WeatherState.LightRain : WeatherState.Overcast);
                else // HeavyRain
                    TargetState = roll < 0.45f ? WeatherState.HeavyRain
                                : (roll < 0.85f ? WeatherState.LightRain : WeatherState.Overcast);
            }

            LogStateChange();
        }

        private void LogStateChange(string tag = "")
        {
            Debug.Log($"[Weather] {CurrentState} -> {TargetState} (intensity~{GetIntensity(TargetState):F2})"
                      + (string.IsNullOrEmpty(tag) ? "" : $" [{tag}]"));
        }

        /// <summary>Schedule synced thunder strikes during heavy precipitation (rain or blizzard).</summary>
        private void ScheduleThunder()
        {
            bool heavyPrecip = TargetState == WeatherState.HeavyRain || TargetState == WeatherState.Blizzard
                            || CurrentState == WeatherState.HeavyRain || CurrentState == WeatherState.Blizzard;
            if (!heavyPrecip || Intensity < 0.6f) return;

            var profile = Profile ?? WeatherClimateProfile.Default();
            if (profile.thunderFrequency <= 0.001f) return;

            _thunderTimer += Time.deltaTime;
            if (_thunderTimer < _nextThunder) return;

            _thunderTimer = 0f;
            float baseGap = Mathf.Lerp(40f, 10f, profile.thunderFrequency);
            _nextThunder = Random.Range(baseGap * 0.6f, baseGap * 1.6f);
            OnThunder?.Invoke(PickStrikePosition());
        }

        private Vector3 PickStrikePosition()
        {
            Vector3 anchor = transform.position;
            Vector3 up = Vector3.up;
            var body = GravityProvider.ActiveBody;
            if (body != null) up = body.UpAt(anchor);

            Vector3 tangent = Random.insideUnitSphere;
            tangent -= up * Vector3.Dot(tangent, up);
            if (tangent.sqrMagnitude < 1e-4f) tangent = Vector3.ProjectOnPlane(Random.insideUnitSphere, up);
            tangent.Normalize();

            float dist = Random.Range(600f, 3000f);
            float elev = Random.Range(18f, 55f) * Mathf.Deg2Rad;
            Vector3 dir = (tangent * Mathf.Cos(elev) + up * Mathf.Sin(elev)).normalized;
            return anchor + dir * dist;
        }

        private float GetIntensity(WeatherState state) => state switch
        {
            WeatherState.Clear      => 0f,
            WeatherState.Overcast   => 0f,
            WeatherState.LightRain  => 0.5f,
            WeatherState.HeavyRain  => 1.0f,
            WeatherState.Snow       => 0.5f,
            WeatherState.Blizzard   => 1.0f,
            _ => 0f
        };

        /// <summary>
        /// Weather + Seasons → wind coupling. Publishes a smoothed wind multiplier.
        /// </summary>
        private void UpdateWindMultiplier()
        {
            var seasonInfo = PlanetarySeasons.GetCurrentSeasonInfo();
            float seasonalBase = seasonInfo.windMultiplier;

            if (!IsWeatherActive || Intensity <= 0.001f)
            {
                WindMultiplier = Mathf.Lerp(WindMultiplier, seasonalBase, Time.deltaTime * 0.5f);
                return;
            }

            var profile = Profile ?? WeatherClimateProfile.Default();
            float stormBoost = Mathf.Max(0f, profile.stormWindMultiplier - 1f);

            float target = TargetState switch
            {
                WeatherState.HeavyRain or WeatherState.Blizzard => 1f + stormBoost,
                WeatherState.LightRain or WeatherState.Snow => 1f + stormBoost * 0.55f,
                WeatherState.Overcast => 1f + stormBoost * 0.25f,
                _ => 1f
            };

            target *= seasonalBase;
            WindMultiplier = Mathf.Lerp(WindMultiplier, Mathf.Lerp(seasonalBase, target, Intensity), Time.deltaTime * 0.5f);
        }

        /// <summary>Force a specific weather state (for testing / console commands).</summary>
        public void ForceWeather(WeatherState state)
        {
            CurrentState = state;
            TargetState = state;
            TransitionProgress = 1f;
            Intensity = IsWeatherActive ? GetIntensity(state) : 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            WindMultiplier = 1f;
        }
    }
}
