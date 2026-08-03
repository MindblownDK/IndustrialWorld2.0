// Assets/Scripts/VoxelEngine/Player/PlayerEquipment.cs
//
// Lightweight player equipment container. Roadmap 11.3 starts with two dedicated
// jetpack equipment slots and a quick-equip path from the active inventory item.
// Full armor UI/oxygen/fuel persistence can build on this without changing the
// PlayerController flight contract.
//
// ── Jetpack fuel model (dual-pool, save-compatible) ─────────────────
//   • H₂ pool    → ItemStack.durability (ml) on packs that burn hydrogen.
//   • Power pool → ItemStack.charge (Wh) on hybrids; packs that ONLY use
//                  power keep their charge in durability (legacy stacks okay).
//   • Hybrid: power cruises, H₂ cruises AND unlocks shift boost.
//   • Atmospheric packs only ignite inside an atmosphere; hydrogen works
//     everywhere; two identical packs fly faster and drain as one big tank.

using UnityEngine;
using VoxelEngine.Combat;
using VoxelEngine.Items;

namespace VoxelEngine.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerEquipment : MonoBehaviour
    {
        public const int JetpackSlotCount = 2;
        public const int HelmetSlotCount = 1;
        public const int OxygenTankSlotCount = 1;
        public const int ArmorSlotCount      = 1;

        /// <summary>Speed bonus while two identical usable packs are equipped.</summary>
        public const float TwinSpeedBonus = 1.35f;
        /// <summary>Boost bonus while two identical usable packs are equipped.</summary>
        public const float TwinBoostBonus = 1.20f;
        /// <summary>Air density (kg/m³) below which a pack counts as "in vacuum".</summary>
        public const float AtmosphereDensityThreshold = 0.08f;

        [SerializeField] private ItemContainer _jetpackSlots;
        [SerializeField] private ItemContainer _helmetSlots;
        [SerializeField] private ItemContainer _oxygenTankSlots;
        [SerializeField] private ItemContainer _armorSlots;
        private Inventory _inventory;

        public ItemContainer JetpackSlots
        {
            get { EnsureContainers(); return _jetpackSlots; }
        }

        public ItemContainer HelmetSlots
        {
            get { EnsureContainers(); return _helmetSlots; }
        }

        public ItemContainer OxygenTankSlots
        {
            get { EnsureContainers(); return _oxygenTankSlots; }
        }

        public ItemContainer ArmorSlots
        {
            get { EnsureContainers(); return _armorSlots; }
        }

        private void Awake()
        {
            _inventory = GetComponent<Inventory>();
            EnsureContainers();
        }

        private void Start()
        {
            // Covers scene-load ordering where PlayerStats is not available when
            // the serialized armor container first raises its change event.
            SyncEquippedArmor();
        }

        private void EnsureContainers()
        {
            if (_jetpackSlots == null) _jetpackSlots = new ItemContainer("Jetpack Slots", JetpackSlotCount);
            else _jetpackSlots.Resize(JetpackSlotCount);
            _jetpackSlots.AcceptFilter = (item, wanted) => item is JetpackItem ? Mathf.Min(1, wanted) : 0;

            if (_helmetSlots == null) _helmetSlots = new ItemContainer("Helmet Slot", HelmetSlotCount);
            else _helmetSlots.Resize(HelmetSlotCount);
            _helmetSlots.AcceptFilter = (item, wanted) => item is SpaceHelmetItem ? Mathf.Min(1, wanted) : 0;

            if (_oxygenTankSlots == null) _oxygenTankSlots = new ItemContainer("Oxygen Tank Slot", OxygenTankSlotCount);
            else _oxygenTankSlots.Resize(OxygenTankSlotCount);
            _oxygenTankSlots.AcceptFilter = (item, wanted) => item is OxygenTankItem ? Mathf.Min(1, wanted) : 0;

            if (_armorSlots == null) _armorSlots = new ItemContainer("Armor Slot", ArmorSlotCount);
            else _armorSlots.Resize(ArmorSlotCount);
            _armorSlots.AcceptFilter = (item, wanted) => item is ArmorItem ? Mathf.Min(1, wanted) : 0;
            // Keep PlayerStats.equippedArmor (read by TakeDamage) in lock-step with the slot
            // so drag-equip / shift-click / the legacy RMB path all agree on what's worn.
            _armorSlots.OnChanged -= SyncEquippedArmor;
            _armorSlots.OnChanged += SyncEquippedArmor;
        }

        // ════════════════════════════════════════════════════════════
        //                     JETPACK FLIGHT STATE
        // ════════════════════════════════════════════════════════════

        /// <summary>Immutable snapshot of the equipped packs for one frame —
        /// single source of truth for the controller, the HUD and the bay UI.</summary>
        public readonly struct JetpackSummary
        {
            public readonly bool anyPack;
            public readonly bool canFly;
            public readonly bool canBoost;
            public readonly bool twinActive;
            public readonly float speedMul;
            public readonly float boostMul;
            public readonly int h2;
            public readonly int h2Cap;
            public readonly int power;
            public readonly int powerCap;
            public readonly JetpackItem drivePack;
            public readonly string offlineReason;

            public JetpackSummary(
                bool anyPack, bool canFly, bool canBoost, bool twinActive,
                float speedMul, float boostMul,
                int h2, int h2Cap, int power, int powerCap,
                JetpackItem drivePack, string offlineReason)
            {
                this.anyPack = anyPack; this.canFly = canFly; this.canBoost = canBoost;
                this.twinActive = twinActive;
                this.speedMul = speedMul; this.boostMul = boostMul;
                this.h2 = h2; this.h2Cap = h2Cap; this.power = power; this.powerCap = powerCap;
                this.drivePack = drivePack; this.offlineReason = offlineReason;
            }

            public static readonly JetpackSummary Empty = new JetpackSummary(
                false, false, false, false, 1f, 1f, 0, 0, 0, 0, null, "No jetpack equipped");
        }

        private JetpackSummary _summaryCache;
        private int _summaryCachedFrame = -1;

        /// <summary>Frame-cached snapshot — safe to call from UI and controller alike.</summary>
        public JetpackSummary GetJetpackSummary()
        {
            if (_summaryCachedFrame != Time.frameCount)
            {
                _summaryCache = BuildSummary();
                _summaryCachedFrame = Time.frameCount;
            }
            return _summaryCache;
        }

        /// <summary>True when the pack may ignite at the player's current position.</summary>
        public bool EnvironmentOk(JetpackItem pack)
        {
            if (pack == null) return false;
            var atmosphere = VoxelEngine.GridSystem.AtmosphereManager.Sample(transform.position);
            bool inAtmosphere = atmosphere.AirDensity >= AtmosphereDensityThreshold;
            return inAtmosphere ? pack.supportsAtmosphere : pack.supportsVacuum;
        }

        private static bool PackHasFuel(JetpackItem pack, ItemStack stack)
        {
            if (pack == null || stack == null || stack.IsEmpty) return false;
            if (!NeedsFuel(pack)) return true;
            return JetpackItem.GetH2Ml(stack) > 0 || JetpackItem.GetPowerMl(stack) > 0;
        }

        /// <summary>Shift boost on the Hybrid is a hydrogen afterburner — the power
        /// cell alone only cruises. Pure packs boost on their own fuel.</summary>
        private static bool PackCanBoost(JetpackItem pack, ItemStack stack)
        {
            if (pack == null || stack == null || stack.IsEmpty) return false;
            bool hybrid = pack.UsesHydrogenEffective && pack.UsesPowerEffective;
            if (hybrid) return JetpackItem.GetH2Ml(stack) > 0;
            if (pack.UsesHydrogenEffective) return JetpackItem.GetH2Ml(stack) > 0;
            if (pack.UsesPowerEffective) return JetpackItem.GetPowerMl(stack) > 0;
            return true; // unfuelled legacy pack
        }

        private JetpackSummary BuildSummary()
        {
            EnsureContainers();
            NormalizeAllJetpacks();

            int h2 = 0, h2Cap = 0, power = 0, powerCap = 0;
            bool anyPack = false, anyEnvBlocked = false;
            ItemStack bestStack = null;
            JetpackItem bestPack = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var s = _jetpackSlots.GetSlot(i);
                if (s == null || s.IsEmpty || s.item is not JetpackItem p) continue;
                anyPack = true;
                int pH2 = JetpackItem.GetH2Ml(s);
                int pPw = JetpackItem.GetPowerMl(s);
                h2 += pH2; h2Cap += p.HydrogenCapacityMl;
                power += pPw; powerCap += p.PowerCapacityMl;

                if (!EnvironmentOk(p)) { anyEnvBlocked = true; continue; }
                if (!PackHasFuel(p, s)) continue;

                float score = p.flightSpeedMultiplier + p.boostMultiplier * 0.25f
                            + Mathf.Clamp01((pH2 + pPw) / (float)Mathf.Max(1, p.HydrogenCapacityMl + p.PowerCapacityMl)) * 0.05f;
                if (score > bestScore) { bestScore = score; bestStack = s; bestPack = p; }
            }

            if (!anyPack) return JetpackSummary.Empty;

            // Twin drive: two identical packs, both usable in this environment with fuel.
            bool twin = false;
            if (_jetpackSlots.Size >= 2)
            {
                var a = _jetpackSlots.GetSlot(0);
                var b = _jetpackSlots.GetSlot(1);
                if (a != null && b != null && !a.IsEmpty && !b.IsEmpty
                    && a.item == b.item && a.item is JetpackItem tp
                    && EnvironmentOk(tp) && PackHasFuel(tp, a) && PackHasFuel(tp, b))
                    twin = true;
            }

            if (bestPack == null)
            {
                string why = anyEnvBlocked ? "No atmosphere — engine can't ignite here"
                                           : "Out of fuel — fill Portable H₂ Tanks / Batteries";
                return new JetpackSummary(true, false, false, false, 1f, 1f,
                    h2, h2Cap, power, powerCap, null, why);
            }

            bool canBoost = PackCanBoost(bestPack, bestStack);
            float speed = Mathf.Max(0.1f, bestPack.flightSpeedMultiplier) * (twin ? TwinSpeedBonus : 1f);
            float boost = Mathf.Max(1f, bestPack.boostMultiplier) * (twin ? TwinBoostBonus : 1f);
            return new JetpackSummary(true, true, canBoost, twin, speed, boost,
                h2, h2Cap, power, powerCap, bestPack, null);
        }

        /// <summary>Environment-aware: a usable pack is equipped AND can ignite here.</summary>
        public bool HasUsableJetpack => GetJetpackSummary().canFly;

        /// <summary>Best equipped pack definition (may be empty of fuel — for display).</summary>
        public JetpackItem GetBestJetpack()
        {
            EnsureContainers();
            JetpackItem best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < JetpackSlots.Size; i++)
            {
                var s = JetpackSlots.GetSlot(i);
                if (s == null || s.IsEmpty || s.item is not JetpackItem pack) continue;
                float score = pack.flightSpeedMultiplier + pack.boostMultiplier * 0.25f;
                if (score > bestScore) { bestScore = score; best = pack; }
            }
            return best;
        }

        /// <summary>Legacy accessor — best equipped pack stack (optionally requiring fuel).</summary>
        public ItemStack GetBestJetpackStack(bool requireFuel = true)
        {
            EnsureContainers();
            EnsureAllJetpackFuelInitialized();
            var summary = GetJetpackSummary();
            if (requireFuel)
            {
                if (summary.drivePack == null) return null;
                for (int i = 0; i < _jetpackSlots.Size; i++)
                {
                    var s = _jetpackSlots.GetSlot(i);
                    if (s != null && !s.IsEmpty && s.item == summary.drivePack && PackHasFuel(summary.drivePack, s))
                        return s;
                }
                return null;
            }
            var def = GetBestJetpack();
            if (def == null) return null;
            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var s = _jetpackSlots.GetSlot(i);
                if (s != null && !s.IsEmpty && s.item == def) return s;
            }
            return null;
        }

        public float FlightSpeedMultiplier => GetJetpackSummary().speedMul;
        public float BoostMultiplier => GetJetpackSummary().boostMul;
        public bool CanBoostNow => GetJetpackSummary().canBoost;

        public static bool NeedsFuel(JetpackItem pack)
        {
            if (pack == null) return false;
            return pack.UsesHydrogenEffective || pack.UsesPowerEffective;
        }

        public static bool PackUsesHydrogen(JetpackItem pack) => pack != null && pack.UsesHydrogenEffective;
        public static bool PackUsesPower(JetpackItem pack) => pack != null && pack.UsesPowerEffective;

        /// <summary>Defensive: old assets may miss flags — infer from family.</summary>
        public static void FixOldJetpackFlags(JetpackItem pack)
        {
            if (pack == null) return;
            if (!pack.usesHydrogen && !pack.usesPower)
            {
                pack.usesHydrogen = pack.family == JetpackFamily.HydrogenBoost || pack.family == JetpackFamily.Hybrid;
                pack.usesPower = pack.family == JetpackFamily.Atmospheric || pack.family == JetpackFamily.Hybrid;
            }
            if (pack.autoRechargeThreshold <= 0.001f) pack.autoRechargeThreshold = 0.10f;
            if (pack.chargedCellRefuelMl <= 0) pack.chargedCellRefuelMl = 350;
        }

        /// <summary>
        /// Clamp invalid pool values (never destroys fuel — an over-full legacy stack
        /// stays over-full until it drains or its overflow is spilled into inventory
        /// tanks by <see cref="EnsureAllJetpackFuelInitialized"/>).
        /// </summary>
        public static void EnsureJetpackFuel(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || stack.item is not JetpackItem pack) return;
            if (stack.durability < 0) stack.durability = 0;
            if (stack.charge < 0) stack.charge = 0;
            // Hidden pools must stay zeroed (charge on a pack with no power cell).
            if (!pack.UsesPowerEffective && stack.charge != 0) stack.charge = 0;
        }

        public void EnsureAllJetpackFuelInitialized()
        {
            NormalizeAllJetpacks();
            // Defensive: replay recharge so inventory tanks/cells/batteries work right after equip.
            TryAutoRefuelFromInventory(force: true);
        }

        /// <summary>Flag-fix + pool clamp + overflow spill on every equipped pack.
        /// Pure data hygiene — never refuels, never voids.</summary>
        private void NormalizeAllJetpacks()
        {
            EnsureContainers();
            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var s = _jetpackSlots.GetSlot(i);
                if (s == null || s.IsEmpty || s.item is not JetpackItem p) continue;
                FixOldJetpackFlags(p);
                EnsureJetpackFuel(s);
                SpillOverflowToInventory(s, p);
            }
        }

        /// <summary>
        /// Legacy stacks can carry MORE fuel than the tank holds (e.g. a 2000 ml fill
        /// on a 1200 ml pack). Never void it: spill the excess back into matching
        /// portable containers (H₂ tanks / portable batteries); only keep the
        /// remainder on the pack when there is nowhere for it to go.
        /// </summary>
        private void SpillOverflowToInventory(ItemStack stack, JetpackItem pack)
        {
            if (_inventory == null) _inventory = GetComponent<Inventory>();
            var inv = _inventory != null ? _inventory.container : null;
            if (inv == null) return;

            int h2Over = JetpackItem.GetH2Ml(stack) - pack.HydrogenCapacityMl;
            if (h2Over > 0)
            {
                for (int i = 0; i < inv.Size && h2Over > 0; i++)
                {
                    var s = inv.GetSlot(i);
                    if (s == null || s.IsEmpty || !HydrogenCanisterItem.IsPortableHydrogenTank(s.item)) continue;
                    int moved = HydrogenCanisterItem.TryAddMl(s, h2Over);
                    if (moved <= 0) continue;
                    h2Over -= moved;
                    JetpackItem.SetH2Ml(stack, JetpackItem.GetH2Ml(stack) - moved);
                    inv.SetSlot(i, s);
                }
                if (h2Over <= 0) inv.RaiseChanged();
            }

            int pwOver = JetpackItem.GetPowerMl(stack) - pack.PowerCapacityMl;
            if (pwOver > 0)
            {
                for (int i = 0; i < inv.Size && pwOver > 0; i++)
                {
                    var s = inv.GetSlot(i);
                    if (s == null || s.IsEmpty || !PortableBatteryItem.IsPortableBattery(s.item)) continue;
                    int moved = PortableBatteryItem.TryAddMl(s, pwOver);
                    if (moved <= 0) continue;
                    pwOver -= moved;
                    JetpackItem.SetPowerMl(stack, JetpackItem.GetPowerMl(stack) - moved);
                    inv.SetSlot(i, s);
                }
                if (pwOver <= 0) inv.RaiseChanged();
            }
        }

        /// <summary>
        /// Drain fuel from the drive pack for one flight tick. Returns false when no
        /// fueled, environment-legal pack remains (caller should cut flight).
        /// <paramref name="wantBoost"/> only engages the hydrogen afterburner when
        /// the drive pack actually has H₂ — hybrid power-cell flight never boosts.
        /// </summary>
        public bool TryConsumeFlightFuel(float dt, bool wantBoost)
        {
            EnsureContainers();
            if (dt <= 0f) return HasUsableJetpack;
            // Top up any pack already at/under threshold before selecting.
            TryAutoRefuelFromInventory(force: false);

            ItemStack driveStack = null;
            JetpackItem drivePack = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var s = _jetpackSlots.GetSlot(i);
                if (s == null || s.IsEmpty || s.item is not JetpackItem p) continue;
                EnsureJetpackFuel(s);
                if (!EnvironmentOk(p)) continue;
                if (!PackHasFuel(p, s)) continue;
                float score = p.flightSpeedMultiplier + p.boostMultiplier * 0.25f;
                if (score > bestScore) { bestScore = score; driveStack = s; drivePack = p; }
            }
            if (driveStack == null || drivePack == null) return false;
            if (!NeedsFuel(drivePack)) return true;

            bool boosting = wantBoost && PackCanBoost(drivePack, driveStack);
            bool hybrid = drivePack.UsesHydrogenEffective && drivePack.UsesPowerEffective;

            float cruise = Mathf.Max(0f, drivePack.drainMlPerSecond > 0f ? drivePack.drainMlPerSecond : drivePack.drainPerSecond);
            float boostDrain = Mathf.Max(0f, drivePack.boostDrainMlPerSecond > 0f ? drivePack.boostDrainMlPerSecond : drivePack.boostDrainPerSecond);

            // Mobility Servos improve the whole equipped flight package rather than
            // only a specific jetpack family. The armor is the single source of this
            // modifier, so swapping armor changes fuel efficiency immediately.
            float fuelEfficiency = JetpackFuelEfficiency;
            cruise *= fuelEfficiency;
            boostDrain *= fuelEfficiency;

            // Twin drive: two identical packs share the work — drain the fuller one
            // so the pair behaves like one tank with double capacity.
            ItemStack drainStack = driveStack;
            var summary = GetJetpackSummary();
            if (summary.twinActive && _jetpackSlots.Size >= 2)
            {
                var a = _jetpackSlots.GetSlot(0);
                var b = _jetpackSlots.GetSlot(1);
                if (a != null && b != null && !a.IsEmpty && !b.IsEmpty && a.item == b.item)
                {
                    if (hybrid && boosting)
                    {
                        if (JetpackItem.GetH2Ml(b) > JetpackItem.GetH2Ml(a)) drainStack = b;
                    }
                    else
                    {
                        int aTotal = JetpackItem.GetH2Ml(a) + JetpackItem.GetPowerMl(a);
                        int bTotal = JetpackItem.GetH2Ml(b) + JetpackItem.GetPowerMl(b);
                        if (bTotal > aTotal) drainStack = b;
                    }
                    drivePack = drainStack.item as JetpackItem ?? drivePack;
                }
            }

            var box = drainStack.payload as JetpackFuelBox;
            if (box == null) { box = new JetpackFuelBox(); drainStack.payload = box; }

            if (hybrid)
            {
                if (boosting)
                {
                    // H₂ afterburner — the whole burn comes from the hydrogen tank.
                    DrainPool(drainStack, h2: true, cruise + boostDrain, dt, box);
                }
                else if (JetpackItem.GetPowerMl(drainStack) > 0)
                {
                    DrainPool(drainStack, h2: false, cruise, dt, box);
                }
                else
                {
                    DrainPool(drainStack, h2: true, cruise, dt, box);
                }
            }
            else if (drivePack.UsesHydrogenEffective)
            {
                DrainPool(drainStack, h2: true, boosting ? cruise + boostDrain : cruise, dt, box);
            }
            else if (drivePack.UsesPowerEffective)
            {
                DrainPool(drainStack, h2: false, boosting ? cruise + boostDrain : cruise, dt, box);
            }

            if (PackHasFuel(drivePack, drainStack)) return true;
            // Drained pack ran dry — another equipped pack may still be fueled.
            _summaryCachedFrame = -1;
            return GetJetpackSummary().canFly;
        }

        private static void DrainPool(ItemStack stack, bool h2, float ratePerSecond, float dt, JetpackFuelBox box)
        {
            float cost = Mathf.Max(0f, ratePerSecond) * dt;
            if (cost <= 0f) return;
            if (h2)
            {
                box.fracH2 += cost;
                int whole = Mathf.FloorToInt(box.fracH2);
                if (whole <= 0) return;
                box.fracH2 -= whole;
                JetpackItem.TakeH2(stack, whole);
            }
            else
            {
                box.fracPower += cost;
                int whole = Mathf.FloorToInt(box.fracPower);
                if (whole <= 0) return;
                box.fracPower -= whole;
                JetpackItem.TakePower(stack, whole);
            }
        }

        // ── Aggregate pool getters (HUD / jetpack bay display) ──────

        /// <summary>Summed H₂ (ml) across all equipped packs.</summary>
        public int TotalH2Ml
        {
            get
            {
                EnsureContainers();
                int sum = 0;
                for (int i = 0; i < JetpackSlots.Size; i++)
                {
                    var s = JetpackSlots.GetSlot(i);
                    sum += JetpackItem.GetH2Ml(s);
                }
                return sum;
            }
        }

        public int TotalH2CapacityMl
        {
            get
            {
                EnsureContainers();
                int sum = 0;
                for (int i = 0; i < JetpackSlots.Size; i++)
                {
                    var s = JetpackSlots.GetSlot(i);
                    if (s != null && !s.IsEmpty && s.item is JetpackItem p) sum += p.HydrogenCapacityMl;
                }
                return sum;
            }
        }

        /// <summary>Summed power charge (Wh) across all equipped packs.</summary>
        public int TotalPowerMl
        {
            get
            {
                EnsureContainers();
                int sum = 0;
                for (int i = 0; i < JetpackSlots.Size; i++)
                {
                    var s = JetpackSlots.GetSlot(i);
                    sum += JetpackItem.GetPowerMl(s);
                }
                return sum;
            }
        }

        public int TotalPowerCapacityMl
        {
            get
            {
                EnsureContainers();
                int sum = 0;
                for (int i = 0; i < JetpackSlots.Size; i++)
                {
                    var s = JetpackSlots.GetSlot(i);
                    if (s != null && !s.IsEmpty && s.item is JetpackItem p) sum += p.PowerCapacityMl;
                }
                return sum;
            }
        }

        public bool AnyHydrogenPackEquipped => TotalH2CapacityMl > 0;
        public bool AnyPowerPackEquipped => TotalPowerCapacityMl > 0;

        // ── Legacy fuel getters (kept for older UI call sites) ──────

        /// <summary>Remaining fuel 0..1 for the best pack (its dominant pool).</summary>
        public float BestJetpackFuel01
        {
            get
            {
                var stack = GetBestJetpackStack(requireFuel: false);
                if (stack == null || stack.item is not JetpackItem pack) return 0f;
                EnsureJetpackFuel(stack);
                if (!NeedsFuel(pack)) return 1f;
                if (pack.UsesHydrogenEffective)
                    return Mathf.Clamp01(JetpackItem.GetH2Ml(stack) / (float)Mathf.Max(1, pack.HydrogenCapacityMl));
                return Mathf.Clamp01(JetpackItem.GetPowerMl(stack) / (float)Mathf.Max(1, pack.PowerCapacityMl));
            }
        }

        public int BestJetpackFuelUnits
        {
            get
            {
                var stack = GetBestJetpackStack(requireFuel: false);
                if (stack == null || stack.item is not JetpackItem pack) return 0;
                return pack.UsesHydrogenEffective ? JetpackItem.GetH2Ml(stack) : JetpackItem.GetPowerMl(stack);
            }
        }

        public int BestJetpackFuelCapacity
        {
            get
            {
                var stack = GetBestJetpackStack(requireFuel: false);
                if (stack == null || stack.item is not JetpackItem pack) return 0;
                return Mathf.Max(pack.HydrogenCapacityMl, pack.PowerCapacityMl);
            }
        }

        // ── Idle auto-refuel ────────────────────────────────────────
        // The 10% → 100% rule should work ANYWHERE — not just mid-flight ticks
        // or when the inventory opens. Charge a portable battery, walk around
        // with it, and a thirsty pack sips from it on its own. Slow 0.5 Hz tick
        // so this costs nothing; hard pause freezes it with the world.
        private float _idleRefuelAccum;

        private void Update()
        {
            _idleRefuelAccum += Time.deltaTime;
            if (_idleRefuelAccum < 2f) return;
            _idleRefuelAccum = 0f;
            TryAutoRefuelFromInventory(force: false);
        }

        // ── Inventory refuel ────────────────────────────────────────

        /// <summary>
        /// Recharge equipped packs from inventory. H₂ tanks top up the hydrogen
        /// pool; portable batteries and charged cells top up the power cell.
        /// Normally only runs at/under the pack's recharge threshold (10%).
        /// </summary>
        public int TryAutoRefuelFromInventory(bool force = false)
        {
            EnsureContainers();
            if (_inventory == null) _inventory = GetComponent<Inventory>();
            int total = 0;
            bool meaningfulH2 = false, meaningfulPwr = false;
            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var stack = _jetpackSlots.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item is not JetpackItem pack) continue;
                EnsureJetpackFuel(stack);
                if (!NeedsFuel(pack)) continue;

                float threshold = pack.RechargeThreshold;
                int h2Cap = Mathf.Max(1, pack.HydrogenCapacityMl);
                int pCap  = Mathf.Max(1, pack.PowerCapacityMl);
                bool h2Low = pack.UsesHydrogenEffective
                    && JetpackItem.GetH2Ml(stack) <= threshold * h2Cap + 0.001f;
                bool pwLow = pack.UsesPowerEffective
                    && JetpackItem.GetPowerMl(stack) <= threshold * pCap + 0.001f;
                if (!force && !h2Low && !pwLow) continue;

                int before = total;
                total += TryRechargeSlot(i, stack, pack, force: true);
                if (total <= before) continue;

                // Only count it as a REAL refuel when the pool actually left the red
                // zone (≤10% → >25%). Trickles that keep the pack empty would
                // otherwise re-fire the toast every single tick.
                if (h2Low && JetpackItem.GetH2Ml(stack)  > 0.25f * h2Cap) meaningfulH2  = true;
                if (pwLow && JetpackItem.GetPowerMl(stack) > 0.25f * pCap) meaningfulPwr = true;
            }
            // Automatic (not UI-opening) top-ups get a small heads-up so the player
            // understands their portable tanks/batteries got sipped at the 10% mark —
            // gated to meaningful refuels + a cooldown, so it can never spam.
            if (!force && (meaningfulH2 || meaningfulPwr) && Time.unscaledTime >= _nextRefuelToastTime)
            {
                _nextRefuelToastTime = Time.unscaledTime + 45f;
                string fuel = meaningfulH2 && meaningfulPwr ? "H₂ + PWR" : meaningfulH2 ? "H₂" : "PWR";
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    "Jetpack Refuelled",
                    $"{fuel} hit 10% — topped up to 100% from your portable {(meaningfulH2 ? "tanks" : "batteries")}",
                    null, new Color(0.45f, 0.9f, 1f));
            }
            return total;
        }

        private float _nextRefuelToastTime;

        private int TryRechargeSlot(int slotIndex, ItemStack stack, JetpackItem pack, bool force)
        {
            if (_inventory == null) _inventory = GetComponent<Inventory>();
            if (_inventory == null || _inventory.container == null) return 0;
            EnsureJetpackFuel(stack);
            var inv = _inventory.container;
            int restored = 0;

            // 1) Hydrogen side — siphon Portable Hydrogen Tanks (do not destroy the tank).
            if (pack.UsesHydrogenEffective)
            {
                int space = Mathf.Max(0, pack.HydrogenCapacityMl - JetpackItem.GetH2Ml(stack));
                for (int i = 0; i < inv.Size && space > 0; i++)
                {
                    var s = inv.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null) continue;
                    if (!HydrogenCanisterItem.IsPortableHydrogenTank(s.item)) continue;
                    int taken = HydrogenCanisterItem.TryTakeMl(s, space);
                    if (taken <= 0) continue;
                    inv.SetSlot(i, s); // write back reduced tank fill (ml)
                    int added = JetpackItem.AddH2(stack, taken);
                    // Extremely defensive: over-full legacy stacks can't accept — hand the fuel back.
                    if (added < taken) HydrogenCanisterItem.TryAddMl(s, taken - added);
                    space -= added;
                    restored += added;
                }
            }

            // 2) Power side — portable rechargeable batteries (reusable, like tanks).
            if (pack.UsesPowerEffective)
            {
                int space = Mathf.Max(0, pack.PowerCapacityMl - JetpackItem.GetPowerMl(stack));
                for (int i = 0; i < inv.Size && space > 0; i++)
                {
                    var s = inv.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null) continue;
                    if (!PortableBatteryItem.IsPortableBattery(s.item)) continue;
                    int taken = PortableBatteryItem.TryTakeMl(s, space);
                    if (taken <= 0) continue;
                    inv.SetSlot(i, s); // write back reduced charge
                    int added = JetpackItem.AddPower(stack, taken);
                    if (added < taken) PortableBatteryItem.TryAddMl(s, taken - added);
                    space -= added;
                    restored += added;
                }
            }

            // 3) Power side — disposable charged cells (single-use cartridges).
            if (pack.UsesPowerEffective)
            {
                int space = Mathf.Max(0, pack.PowerCapacityMl - JetpackItem.GetPowerMl(stack));
                for (int i = 0; i < inv.Size && space > 0; i++)
                {
                    var s = inv.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null) continue;
                    if (!JetpackItem.IsPowerFuelItem(s.item)) continue;
                    int per = Mathf.Max(1, pack.chargedCellRefuelMl > 0 ? pack.chargedCellRefuelMl : pack.chargedCellRefuel);
                    // Only crack a cell open when it fits entirely — never void charge.
                    // (Smaller gaps are closed by portable batteries instead.)
                    if (space < per) break;
                    int got = inv.Remove(s.item, 1);
                    if (got <= 0) continue;
                    int add = Mathf.Min(space, per);
                    JetpackItem.AddPower(stack, add);
                    space -= add;
                    restored += add;
                }
            }

            if (restored > 0)
            {
                _jetpackSlots.SetSlot(slotIndex, stack);
                inv.RaiseChanged();
                _summaryCachedFrame = -1;
            }
            return restored;
        }

        /// <summary>Fractional fuel accumulators (not serialized — pools are).</summary>
        private sealed class JetpackFuelBox { public float fracH2; public float fracPower; }

        public SpaceHelmetItem EquippedHelmet
        {
            get
            {
                EnsureContainers();
                var stack = _helmetSlots.GetSlot(0);
                return stack != null && !stack.IsEmpty ? stack.item as SpaceHelmetItem : null;
            }
        }

        public OxygenTankItem EquippedOxygenTank
        {
            get
            {
                EnsureContainers();
                var stack = _oxygenTankSlots.GetSlot(0);
                return stack != null && !stack.IsEmpty ? stack.item as OxygenTankItem : null;
            }
        }

        /// <summary>Current armor stack, including its per-piece installed upgrade state.</summary>
        public ItemStack EquippedArmorStack
        {
            get
            {
                EnsureContainers();
                return _armorSlots.GetSlot(0);
            }
        }

        /// <summary>Currently worn armor definition (drives base damage mitigation).</summary>
        public ArmorItem EquippedArmor
        {
            get
            {
                var stack = EquippedArmorStack;
                return stack != null && !stack.IsEmpty ? stack.item as ArmorItem : null;
            }
        }

        private void SyncEquippedArmor()
        {
            var playerStats = PlayerStats.Instance;
            if (playerStats != null) playerStats.equippedArmor = EquippedArmor;
        }

        public bool HasBreathingKit => EquippedHelmet != null && EquippedHelmet.sealedHelmet && EquippedOxygenTank != null;
        public float BonusOxygen => HasBreathingKit ? Mathf.Max(0f, EquippedOxygenTank.bonusOxygen) : 0f;
        public float OxygenDrainMultiplier
        {
            get
            {
                float lifeSupportMultiplier = HasBreathingKit
                    ? Mathf.Clamp(EquippedOxygenTank.drainMultiplier * EquippedHelmet.oxygenEfficiency, 0.05f, 1f)
                    : 1f;
                return lifeSupportMultiplier * ArmorOxygenEfficiencyMultiplier;
            }
        }

        // ── Installed armor module modifiers ────────────────────────

        public int GetArmorUpgradeTier(ArmorUpgradeKind kind)
        {
            return ArmorUpgrades.GetTier(EquippedArmorStack, kind);
        }

        public bool HasHazmatProtection => ArmorUpgrades.HasHazmat(EquippedArmorStack);

        public float HeatDamageMultiplier => Mathf.Clamp(
            1f - ArmorUpgradeKindInfo.EffectPerTier(ArmorUpgradeKind.HeatTolerance)
                 * GetArmorUpgradeTier(ArmorUpgradeKind.HeatTolerance),
            0.10f, 1f);

        public float RadiationDamageMultiplier => HasHazmatProtection
            ? 0f
            : Mathf.Clamp(
                1f - ArmorUpgradeKindInfo.EffectPerTier(ArmorUpgradeKind.RadiationShielding)
                     * GetArmorUpgradeTier(ArmorUpgradeKind.RadiationShielding),
                0.10f, 1f);

        public float FallDamageMultiplier => Mathf.Clamp(
            1f - ArmorUpgradeKindInfo.EffectPerTier(ArmorUpgradeKind.FallImpact)
                 * GetArmorUpgradeTier(ArmorUpgradeKind.FallImpact),
            0.10f, 1f);

        public float ArmorOxygenEfficiencyMultiplier => Mathf.Clamp(
            1f - ArmorUpgradeKindInfo.EffectPerTier(ArmorUpgradeKind.OxygenEfficiency)
                 * GetArmorUpgradeTier(ArmorUpgradeKind.OxygenEfficiency),
            0.10f, 1f);

        public float JetpackSpeedMultiplier => 1f +
            ArmorUpgradeKindInfo.EffectPerTier(ArmorUpgradeKind.Mobility)
            * GetArmorUpgradeTier(ArmorUpgradeKind.Mobility);

        public float JetpackFuelEfficiency => Mathf.Clamp(
            1f - ArmorUpgradeKindInfo.EffectPerTier(ArmorUpgradeKind.Mobility)
                 * GetArmorUpgradeTier(ArmorUpgradeKind.Mobility),
            0.10f, 1f);

        /// <summary>
        /// Backward-compatible direct application API. The Armor Upgrade Station
        /// owns normal timed installation; this remains useful for future scripted
        /// rewards without consuming the module itself.
        /// </summary>
        public bool TryApplyArmorUpgrade(ArmorUpgradeItem module)
        {
            var armorStack = EquippedArmorStack;
            if (!ArmorUpgrades.TryApply(armorStack, module)) return false;
            _armorSlots.SetSlot(0, armorStack);
            SyncEquippedArmor();
            return true;
        }

        /// <summary>
        /// If the active hotbar stack is a JetpackItem, move one into the first free
        /// jetpack slot — fuel pools travel WITH the pack (quick-equip never voids).
        /// </summary>
        public bool TryQuickEquipActiveJetpack()
        {
            EnsureContainers();
            if (_inventory == null) _inventory = GetComponent<Inventory>();
            if (_inventory == null || _inventory.container == null) return false;
            var active = _inventory.ActiveStack;
            if (active == null || active.IsEmpty || active.item is not JetpackItem pack) return false;

            for (int i = 0; i < _jetpackSlots.Size; i++)
            {
                var slot = _jetpackSlots.GetSlot(i);
                if (slot != null && !slot.IsEmpty) continue;
                _jetpackSlots.SetSlot(i, new ItemStack
                {
                    item = pack,
                    count = 1,
                    durability = active.durability,
                    charge = active.charge,
                    payload = active.payload,
                });
                _inventory.container.Remove(pack, 1);
                _summaryCachedFrame = -1;
                VoxelEngine.UI.BuildFeedbackHud.Show("Jetpack Equipped", pack.displayName, pack.icon, pack.iconTint);
                return true;
            }

            VoxelEngine.UI.BuildFeedbackHud.Show("Jetpack Slots Full", "Two jetpack slots are already occupied", pack.icon, Color.yellow);
            return false;
        }
    }
}
