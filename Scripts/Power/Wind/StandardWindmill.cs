// Assets/Scripts/VoxelEngine/Power/Wind/StandardWindmill.cs
// Concrete 3-size standard windmill (Vestas-inspired).
// Uses WindmillAssembly for full multi-part build sequence.
// Small, Medium, Large (V236 offshore capable).
// Fully stationary, non-grid. Beautiful rotor + customizable max power.

using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Power.Wind
{
    public class StandardWindmill : WindmillBase
    {
        [Header("Size Config")]
        public WindmillDefinition.SizeCategory size = WindmillDefinition.SizeCategory.Small;

        private WindmillAssembly _assembly;

        protected override void Awake()
        {
            base.Awake();
            _assembly = GetComponent<WindmillAssembly>();
            if (_assembly == null) _assembly = gameObject.AddComponent<WindmillAssembly>();

            // Apply size-based definition
            ApplySizeDefinition();
            _assembly.SetDefinition(definition);
            _assembly.windmillType = WindmillType.Standard;

            // Large ones get climbable interior + higher connect radius
            if (size == WindmillDefinition.SizeCategory.Large)
            {
                connectRadius = 55f;
                _assembly.definition.hasClimbableInterior = true;
            }
        }

        private void ApplySizeDefinition()
        {
            if (definition != null) return;

            definition = ScriptableObject.CreateInstance<WindmillDefinition>();
            definition.type = WindmillDefinition.WindmillType.Standard;
            definition.size = size;

            switch (size)
            {
                case WindmillDefinition.SizeCategory.Small:
                    definition.definitionId = "standard_small";
                    definition.displayName = "Small Standard Windmill (Vestas V82)";
                    definition.maxPowerWatts = 2500000f;   // ~2.5 MW
                    definition.towerHeight = 78f;
                    definition.rotorDiameter = 82f;
                    definition.maxEffectiveHeight = 110f;
                    definition.heightBonusPerMeter = 0.007f;
                    definition.requiresWings = 3;
                    definition.hasClimbableInterior = false;
                    break;

                case WindmillDefinition.SizeCategory.Medium:
                    definition.definitionId = "standard_medium";
                    definition.displayName = "Medium Standard Windmill (Vestas V150)";
                    definition.maxPowerWatts = 6500000f;   // ~6.5 MW
                    definition.towerHeight = 105f;
                    definition.rotorDiameter = 150f;
                    definition.maxEffectiveHeight = 160f;
                    definition.heightBonusPerMeter = 0.009f;
                    definition.hasClimbableInterior = false;
                    break;

                case WindmillDefinition.SizeCategory.Large:
                    definition.definitionId = "standard_large_v236";
                    definition.displayName = "Large Offshore Windmill (Vestas V236)";
                    definition.maxPowerWatts = 15000000f;  // 15 MW
                    definition.towerHeight = 162f;
                    definition.rotorDiameter = 236f;
                    definition.maxEffectiveHeight = 220f;
                    definition.heightBonusPerMeter = 0.011f;
                    definition.supportsWaterPlacement = true;
                    definition.hasClimbableInterior = true;
                    definition.requiresGearbox = true;
                    definition.requiresGenerator = true;
                    break;
            }

            // Link power value
            wattsPerSecond = definition.maxPowerWatts;
        }

        protected override void Update()
        {
            if (_assembly != null && !_assembly.IsFullyAssembled())
            {
                if (powerGenRef != null) powerGenRef.wattsPerSecond = 0;
                return;
            }
            base.Update();
        }

        // Called by player interaction or build system after placement
        public void BeginAssembly()
        {
            if (_assembly != null)
                _assembly.standardStage = WindmillAssembly.AssemblyStage.Placed;
        }
    }
}
