// Assets/Scripts/VoxelEngine/Power/Wind/HelixWindmill.cs
// Vertical helix windmill. Two sizes (Small / Large generator).
// Supports cross-size wings: large wings on small gen = no extra power; small wings on large gen = worse efficiency.
// Stationary, non-grid. Beautiful vertical rotor.
// Assembled in two parts.

using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Power.Wind
{
    public class HelixWindmill : WindmillBase
    {
        public enum HelixGeneratorSize { Small, Large }

        [Header("Helix Config")]
        public HelixGeneratorSize generatorSize = HelixGeneratorSize.Small;
        public WindmillDefinition.WingSize wingSize = WindmillDefinition.WingSize.Small;
        public bool wingsInstalled = false;

        private WindmillAssembly _assembly;

        protected override void Awake()
        {
            base.Awake();
            _assembly = GetComponent<WindmillAssembly>();
            if (_assembly == null) _assembly = gameObject.AddComponent<WindmillAssembly>();

            ApplyHelixDefinition();
            _assembly.SetDefinition(definition);
            _assembly.windmillType = WindmillDefinition.WindmillType.HelixVertical;
        }

        private void ApplyHelixDefinition()
        {
            if (definition != null) return;

            definition = ScriptableObject.CreateInstance<WindmillDefinition>();
            definition.type = WindmillDefinition.WindmillType.HelixVertical;

            switch (generatorSize)
            {
                case HelixGeneratorSize.Small:
                    definition.definitionId = "helix_small";
                    definition.displayName = "Small Vertical Helix Windmill";
                    definition.maxPowerWatts = 1200000f; // 1.2 MW
                    definition.towerHeight = 32f;
                    definition.rotorDiameter = 14f;
                    definition.maxEffectiveHeight = 65f;
                    definition.heightBonusPerMeter = 0.006f;
                    break;

                case HelixGeneratorSize.Large:
                    definition.definitionId = "helix_large";
                    definition.displayName = "Large Vertical Helix Windmill";
                    definition.maxPowerWatts = 4800000f; // 4.8 MW
                    definition.towerHeight = 58f;
                    definition.rotorDiameter = 28f;
                    definition.maxEffectiveHeight = 95f;
                    definition.heightBonusPerMeter = 0.0085f;
                    break;
            }

            wattsPerSecond = definition.maxPowerWatts;
        }

        protected override void CalculatePower()
        {
            if (!wingsInstalled || _assembly == null || !_assembly.IsFullyAssembled())
            {
                wattsPerSecond = 0f;
                return;
            }

            base.CalculatePower();

            // Apply cross-size efficiency
            float mismatchPenalty = 1f;
            if (generatorSize == HelixGeneratorSize.Large && wingSize == WindmillDefinition.WingSize.Small)
                mismatchPenalty = 0.58f;   // small wings on large gen = worse

            // Large wings on small gen = no gain (capped already by generator)
            if (generatorSize == HelixGeneratorSize.Small && wingSize == WindmillDefinition.WingSize.Large)
                mismatchPenalty = 1.0f;

            wattsPerSecond *= mismatchPenalty;

            // Update assembly currentEfficiency for visuals
            if (_assembly != null) _assembly.currentEfficiency = mismatchPenalty;
        }

        protected override void Update()
        {
            if (_assembly != null && _assembly.helixStage != WindmillAssembly.HelixStage.WingsInstalled)
            {
                wattsPerSecond = 0;
                return;
            }
            base.Update();
        }

        public void SetWingsSize(WindmillDefinition.WingSize size)
        {
            wingSize = size;
            wingsInstalled = true;
            if (_assembly != null) _assembly.helixStage = WindmillAssembly.HelixStage.WingsInstalled;
        }
    }
}
