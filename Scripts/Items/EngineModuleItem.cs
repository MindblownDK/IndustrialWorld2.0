// Assets/Scripts/VoxelEngine/Items/EngineModuleItem.cs
//
// Modular Upgrade Module for maritime engines and maritime generators.
// Modules are socketed into the Module Slots on an engine/generator block
// (opened via right-click panel). Adding high-tier upgrades introduces new
// logistics requirements (coolant, fresh/sea water) modelled by the block at
// runtime — socketing a module is always reversible by taking it out again.

using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>What kind of upgrade a module provides. Exactly one module of each
    /// kind may be socketed per block — the AcceptFilter on the module container
    /// enforces this so stacking modules is a deliberate build choice, never an accident.</summary>
    public enum EngineModuleKind : byte
    {
        /// <summary>+20% output power, +10% RPM cap, +10% fuel use, faster exhaust smoke.</summary>
        HighFlowTurbocharger = 0,
        /// <summary>+40% max output power, -15% fuel use, unlocks the mandatory active-coolant requirement.</summary>
        EfficiencyTuningChip = 1,
        /// <summary>+30% output power, +15% speed cap, +50% heat generation, visibly dirtier exhaust.</summary>
        OverclockedFuelInjectors = 2,
        /// <summary>200% heat dissipation while a continuous fresh/sea water supply is present.</summary>
        SuperCoolerRadiatorJacket = 3,
    }

    [CreateAssetMenu(menuName = "Voxel Engine/Items/Engine Upgrade Module", fileName = "Module_New")]
    public class EngineModuleItem : ItemDefinition
    {
        [Header("Module Identity")]
        public EngineModuleKind moduleKind = EngineModuleKind.HighFlowTurbocharger;

        [Header("Compatibility")]
        [Tooltip("May be socketed into Tier 1 (Crude Inline-4) engines.")]
        public bool worksOnTier1 = true;
        [Tooltip("May be socketed into Tier 2 (Heavy Fuel Oil V8) engines.")]
        public bool worksOnTier2 = true;
        [Tooltip("May be socketed into Tier 3 (MGO Marine V12) engines.")]
        public bool worksOnTier3 = true;
        [Tooltip("May be socketed into Maritime Generators.")]
        public bool worksOnGenerator = false;

        [Header("Output Effects (multiplicative stacking per module kind)")]
        [Tooltip("Extra output power added per socketed module of this kind. 0.20 = +20%.")]
        public float outputPowerBonus = 0f;
        [Tooltip("Extra RPM/speed cap added per socketed module. 0.10 = +10%.")]
        public float speedCapBonus = 0f;
        [Tooltip("Fuel-use modifier per module. -0.15 = 15% less fuel, +0.10 = 10% more.")]
        public float fuelUseModifier = 0f;
        [Tooltip("Extra heat generated per module while running. 0.50 = +50%.")]
        public float heatGenerationBonus = 0f;
        [Tooltip("Heat dissipation multiplier per module. 2.0 = +200% dissipation (total x3).")]
        public float dissipationMultiplier = 1f;

        [Header("Logistics Requirements")]
        [Tooltip("If true, the host REQUIRES an active coolant flow while running or it overheats within ~15 s.")]
        public bool requiresActiveCoolant = false;
        [Tooltip("Litres of fresh/sea water drawn from the grid per second while the host runs (radiator feed). 0 = none.")]
        public float waterDrawLitresPerSec = 0f;
        [Tooltip("Multiplier on exhaust smoke velocity (turbo blow-through). 1 = unchanged.")]
        public float exhaustSmokeVelocityMul = 1f;
        [Tooltip("If true the exhaust turns visibly darker/dirtier while this module is socketed.")]
        public bool dirtyExhaust = false;

        /// <summary>True when this module may be socketed into the given engine tier.</summary>
        public bool IsCompatibleWithTier(Maritime.EngineTier tier) => tier switch
        {
            Maritime.EngineTier.Small  => worksOnTier1,
            Maritime.EngineTier.Medium => worksOnTier2,
            Maritime.EngineTier.Giant  => worksOnTier3,
            _ => false,
        };
    }
}
