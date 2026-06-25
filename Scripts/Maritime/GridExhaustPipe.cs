// Assets/Scripts/VoxelEngine/Maritime/GridExhaustPipe.cs
//
// Exhaust Pipe — venting / cooling. Every engine MUST have at least one
// adjacent exhaust pipe or it chokes (zero torque). The pipe vents exhaust
// gas from adjacent engines and emits visible smoke particles while venting.
//
// Mechanics:
//   • Scans 6 face-neighbours for GridMaritimeEngine blocks.
//   • While any neighbour engine is running + producing exhaust gas, the pipe
//     emits smoke particles (black for diesel, grey for small engines).
//   • The actual vent RATE is handled inside GridMaritimeEngine.RefreshMaritimeNode
//     (it checks HasExhaust = adjacent pipe exists → reduces ExhaustGas).

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public class GridExhaustPipe : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.ExhaustPipe;

        [Header("Exhaust VFX")]
        [Tooltip("Max smoke particles per second while venting.")]
        public float smokeRate = 40f;
        [Tooltip("Smoke colour when venting from a Giant Diesel (heavy black smoke).")]
        public Color heavySmoke = new Color(0.08f, 0.07f, 0.06f, 0.7f);
        [Tooltip("Smoke colour when venting from a Small Engine (light grey sputter).")]
        public Color lightSmoke = new Color(0.35f, 0.33f, 0.30f, 0.5f);

        private ParticleSystem _smokeFX;
        private bool _venting;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Exhaust Pipe";
            CreateSmokeEffect();
        }

        /// <summary>True if any adjacent engine is currently venting exhaust gas.</summary>
        public bool IsVenting => _venting;

        private void Update()
        {
            if (_smokeFX == null) return;

            // Check adjacent engines.
            _venting = false;
            Color targetColor = lightSmoke;
            float maxExhaust = 0f;

            if (Grid != null)
            {
                var faces = new[]
                {
                    new Vector3Int( 1,0,0), new(-1,0,0),
                    new( 0,1,0), new( 0,-1,0),
                    new( 0,0,1), new( 0,0,-1),
                };
                foreach (var off in faces)
                {
                    var nb = Grid.GetBlock(GridPos + off);
                    if (nb is GridMaritimeEngine eng && eng.IsRunning && eng.ExhaustGas > 0.5f)
                    {
                        _venting = true;
                        if (eng.ExhaustGas > maxExhaust)
                        {
                            maxExhaust = eng.ExhaustGas;
                            // Giant diesel = heavy black smoke; others = light grey.
                            targetColor = eng.tier == EngineTier.Giant ? heavySmoke : lightSmoke;
                        }
                    }
                }
            }

            // Drive the particle system.
            var emission = _smokeFX.emission;
            if (_venting)
            {
                float intensity = Mathf.Clamp01(maxExhaust / 50f);
                emission.rateOverTime = smokeRate * intensity;

                var main = _smokeFX.main;
                main.startColor = targetColor;
            }
            else
            {
                emission.rateOverTime = 0f;
            }
        }

        private void CreateSmokeEffect()
        {
            var go = new GameObject("ExhaustSmoke");
            go.transform.SetParent(transform, false);
            float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;
            go.transform.localPosition = new Vector3(0, cs * 0.55f, 0);

            _smokeFX = go.AddComponent<ParticleSystem>();
            var main = _smokeFX.main;
            main.loop = true;
            main.startLifetime = 2.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.maxParticles = 150;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.3f; // smoke rises
            main.startColor = lightSmoke;

            var shape = _smokeFX.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25;
            shape.radius = cs * 0.15f;

            var emission = _smokeFX.emission;
            emission.rateOverTime = 0;

            var velOverLife = _smokeFX.velocityOverLifetime;
            velOverLife.enabled = true;
            velOverLife.y = new ParticleSystem.MinMaxCurve(1f, 2.5f);

            var sizeOverLife = _smokeFX.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.3f);
            sizeCurve.AddKey(1f, 2.5f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var colorOverLife = _smokeFX.colorOverLifetime;
            colorOverLife.enabled = true;
            var colorGradient = new Gradient();
            colorGradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(colorGradient);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            rend.material = new Material(sh) { color = lightSmoke };
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
