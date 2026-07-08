// Assets/Scripts/VoxelEngine/GridSystem/GridThruster.cs
//
// Thruster block. Three types:
//   Atmospheric — uses power only, works in atmosphere
//   Hydrogen    — uses hydrogen gas, works everywhere
//   Ion         — uses power, low thrust, high efficiency, works in space
//
// Immersive feedback: per-thruster particle plume, dynamic exhaust light, and
// procedural audio loop whose volume/pitch scales with the actual thrust fraction.

using UnityEngine;
using VoxelEngine.FX;

namespace VoxelEngine.GridSystem
{
    public enum ThrusterType { Atmospheric, Hydrogen, Ion }

    public class GridThruster : GridBlock
    {
        [Header("Thruster")]
        public ThrusterType thrusterType = ThrusterType.Atmospheric;

        [Tooltip("Maximum thrust force in Newtons.")]
        public float maxThrustN = 50000f;

        [Tooltip("Power consumed at max thrust (W). Atmospheric + Ion only.")]
        public float powerAtMaxThrust = 500f;

        [Tooltip("Hydrogen consumed per second at max thrust. Hydrogen type only.")]
        public float hydrogenPerSecond = 10f;

        /// <summary>World-space direction this thruster PUSHES the ship. The exhaust flame
        /// (the "particles") exits the block's local -forward, so the reaction force pushes
        /// the ship along its local +forward.</summary>
        public Vector3 PushDirection => transform.forward;

        /// <summary>Is this thruster operational right now?</summary>
        public bool IsOperational
        {
            get
            {
                if (Grid == null || !Enabled) return false;
                switch (thrusterType)
                {
                    case ThrusterType.Atmospheric:
                    case ThrusterType.Ion:
                        return Grid.HasPower;
                    case ThrusterType.Hydrogen:
                        return GridGasNetwork.Instance != null
                            && GridGasNetwork.Instance.AvailableGasFor(this, Gas.GasType.Hydrogen) > 0.1f;
                    default: return false;
                }
            }
        }

        public override float PowerDraw
        {
            get
            {
                if (Grid == null || !Enabled) return 0;
                if (thrusterType == ThrusterType.Hydrogen) return 0; // hydrogen doesn't use power
                return powerAtMaxThrust * ThrustFraction;
            }
        }

        /// <summary>
        /// 0..1 fraction of max thrust this specific engine is producing right now.
        /// Set by GridEntity each physics frame based on the pilot's input and this
        /// thruster's orientation. Drives flame, audio, and exhaust light.
        /// </summary>
        public float ThrustFraction { get; set; }

        /// <summary>Full thrust (N) this engine provides toward the pilot's desired direction,
        /// consuming its fuel/power. Direction handling is done by the grid (intuitive WASD).</summary>
        public float AvailableThrust(Vector3 input, GridEntity grid)
        {
            float thrust = maxThrustN;

            if (thrusterType == ThrusterType.Atmospheric && grid != null)
            {
                float density = AtmosphereManager.GetAirDensity(grid.transform.position);
                thrust *= Mathf.Clamp01(density / 1.225f);
            }
            if (thrusterType == ThrusterType.Hydrogen && grid != null)
            {
                float consumed = hydrogenPerSecond * Time.fixedDeltaTime;
                float drawn = GridGasNetwork.Instance != null
                    ? GridGasNetwork.Instance.DrawGasFor(this, Gas.GasType.Hydrogen, consumed)
                    : 0f;
                if (drawn < consumed * 0.5f) return 0;
            }
            return thrust;
        }

        public float GetCurrentThrust(Vector3 input, GridEntity grid)
        {
            float fraction = ThrustFraction;
            if (fraction <= 0.01f) return 0;

            float thrust = maxThrustN * fraction;

            // Atmospheric efficiency (Atmospheric thrusters lose power in thin air / space)
            if (thrusterType == ThrusterType.Atmospheric && grid != null)
            {
                float density = AtmosphereManager.GetAirDensity(grid.transform.position);
                thrust *= Mathf.Clamp01(density / 1.225f);
            }

            // Consume resources.
            if (thrusterType == ThrusterType.Hydrogen && grid != null)
            {
                float consumed = hydrogenPerSecond * fraction * Time.fixedDeltaTime;
                float drawn = GridGasNetwork.Instance != null
                    ? GridGasNetwork.Instance.DrawGasFor(this, Gas.GasType.Hydrogen, consumed)
                    : 0f;
                if (drawn < consumed * 0.5f) return 0;
            }

            return thrust;
        }

        // Particle effect for visual thrust.
        private ParticleSystem _thrustFX;
        private AudioSource _audio;
        private Light _exhaustLight;
        private float _maxLightIntensity;

        public override void OnPlaced()
        {
            base.OnPlaced();
            CreateThrustEffect();
            CreateAudio();
            CreateExhaustLight();
        }

        private void Start()
        {
            // Old saves / loaded grids didn't run OnPlaced, so ensure the immersive
            // feedback objects exist before the first Update tick.
            if (_thrustFX == null) CreateThrustEffect();
            if (_audio == null) CreateAudio();
            if (_exhaustLight == null) CreateExhaustLight();
        }

        private void Update()
        {
            if (_thrustFX == null) return;

            float fraction = IsOperational ? ThrustFraction : 0f;
            // Apply a slight delay curve so the plume "spools" with the engine.
            fraction = Mathf.Pow(fraction, 1.5f);

            var emission = _thrustFX.emission;
            emission.rateOverTime = fraction * _emissionRate;

            var main = _thrustFX.main;
            main.startSize = _baseStartSize * Mathf.Lerp(0.6f, 1.0f, fraction);
            main.startSpeed = _baseStartSpeed * Mathf.Lerp(0.5f, 1.0f, fraction);

            // Drive the audio rumble.
            if (_audio != null)
            {
                _audio.volume = fraction * fraction * _maxVolume;
                _audio.pitch = Mathf.Lerp(0.75f, 1.25f, fraction);
            }

            // Drive the exhaust glow.
            if (_exhaustLight != null)
            {
                _exhaustLight.intensity = fraction * _maxLightIntensity;
                _exhaustLight.range = _baseLightRange * Mathf.Lerp(0.6f, 1.0f, fraction);
            }
        }

        private float _emissionRate;
        private float _baseStartSize;
        private float _baseStartSpeed;
        private float _baseLightRange;
        private float _maxVolume;

        private Color FlameColor => thrusterType switch
        {
            ThrusterType.Atmospheric => new Color(1.00f, 0.55f, 0.12f, 0.9f),
            ThrusterType.Hydrogen    => new Color(0.25f, 0.65f, 1.00f, 0.9f),
            ThrusterType.Ion         => new Color(0.55f, 0.25f, 1.00f, 0.8f),
            _ => Color.white
        };

        private void CreateThrustEffect()
        {
            float cs = gridSize.CellSize();
            var go = new GameObject("ThrustFX");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = -Vector3.forward * cs * 0.5f;
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);

            _thrustFX = go.AddComponent<ParticleSystem>();
            var main = _thrustFX.main;
            main.loop = true;
            main.startLifetime = 0.5f + (cs * 0.08f);       // longer plume for large grids
            _baseStartSpeed = 12f + (cs * 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(_baseStartSpeed * 0.7f, _baseStartSpeed * 1.3f);
            _baseStartSize = 0.25f + (cs * 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(_baseStartSize * 0.6f, _baseStartSize);
            main.maxParticles = 500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.startColor = FlameColor;
            main.playOnAwake = true;

            var shape = _thrustFX.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = cs * 0.15f;

            _emissionRate = 120f + (cs * 25f);
            var emission = _thrustFX.emission;
            emission.rateOverTime = 0;

            var velocity = _thrustFX.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.speedModifier = 1.0f;

            var sizeOverLifetime = _thrustFX.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve growCurve = new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0.3f, 1.0f),
                new Keyframe(1f, 1.6f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, growCurve);

            var colorOverLifetime = _thrustFX.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            Color core = FlameColor;
            Color fade = new Color(core.r, core.g, core.b, 0f);
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(core, 0f), new GradientColorKey(Color.white, 0.2f), new GradientColorKey(fade, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.55f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = grad;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            rend.material = new Material(sh) { color = FlameColor };
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.sortingFudge = -50f; // draw plume behind the ship geometry
        }

        private void CreateAudio()
        {
            var go = new GameObject("ThrusterAudio");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = -Vector3.forward * gridSize.CellSize() * 0.55f;

            _audio = go.AddComponent<AudioSource>();
            _audio.playOnAwake = true;
            _audio.loop = true;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 3f;
            _audio.maxDistance = 80f;
            _audio.dopplerLevel = 0.4f;
            AudioManager.Route(_audio, music: false);

            Sfx sfx = thrusterType switch
            {
                ThrusterType.Atmospheric => Sfx.ThrusterAtmo,
                ThrusterType.Hydrogen    => Sfx.ThrusterHydrogen,
                ThrusterType.Ion         => Sfx.ThrusterIon,
                _ => Sfx.ThrusterAtmo
            };
            _audio.clip = SfxLibrary.Get(sfx, 0);
            _maxVolume = 0.7f;
            _audio.volume = 0f;
            _audio.Play();
        }

        private void CreateExhaustLight()
        {
            float cs = gridSize.CellSize();
            var go = new GameObject("ThrusterLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = -Vector3.forward * cs * 0.6f;

            _exhaustLight = go.AddComponent<Light>();
            _exhaustLight.type = LightType.Point;
            _exhaustLight.color = FlameColor;
            _exhaustLight.shadows = LightShadows.None;
            _baseLightRange = cs * 4.5f;
            _exhaustLight.range = _baseLightRange;
            _maxLightIntensity = 1.8f + (cs * 0.25f);
            _exhaustLight.intensity = 0f;
        }

        // Shorthand
        private GridSize gridSize => Grid != null ? Grid.gridSize : GridSize.Large;
    }
}
