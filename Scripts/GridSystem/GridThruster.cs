// Assets/Scripts/VoxelEngine/GridSystem/GridThruster.cs
//
// Thruster block. Three types:
//   Atmospheric — uses power only, works in atmosphere
//   Hydrogen    — uses hydrogen gas, works everywhere  
//   Ion         — uses power, low thrust, high efficiency, works in space

using UnityEngine;

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
                            && GridGasNetwork.Instance.AvailableGas(Grid, Gas.GasType.Hydrogen) > 0.1f;
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
                float input = GetThrustFraction();
                return powerAtMaxThrust * input;
            }
        }

        /// <summary>Calculate current thrust output for the grid's input.</summary>
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
                    ? GridGasNetwork.Instance.DrawGas(grid, Gas.GasType.Hydrogen, consumed)
                    : 0f;
                if (drawn < consumed * 0.5f) return 0;
            }
            return thrust;
        }

        public float GetCurrentThrust(Vector3 input, GridEntity grid)
        {
            float fraction = GetThrustFraction();
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
                    ? GridGasNetwork.Instance.DrawGas(grid, Gas.GasType.Hydrogen, consumed)
                    : 0f;
                if (drawn < consumed * 0.5f) return 0;
            }

            return thrust;
        }

        /// <summary>0..1 fraction of max thrust this engine is producing right now.
        /// Public so the audio system can drive the thruster roar volume/pitch.</summary>
        public float GetThrustFraction()
        {
            // Drives flame FX + audio: lit whenever the ship is being thrust.
            if (Grid == null) return 0;
            return Mathf.Clamp01(Grid.ThrustInput.magnitude);
        }

        // Particle effect for visual thrust.
        private ParticleSystem _thrustFX;

        public override void OnPlaced()
        {
            base.OnPlaced();
            CreateThrustEffect();
        }

        private void Update()
        {
            if (_thrustFX == null) return;
            float fraction = GetThrustFraction();
            var emission = _thrustFX.emission;
            emission.rateOverTime = IsOperational ? fraction * 100f : 0;
        }

        private void CreateThrustEffect()
        {
            var go = new GameObject("ThrustFX");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = -Vector3.forward * gridSize.CellSize() * 0.5f;
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);

            _thrustFX = go.AddComponent<ParticleSystem>();
            var main = _thrustFX.main;
            main.loop = true;
            main.startLifetime = 0.3f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            Color flameColor = thrusterType switch
            {
                ThrusterType.Atmospheric => new Color(1f, 0.6f, 0.2f, 0.8f),
                ThrusterType.Hydrogen    => new Color(0.3f, 0.6f, 1f, 0.8f),
                ThrusterType.Ion         => new Color(0.5f, 0.3f, 1f, 0.6f),
                _ => Color.white
            };
            main.startColor = flameColor;

            var shape = _thrustFX.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10;
            shape.radius = 0.15f;

            var emission = _thrustFX.emission;
            emission.rateOverTime = 0;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            rend.material = new Material(sh) { color = flameColor };
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Shorthand
        private GridSize gridSize => Grid != null ? Grid.gridSize : GridSize.Large;
    }
}
