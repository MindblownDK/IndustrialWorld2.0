// Assets/Scripts/VoxelEngine/Combat/BossRelic.cs
//
// BOSS RELIC CORES — the thing that makes killing a named boss matter.
//
// The roadmap asks for three linked properties: enemy TIER determines loot tier, named
// bosses guarantee a unique Boss Relic Core, and relics are REQUIRED for selected
// late-game items and research including the Star Builder and Dyson Sphere.
//
// WHY A RELIC IS NOT JUST A RARE DROP
// A rare drop is a lottery ticket: kill things until the number comes up. That trains
// grinding, and grinding is exactly what the roadmap's own rule forbids - "low-tier
// farming cannot replace boss progression". A relic is therefore:
//
//   • GUARANTEED from its boss, so the encounter is the cost, not the RNG.
//   • UNIQUE to that boss, so you cannot substitute an easier one.
//   • CONSUMED BY RESEARCH rather than by crafting, so it gates a branch of the tech
//     tree permanently instead of being farmed for volume.
//
// Together those mean the only way to reach the late game is to actually beat the named
// encounters, which is the progression shape the roadmap is describing.
//
// WHY THE GATE LIVES HERE AND NOT IN ScienceCost
// `ResearchNode.ScienceCost` is typed to `ScienceItem`, and widening it would touch
// every research node in the project. A relic requirement is also not really a cost -
// it is a FACILITY-style prerequisite, the same shape as `requiresOrbitalLab`. So it
// reuses that proven gate and plugs into `GetFacilityBlockReason`, which both research
// entry points already funnel through.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    /// <summary>
    /// Which named boss a relic came from. Appended, never reordered: saves and authored
    /// assets store this as an int.
    /// </summary>
    public enum BossRelicKind
    {
        None = 0,
        /// <summary>Basilisk - petrification encounter.</summary>
        PetrifiedCore = 1,
        /// <summary>Ifrit Djinn - fire and teleportation.</summary>
        EmberCore = 2,
        /// <summary>Griffin / Roc - aerial encounter.</summary>
        SkyCore = 3,
        /// <summary>Karkadann / Manticore - brute encounter.</summary>
        BruteCore = 4,
        /// <summary>Leviathan - maritime encounter.</summary>
        AbyssCore = 5,
    }

    /// <summary>
    /// How dangerous an enemy is. Drives which loot table it rolls, so a low-tier enemy
    /// can never drop high-tier loot no matter how many are killed.
    /// </summary>
    public enum EnemyTier
    {
        Common = 0,
        Elite = 1,
        Boss = 2,
    }

    /// <summary>
    /// Tracks which boss relics the player has ever obtained.
    ///
    /// Deliberately a PERMANENT record rather than an inventory check. A relic is spent
    /// by the research that needs it, but the achievement of having beaten that boss is
    /// not undone by spending it - otherwise a player who researched one node would be
    /// locked out of a second node needing the same relic, and would have to re-kill a
    /// unique boss that may not respawn.
    /// </summary>
    public static class BossRelicLedger
    {
        private static readonly HashSet<BossRelicKind> _claimed = new();

        /// <summary>Every relic the player has ever collected.</summary>
        public static IReadOnlyCollection<BossRelicKind> Claimed => _claimed;

        public static bool Has(BossRelicKind kind)
            => kind == BossRelicKind.None || _claimed.Contains(kind);

        /// <summary>Records a relic. Returns true when it was new.</summary>
        public static bool Claim(BossRelicKind kind)
        {
            if (kind == BossRelicKind.None) return false;
            if (!_claimed.Add(kind)) return false;

            Debug.Log($"[Relic] Claimed {Label(kind)}.");
            OnChanged?.Invoke();
            return true;
        }

        public static void LoadFrom(IEnumerable<int> kinds)
        {
            _claimed.Clear();
            if (kinds == null) return;
            foreach (int raw in kinds)
            {
                if (!System.Enum.IsDefined(typeof(BossRelicKind), raw)) continue;
                var kind = (BossRelicKind)raw;
                if (kind != BossRelicKind.None) _claimed.Add(kind);
            }
            OnChanged?.Invoke();
        }

        public static List<int> SaveTo()
        {
            var list = new List<int>(_claimed.Count);
            foreach (var kind in _claimed) list.Add((int)kind);
            return list;
        }

        public static void Clear()
        {
            _claimed.Clear();
            OnChanged?.Invoke();
        }

        /// <summary>Raised when a relic is claimed, so UI can refresh.</summary>
        public static event System.Action OnChanged;

        public static string Label(BossRelicKind kind) => kind switch
        {
            BossRelicKind.PetrifiedCore => "Petrified Core",
            BossRelicKind.EmberCore => "Ember Core",
            BossRelicKind.SkyCore => "Sky Core",
            BossRelicKind.BruteCore => "Brute Core",
            BossRelicKind.AbyssCore => "Abyss Core",
            _ => "",
        };

        /// <summary>Which boss yields this relic, so a locked node can say where to go.</summary>
        public static string SourceLabel(BossRelicKind kind) => kind switch
        {
            BossRelicKind.PetrifiedCore => "the Basilisk",
            BossRelicKind.EmberCore => "an Ifrit Djinn",
            BossRelicKind.SkyCore => "a Roc",
            BossRelicKind.BruteCore => "a Karkadann",
            BossRelicKind.AbyssCore => "the Leviathan",
            _ => "a named boss",
        };

        /// <summary>Item id a relic's carried item uses, so setup and runtime agree.</summary>
        public static string ItemIdFor(BossRelicKind kind) => kind switch
        {
            BossRelicKind.PetrifiedCore => "relic_petrified_core",
            BossRelicKind.EmberCore => "relic_ember_core",
            BossRelicKind.SkyCore => "relic_sky_core",
            BossRelicKind.BruteCore => "relic_brute_core",
            BossRelicKind.AbyssCore => "relic_abyss_core",
            _ => "",
        };
    }

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
