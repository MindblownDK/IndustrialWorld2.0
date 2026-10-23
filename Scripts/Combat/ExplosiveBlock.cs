// Assets/Scripts/VoxelEngine/Combat/ExplosiveBlock.cs
//
// A placeable high-yield explosive (Powder Keg / TNT / Tsar). Fuses after placement
// and detonates in a big blast via the centralized Explosion (creature/player/block
// damage + a voxel crater + camera shake + particle VFX). Detonates immediately if
// destroyed by weapon fire or caught in another blast (chain reactions). The fuse is
// chosen per-placement on the build wheel (17.45.0-dev) through the static NextFuse -
// including MANUAL, which arms the charge with no countdown at all so the player
// decides the moment - and a pulsing point-light glows faster + redder as it runs out.

using UnityEngine;

namespace VoxelEngine.Combat
{
    public class ExplosiveBlock : Damageable
    {
        [Header("Powder Keg")]
        [Tooltip("Seconds after placement before it detonates on its own.")]
        public float fuse = 5f;
        public float explosionRadius   = 12f;
        public float explosionDamage   = 250f;
        public float voxelDamageRadius = 4f;
        public Material explosionMaterial;

        [Header("Fuse Indicator")]
        public Color safeColor    = new Color(0.40f, 0.90f, 0.30f);
        public Color dangerColor  = new Color(1.00f, 0.20f, 0.10f);
        /// <summary>Colour of a manually armed charge: live, but not counting down.</summary>
        public Color armedColor   = new Color(1.00f, 0.62f, 0.18f);

        /// <summary>Sentinel in <see cref="NextFuse"/>: arm the charge with no countdown.</summary>
        public const float ManualFuse = 0f;

        /// <summary>Fuse (seconds) applied to the NEXT placed explosive, chosen on the build
        /// wheel. &lt;0 = use the prefab default, 0 = manual, &gt;0 = that many seconds.</summary>
        public static float NextFuse = -1f;

        private bool _detonated;
        private Light _glow;
        private float _initialFuse;
        /// <summary>Armed with no countdown. It still detonates the instant it takes damage.</summary>
        public bool manualFuse;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 5f);   // fragile — a hit sets it off
            base.Awake();
            if (NextFuse == ManualFuse) { manualFuse = true; fuse = float.PositiveInfinity; }
            else if (NextFuse > 0f) fuse = NextFuse;
            _initialFuse = Mathf.Max(0.1f, fuse);

            _glow = gameObject.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.range = 4f;
            _glow.intensity = 1.5f;
            _glow.color = safeColor;
        }

        // Suppress the misleading "Hit X dmg" feedback when the keg takes explosion/chain damage.
        protected override void OnHit(DamageEvent e) { }

        private void Update()
        {
            if (manualFuse)
            {
                // A manually armed charge is live but never counts down: a slow, steady
                // amber pulse says "armed" without pretending to know when.
                if (_glow != null)
                {
                    _glow.color = armedColor;
                    _glow.intensity = 0.9f + 0.35f * Mathf.Sin(Time.time * 2.2f);
                    _glow.range = 5f;
                }
                return;
            }

            fuse -= Time.deltaTime;
            if (fuse <= 0f) { Detonate(); return; }

            // Pulsing glow countdown: blinks faster + shifts green→red as the fuse runs out.
            if (_glow != null)
            {
                float frac = Mathf.Clamp01(fuse / _initialFuse);          // 1 = just placed → 0 = about to blow
                float speed = Mathf.Lerp(22f, 4f, frac);                  // fast when about to blow, slow when safe
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * speed);
                _glow.color = Color.Lerp(dangerColor, safeColor, frac);
                _glow.intensity = (0.5f + 2.5f * pulse) * (0.6f + 0.4f * (1f - frac));
                _glow.range = Mathf.Lerp(3.5f, 8f, 1f - frac);
            }
        }

        // Weapon fire / chain explosions that destroy it set it off instantly.
        protected override void Die(DamageEvent e) => Detonate();

        /// <summary>Blows the charge now - used by the manual fuse and by chain reactions.</summary>
        public void Detonate()
        {
            if (_detonated) return;
            _detonated = true;
            Explosion.Detonate(transform.position, explosionRadius, explosionDamage,
                               gameObject, voxelDamageRadius, explosionMaterial);
            Destroy(gameObject);
        }
    }
}
