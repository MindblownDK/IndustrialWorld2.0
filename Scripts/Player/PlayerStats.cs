// Assets/Scripts/VoxelEngine/Player/PlayerStats.cs
//
// Central player stats: HP, hunger, oxygen, damage bonus, sprint multiplier, inventory size.
// Stamina removed from gameplay (6.74) — fields kept inert for save compatibility.
// Listens to ResearchManager to recompute when player upgrades unlock.

using System;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.Player
{
    /// <summary>Current atmosphere state used by player oxygen and life-support UI.</summary>
    public enum OxygenEnvironment
    {
        Breathable = 0,
        Underwater = 1,
        Vacuum = 2,
    }

    public class PlayerStats : MonoBehaviour
    {
        public static PlayerStats Instance { get; private set; }

        [Header("Baselines (before upgrades)")]
        public float baseMaxHealth      = 100f;
        public VoxelEngine.Combat.ArmorItem equippedArmor; // currently worn Crusader armor
        // Poison damage-over-time (venomous creatures); bypasses armor.
        private float _poisonTimer;
        private float _poisonDps;
        // Burn damage-over-time (fire creatures); escalates with heavy base armor,
        // then is mitigated by installed Heat Tolerance modules.
        private float _burnTimer;
        private float _burnDps;
        // Radiation is kept separate from poison/burn so Hazmat and Radiation
        // Shielding can protect it without changing other damage types.
        private float _radiationTimer;
        private float _radiationDps;
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
        public bool  IsDead      { get; private set; }
        public float MaxHunger   { get; private set; }
        public float Hunger      { get; private set; }
        public float MaxOxygen   { get; private set; }
        public float Oxygen      { get; private set; }
        public OxygenEnvironment CurrentOxygenEnvironment { get; private set; } = OxygenEnvironment.Breathable;
        public bool RequiresLifeSupport => CurrentOxygenEnvironment != OxygenEnvironment.Breathable;
        public bool IsVacuumExposure => CurrentOxygenEnvironment == OxygenEnvironment.Vacuum;
        public string LifeSupportStatus { get; private set; } = "BREATHABLE";

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

            // Oxygen/life support: underwater and non-breathable atmosphere both
            // consume the same reserve. A sealed helmet + oxygen tank extends that
            // reserve and slows drain; oxygen-efficiency armor upgrades apply through
            // PlayerEquipment.OxygenDrainMultiplier.
            var equipment = GetComponent<PlayerEquipment>();
            MaxOxygen = baseMaxOxygen + (equipment != null ? equipment.BonusOxygen : 0f);
            var waterState = GetComponent<PlayerWaterState>();
            CurrentOxygenEnvironment = ResolveOxygenEnvironment(waterState);
            bool oxygenBlocked = CurrentOxygenEnvironment != OxygenEnvironment.Breathable;
            bool sealed = equipment != null && equipment.HasBreathingKit;

            if (oxygenBlocked)
            {
                float baseDrain = CurrentOxygenEnvironment == OxygenEnvironment.Vacuum ? 9f : 5f;
                // An unsealed player loses breathable reserve rapidly in vacuum.
                if (CurrentOxygenEnvironment == OxygenEnvironment.Vacuum && !sealed) baseDrain *= 2f;
                float drainMul = equipment != null ? equipment.OxygenDrainMultiplier : 1f;
                Oxygen -= Time.deltaTime * baseDrain * drainMul;
                if (Oxygen <= 0f)
                {
                    Oxygen = 0f;
                    float suffocationDamage = CurrentOxygenEnvironment == OxygenEnvironment.Vacuum ? 18f : 10f;
                    ApplyOxygenFailureDamage(suffocationDamage * Time.deltaTime);
                }
            }
            else
            {
                Oxygen = Mathf.Min(MaxOxygen, Oxygen + Time.deltaTime * 25f); // fast recharge in breathable air
            }

            LifeSupportStatus = CurrentOxygenEnvironment switch
            {
                OxygenEnvironment.Underwater => sealed ? "SUBMERGED · SEALED" : "SUBMERGED · HOLDING BREATH",
                OxygenEnvironment.Vacuum => sealed ? "VACUUM · LIFE SUPPORT" : "VACUUM · NO LIFE SUPPORT",
                _ => "BREATHABLE",
            };

            // Poison: damage-over-time that bypasses armor (Manticore venom, etc.).
            if (_poisonTimer > 0f)
            {
                _poisonTimer -= Time.deltaTime;
                Health = Mathf.Max(0f, Health - _poisonDps * Time.deltaTime);
                OnStatsChanged?.Invoke();
                if (_poisonTimer <= 0f) _poisonDps = 0f;
                if (Health <= 0f) Die();
            }

            // Burn: fire DoT bypasses base physical mitigation and escalates with
            // heavy plate, while installed Heat Tolerance reduces the final heat hit.
            if (_burnTimer > 0f)
            {
                _burnTimer -= Time.deltaTime;
                float armorFactor = equippedArmor != null ? equippedArmor.damageReduction : 0f;
                float heatMultiplier = equipment != null ? equipment.HeatDamageMultiplier : 1f;
                float effective = _burnDps * (1f + armorFactor * 1.5f) * heatMultiplier;
                Health = Mathf.Max(0f, Health - effective * Time.deltaTime);
                OnStatsChanged?.Invoke();
                if (_burnTimer <= 0f) _burnDps = 0f;
                if (Health <= 0f) Die();
            }

            ApplyEnvironmentalHazards(equipment);

            if (Health > MaxHealth)   Health = MaxHealth;
            if (Stamina > MaxStamina) Stamina = MaxStamina;
            if (Hunger > MaxHunger)   Hunger = MaxHunger;
            if (Oxygen > MaxOxygen)   Oxygen = MaxOxygen;
        }

        private OxygenEnvironment ResolveOxygenEnvironment(PlayerWaterState waterState)
        {
            bool underwater = waterState != null
                && (waterState.IsHeadUnderwater || (waterState.IsSwimming && waterState.WaterDepth > 0.90f));
            if (underwater) return OxygenEnvironment.Underwater;

            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return OxygenEnvironment.Breathable;
            float density = VoxelEngine.GridSystem.AtmosphereManager.GetAirDensity(transform.position);
            bool breathable = body.settings.HasOxygen
                && density >= PlayerEquipment.AtmosphereDensityThreshold;
            return breathable ? OxygenEnvironment.Breathable : OxygenEnvironment.Vacuum;
        }

        /// <summary>Suffocation/vacuum damage bypasses physical armor mitigation.</summary>
        private void ApplyOxygenFailureDamage(float amount)
        {
            if (amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            OnStatsChanged?.Invoke();
            if (Health <= 0f) Die();
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
            if (equippedArmor != null) amount *= (1f - equippedArmor.damageReduction);
            Health = Mathf.Max(0, Health - amount);
            OnStatsChanged?.Invoke();
            if (Health <= 0) Die();
        }
        /// <summary>Apply a venom damage-over-time effect that bypasses armor. Refreshes/extends an active poison.</summary>
        public void ApplyPoison(float dps, float duration)
        {
            if (dps <= 0f || duration <= 0f) return;
            _poisonDps   = Mathf.Max(_poisonDps, dps);
            _poisonTimer = Mathf.Max(_poisonTimer, duration);
            OnStatsChanged?.Invoke();
        }
        /// <summary>Applies a time-limited radiation effect after armor mitigation.</summary>
        public void ApplyRadiation(float dps, float duration)
        {
            if (dps <= 0f || duration <= 0f) return;
            var equipment = GetComponent<PlayerEquipment>();
            float multiplier = equipment != null ? equipment.RadiationDamageMultiplier : 1f;
            _radiationDps = Mathf.Max(_radiationDps, dps * multiplier);
            _radiationTimer = Mathf.Max(_radiationTimer, duration);
            OnStatsChanged?.Invoke();
        }

        private void ApplyEnvironmentalHazards(PlayerEquipment equipment)
        {
            bool tookDamage = false;

            float heatDamage = PlayerHazardService.HeatDamagePerSecond();
            if (heatDamage > 0f)
            {
                float multiplier = equipment != null ? equipment.HeatDamageMultiplier : 1f;
                Health = Mathf.Max(0f, Health - heatDamage * multiplier * Time.deltaTime);
                tookDamage = true;
            }

            float radiationDamage = PlayerHazardService.RadiationDamagePerSecond();
            if (radiationDamage > 0f)
            {
                float multiplier = equipment != null ? equipment.RadiationDamageMultiplier : 1f;
                Health = Mathf.Max(0f, Health - radiationDamage * multiplier * Time.deltaTime);
                tookDamage = true;
            }

            if (_radiationTimer > 0f)
            {
                _radiationTimer -= Time.deltaTime;
                Health = Mathf.Max(0f, Health - _radiationDps * Time.deltaTime);
                if (_radiationTimer <= 0f) _radiationDps = 0f;
                tookDamage = true;
            }

            if (!tookDamage) return;
            OnStatsChanged?.Invoke();
            if (Health <= 0f) Die();
        }

        /// <summary>Apply a burn damage-over-time effect. Burns ESCALATE with worn armor (heated steel hurts more). Refreshes/extends an active burn.</summary>
        public void ApplyBurn(float dps, float duration)
        {
            if (dps <= 0f || duration <= 0f) return;
            _burnDps   = Mathf.Max(_burnDps, dps);
            _burnTimer = Mathf.Max(_burnTimer, duration);
            OnStatsChanged?.Invoke();
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
            if (IsDead) return;
            Debug.Log("[Player] Died — awaiting respawn selection.");
            IsDead = true;
            Health = 0f;
            Stamina = 0f;
            VoxelEngine.Settings.GameSettings.FlyMode = false;
            VoxelEngine.UI.DeathScreenHud.Show(this);
            OnStatsChanged?.Invoke();
        }

        public void RespawnAt(Vector3 position)
        {
            IsDead = false;
            Health  = MaxHealth;
            Stamina = MaxStamina;
            MaxHunger = baseMaxHunger;
            Hunger = MaxHunger;
            MaxOxygen = baseMaxOxygen;
            Oxygen = MaxOxygen;

            var spawner = GetComponent<PlayerSpawner>();
            if (spawner != null) spawner.RespawnAt(position);
            else transform.position = position;
            OnStatsChanged?.Invoke();
        }

        // ============================================================
        //                       Stamina hooks
        // ============================================================
        public bool TrySpendStamina(float amount) { return true; /* stamina removed */ }
        public void RegenStamina(float dt) { /* stamina removed */ }
        public void DrainStamina(float dt) { /* stamina removed */ }
    }
}
