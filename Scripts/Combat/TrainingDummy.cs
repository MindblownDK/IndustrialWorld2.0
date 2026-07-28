// Assets/Scripts/VoxelEngine/Combat/TrainingDummy.cs
//
// Placeable combat target for testing weapons. Takes damage, squashes when
// defeated, and respawns after a few seconds so it can be hit again. A safe
// first IDamageable for the player to swing/shoot at before real enemies exist.

using UnityEngine;

namespace VoxelEngine.Combat
{
    public class TrainingDummy : Damageable
    {
        [Tooltip("Seconds after defeat before the dummy resets.")]
        public float respawnSeconds = 6f;

        private bool _down;
        private float _respawnAt;
        private Quaternion _startRot;

        protected override void Awake()
        {
            // Dummies are sturdier than the default Damageable.
            maxHealth = Mathf.Max(maxHealth, 100f);
            base.Awake();
            _startRot = transform.localRotation;
        }

        protected override void OnHit(DamageEvent e)
        {
            // Clearer feedback so the player can read their damage numbers.
            VoxelEngine.UI.BuildFeedbackHud.Show("Dummy", Mathf.RoundToInt(e.amount) + " dmg", null,
                new Color(1f, 0.55f, 0.2f));
        }

        protected override void Die(DamageEvent e)
        {
            // Don't destroy — go down, then respawn so the dummy is reusable.
            _down = true;
            _respawnAt = Time.time + respawnSeconds;
            Health = 0f;
            transform.localRotation = _startRot * Quaternion.Euler(85f, 0f, 0f); // knocked down (falls forward)
        }

        private void Update()
        {
            if (_down && Time.time >= _respawnAt)
            {
                _down = false;
                Health = maxHealth;
                transform.localRotation = _startRot; // stand back up
            }
        }
    }
}
