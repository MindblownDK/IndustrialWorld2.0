// Assets/Scripts/VoxelEngine/FX/WindLinesVisuals.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║           SUBTLE SURFACE WIND LINES (17.20.0-dev)                ║
// ║  Thin, low-alpha streaks drifting through the air just above the ║
// ║  ground, showing where and how hard the wind blows - the surface ║
// ║  read of the game's own wind simulation.                         ║
// ║                                                                  ║
// ║  Source of truth: WindField.Current (the same continuous noise-  ║
// ║  driven wind the GPU grass, sails and audio already read),       ║
// ║  projected onto the local surface tangent so the streaks hug the ║
// ║  ground on any sphere, and gated by AtmosphereManager - no air,  ║
// ║  no lines (space and airless moons stay clean). Density thins    ║
// ║  the lines at altitude exactly like it thins the sail force.     ║
// ║                                                                  ║
// ║  Ctrl+B toggles (the key half is rebindable in Settings -        ║
// ║  Keybinds); the on/off preference persists. Lives on the weather ║
// ║  follow rig, one velocity-stretched particle system, zero per-   ║
// ║  frame allocations, modules written only when values move.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;
using VoxelEngine.Settings;
using VoxelEngine.UI;

namespace VoxelEngine.FX
{
    public class WindLinesVisuals : MonoBehaviour
    {
        // ── look (code constants; nothing authored to overwrite) ──
        private const float MaxRate          = 110f;   // particles/s at full gale
        private const float MinVisibleSpeed  = 0.10f;  // m/s below which the air reads as still
        private const float IntensityRange   = 1.4f;   // speed span from first wisp to full rate
        private const float SpeedGain        = 2.2f;   // visual drift gain over true wind speed
        private const float MinDrift         = 0.9f;   // even a breeze must visibly move
        private const float MaxDrift         = 14f;    // gale ceiling for the streaks
        private const float EmitterDepth     = 2.5f;   // metres below the camera anchor
        private const float RepositionEps    = 0.25f;  // metres before the emitter re-seats

        private ParticleSystem _ps;
        private ParticleSystem.EmissionModule _emission;
        private ParticleSystem.VelocityOverLifetimeModule _vel;
        private Transform _emitterT;

        /// <summary>Singleton: both the weather rig and the cosmos bootstrap may
        /// attach this component; the second instance retires itself.</summary>
        public static WindLinesVisuals Instance { get; private set; }

        // Anchor source: the weather follow rig when the world has one, else the
        // main camera - so the lines work even when hosted on the WindField object.
        private Transform _anchorT;
        private float _anchorRetry;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private Vector3 AnchorPosition()
        {
            if (_anchorT == null && Time.unscaledTime >= _anchorRetry)
            {
                _anchorRetry = Time.unscaledTime + 1f;
                var wm = VoxelEngine.Weather.WeatherManager.Instance;
                if (wm != null) _anchorT = wm.transform;
                else
                {
                    var cam = Camera.main;
                    if (cam != null) _anchorT = cam.transform;
                }
            }
            return _anchorT != null ? _anchorT.position : transform.position;
        }

        // Cached state so Update never touches a module without a real change.
        private Vector3 _lastVel = Vector3.zero;
        private float _lastRate = -1f;
        private Vector3 _lastPos;
        private Quaternion _lastRot = Quaternion.identity;
        private bool _poseValid;
        private bool _wasVisible;

        private void Start()
        {
            // A dedicated server has no screen and no shaders - retire like the
            // rest of the weather visuals.
            if (VoxelEngine.Networking.NetworkSession.IsDedicated) { enabled = false; return; }
            BuildSystem();
            _wasVisible = GameSettings.WindLinesVisible;
            if (!_wasVisible && _ps != null) _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void Update()
        {
            HandleToggleInput();

            bool want = GameSettings.WindLinesVisible;
            if (!want)
            {
                if (_wasVisible)
                {
                    _wasVisible = false;
                    _lastRate = -1f;
                    if (_ps != null && _ps.isPlaying) _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                return;
            }
            _wasVisible = true;
            if (_ps == null) return;

            Vector3 anchor = AnchorPosition();
            Vector3 up = GravityProvider.GetUp(anchor);
            if (up.sqrMagnitude < 1e-6f) { StopLines(); return; }
            up.Normalize();

            // No air, no wind lines: space, airless moons and the upper vacuum
            // stay clean, and thin altitude air thins the streaks.
            AtmosphereSample atmo = AtmosphereManager.Sample(anchor);
            if (!atmo.HasAtmosphere || atmo.AirDensity <= 0.0001f) { StopLines(); return; }

            var field = WindField.Instance;
            if (field == null) { StopLines(); return; }

            // Wind blows ACROSS the surface - strip the radial component so the
            // streaks hug the ground instead of raining from the sky (the same
            // projection the sail force uses).
            Vector3 wind = Vector3.ProjectOnPlane(field.Current, up);
            float speed = wind.magnitude * atmo.Density01;
            if (speed < MinVisibleSpeed) { StopLines(); return; }

            Vector3 dir = wind.normalized;
            float drift = Mathf.Clamp(speed * SpeedGain + MinDrift, MinDrift, MaxDrift);
            Vector3 vel = dir * drift;

            // Seat the emitter box flat on the local surface, long edge across
            // the wind, and only re-seat when the anchor actually moved.
            Vector3 pos = anchor - up * EmitterDepth;
            Quaternion rot = Quaternion.LookRotation(dir, up);
            if (!_poseValid
                || (pos - _lastPos).sqrMagnitude > RepositionEps * RepositionEps
                || Quaternion.Angle(rot, _lastRot) > 2f)
            {
                _emitterT.SetPositionAndRotation(pos, rot);
                _lastPos = pos; _lastRot = rot; _poseValid = true;
            }

            if ((vel - _lastVel).sqrMagnitude > 0.01f)
            {
                _lastVel = vel;
                _vel.x = new ParticleSystem.MinMaxCurve(vel.x);
                _vel.y = new ParticleSystem.MinMaxCurve(vel.y);
                _vel.z = new ParticleSystem.MinMaxCurve(vel.z);
            }

            float intensity = Mathf.Clamp01((speed - MinVisibleSpeed) / IntensityRange);
            float rate = intensity * MaxRate;
            if (Mathf.Abs(rate - _lastRate) > 2f)
            {
                _lastRate = rate;
                _emission.rateOverTime = rate;
            }
            if (!_ps.isPlaying) _ps.Play();
        }

        private void StopLines()
        {
            if (_lastRate != 0f)
            {
                _lastRate = 0f;
                _emission.rateOverTime = 0f;
            }
            if (_ps != null && _ps.isPlaying) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // ── Ctrl+B by default: the binding itself carries the modifier (the
        //    stored code is literally "Ctrl+B"), so WasPressed already requires
        //    Ctrl - no separate modifier check here. Rebindable in Settings -
        //    Keybinds, combos included. ──
        private void HandleToggleInput()
        {
            if (UIState.IsBlocking || UIState.TextInputActive) return;
            if (!GameSettings.WasPressed(InputAction.WindLines)) return;

            GameSettings.WindLinesVisible = !GameSettings.WindLinesVisible;
            bool on = GameSettings.WindLinesVisible;
            BuildFeedbackHud.Show("Wind Lines",
                on ? "Surface wind lines ON" : "Surface wind lines OFF",
                null, on ? new Color(0.62f, 0.86f, 1f) : new Color(0.75f, 0.75f, 0.78f));
        }

        // ── one velocity-stretched particle system, built at runtime ──
        private void BuildSystem()
        {
            var go = new GameObject("WindLineParticles");
            go.transform.SetParent(transform, false);
            _emitterT = go.transform;

            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 4.0f);
            main.startSpeed = 0f;                       // motion comes from the wind velocity module
            main.startSize = new ParticleSystem.MinMaxCurve(0.030f, 0.055f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.86f, 0.93f, 1.00f, 0.9f),
                new Color(0.96f, 0.98f, 1.00f, 1.0f));
            main.maxParticles = 420;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.stopAction = ParticleSystemStopAction.None;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(46f, 2.6f, 46f);  // a wide, thin slab hugging the surface

            _vel = _ps.velocityOverLifetime;
            _vel.enabled = true;
            _vel.space = ParticleSystemSimulationSpace.World;
            _vel.x = new ParticleSystem.MinMaxCurve(0f);
            _vel.y = new ParticleSystem.MinMaxCurve(0f);
            _vel.z = new ParticleSystem.MinMaxCurve(0f);

            // A slow curl keeps the streaks from reading as ruler lines.
            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = 0.30f;
            noise.frequency = 0.09f;
            noise.scrollSpeed = 0.25f;
            noise.octaveCount = 1;

            _emission = _ps.emission;
            _emission.rateOverTime = 0f;

            // Fade in, drift, fade out - never a hard pop at the box edges.
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.42f, 0.15f),
                        new GradientAlphaKey(0.42f, 0.70f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;   // streak along the velocity
            renderer.lengthScale = 1.0f;
            renderer.velocityScale = 0.55f;                           // harder wind = longer wisps
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sharedMaterial = CreateLineMaterial();
        }

        private static Material CreateLineMaterial()
        {
            // The project's own weather particle shader FIRST - it is the proven
            // transparent path the rain and snow already render through (Blend
            // SrcAlpha/OneMinusSrcAlpha, ZWrite off, tint + shape modes). A fresh
            // Material() on the generic URP particle shader defaults to OPAQUE and
            // rendered the wisps effectively invisible - the 17.20.0 bug.
            var shader = Shader.Find("VoxelEngine/WeatherParticlesURP")
                      ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "WindLineMat" };
            var tint = new Color(0.92f, 0.96f, 1.00f, 1f);
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", tint);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_ShapeMode")) mat.SetFloat("_ShapeMode", 0f);   // soft puff, stretched by the renderer
            // Fallback shaders need transparency configured by hand.
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            mat.color = tint;
            mat.renderQueue = 3020;
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return mat;
        }
    }
}
