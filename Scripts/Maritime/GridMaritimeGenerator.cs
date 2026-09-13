// Assets/Scripts/VoxelEngine/Maritime/GridMaritimeGenerator.cs
//
// Maritime Generator (2×2×2) — converts shaft torque into electricity.
// Attached to the END of a propulsion chain (after a gearbox for best
// efficiency: more speed = more power at the generator).
//
// The MaritimePropagationJob computes:
//   ElectricityOutput = shaftTorque × shaftRPM × (2π/60) × efficiency × speedBonus × modules
//
// v9.56.0-dev — Giant Diesel to generator fix:
//   • Speed bonus now yields up to +50% at rated RPM (rated * speed01*(1+bonus*speed01)).
//     Previously speedCurve = speed01*(1+bonus*speed01)/(1+bonus) gave only 41% at half speed,
//     making 1200 RPM Giant -> 2400 RPM generator weak. New curve gives 62.5% at half speed
//     and full +50% bonus at rated, so direct-drive Giant still useful and 2:1 gearbox = full bonus.
//   • PowerOutput now allows up to EffectiveMax * (1+maxSpeedBonus) to expose the bonus.
//   • SelfHeat uses bonus-inclusive denominator so heat scales correctly.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    public class GridMaritimeGenerator : MaritimeBlockBase, IGridDataProvider, VoxelEngine.Thermal.IHeatSourceBlock
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Generator;

        [Header("Generator")]
        [Tooltip("Max RPM this generator can accept.")]
        public float maxRPM = 1800f;
        [Tooltip("Max electrical output (W). Excess shaft power is clipped, plus speed bonus.")]
        public float maxWattOutput = 50000f;

        [Header("Speed Bonus")]
        [Tooltip("Extra output gained at rated RPM. 0.5 = up to +50% more power at full rated speed.")]
        [Range(0f, 1f)] public float maxSpeedBonus = 0.5f;

        [Header("Internal Battery Buffer")]
        [Tooltip("Small internal battery that smooths output (Wh).")]
        public float bufferCapacityWh = 2000f;
        [Tooltip("Current battery buffer level (Wh).")]
        public float BufferCharge { get; private set; }
        public float BufferFill01 => bufferCapacityWh > 0f ? Mathf.Clamp01(BufferCharge / bufferCapacityWh) : 0f;

        [Header("Coolant (unlocked by Efficiency Tuning Chip)")]
        [Tooltip("Internal coolant buffer capacity (litres).")]
        public float coolantCapacity = 40f;
        [Tooltip("Coolant consumed per second at full load (L/s).")]
        public float coolantConsumptionRate = 0.2f;
        [Tooltip("Coolant pulled from grid tanks per second when refilling.")]
        public float coolantRefillRate = 6f;
        public float CoolantBuffer { get; private set; }
        public float CoolantFill01 => coolantCapacity > 0f ? Mathf.Clamp01(CoolantBuffer / coolantCapacity) : 0f;
        public bool HasCoolant => CoolantBuffer > 0.01f;

        [Header("Thermal Management")]
        [Tooltip("Heat generated per second at full electrical load (°C/s).")]
        public float baseHeatRate = 1.6f;
        [Tooltip("Passive heat dissipation per second (°C/s).")]
        public float baseDissipationRate = 1.2f;
        [Tooltip("Extra dissipation per second (°C/s) while coolant is flowing.")]
        public float coolantDissipationRate = 2.2f;
        public float TemperatureC { get; private set; } = GridMaritimeEngine.AmbientTemperatureC;
        public bool CriticalFailure { get; private set; }
        public float Heat01 => Mathf.Clamp01(TemperatureC / GridMaritimeEngine.CriticalTemperatureC);

        public float SelfHeatC
        {
            get
            {
                if (!Enabled) return 0f;
                float maxWithBonus = EffectiveMaxWattOutput * (1f + Mathf.Max(0f, maxSpeedBonus));
                float load = maxWithBonus > 0.01f ? Mathf.Clamp01(GeneratedWatts / maxWithBonus) : 0f;
                float heat = VoxelEngine.Thermal.ThermalRules.MaritimeGeneratorSelfHeatC * load;
                if (CriticalFailure) heat = Mathf.Max(heat, VoxelEngine.Thermal.ThermalRules.MaritimeGeneratorSelfHeatC * 0.6f * Heat01);
                return heat;
            }
        }

        public float NeighbourHeatC
        {
            get
            {
                float self = SelfHeatC;
                if (self <= 0f) return 0f;
                return self * (VoxelEngine.Thermal.ThermalRules.MaritimeGeneratorNeighbourHeatC
                               / VoxelEngine.Thermal.ThermalRules.MaritimeGeneratorSelfHeatC);
            }
        }
        public bool IsCriticalHeat => CriticalFailure;

        public ItemContainer ModuleSlots { get; private set; }
        public const int MaxModuleSlots = 2;

        public int EfficiencyChipCount { get; private set; }
        public int RadiatorModuleCount { get; private set; }
        public float ModuleOutputMultiplier { get; private set; } = 1f;
        public bool RadiatorCoolingActive { get; private set; }
        public float RadiatorWaterFill01 { get; private set; }

        public float GeneratedWatts { get; private set; }
        public float CurrentRPM { get; private set; }
        public float Speed01 => maxRPM > 0.01f ? Mathf.Clamp01(CurrentRPM / maxRPM) : 0f;
        public float CurrentSpeedBonusMultiplier => 1f + maxSpeedBonus * Speed01;

        public float EffectiveMaxWattOutput => maxWattOutput * ModuleOutputMultiplier;
        public float EffectiveMaxWithBonus => EffectiveMaxWattOutput * (1f + Mathf.Max(0f, maxSpeedBonus));

        public override float PowerOutput
        {
            get
            {
                if (!Enabled || CriticalFailure) return 0f;
                // v9.56: allow bonus up to +50% so Giant 1200 RPM direct + gearbox yields full advertised power
                float cap = EffectiveMaxWithBonus;
                return Mathf.Min(BufferCharge > 0.1f ? GeneratedWatts : 0f, cap);
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            // v9.56.2-dev — fully dynamic: preserves live prefab stats for balancing.
            // Only upgrades very old sentinel values (50kW/1800RPM/2000Wh) to current tier defaults.
            // Any custom balanced value (e.g., 600kW, 2000RPM, 25000Wh) is kept as-is and flows
            // into MechanicalPropagationJob and PilotRouteAssessment dynamically.
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Maritime Generator";
            if (Mathf.Approximately(maxRPM, 1800f)) maxRPM = 2400f;
            if (Mathf.Approximately(maxWattOutput, 50000f)) maxWattOutput = 500000f;
            if (Mathf.Approximately(bufferCapacityWh, 2000f)) bufferCapacityWh = 20000f;
            if (TemperatureC < GridMaritimeEngine.AmbientTemperatureC)
                TemperatureC = GridMaritimeEngine.AmbientTemperatureC;
            EnsureModuleSlots();
        }

        public void EnsureModuleSlots()
        {
            if (ModuleSlots == null) ModuleSlots = new ItemContainer("Module Slots", MaxModuleSlots);
            else ModuleSlots.Resize(MaxModuleSlots);
            ModuleSlots.AcceptFilter = (item, wanted) => CanSocketModule(item) ? wanted : 0;
        }

        public ItemContainer GetModuleSlots()
        {
            EnsureModuleSlots();
            return ModuleSlots;
        }

        public bool CanSocketModule(ItemDefinition item)
        {
            return item is EngineModuleItem module && module.worksOnGenerator;
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.MaxRPM = maxRPM;
            node.MaxTorque = 0f;
            node.GearRatio = 1f;
            node.OutputMultiplier = 1f;
            node.RatedElectricalOutputWatts = Mathf.Max(0f, maxWattOutput);
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            EnsureModuleSlots();
            RefreshModuleTotals();

            node.FuelAvailable01 = Enabled ? 1f : 0f;
            node.MaxRPM = maxRPM;
            node.OutputMultiplier = ModuleOutputMultiplier;
            node.RatedElectricalOutputWatts = Mathf.Max(0f, EffectiveMaxWattOutput);
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

            float dt = Time.fixedDeltaTime;
            float charge = GeneratedWatts * dt / 3600f;
            float drain = Mathf.Min(GeneratedWatts, EffectiveMaxWithBonus) * dt / 3600f;
            BufferCharge = Mathf.Clamp(BufferCharge + charge - drain * 0.5f, 0f, bufferCapacityWh);
        }

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

        public bool RequiresActiveCoolantFlow => EfficiencyChipCount > 0;

        private void TickThermal(float dt)
        {
            float maxWithBonus = EffectiveMaxWithBonus;
            float load01 = maxWithBonus > 0.01f
                ? Mathf.Clamp01(GeneratedWatts / maxWithBonus)
                : 0f;

            RadiatorWaterFill01 = 0f;
            RadiatorCoolingActive = false;
            if (RadiatorModuleCount > 0 && load01 > 0.01f)
            {
                float want = GridMaritimeEngine.RadiatorWaterDrawPerModule * RadiatorModuleCount * dt;
                float got = want > 0.0001f ? DrawLiquidFuel(LiquidType.Water, want) : 0f;
                RadiatorWaterFill01 = want > 0.0001f ? Mathf.Clamp01(got / want) : 1f;
                RadiatorCoolingActive = RadiatorWaterFill01 > 0.5f;
            }

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

            if (load01 > 0.01f && RequiresActiveCoolantFlow && !HasCoolant)
                net += GridMaritimeEngine.EfficiencyChipDryHeatRate;

            TemperatureC = Mathf.Clamp(TemperatureC + net * dt,
                GridMaritimeEngine.AmbientTemperatureC, GridMaritimeEngine.MaxTemperatureC);

            if (TemperatureC >= GridMaritimeEngine.CriticalTemperatureC)
                CriticalFailure = true;
            else if (CriticalFailure && TemperatureC <= GridMaritimeEngine.RecoverTemperatureC)
                CriticalFailure = false;
        }

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
                $"SPEED BONUS +{(CurrentSpeedBonusMultiplier - 1f) * 100f:0}% (max {EffectiveMaxWithBonus:0} W)\n" +
                $"BUFFER {BufferFill01 * 100f:0}% ({BufferCharge:0} Wh)\n" +
                $"HEAT {Heat01 * 100f:0}% ({TemperatureC:0}°C)";
        }
    }
}
