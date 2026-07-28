// Assets/Scripts/VoxelEngine/Maritime/GridMaritimeGenerator.cs
//
// Maritime Generator (2×2×2) — converts shaft torque into electricity.
// Attached to the END of a propulsion chain (after a gearbox for best
// efficiency: more speed = more power at the generator).
//
// The MaritimePropagationJob computes:
//   ElectricityOutput = shaftTorque × shaftRPM × (2π/60) × efficiency × speedBonus × modules
//
// v6.10.0-dev — Speed-Responsive Output + Upgrade Modules:
//   • Speed Bonus: the faster the input shaft spins (toward rated RPM), the more
//     power is generated — up to +50% at rated speed.
//   • 2 Module Slots accept EngineModuleItems (Efficiency Tuning Chip raises the
//     max output power a lot but UNLOCKS a mandatory coolant requirement;
//     Super-Cooler Radiator Jacket triples heat dissipation while water flows).
//   • Live temperature model: ≥100°C = thermal shutdown until < 80°C.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    public class GridMaritimeGenerator : MaritimeBlockBase, IGridDataProvider
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Generator;

        [Header("Generator")]
        [Tooltip("Max RPM this generator can accept.")]
        public float maxRPM = 1800f;
        [Tooltip("Max electrical output (W). Excess shaft power is clipped.")]
        public float maxWattOutput = 50000f;

        [Header("Speed Bonus")]
        [Tooltip("Extra output gained at rated RPM. 0.5 = up to +50% more power at full rated speed.")]
        [Range(0f, 1f)] public float maxSpeedBonus = 0.5f;

        [Header("Internal Battery Buffer")]
        [Tooltip("Small internal battery that smooths output (Wh).")]
        public float bufferCapacityWh = 2000f;
        [Tooltip("Current battery buffer level (Wh).")]
        public float BufferCharge { get; private set; }
        /// <summary>0..1 buffer fill for the UI indicator.</summary>
        public float BufferFill01 => bufferCapacityWh > 0f ? Mathf.Clamp01(BufferCharge / bufferCapacityWh) : 0f;

        [Header("Coolant (unlocked by Efficiency Tuning Chip)")]
        [Tooltip("Internal coolant buffer capacity (litres).")]
        public float coolantCapacity = 40f;
        [Tooltip("Coolant consumed per second at full load (L/s).")]
        public float coolantConsumptionRate = 0.2f;
        [Tooltip("Coolant pulled from grid tanks per second when refilling.")]
        public float coolantRefillRate = 6f;
        /// <summary>Current coolant buffer level (L).</summary>
        public float CoolantBuffer { get; private set; }
        /// <summary>0..1 coolant fill ratio.</summary>
        public float CoolantFill01 => coolantCapacity > 0f ? Mathf.Clamp01(CoolantBuffer / coolantCapacity) : 0f;
        /// <summary>True if any coolant is in the buffer (an active flow can be sustained).</summary>
        public bool HasCoolant => CoolantBuffer > 0.01f;

        [Header("Thermal Management")]
        [Tooltip("Heat generated per second at full electrical load (°C/s).")]
        public float baseHeatRate = 1.6f;
        [Tooltip("Passive heat dissipation per second (°C/s).")]
        public float baseDissipationRate = 1.2f;
        [Tooltip("Extra dissipation per second (°C/s) while coolant is flowing.")]
        public float coolantDissipationRate = 2.2f;
        /// <summary>Generator temperature in °C.</summary>
        public float TemperatureC { get; private set; } = GridMaritimeEngine.AmbientTemperatureC;
        /// <summary>Anchored thermal shutdown — clears below 80°C.</summary>
        public bool CriticalFailure { get; private set; }
        /// <summary>0..1 heat normalized against the critical point (UI bars).</summary>
        public float Heat01 => Mathf.Clamp01(TemperatureC / GridMaritimeEngine.CriticalTemperatureC);
        /// <summary>≥ 100°C or latched — thermal shutdown, output shaft power rejected.</summary>
        public bool IsCriticalHeat => CriticalFailure;

        // ── Modules / upgrades ───────────────────────────────────────
        /// <summary>Generator module slots — Efficiency Tuning Chips and
        /// Super-Cooler Radiator Jackets only.</summary>
        public ItemContainer ModuleSlots { get; private set; }
        public const int MaxModuleSlots = 2;

        public int EfficiencyChipCount { get; private set; }
        public int RadiatorModuleCount { get; private set; }
        /// <summary>Output multiplier from socketed modules (1 = stock).</summary>
        public float ModuleOutputMultiplier { get; private set; } = 1f;
        /// <summary>True while a radiator is socketed AND water is flowing.</summary>
        public bool RadiatorCoolingActive { get; private set; }
        /// <summary>0..1 of the radiator water demand met this tick.</summary>
        public float RadiatorWaterFill01 { get; private set; }

        /// <summary>Live electricity output (W) — set by ApplyResults.</summary>
        public float GeneratedWatts { get; private set; }
        /// <summary>Current shaft RPM.</summary>
        public float CurrentRPM { get; private set; }
        /// <summary>0..1 shaft speed vs rated (drives the +50% speed bonus).</summary>
        public float Speed01 => maxRPM > 0.01f ? Mathf.Clamp01(CurrentRPM / maxRPM) : 0f;
        /// <summary>Current speed bonus multiplier actually applied to output (1.0–1.5).</summary>
        public float CurrentSpeedBonusMultiplier => 1f + maxSpeedBonus * Speed01;

        /// <summary>Rated max output with socketed modules applied.</summary>
        public float EffectiveMaxWattOutput => maxWattOutput * ModuleOutputMultiplier;

        public override float PowerOutput
        {
            get
            {
                if (!Enabled || CriticalFailure) return 0f;
                // Output comes from the buffer, which is charged by generation.
                return Mathf.Min(BufferCharge > 0.1f ? GeneratedWatts : 0f, EffectiveMaxWattOutput);
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Maritime Generator";
            if (Mathf.Approximately(maxRPM, 1800f)) maxRPM = 2400f;
            if (Mathf.Approximately(maxWattOutput, 50000f)) maxWattOutput = 500000f;
            if (Mathf.Approximately(bufferCapacityWh, 2000f)) bufferCapacityWh = 20000f;
            if (TemperatureC < GridMaritimeEngine.AmbientTemperatureC)
                TemperatureC = GridMaritimeEngine.AmbientTemperatureC;
            EnsureModuleSlots();
        }

        /// <summary>Ensure (or create) the generator module container.</summary>
        public void EnsureModuleSlots()
        {
            if (ModuleSlots == null) ModuleSlots = new ItemContainer("Module Slots", MaxModuleSlots);
            else ModuleSlots.Resize(MaxModuleSlots);
            ModuleSlots.AcceptFilter = (item, wanted) => CanSocketModule(item) ? wanted : 0;
        }

        /// <summary>Module container accessor that guarantees the container exists (UI use).</summary>
        public ItemContainer GetModuleSlots()
        {
            EnsureModuleSlots();
            return ModuleSlots;
        }

        /// <summary>True when the item may be socketed into this generator's module slots.</summary>
        public bool CanSocketModule(ItemDefinition item)
        {
            return item is EngineModuleItem module && module.worksOnGenerator;
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxRPM;
            node.MaxTorque = 0f; // generator is a pure load sink
            node.GearRatio = 1f;
            node.OutputMultiplier = 1f;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            EnsureModuleSlots();
            RefreshModuleTotals();

            node.FuelAvailable01 = Enabled ? 1f : 0f;
            node.MaxRPM = maxRPM; // speed bonus saturates against rated RPM
            node.OutputMultiplier = ModuleOutputMultiplier;
            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);

            TickThermal(Time.fixedDeltaTime);
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            GeneratedWatts = node.ElectricityOutput;
            CurrentRPM = node.CurrentRPM;

            // Charge the internal buffer from generation, drain it from output.
            float dt = Time.fixedDeltaTime;
            float charge = GeneratedWatts * dt / 3600f; // W·s → Wh
            float drain = Mathf.Min(GeneratedWatts, EffectiveMaxWattOutput) * dt / 3600f;
            BufferCharge = Mathf.Clamp(BufferCharge + charge - drain * 0.5f, 0f, bufferCapacityWh);
        }

        // ══════════════════════════════════════════════════════════════
        //  MODULES
        // ══════════════════════════════════════════════════════════════
        private void RefreshModuleTotals()
        {
            EfficiencyChipCount = 0;
            RadiatorModuleCount = 0;
            float outputBonus = 0f;
            float heatBonus = 0f;
            float dissipationMul = 1f;

            if (ModuleSlots != null)
            {
                for (int i = 0; i < ModuleSlots.Size; i++)
                {
                    var stack = ModuleSlots.GetSlot(i);
                    if (stack == null || stack.IsEmpty) continue;
                    if (stack.item is not EngineModuleItem module) continue;
                    int n = Mathf.Max(1, stack.count);

                    switch (module.moduleKind)
                    {
                        case EngineModuleKind.EfficiencyTuningChip: EfficiencyChipCount += n; break;
                        case EngineModuleKind.SuperCoolerRadiatorJacket: RadiatorModuleCount += n; break;
                    }

                    outputBonus += module.outputPowerBonus * n;
                    heatBonus += module.heatGenerationBonus * n;
                    dissipationMul *= Mathf.Pow(Mathf.Max(1f, module.dissipationMultiplier), n);
                }
            }

            ModuleOutputMultiplier = Mathf.Max(0.05f, 1f + outputBonus);
            _moduleHeatBonus = heatBonus;
            _moduleDissipationMultiplier = dissipationMul;
        }

        private float _moduleHeatBonus;
        private float _moduleDissipationMultiplier = 1f;

        /// <summary>True while an Efficiency Tuning Chip is socketed — coolant flow is mandatory.</summary>
        public bool RequiresActiveCoolantFlow => EfficiencyChipCount > 0;

        // ══════════════════════════════════════════════════════════════
        //  THERMAL MODEL
        // ══════════════════════════════════════════════════════════════
        private void TickThermal(float dt)
        {
            float load01 = EffectiveMaxWattOutput > 0.01f
                ? Mathf.Clamp01(GeneratedWatts / EffectiveMaxWattOutput)
                : 0f;

            // Radiator jackets draw fresh/sea water from grid tanks while generating.
            RadiatorWaterFill01 = 0f;
            RadiatorCoolingActive = false;
            if (RadiatorModuleCount > 0 && load01 > 0.01f)
            {
                float want = GridMaritimeEngine.RadiatorWaterDrawPerModule * RadiatorModuleCount * dt;
                float got = want > 0.0001f ? DrawLiquidFuel(LiquidType.Water, want) : 0f;
                RadiatorWaterFill01 = want > 0.0001f ? Mathf.Clamp01(got / want) : 1f;
                RadiatorCoolingActive = RadiatorWaterFill01 > 0.5f;
            }

            // Coolant: consumed while generating when the chip demands active flow.
            if (load01 > 0.01f)
            {
                RefillCoolant(dt);
                if (HasCoolant)
                    CoolantBuffer = Mathf.Max(0f, CoolantBuffer - coolantConsumptionRate * load01 * dt);
            }

            float heatGen = load01 > 0.01f
                ? baseHeatRate * load01 * (1f + Mathf.Max(0f, _moduleHeatBonus))
                : 0f;

            float dissipation = baseDissipationRate;
            if (HasCoolant) dissipation += coolantDissipationRate;
            if (RadiatorModuleCount > 0 && RadiatorCoolingActive)
                dissipation *= _moduleDissipationMultiplier;

            float net = heatGen - dissipation;

            // Efficiency chip without an active coolant flow → overheat in ~15 s.
            if (load01 > 0.01f && RequiresActiveCoolantFlow && !HasCoolant)
                net += GridMaritimeEngine.EfficiencyChipDryHeatRate;

            TemperatureC = Mathf.Clamp(TemperatureC + net * dt,
                GridMaritimeEngine.AmbientTemperatureC, GridMaritimeEngine.MaxTemperatureC);

            if (TemperatureC >= GridMaritimeEngine.CriticalTemperatureC)
                CriticalFailure = true;
            else if (CriticalFailure && TemperatureC <= GridMaritimeEngine.RecoverTemperatureC)
                CriticalFailure = false;
        }

        /// <summary>Refill coolant from grid tanks. Prefers Marine Engine Coolant, falls back to Water.</summary>
        private void RefillCoolant(float dt)
        {
            float space = coolantCapacity - CoolantBuffer;
            if (space < 0.01f) return;

            float want = Mathf.Min(space, coolantRefillRate * dt);
            float drawn = DrawLiquidFuel(LiquidType.MarineEngineCoolant, want);
            if (drawn <= 0.01f)
                drawn = DrawLiquidFuel(LiquidType.Water, want);
            if (drawn > 0.01f)
                CoolantBuffer += drawn;
        }

        // ══════════════════════════════════════════════════════════════
        //  IGridDataProvider — live data for Grid Screens
        // ══════════════════════════════════════════════════════════════
        public string SourceName => blockName;
        public string DataCategory => "Maritime Generators";
        public string GetDisplayData()
        {
            string status =
                CriticalFailure ? "CRITICAL HEAT — SHUTDOWN" :
                !Enabled ? "OFFLINE" :
                GeneratedWatts > 1f ? "GENERATING" : "IDLE";
            return
                $"GENERATOR {status}\n" +
                $"{PowerFormat.Watts(GeneratedWatts)} ({CurrentRPM:0} RPM)\n" +
                $"SPEED BONUS +{(CurrentSpeedBonusMultiplier - 1f) * 100f:0}%\n" +
                $"BUFFER {BufferFill01 * 100f:0}% ({BufferCharge:0} Wh)\n" +
                $"HEAT {Heat01 * 100f:0}% ({TemperatureC:0}°C)";
        }
    }
}
