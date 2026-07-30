// Assets/Scripts/VoxelEngine/Combat/ExplosiveBlock.cs
//
// A placeable high-yield explosive (Powder Keg / TNT). Fuses after placement and
// detonates in a big mushroom-cloud blast (delegating to the centralized Explosion:
// creature/player/block damage + a voxel crater + camera shake + particle VFX).
// Also detonates immediately if destroyed by weapon fire or caught in another blast
// (chain reactions). Extends Damageable so weapons / explosions trigger it.

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

        private bool _detonated;

        protected override void Awake()
        {
            maxHealth = Mathf.Max(maxHealth, 5f);   // fragile — a hit sets it off
            base.Awake();
        }

        private void Update()
        {
            fuse -= Time.deltaTime;
            if (fuse <= 0f) Detonate();
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
