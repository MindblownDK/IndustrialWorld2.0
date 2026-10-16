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
//
// 17.31.0-dev — PER-REGION WEATHER. The state machine above is the weather SYSTEM of the body:
// whether the world is in a clear spell, a rain phase or a storm. WHERE that system is depositing
// is a place, decided by WeatherFronts' drifting field, and that is what a player experiences:
// the bands pass over some ground and not other ground, so a base under a band gets the rain
// while the mine two kilometres upwind stays dry. Intensity below is the PLAYER'S sky (system
// strength x the coverage over them); PlanetIntensity is the system's own strength, which is what
// the cloud shell, an orbiting sensor and the smog washout want. Every consumer that asks about
// some other place (a solar panel asks about the sky above itself) uses PrecipitationAt /
// CoverageAt, so no system has to pretend the world has one weather.

using UnityEngine;
using UnityEngine.InputSystem;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Environment;

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

        /// <summary>14.46.0 - weather is host-authoritative. On a client the
        /// local RNG never rolls: states arrive from the host and only the
        /// blend/intensity/proximity math runs here. Offline and hosting
        /// machines roll exactly as before.</summary>
        public bool RemoteDriven =>
            VoxelEngine.Networking.NetworkSession.Mode == VoxelEngine.Networking.SessionMode.Client;

        /// <summary>Client-side: adopt the host's sky. Values outside the
        /// enum are ignored (a newer host than this build knows states we
        /// cannot render). A fresh target restarts the blend; a repeat
        /// keepalive changes nothing.
        ///
        /// 17.31.0-dev: the host also publishes its front clock, because the regional field is a
        /// function of that clock. A first packet that is far out of step (more than 30 seconds,
        /// i.e. a guest joining a host that has been running for a while) snaps onto it - once,
        /// at the moment of joining, which is already a load hitch - and from then on the client
        /// keeps counting between packets and eases onto each published value, so the bands drift
        /// smoothly instead of stepping once every keepalive. A clock of 0 or less means an older
        /// host and is ignored: the sky still arrives, the bands are then the client's own.</summary>
        public void ApplyRemote(byte current, byte target, float clock = 0f)
        {
            if (!RemoteDriven) return;
            if (clock > 0f)
            {
                // A host that has been running for a while is far out of step, and easing that gap
                // would leave a guest looking at the wrong bands for half a minute: snap once, then
                // correct the remaining drift half a gap per packet.
                _weatherClock = Mathf.Abs(clock - _weatherClock) > 30f
                    ? clock
                    : Mathf.Lerp(_weatherClock, clock, 0.5f);
            }
            if (!System.Enum.IsDefined(typeof(WeatherState), (int)current) ||
                !System.Enum.IsDefined(typeof(WeatherState), (int)target)) return;
            var newCurrent = (WeatherState)current;
            var newTarget = (WeatherState)target;
            if (newCurrent == CurrentState && newTarget == TargetState) return;
            CurrentState = newCurrent;
            TargetState = newTarget;
            TransitionProgress = newCurrent == newTarget ? 1f : 0f;
            Debug.Log($"[Weather] Host sky adopted: {CurrentState} -> {TargetState}.");
        }

        /// <summary>0 = fully previous state, 1 = fully target state.</summary>
        public float TransitionProgress { get; private set; } = 1f;

        /// <summary>Current ambient surface temperature of the world in °C.</summary>
        public float CurrentTemperature => PlanetarySeasons.GetCurrentTemperature();

        /// <summary>Current planetary seasons telemetry snapshot.</summary>
        public PlanetSeasonInfo CurrentSeasonInfo => PlanetarySeasons.GetCurrentSeasonInfo();

        /// <summary>
        /// Precipitation intensity reaching the PLAYER (0 = none, 1 = max): the body's weather
        /// system multiplied by how much of it is over the player (17.31.0-dev — the regional
        /// bands). Everything the player sees or hears reads this, which is what makes walking
        /// out of a rain band silent it. For the system's own strength, which is what the cloud
        /// shells and an orbiting sensor want, use <see cref="PlanetIntensity"/>.
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
        /// 17.31.0-dev — the weather SYSTEM's strength over the whole body, without the regional
        /// coverage: the cloud shell, an orbiting sensor and the planet-wide smog washout ask this
        /// question, because a storm is a storm even where its bands happen to be clear. Equal to
        /// the old `Intensity` before regions existed.
        /// </summary>
        public float PlanetIntensity { get; private set; }

        /// <summary>
        /// How much of the weather system is over the PLAYER right now, 0..1 (see
        /// <see cref="WeatherFronts.Coverage01"/>). 0 = standing in a gap between bands,
        /// 1 = fully under one. 1 on a body with no weather, so nothing else has to special-case it.
        /// </summary>
        public float FrontCoverage => _frontCoverage;

        /// <summary>
        /// The coverage actually applied to the player's sky: forced test weather
        /// (<see cref="IsManuallyHeld"/>) is deliberately exempt, so Ctrl+Alt+R really does give a
        /// full storm instead of whatever the bands were doing at the time.
        /// </summary>
        public float EffectiveCoverage => _manualHold ? 1f : FrontCoverage;

        /// <summary>
        /// 17.31.0-dev — the front clock, in seconds. The regional field is a pure function of it,
        /// so every machine that shares this number shares the same bands: the host publishes it in
        /// the weather broadcast and guests ease onto it.
        /// </summary>
        public float WeatherClock => _weatherClock;

        /// <summary>
        /// The state the player is under (the state the blend is closer to). Position does not
        /// enter into it: which state the body is in is the system's decision, only its strength
        /// and coverage vary by place.
        /// </summary>
        public WeatherState LocalState => TransitionProgress >= 0.5f ? TargetState : CurrentState;

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

        /// <summary>True if the planet is precipitating, whether or not the player is under it.
        /// 17.31.0-dev: this asks the SYSTEM (PlanetIntensity), not the player's sky - the whole
        /// point of the property is to say what the world is doing somewhere the player may not
        /// be, and it is what the smog washout reads.</summary>
        public bool IsPlanetPrecipitating => IsWeatherActive && PlanetIntensity > 0.05f;

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
        private float _weatherClock;          // 17.31.0-dev front clock (seconds)
        private float _frontCoverage = 1f;
        private float _frontSurfaceRadius;
        private float _frontBandMetres;
        private int _frontSeed;

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
            // 17.20.0-dev: subtle surface wind lines ride the same camera-follow rig
            // (Ctrl+B toggles; created if missing, reused if the setup pre-added one).
            // 17.20.1-dev: singleton-checked - the cosmos bootstrap may already host
            // the instance on the WindField object, and then this must not double up.
            if (VoxelEngine.FX.WindLinesVisuals.Instance == null
                && GetComponent<VoxelEngine.FX.WindLinesVisuals>() == null)
                gameObject.AddComponent<VoxelEngine.FX.WindLinesVisuals>();

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

            // 17.31.0-dev — per-region weather. The clock advances on every machine (a guest's
            // own clock is corrected by the host's broadcast), and the coverage over the player is
            // re-read every frame: two 3D value-noise samples, which is the entire runtime cost of
            // regions. The biome / snow check below keeps its own 2-second cadence.
            _weatherClock += Time.deltaTime;
            UpdateFrontCoverage();

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
                          $"(system {PlanetIntensity:F2}, band {EffectiveCoverage:F2}) " +
                          $"snow={IsSnowBiome} season={season.SeasonName} ({season.effectiveTemperature:F1}°C) " +
                          $"body={(body != null ? body.DisplayName : "none")}");
            }

            // Update intensity based on current/target blend, then apply PLACE: the system's
            // strength is one number for the body, and what lands on the player is that strength
            // multiplied by how much of the system is over them.
            PlanetIntensity = SystemIntensity();
            Intensity = PlanetIntensity * EffectiveCoverage;

            UpdateSurfaceProximity();
            LocalIntensity = Intensity * SurfaceProximity;

            UpdateWindMultiplier();

            // State timer — pick next weather (only when weather is actually active and the
            // sky is not being held by hand for testing). 14.46.0: on a
            // CLIENT the roll never runs - states come from the host - but
            // thunder keeps scheduling locally, because thunder is an FX of
            // the state, not a state decision.
            if (IsWeatherActive && !_manualHold)
            {
                if (!RemoteDriven)
                {
                    _stateTimer += Time.deltaTime;
                    if (_stateTimer >= _nextStateChange)
                    {
                        _stateTimer = 0f;
                        _nextStateChange = Random.Range(minStateDuration, maxStateDuration);
                        PickNextState();
                    }
                }

                ScheduleThunder();
            }
            else if (!IsWeatherActive && TargetState != WeatherState.Clear)
            {
                if (!RemoteDriven) ForceWeather(WeatherState.Clear);
            }
            else if (IsWeatherActive && _manualHold)
            {
                ScheduleThunder();   // a held storm still thunders
            }
        }

        /// <summary>
        /// Developer hotkeys (Editor-only): steps through weather states or jumps to a storm.
        /// Disabled in built players.
        /// </summary>
        private void HandleDebugHotkeys()
        {
#if UNITY_EDITOR
            // 14.46.0: a client's sky belongs to the host - forcing it
            // locally would just desync until the next keepalive.
            if (RemoteDriven) return;
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
#endif
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

            // 17.31.0-dev: the same question the map's weather overlay asks about other places,
            // answered through one helper so a region and the player can never disagree.
            IsSnowBiome = IsSnowAt(world.Viewer.position); // cold biomes / cold season
        }

        /// <summary>
        /// 17.31.0-dev — does precipitation at THIS place fall as snow? A forced precipitation type
        /// always wins (that is the body's climate talking), then a freezing season, then the local
        /// biome temperature. This is the per-place version of <see cref="IsSnowBiome"/>, which the
        /// player's own sky keeps using.
        /// </summary>
        private static bool IsSnowAt(Vector3 position)
        {
            var manager = Instance;
            var profile = manager != null ? manager.Profile : null;
            profile ??= WeatherClimateProfile.Default();

            if (profile.precipitation == WeatherClimateProfile.Precipitation.Snow) return true;
            if (profile.precipitation == WeatherClimateProfile.Precipitation.Rain) return false;
            if (profile.precipitation == WeatherClimateProfile.Precipitation.None) return false;

            var seasonInfo = PlanetarySeasons.GetCurrentSeasonInfo();
            if (seasonInfo.isFreezing) return true;

            var world = ActiveWorld.Current;
            if (world == null || world.Viewer == null) return seasonInfo.effectiveTemperature < 2f;

            var climate = BiomePicker.SampleClimate(world.Seed,
                Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
            float seasonalTempShift = seasonInfo.seasonalTemperatureOffset / 50f;
            return Mathf.Clamp01(climate.x + seasonalTempShift) < 0.28f;
        }

        /// <summary>
        /// 17.31.0-dev — the body's weather system at its own strength, ignoring where its bands
        /// happen to be. Zero on a body with no weather.
        /// </summary>
        private float SystemIntensity()
        {
            if (!IsWeatherActive) return 0f;
            return Mathf.Lerp(GetIntensity(CurrentState), GetIntensity(TargetState), TransitionProgress);
        }

        /// <summary>
        /// 17.31.0-dev — re-read the body's front parameters and the coverage over the player.
        /// Called every frame from <see cref="Update"/>; it also keeps the parameters current for
        /// every other caller of <see cref="CoverageForDirection"/>, including the map overlay
        /// while the player is standing somewhere else entirely.
        /// </summary>
        private void UpdateFrontCoverage()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null)
            {
                _frontSurfaceRadius = 0f;
                _frontBandMetres = 0f;
                _frontSeed = 0;
                _frontCoverage = 1f;
                return;
            }

            // The body's own runtime seed decides where its bands sit, so two worlds never share a
            // sky and every machine in THIS world computes the same bands from the same seed.
            _frontSurfaceRadius = Mathf.Max(50f, body.SurfaceRadius);
            _frontBandMetres = WeatherFronts.BandMetresFor(_frontSurfaceRadius);
            _frontSeed = body.genParams.seed;

            if (!IsWeatherActive || playerCamera == null) { _frontCoverage = 1f; return; }
            _frontCoverage = CoverageForDirection(GravityProvider.GetUp(playerCamera.position));
        }

        /// <summary>
        /// 17.31.0-dev — how much of the weather system sits over a piece of the surface, given the
        /// surface direction at that piece. Public because the map draws the bands with it; every
        /// gameplay question about another place should go through <see cref="CoverageAt"/> or
        /// <see cref="PrecipitationAt"/> instead, which start from a world position.
        /// </summary>
        public float CoverageForDirection(Vector3 up)
        {
            if (!IsWeatherActive || _frontBandMetres <= 0f) return 1f;
            var profile = Profile ?? WeatherClimateProfile.Default();
            return WeatherFronts.Coverage01(up, _frontSurfaceRadius, _frontBandMetres, _frontSeed,
                _weatherClock, WeatherFronts.CoverageBiasFor(profile.overcastBias));
        }

        /// <summary>17.31.0-dev — coverage of the weather system over a WORLD position.</summary>
        public float CoverageAt(Vector3 worldPosition)
            => CoverageForDirection(GravityProvider.GetUp(worldPosition));

        /// <summary>
        /// 17.31.0-dev — what is actually falling at a WORLD position, 0..1: the body's system
        /// strength multiplied by the coverage over that place. This is the question a solar panel,
        /// a machine or any other system that is not the player has to ask, and it is the reason
        /// per-region weather is worth having: the sky above the panel is the panel's business.
        /// No surface proximity is applied - callers here are on the ground or in a hull.
        /// </summary>
        public float PrecipitationAt(Vector3 worldPosition)
            => IsWeatherActive
                ? SystemIntensity() * (IsManuallyHeld ? 1f : CoverageAt(worldPosition))
                : 0f;

        /// <summary>17.31.0-dev — does precipitation at a WORLD position fall as snow?</summary>
        public bool SnowfallAt(Vector3 worldPosition) => IsSnowAt(worldPosition);

        private void PickNextState()
        {
            CurrentState = TargetState;
            TransitionProgress = 0f;

            var profile = Profile ?? WeatherClimateProfile.Default();
            var seasonInfo = PlanetarySeasons.GetCurrentSeasonInfo();
            float overcast = profile.overcastBias;
            float storm = Mathf.Clamp01(profile.stormChance + seasonInfo.stormChanceModifier);
            float roll = Random.value;

            // Orbital climate control (11.14.0-dev). Satellites INFLUENCE the odds rather
            // than dictate the sky: the roll is biased, and storm chance is scaled, but
            // every outcome stays reachable. Hooking it here — the single point where the
            // next state is chosen — means one change covers every climate path below.
            float influence = VoxelEngine.GridSystem.GridSatellitePayload.ResolveInfluence();
            if (Mathf.Abs(influence) > 0.001f)
            {
                // Suppression (influence < 0) pushes the roll up, toward the clear/overcast
                // branches that sit at the high end of each comparison chain.
                roll = Mathf.Clamp01(roll - influence * 0.45f);
                storm = Mathf.Clamp01(storm * (1f + influence * 0.8f));
                _lastInfluence = influence;
            }
            else _lastInfluence = 0f;

            bool noPrecip = profile.precipitation == WeatherClimateProfile.Precipitation.None;
            bool snow = IsSnowBiome || profile.precipitation == WeatherClimateProfile.Precipitation.Snow || seasonInfo.isFreezing;

            // A polluted atmosphere modestly favours precipitation: weather "fights back"
            // without locking the player into storms. The local plume matters most, body-wide
            // burden supplies a smaller background term, and the total response is hard-capped
            // at ten percentage points. Existing dilution/recovery lowers this on later rolls.
            if (!noPrecip && playerCamera != null)
            {
                PollutionTelemetry air = PollutionService.TelemetryAt(playerCamera.position);
                float burden = Mathf.Clamp01(air.LocalAir01 * 0.72f + air.BodyAir01 * 0.28f);
                float response = Mathf.SmoothStep(0f, 0.10f,
                    Mathf.InverseLerp(0.08f, 0.75f, burden));
                roll = Mathf.Clamp01(roll - response);
                storm = Mathf.Clamp01(storm + response * 0.65f);
            }

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

            // Desert / ash worlds: wind & occasional overcast only — NEVER rain or snow.
            if (noPrecip)
            {
                TargetState = roll < Mathf.Clamp01(overcast + 0.12f) ? WeatherState.Overcast : WeatherState.Clear;
                LogStateChange();
                return;
            }

            if (snow)
            {
                // Snow / winter still spends real time under a clear sky. Pollution can shift
                // these thresholds by at most the capped response above.
                if (CurrentState == WeatherState.Clear)
                    TargetState = roll < 0.18f ? WeatherState.Snow
                                : (roll < Mathf.Clamp01(0.18f + overcast * 0.45f) ? WeatherState.Overcast : WeatherState.Clear);
                else if (CurrentState == WeatherState.Overcast)
                    TargetState = roll < 0.28f ? WeatherState.Snow
                                : (roll < 0.50f ? WeatherState.Overcast : WeatherState.Clear);
                else if (CurrentState == WeatherState.Snow)
                    TargetState = roll < storm ? WeatherState.Blizzard
                                : (roll < 0.52f ? WeatherState.Snow : WeatherState.Clear);
                else // Blizzard
                    TargetState = roll < 0.24f ? WeatherState.Blizzard
                                : (roll < 0.58f ? WeatherState.Snow : WeatherState.Clear);
            }
            else
            {
                // Temperate weather now returns to clear conditions frequently; pollution adds
                // only a small bounded nudge toward rain and heavy rain.
                if (CurrentState == WeatherState.Clear)
                    TargetState = roll < 0.20f ? WeatherState.Overcast
                                : (roll < 0.32f ? WeatherState.LightRain : WeatherState.Clear);
                else if (CurrentState == WeatherState.Overcast)
                    TargetState = roll < 0.28f ? WeatherState.LightRain
                                : (roll < 0.54f ? WeatherState.Overcast : WeatherState.Clear);
                else if (CurrentState == WeatherState.LightRain)
                    TargetState = roll < storm ? WeatherState.HeavyRain
                                : (roll < 0.55f ? WeatherState.LightRain : WeatherState.Clear);
                else // HeavyRain
                    TargetState = roll < 0.25f ? WeatherState.HeavyRain
                                : (roll < 0.56f ? WeatherState.LightRain : WeatherState.Clear);
            }

            LogStateChange();
        }

        /// <summary>Most recent orbital climate influence applied to a state pick, -1..1.</summary>
        private float _lastInfluence;

        /// <summary>Orbital climate influence currently steering this world, -1..1.</summary>
        public float ClimateInfluence => _lastInfluence;

        private void LogStateChange(string tag = "")
        {
            string climate = Mathf.Abs(_lastInfluence) > 0.001f
                ? $" [climate {_lastInfluence:+0.00;-0.00}]" : "";
            Debug.Log($"[Weather] {CurrentState} -> {TargetState} (intensity~{GetIntensity(TargetState):F2})"
                      + (string.IsNullOrEmpty(tag) ? "" : $" [{tag}]") + climate);
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
            PlanetIntensity = IsWeatherActive ? GetIntensity(state) : 0f;
            Intensity = PlanetIntensity * EffectiveCoverage;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            WindMultiplier = 1f;
        }
    }
}
