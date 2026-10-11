// Assets/Scripts/VoxelEngine/Combat/BossEncounter.cs
//
// Additive marker for named boss variants. Kept in a same-named MonoBehaviour file so
// Unity can serialize the component and reconnect existing boss prefab references.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    /// <summary>
    /// Marks an enemy as a named boss that guarantees a relic.
    ///
    /// Attached alongside the existing enemy behaviour rather than replacing it, the same
    /// additive approach livestock husbandry used: the creature keeps its authored AI,
    /// health and ordinary drops, and simply also yields a relic when killed.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Damageable))]
    public class BossEncounter : MonoBehaviour
    {
        [Header("Identity")]
        public EnemyTier tier = EnemyTier.Boss;

        [Tooltip("Relic guaranteed by this encounter. Never rolled - beating the boss IS " +
                 "the cost, so the drop must not also be a lottery.")]
        public BossRelicKind relic = BossRelicKind.None;

        [Tooltip("Health multiplier applied on top of the creature's authored value, so a " +
                 "boss variant is meaningfully harder than the common version.")]
        public float healthMultiplier = 4f;

        [Tooltip("Optional carried relic item, spawned on death. Assigned by setup.")]
        public ItemDefinition relicItem;

        private Damageable _damageable;
        private bool _granted;

        private void Awake()
        {
            _damageable = GetComponent<Damageable>();
            if (_damageable == null) return;

            // Scale health at spawn rather than authoring a separate prefab per boss:
            // one prefab with a multiplier keeps the boss and its common version from
            // drifting apart every time the base creature is retuned.
            if (healthMultiplier > 1f)
                _damageable.maxHealth *= healthMultiplier;
        }

        /// <summary>
        /// Granting happens in OnDestroy, NOT in Update.
        ///
        /// `Damageable.Die` calls `Destroy(gameObject)` in the same frame health reaches
        /// zero, so a polling check in Update can miss the death entirely - the object is
        /// already gone before the next tick. OnDestroy is the only hook guaranteed to
        /// run, and it runs exactly once.
        /// </summary>
        private void OnDestroy()
        {
            if (_granted || _damageable == null) return;

            // Only a death counts. A scene unload, a chunk despawn or exiting play mode
            // also destroy the object, and none of those should hand out a relic.
            if (_damageable.IsAlive) return;
            if (!Application.isPlaying) return;

            _granted = true;

            // The relic is recorded the moment the boss dies, independent of whether the
            // player picks the item up. A unique boss that may never respawn must not be
            // able to lose its reward to a corpse that fell through the terrain.
            BossRelicLedger.Claim(relic);

            if (relicItem != null)
            {
                DroppedItem.Spawn(new ItemStack(relicItem, 1),
                    transform.position + Vector3.up * 0.8f, Vector3.up);
            }
        }
    }
}
