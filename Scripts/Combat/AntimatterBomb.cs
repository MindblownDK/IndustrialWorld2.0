// Assets/Scripts/VoxelEngine/Combat/AntimatterBomb.cs
//
// The ultimate explosive. Instead of an instant blast it runs a "star-death" sequence:
// a core sphere EXPANDS slowly (the doomed star swelling), then CONTRACTS fast (the
// collapse), then a blinding WHITE GLOW at a tiny point, then a MASSIVE detonation.
// Fuses after placement like the other bombs; can be triggered early by damage (chain).
// Fuse honours ExplosiveBlock.NextFuse (the bomb-fuse slider).

using UnityEngine;

namespace VoxelEngine.Combat
{
    public class AntimatterBomb : Damageable
    {
        [Header("Antimatter Bomb")]
        public float fuse = 8f;
        public float explosionRadius   = 80f;     // MASSIVE
        public float explosionDamage   = 30000f;  // 40× a Tsar's punch
        public float voxelDamageRadius = 10f;
        public Material coreMaterial;        // the expanding/collapsing sphere
        public Material explosionMaterial;   // the final blast VFX tint

        private bool _triggered;
        private enum Phase { Idle, Expand, Contract, Glow }
        private Phase _phase = Phase.Idle;
        private float _pt;
        private GameObject _core;
        private Light _coreLight;

        private const float ExpandDur   = 2.2f;   // slow swell
        private const float ContractDur = 0.5f;   // fast collapse
        private const float GlowDur     = 0.35f;  // blinding point

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 5f);
            base.Awake();
            if (ExplosiveBlock.NextFuse > 0f) fuse = ExplosiveBlock.NextFuse;
        }

        // No misleading "Hit dmg" feedback.
        protected override void OnHit(DamageEvent e) { }

        private void Update()
        {
            if (!_triggered)
            {
                fuse -= Time.deltaTime;
                if (fuse <= 0f) Trigger();
            }
            else RunSequence(Time.deltaTime);
        }

        protected override void Die(DamageEvent e) => Trigger();

        private void Trigger()
        {
            if (_triggered) return;
            _triggered = true;

            _core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(_core.GetComponent<Collider>());
            _core.transform.SetParent(transform, false);
            _core.transform.localPosition = Vector3.up * 0.5f;
            _core.transform.localScale = Vector3.zero;
            if (coreMaterial != null) _core.GetComponent<Renderer>().sharedMaterial = coreMaterial;

            _coreLight = _core.AddComponent<Light>();
            _coreLight.type = LightType.Point;
            _coreLight.color = new Color(0.60f, 0.45f, 1.0f);   // containment violet
            _coreLight.range = 20f;
            _coreLight.intensity = 4f;

            _phase = Phase.Expand; _pt = 0f;
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Antimatter", "Containment failing...", null, new Color(0.7f, 0.4f, 1f));
        }

        private void RunSequence(float dt)
        {
            _pt += dt;
            float maxR = Mathf.Max(1f, explosionRadius);

            switch (_phase)
            {
                case Phase.Expand:
                {
                    float k = Mathf.Clamp01(_pt / ExpandDur);
                    float e = 1f - (1f - k) * (1f - k);             // ease-out: slow then settling
                    _core.transform.localScale = Vector3.one * (maxR * e);
                    _coreLight.intensity = Mathf.Lerp(4f, 14f, k);
                    _coreLight.range = Mathf.Lerp(20f, maxR * 2f, k);
                    if (_pt >= ExpandDur) { _phase = Phase.Contract; _pt = 0f; }
                    break;
                }
                case Phase.Contract:
                {
                    float k = Mathf.Clamp01(_pt / ContractDur);
                    float e = k * k;                                 // ease-in: accelerates inward
                    _core.transform.localScale = Vector3.one * Mathf.Lerp(maxR, 1f, e);
                    _coreLight.intensity = Mathf.Lerp(14f, 40f, k);
                    if (_pt >= ContractDur)
                    {
                        _phase = Phase.Glow; _pt = 0f;
                        _core.GetComponent<Renderer>().sharedMaterial = WhiteMat();
                        _coreLight.color = Color.white;
                    }
                    break;
                }
                case Phase.Glow:
                {
                    float k = Mathf.Clamp01(_pt / GlowDur);
                    _coreLight.intensity = Mathf.Lerp(80f, 400f, k);
                    _core.transform.localScale = Vector3.one * (1f + k * 2.5f);
                    if (_pt >= GlowDur) DetonateFinal();
                    break;
                }
            }
        }

        private static Material _whiteMat;
        private static Material WhiteMat()
        {
            if (_whiteMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                _whiteMat = new Material(sh) { color = Color.white };
                if (_whiteMat.HasProperty("_BaseColor")) _whiteMat.SetColor("_BaseColor", Color.white);
            }
            return _whiteMat;
        }

        private void DetonateFinal()
        {
            // MASSIVE blast (damage + voxel crater + shake + mushroom VFX tinted by explosionMaterial).
            Explosion.Detonate(transform.position, explosionRadius, explosionDamage, gameObject, voxelDamageRadius, explosionMaterial);

            // Blinding white flash sphere + a colossal light pop.
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(flash.GetComponent<Collider>());
            flash.transform.position = transform.position;
            flash.transform.localScale = Vector3.one * explosionRadius;
            flash.GetComponent<Renderer>().sharedMaterial = WhiteMat();
            Object.Destroy(flash, 0.25f);

            var blastLight = new GameObject("AntimatterBlastLight");
            blastLight.transform.position = transform.position;
            var l = blastLight.AddComponent<Light>();
            l.type = LightType.Point; l.color = Color.white; l.range = explosionRadius * 3f; l.intensity = 1200f;
            Object.Destroy(blastLight, 0.4f);

            Object.Destroy(gameObject);
        }
    }
}
