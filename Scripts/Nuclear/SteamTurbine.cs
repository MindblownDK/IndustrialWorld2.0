// Assets/Scripts/VoxelEngine/Nuclear/SteamTurbine.cs
//
// Converts steam (from reactor via GasPipe) into electricity.
// Internal water and steam tanks. Exhausted steam condenses back to water.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Gas;
using VoxelEngine.Power;

namespace VoxelEngine.Nuclear
{
    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PowerGenerator))]
    public class SteamTurbine : MonoBehaviour
    {
        [Header("Conversion")]
        [Range(0.1f, 0.5f)] public float efficiency = 0.33f;
        public float maxSteamInputPerSec = 100f;

        [Header("Internal Tanks")]
        public float steamTankCapacity = 300f;
        public float waterTankCapacity = 300f;
        public float steamAmount;
        public float waterAmount;

        [Header("Power")]
        public float maxWattsOutput = 330000f;

        public float CurrentOutput { get; private set; }
        public bool IsRunning => steamAmount > 1f;
        public float SteamFill01 => steamTankCapacity > 0 ? Mathf.Clamp01(steamAmount / steamTankCapacity) : 0;
        public float WaterFill01 => waterTankCapacity > 0 ? Mathf.Clamp01(waterAmount / waterTankCapacity) : 0;
        public float SpinSpeed01 { get; private set; }

        private PowerGenerator _gen;
        private float _pullTimer;
        private ParticleSystem _steamPS;

        private void Awake()
        {
            _gen = GetComponent<PowerGenerator>();
            CreateSteamEffect();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Pull steam from gas network.
            _pullTimer += dt;
            if (_pullTimer >= 0.5f)
            {
                _pullTimer = 0;
                PullSteam();
                PushWater();
            }

            // Convert steam to power + condensed water.
            if (steamAmount > 0.1f)
            {
                float steamUsed = Mathf.Min(steamAmount, maxSteamInputPerSec * dt);
                steamAmount -= steamUsed;

                // Condensed water from exhaust steam.
                waterAmount = Mathf.Min(waterTankCapacity, waterAmount + steamUsed * 0.5f);

                // Power output.
                float powerFraction = steamUsed / (maxSteamInputPerSec * dt);
                CurrentOutput = maxWattsOutput * efficiency * powerFraction;
                _gen.wattsPerSecond = CurrentOutput;
                _gen.isOn = true;
                SpinSpeed01 = Mathf.Clamp01(powerFraction);
            }
            else
            {
                CurrentOutput = 0;
                _gen.isOn = false;
                _gen.wattsPerSecond = 0;
                SpinSpeed01 = Mathf.MoveTowards(SpinSpeed01, 0, dt * 0.5f);
            }

            if (_steamPS != null)
            {
                var em = _steamPS.emission;
                em.rateOverTime = SpinSpeed01 * 40f;
            }
        }

        private void PullSteam()
        {
            float space = steamTankCapacity - steamAmount;
            if (space <= 0) return;
            var tank = GasNetwork.Instance?.FindTankNear(transform.position, GasType.Steam, true);
            if (tank != null)
            {
                float taken = tank.TryTake(GasType.Steam, Mathf.Min(space, 50f));
                steamAmount += taken;
            }
        }

        private void PushWater()
        {
            if (waterAmount <= 0) return;
            var hits = Physics.OverlapSphere(transform.position, 3f);
            foreach (var col in hits)
            {
                var wt = col.GetComponent<VoxelEngine.Fluids.WaterTank>();
                if (wt != null)
                {
                    float pushed = wt.AddSome(Mathf.Min(waterAmount, 50f));
                    waterAmount -= pushed;
                    if (waterAmount <= 0) break;
                }
            }
        }

        private void CreateSteamEffect()
        {
            var go = new GameObject("SteamFX");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1.5f;
            _steamPS = go.AddComponent<ParticleSystem>();
            var main = _steamPS.main;
            main.loop = true; main.startLifetime = 2f; main.startSpeed = 1.5f;
            main.startSize = 0.5f; main.startColor = new Color(1, 1, 1, 0.4f);
            main.maxParticles = 100; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.1f;
            var shape = _steamPS.shape; shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15; shape.radius = 0.3f;
            var em = _steamPS.emission; em.rateOverTime = 0;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            rend.material = new Material(sh) { color = new Color(1, 1, 1, 0.3f) };
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
