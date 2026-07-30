// Assets/Scripts/VoxelEngine/Combat/ExplosiveBlock.cs
//
// A placeable high-yield explosive (Powder Keg / TNT / Tsar). Fuses after placement
// and detonates in a big blast via the centralized Explosion (creature/player/block
// damage + a voxel crater + camera shake + particle VFX). Detonates immediately if
// destroyed by weapon fire or caught in another blast (chain reactions). The fuse can
// be set per-placement via the static NextFuse (driven by the bomb-fuse slider UI),
// and a pulsing point-light glows faster + redder as the countdown runs out.

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

        /// <summary>Fuse (seconds) applied to the NEXT placed explosive (set by the bomb-fuse slider UI). &lt;=0 = use the prefab default.</summary>
        public static float NextFuse = -1f;

        private bool _detonated;
        private Light _glow;
        private float _initialFuse;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 5f);   // fragile — a hit sets it off
            base.Awake();
            if (NextFuse > 0f) fuse = NextFuse;
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

        private void Detonate()
        {
            if (_detonated) return;
            _detonated = true;
            Explosion.Detonate(transform.position, explosionRadius, explosionDamage,
                               gameObject, voxelDamageRadius, explosionMaterial);
            Destroy(gameObject);
        }
    }
}
