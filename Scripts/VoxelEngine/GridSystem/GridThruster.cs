// Assets/Scripts/VoxelEngine/GridSystem/GridThruster.cs
//
// Thruster block. Supports 4 types (Space Engineers + IndustrialWorld expansion):
//   Atmospheric — power only, atmosphere
//   Hydrogen    — H2 gas, anywhere
//   Ion         — power only, efficient in space
//   LiquidFuel  — consumes mixed liquid fuel (Kerosene + LiqH2 + LiqCH4) — Phase 2 full chain
//
// All logic hardened, null-safe, with FX. Performance-friendly for large grids.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public enum ThrusterType { Atmospheric, Hydrogen, Ion, LiquidFuel }

    public class GridThruster : GridBlock
    {
        [Header("Thruster Configuration")]
        public ThrusterType thrusterType = ThrusterType.Atmospheric;

        [Tooltip("Maximum thrust force in Newtons.")]
        public float maxThrustN = 50000f;

        [Tooltip("Power consumed at max thrust (W). Atmospheric + Ion + LiquidFuel.")]
        public float powerAtMaxThrust = 500f;

        [Tooltip("Hydrogen consumed per second at max thrust. Hydrogen type only.")]
        public float hydrogenPerSecond = 10f;

        [Tooltip("Liquid fuel consumed per second at max thrust. LiquidFuel type only. (Phase 2)")]
        public float liquidFuelPerSecond = 5f;

        /// <summary>Is this thruster operational right now?</summary>
        public bool IsOperational
        {
            get
            {
                if (Grid == null) return false;

                return thrusterType switch
                {
                    ThrusterType.Atmospheric or ThrusterType.Ion => Grid.HasPower,
                    ThrusterType.Hydrogen => Grid.HydrogenStored > 0.1f,
                    ThrusterType.LiquidFuel => Grid.LiquidFuelStored > 0.1f || Grid.HasPower, // stub
                    _ => false
                };
            }
        }

        public override float PowerDraw
        {
            get
            {
                if (Grid == null || !IsOperational) return 0f;
                if (thrusterType == ThrusterType.Hydrogen) return 0f;

                float input = GetThrustFraction();
                return powerAtMaxThrust * input;
            }
        }

        /// <summary>Calculate current thrust output and consume resources.</summary>
        public float GetCurrentThrust(Vector3 input, GridEntity grid)
        {
            float fraction = GetThrustFraction();
            if (fraction <= 0.01f) return 0f;

            // Resource consumption (hardened)
            if (thrusterType == ThrusterType.Hydrogen && grid != null)
            {
                float consumed = hydrogenPerSecond * fraction * Time.fixedDeltaTime;
                if (grid.HydrogenStored < consumed) return 0f;
                grid.HydrogenStored -= consumed;
            }
            else if (thrusterType == ThrusterType.LiquidFuel && grid != null)
            {
                float consumed = liquidFuelPerSecond * fraction * Time.fixedDeltaTime;
                if (grid.LiquidFuelStored < consumed) return 0f;
                grid.LiquidFuelStored -= consumed;
            }

            return maxThrustN * fraction;
        }

        protected float GetThrustFraction()
        {
            if (Grid == null) return 0f;
            Vector3 input = Grid.ThrustInput;
            Vector3 localFwd = Grid.transform.InverseTransformDirection(transform.forward);
            float dot = Vector3.Dot(localFwd, input);
            return Mathf.Clamp01(dot);
        }

        // ── Visual FX ──────────────────────────────────────────────
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
            emission.rateOverTime = IsOperational ? fraction * 100f : 0f;
        }

        private void CreateThrustEffect()
        {
            var go = new GameObject("ThrustFX");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = -Vector3.forward * (Grid?.gridSize.CellSize() ?? 2.5f) * 0.5f;
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
                ThrusterType.Hydrogen => new Color(0.3f, 0.6f, 1f, 0.8f),
                ThrusterType.Ion => new Color(0.5f, 0.3f, 1f, 0.6f),
                ThrusterType.LiquidFuel => new Color(1f, 0.4f, 0.1f, 0.9f), // orange for liquid
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

        private GridSize GridSize => Grid != null ? Grid.gridSize : GridSize.Large;
    }
}