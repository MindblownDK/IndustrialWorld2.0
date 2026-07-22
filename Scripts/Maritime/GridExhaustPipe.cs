// Assets/Scripts/VoxelEngine/Maritime/GridExhaustPipe.cs
//
// Exhaust Pipe — venting / cooling. Every engine MUST have at least one
// adjacent exhaust pipe or it chokes (zero torque). The pipe vents exhaust
// gas from adjacent engines and emits visible smoke particles while venting.
//
// Mechanics:
//   • Scans 6 face-neighbours for GridMaritimeEngine blocks.
//   • While any neighbour engine is running + producing exhaust gas, the pipe
//     emits smoke particles styled after the engine's tier:
//       Tier 1 Crude   — pulsating dark blackish-grey puffs tuned to RPM.
//       Tier 2 HFO V8  — steady, thick dark-grey column.
//       Tier 3 MGO V12 — clean, lightly visible blueish-white fast stream.
//   • Engine upgrade modules reshape the plume: High-Flow Turbochargers raise
//     exhaust velocity, Overclocked Fuel Injectors dirty the smoke, and a
//     critical-overheat engine belches heavy black smoke at an increased rate.
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

        [Header("Tiered Smoke Profiles")]
        [Tooltip("Tier 1 Crude Inline-4: pulsating dark blackish-grey puffs.")]
        public Color crudePuffSmoke = new Color(0.16f, 0.15f, 0.14f, 0.65f);
        [Tooltip("Tier 2 HFO V8: steady thick dark-grey column.")]
        public Color hfoColumnSmoke = new Color(0.22f, 0.21f, 0.20f, 0.7f);
        [Tooltip("Tier 3 MGO V12: clean light-grey / blueish-white high-velocity stream.")]
        public Color mgoStreamSmoke = new Color(0.82f, 0.85f, 0.88f, 0.22f);
        [Tooltip("Critical-overheat smoke: heavy oily black regardless of tier.")]
        public Color criticalSmoke = new Color(0.03f, 0.03f, 0.03f, 0.9f);
        [Tooltip("Rate multiplier applied while a neighbouring engine is in critical heat.")]
        public float criticalRateMultiplier = 1.6f;
        [Tooltip("How strongly dirty exhaust (fuel injectors) darkens the plume, 0-1.")]
        [Range(0f, 1f)] public float dirtyDarkenAmount = 0.55f;

        private ParticleSystem _smokeFX;
        private bool _venting;
        private float _puffPhase;

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

            // Scan adjacent engines, keeping the strongest exhaust source as the
            // profile anchor while aggregating module modifiers across all of them.
            _venting = false;
            GridMaritimeEngine anchor = null;
            float maxExhaust = 0f;
            float smokeSpeedMul = 1f;
            bool anyDirty = false;
            bool anyCritical = false;

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
                    if (nb is not GridMaritimeEngine eng || !eng.IsRunning || eng.ExhaustGas <= 0.5f)
                        continue;

                    _venting = true;
                    if (eng.ExhaustGas > maxExhaust) { maxExhaust = eng.ExhaustGas; anchor = eng; }
                    smokeSpeedMul = Mathf.Max(smokeSpeedMul, eng.SmokeSpeedMultiplier);
                    anyDirty |= eng.SmokeDirty;
                    anyCritical |= eng.IsCriticalHeat;
                }
            }

            var emission = _smokeFX.emission;
            if (!_venting || anchor == null)
            {
                emission.rateOverTime = 0f;
                return;
            }

            var main = _smokeFX.main;
            float intensity = Mathf.Clamp01(maxExhaust / 50f);

            // ── Tier profile ────────────────────────────────────────
            Color baseColor;
            float rate = smokeRate * intensity;
            switch (anchor.tier)
            {
                case EngineTier.Medium:
                    // HFO V8 — steady, thick dark-grey column.
                    baseColor = hfoColumnSmoke;
                    rate *= 1.15f;
                    main.startLifetime = 3.4f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
                    break;

                case EngineTier.Giant:
                    // MGO V12 — clean, lightly visible blueish-white fast stream.
                    baseColor = mgoStreamSmoke;
                    rate *= 1.35f;
                    main.startLifetime = 1.4f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.5f);
                    break;

                default:
                    // Crude Inline-4 — pulsating puffs synced to engine RPM.
                    // A 4-stroke inline-4 fires twice per revolution; phase the
                    // emission pulse off that so the puffs visibly track throttle.
                    baseColor = crudePuffSmoke;
                    float puffsPerSecond = Mathf.Max(0.5f, anchor.CurrentRPM / 30f);
                    _puffPhase = (_puffPhase + Time.deltaTime * puffsPerSecond) % 1f;
                    float pulse = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sin(_puffPhase * Mathf.PI * 2f) * 2.2f));
                    rate *= Mathf.Lerp(0.15f, 1.6f, pulse);
                    main.startLifetime = 2.2f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
                    break;
            }

            // ── Module / fault modifiers ────────────────────────────
            float speed = smokeSpeedMul;
            if (anyDirty)
            {
                // Overclocked Fuel Injectors — dark, sooty exhaust.
                baseColor = Color.Lerp(baseColor, new Color(0.05f, 0.045f, 0.04f, Mathf.Min(0.85f, baseColor.a + 0.25f)), dirtyDarkenAmount);
            }
            if (anyCritical)
            {
                // Critical heat — mechanical failure belches heavy black smoke.
                baseColor = criticalSmoke;
                rate *= criticalRateMultiplier;
                speed = Mathf.Max(speed * 0.6f, 0.7f); // sluggish, oily roll-off
            }

            // High-Flow Turbochargers — higher exhaust velocity.
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * speed, 3f * speed);
            var velOverLife = _smokeFX.velocityOverLifetime;
            velOverLife.y = new ParticleSystem.MinMaxCurve(1f * speed, 2.5f * speed);

            emission.rateOverTime = rate;
            main.startColor = baseColor;
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
