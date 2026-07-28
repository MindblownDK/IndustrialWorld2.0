// Assets/Scripts/VoxelEngine/Combat/Damageable.cs
//
// Generic health component for combat targets. Implements IDamageable, tracks HP,
// flashes/spawns feedback on hit, and drops loot on death. Enemies, dummies, and
// future damageable grid blocks all derive from or reuse this.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    public class Damageable : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        public float maxHealth = 50f;
        public float Health { get; protected set; }
        public virtual bool IsAlive => Health > 0f;

        [Header("Loot (on death)")]
        public ItemDefinition[] drops;
        [Tooltip("Min/max random drops rolled on death.")]
        public int minDrops = 0;
        public int maxDrops = 2;
        [Tooltip("Stack size per dropped item.")]
        public int dropCount = 1;

        [Header("Feedback")]
        public Color hitColor = new Color(1f, 0.35f, 0.25f);
        public bool showHitFeedback = true;

        protected virtual void Awake()
        {
            Health = maxHealth;
        }

        public virtual void TakeDamage(DamageEvent e)
        {
            if (!IsAlive) return;
            Health -= e.amount;
            OnHit(e);
            if (Health <= 0f)
            {
                Health = 0f;
                Die(e);
            }
        }

        protected virtual void OnHit(DamageEvent e)
        {
            if (showHitFeedback)
                VoxelEngine.UI.BuildFeedbackHud.Show("Hit", Mathf.RoundToInt(e.amount) + " dmg", null, hitColor);
        }

        protected virtual void Die(DamageEvent e)
        {
            RollDrops();
            Destroy(gameObject);
        }

        protected void RollDrops()
        {
            if (drops == null || drops.Length == 0) return;
            int n = Random.Range(minDrops, maxDrops + 1);
            for (int i = 0; i < n; i++)
            {
                var item = drops[Random.Range(0, drops.Length)];
                if (item != null)
                    DroppedItem.Spawn(new ItemStack(item, Mathf.Max(1, dropCount)),
                        transform.position + Vector3.up * 0.5f, Vector3.up);
            }
        }
    }
}
