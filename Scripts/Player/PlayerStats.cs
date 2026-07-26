// Assets/Scripts/VoxelEngine/Player/PlayerStats.cs
//
// Central player stats: HP, stamina, damage bonus, sprint multiplier, inventory size.
// Listens to ResearchManager to recompute when player upgrades unlock.

using System;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.Player
{
    public class PlayerStats : MonoBehaviour
    {
        public static PlayerStats Instance { get; private set; }

        [Header("Baselines (before upgrades)")]
        public float baseMaxHealth      = 100f;
        public float baseMaxStamina     = 100f;
        public float baseDamage         = 5f;     // bare-hand contribution
        public float baseSprintMultiplier = 1.6f;
        public float baseMaxHunger      = 100f;
        public float baseMaxOxygen      = 100f;
        public int   baseBackpackSlots  = 30;     // matches Inventory.BACKPACK_SIZE

        [Header("Stamina")]
        public float staminaSprintDrain = 18f;    // per second while sprinting
        public float staminaRegen       = 12f;    // per second otherwise
        public float staminaJumpCost    = 12f;

        public float MaxHealth    { get; private set; }
        public float Health       { get; private set; }
        public float MaxStamina   { get; private set; }
        public float Stamina      { get; private set; }
        public float DamageBonus  { get; private set; }
        public float SprintMultiplier { get; private set; }
        public int   BackpackSlots    { get; private set; }
        public bool  HasFlightUnlocked{ get; private set; }
        public float MaxHunger   { get; private set; }
        public float Hunger      { get; private set; }
        public float MaxOxygen   { get; private set; }
        public float Oxygen      { get; private set; }

        public event Action OnStatsChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Recalculate();
            Health  = MaxHealth;
            Stamina = MaxStamina;
            MaxHunger = baseMaxHunger;
            Hunger = MaxHunger;
            MaxOxygen = baseMaxOxygen;
            Oxygen = MaxOxygen;
        }

        private void OnEnable()
        {
            if (ResearchManager.Instance != null)
                ResearchManager.Instance.OnChanged += Recalculate;
        }
        private void OnDisable()
        {
            if (ResearchManager.Instance != null)
                ResearchManager.Instance.OnChanged -= Recalculate;
        }

        private void Update()
        {
            // Out-of-combat stamina regen handled by player controller via TickStamina(...) hook.
            // (Left here so we never have a frame where Stamina goes negative.)
            if (Stamina < 0f) Stamina = 0f;
            // Hunger drains slowly over time.
            Hunger -= Time.deltaTime * 0.08f; // ~20 min to go from 100 to 0
            if (Hunger < 0f) { Hunger = 0f; TakeDamage(Time.deltaTime * 2f); }

            // Oxygen: drains when head underwater. A sealed helmet + oxygen tank
            // extends reserve and slows drain; this is the foundation for the later
            // vacuum/room-pressure life-support pass.
            var equipment = GetComponent<PlayerEquipment>();
            MaxOxygen = baseMaxOxygen + (equipment != null ? equipment.BonusOxygen : 0f);
            var ws = GetComponent<PlayerWaterState>();
            if (ws != null && ws.IsHeadUnderwater)
            {
                float drainMul = equipment != null ? equipment.OxygenDrainMultiplier : 1f;
                Oxygen -= Time.deltaTime * 5f * drainMul;
                if (Oxygen < 0f) { Oxygen = 0f; TakeDamage(Time.deltaTime * 10f); }
            }
            else
            {
                Oxygen = Mathf.Min(MaxOxygen, Oxygen + Time.deltaTime * 25f); // fast regen
            }

            if (Health > MaxHealth)   Health = MaxHealth;
            if (Stamina > MaxStamina) Stamina = MaxStamina;
            if (Hunger > MaxHunger)   Hunger = MaxHunger;
            if (Oxygen > MaxOxygen)   Oxygen = MaxOxygen;
        }

        // ============================================================
        //                       Recompute from research
        // ============================================================
        public void Recalculate()
        {
            float dmg = baseDamage;
            int   inv = baseBackpackSlots;
            float hp  = baseMaxHealth;
            float st  = baseMaxStamina;
            float spr = baseSprintMultiplier;
            bool flight = false;

            var rm = ResearchManager.Instance;
            if (rm != null && rm.tree != null)
            {
                foreach (var n in rm.tree.nodes)
                {
                    if (n == null) continue;
                    int rank = rm.GetRank(n);
                    if (rank <= 0) continue;
                    switch (n.upgradeKind)
                    {
                        case PlayerUpgradeKind.BonusMaxHealth:        hp += n.upgradePerRankAmount * rank; break;
                        case PlayerUpgradeKind.BonusInventorySlots:   inv += Mathf.RoundToInt(n.upgradePerRankAmount) * rank; break;
                        case PlayerUpgradeKind.BonusDamage:           dmg += n.upgradePerRankAmount * rank; break;
                        case PlayerUpgradeKind.BonusMaxStamina:       st += n.upgradePerRankAmount * rank; break;
                        case PlayerUpgradeKind.BonusSprintMultiplier: spr += n.upgradePerRankAmount * rank; break;
                        case PlayerUpgradeKind.UnlockFlight:          flight = true; break;
                    }
                }
            }

            MaxHealth        = hp;
            MaxHunger        = baseMaxHunger;
            MaxOxygen        = baseMaxOxygen;
            MaxStamina       = st;
            DamageBonus      = dmg;
            SprintMultiplier = Mathf.Min(5f, spr);  // hard cap as requested
            BackpackSlots    = inv;
            HasFlightUnlocked = flight;

            // Auto-grow the player's inventory to fit new slots.
            var inventory = GetComponent<Inventory>();
            if (inventory != null && inventory.container != null)
                inventory.container.Resize(Inventory.HOTBAR_SIZE + BackpackSlots);

            OnStatsChanged?.Invoke();
        }

        // ============================================================
        //                       Combat hooks
        // ============================================================
        public void TakeDamage(float amount)
        {
            if (amount <= 0) return;
            Health = Mathf.Max(0, Health - amount);
            OnStatsChanged?.Invoke();
            if (Health <= 0) Die();
        }

        public void Heal(float amount)
        {
            if (amount <= 0) return;
            Health = Mathf.Min(MaxHealth, Health + amount);
            OnStatsChanged?.Invoke();
        }

        public void Feed(float amount)
        {
            if (amount <= 0) return;
            Hunger = Mathf.Min(MaxHunger, Hunger + amount);
            OnStatsChanged?.Invoke();
        }

        private void Die()
        {
            Debug.Log("[Player] Died — respawning.");
            Health  = MaxHealth;
            Stamina = MaxStamina;
            MaxHunger = baseMaxHunger;
            Hunger = MaxHunger;
            MaxOxygen = baseMaxOxygen;
            Oxygen = MaxOxygen;
            // Delegate to the spawner; it knows about bed / world spawn.
            var spawner = GetComponent<PlayerSpawner>();
            if (spawner != null) spawner.Respawn();
            else                 transform.position = new Vector3(0, 250, 0);
            OnStatsChanged?.Invoke();
        }

        // ============================================================
        //                       Stamina hooks
        // ============================================================
        public bool TrySpendStamina(float amount)
        {
            if (Stamina < amount) return false;
            Stamina -= amount;
            OnStatsChanged?.Invoke();
            return true;
        }
        public void RegenStamina(float dt) { Stamina = Mathf.Min(MaxStamina, Stamina + staminaRegen * dt); }
        public void DrainStamina(float dt) { Stamina = Mathf.Max(0,         Stamina - staminaSprintDrain * dt); }
    }
}
