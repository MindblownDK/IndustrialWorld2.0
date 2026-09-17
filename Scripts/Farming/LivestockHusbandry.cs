// Assets/Scripts/VoxelEngine/Farming/LivestockHusbandry.cs
//
// HUSBANDRY — the needs, production and breeding layer that turns an animal into stock.
//
// WHY THIS IS A COMPONENT AND NOT A NEW ANIMAL CLASS
// `PassiveAnimal` already exists and already does cows, sheep and pigs properly: wander
// and flee AI, correct tangent-plane movement on a spherical world, health, and drops
// on death. Writing a second animal class would have duplicated all of that and left
// two things to keep in step forever.
//
// So husbandry is ADDITIVE. Attach it to an existing animal and that animal becomes
// farmable; leave it off and the animal behaves exactly as it does today. Wild herds
// and farmed herds are then the same object with different components, which also
// means a player can tame what they find rather than only what they bought.
//
// WHY LIVESTOCK EARNS ITS KEEP NEXT TO HUNTING
// Hunting is extractive: kill once, walk further next time. Husbandry is renewable, and
// crucially it produces wool and milk WITHOUT death. Killing the animal ends the income,
// so the mechanic itself pushes the player toward keeping animals alive - which is the
// behaviour the feature exists to create, and it needs no rule to enforce it.
//
// NEEDS ARE A CHAIN, NOT A TIMER
// Hunger and thirst drive HEALTH; health gates PRODUCTION; health and maturity gate
// BREEDING. A neglected pen therefore degrades visibly - it stops earning well before
// anything dies - which gives the player time to notice and a reason to care. A plain
// "feed every N minutes or it dies" timer gives neither.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Fauna;

namespace VoxelEngine.Farming
{
    /// <summary>What an animal yields without being killed.</summary>
    public enum LivestockProduct
    {
        None = 0,
        Milk = 1,
        Wool = 2,
    }

    [DisallowMultipleComponent, RequireComponent(typeof(PassiveAnimal))]
    public class LivestockHusbandry : MonoBehaviour
    {
        // ════════════════════════════════════════════════════════════════
        //  AUTHORED
        // ════════════════════════════════════════════════════════════════

        [Header("Needs")]
        [Tooltip("Hunger lost per second. Full to empty is roughly fifteen minutes.")]
        public float hungerDrainPerSecond = 0.11f;

        [Tooltip("Thirst lost per second. Slightly faster than hunger, so water is the " +
                 "need the player learns to watch first.")]
        public float thirstDrainPerSecond = 0.14f;

        [Header("Production")]
        [Tooltip("Seconds between harvestable products while healthy and fed.")]
        public float secondsPerProduct = 240f;

        [Header("Breeding")]
        public float maturitySeconds = 300f;
        public float breedingCooldownSeconds = 420f;

        // ════════════════════════════════════════════════════════════════
        //  STATE
        // ════════════════════════════════════════════════════════════════

        public float Hunger { get; private set; } = 100f;
        public float Thirst { get; private set; } = 100f;
        public float Age { get; private set; }

        public bool IsAdult => Age >= maturitySeconds;
        public bool HasProduct { get; private set; }

        /// <summary>Set by the pen. Halves consumption and speeds production.</summary>
        public bool Sheltered { get; set; }

        /// <summary>The pen that currently manages this animal.</summary>
        public LivestockPen Pen { get; set; }

        public PassiveAnimal Animal { get; private set; }
        public bool IsAlive => Animal != null && Animal.IsAlive;

        private float _productTimer;
        private float _breedTimer;

        // ── Registry ─────────────────────────────────────────────────────────────
        private static readonly List<LivestockHusbandry> s_all = new();
        public static IReadOnlyList<LivestockHusbandry> All => s_all;

        private void Awake()
        {
            Animal = GetComponent<PassiveAnimal>();

            // Staggered so a herd born together does not produce in lockstep, which would
            // turn a steady trickle into a periodic flood.
            _productTimer = Random.Range(0f, secondsPerProduct);
        }

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }

        private void OnDisable()
        {
            s_all.Remove(this);
            Pen = null;
        }

        // ════════════════════════════════════════════════════════════════
        //  SIMULATION
        // ════════════════════════════════════════════════════════════════

        private void Update()
        {
            if (!IsAlive) return;

            float dt = Time.deltaTime;
            Age += dt;
            if (_breedTimer > 0f) _breedTimer -= dt;

            // Shelter halving consumption is the entire mechanical argument for building a
            // barn instead of leaving animals in an open field.
            float shelter = Sheltered ? 0.5f : 1f;

            Hunger = Mathf.Max(0f, Hunger - hungerDrainPerSecond * shelter * dt);
            Thirst = Mathf.Max(0f, Thirst - thirstDrainPerSecond * shelter * dt);

            TickHealth(dt);
            TickProduction(dt);
        }

        private void TickHealth(float dt)
        {
            bool starving = Hunger <= 0f;
            bool parched = Thirst <= 0f;

            if (starving || parched)
            {
                // Deliberately slow. A player who logs off with a half-full trough should
                // return to unhappy animals, not a pen of corpses.
                float dps = (starving ? 0.6f : 0f) + (parched ? 0.9f : 0f);
                // Attrition, not an attack: this must not trigger the flee reflex.
                Animal.ApplyAttritionDamage(dps * dt);
            }
        }

        private void TickProduction(float dt)
        {
            if (HasProduct || Product == LivestockProduct.None) return;

            // Production stops FIRST when an animal is unwell, so neglect shows up as lost
            // income long before it shows up as a death.
            if (!IsAdult || Hunger < 25f || Thirst < 25f) return;
            if (Animal.Health < Animal.maxHealth * 0.5f) return;

            _productTimer += dt * (Sheltered ? 1.25f : 1f);
            if (_productTimer < secondsPerProduct) return;

            _productTimer = 0f;
            HasProduct = true;
        }

        // ════════════════════════════════════════════════════════════════
        //  INTERACTION
        // ════════════════════════════════════════════════════════════════

        public void Feed(float amount) => Hunger = Mathf.Clamp(Hunger + amount, 0f, 100f);
        public void Water(float amount) => Thirst = Mathf.Clamp(Thirst + amount, 0f, 100f);

        /// <summary>Takes the ready product without harming the animal.</summary>
        public bool TryHarvest()
        {
            if (!HasProduct || Product == LivestockProduct.None) return false;
            HasProduct = false;
            return true;
        }

        /// <summary>
        /// Gives a harvested product back, for when the collector could not actually store
        /// it. A full or weight-capped output must cost the player time, never produce.
        /// </summary>
        public void RestoreProduct() => HasProduct = true;

        public bool CanBreed(out string reason)
        {
            if (!IsAlive) { reason = "Dead"; return false; }
            if (!IsAdult) { reason = "Too young"; return false; }
            if (_breedTimer > 0f) { reason = $"Resting ({_breedTimer:0}s)"; return false; }
            if (Hunger < 60f || Thirst < 60f) { reason = "Underfed"; return false; }
            if (Animal.Health < Animal.maxHealth * 0.8f) { reason = "Unhealthy"; return false; }
            reason = "";
            return true;
        }

        /// <summary>Starts the post-birth cooldown and bills the parents for it.</summary>
        public void NoteBred()
        {
            _breedTimer = breedingCooldownSeconds;
            // Breeding costs condition, so a pen cannot be farmed for endless offspring
            // unless the player keeps feeding it.
            Hunger = Mathf.Max(0f, Hunger - 30f);
            Thirst = Mathf.Max(0f, Thirst - 30f);
        }

        /// <summary>Marks a newborn as young so it cannot immediately breed again.</summary>
        public void MarkNewborn()
        {
            Age = 0f;
            _breedTimer = 0f;
            _productTimer = 0f;
            HasProduct = false;
            Hunger = 80f;
            Thirst = 80f;
        }

        // ════════════════════════════════════════════════════════════════
        //  METADATA
        // ════════════════════════════════════════════════════════════════

        public AnimalSpecies Species => Animal != null ? Animal.species : AnimalSpecies.Cow;

        public LivestockProduct Product => ProductOf(Species);

        public static LivestockProduct ProductOf(AnimalSpecies species) => species switch
        {
            AnimalSpecies.Cow => LivestockProduct.Milk,
            AnimalSpecies.Sheep => LivestockProduct.Wool,
            // Pigs and horses yield nothing renewable; pigs are meat and hide, horses ride.
            _ => LivestockProduct.None,
        };

        public string SpeciesLabel => Species.ToString();

        /// <summary>One-line condition summary for the pen roster.</summary>
        public string ConditionLabel
        {
            get
            {
                if (!IsAlive) return "Dead";
                if (Hunger <= 0f || Thirst <= 0f) return "Starving";
                if (Hunger < 25f || Thirst < 25f) return "Hungry";
                if (!IsAdult) return $"Young ({Mathf.Max(0f, maturitySeconds - Age):0}s)";
                if (HasProduct) return "Ready to harvest";
                return "Healthy";
            }
        }

        public float Health01 => Animal != null && Animal.maxHealth > 0f
            ? Mathf.Clamp01(Animal.Health / Animal.maxHealth)
            : 0f;
    }
}
