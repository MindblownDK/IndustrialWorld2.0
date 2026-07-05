// Assets/Scripts/VoxelEngine/Power/Wind/WindmillBase.cs
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Power;

namespace VoxelEngine.Power.Wind
{
    [RequireComponent(typeof(PlacedBlock))]
    public abstract class WindmillBase : PowerGenerator
    {
        [Header("Windmill Definition (Data Driven)")]
        public WindmillDefinition definition;

        [Header("Runtime Overrides")]
        public float runtimeMaxPowerOverride = -1f;

        protected float _currentOutput;

        protected virtual void Awake()
        {
            var placed = GetComponent<PlacedBlock>();
            if (placed != null)
            {
                placed.onGrid = false;
            }

            if (definition == null)
            {
                definition = CreateDefaultDefinition();
            }

            if (runtimeMaxPowerOverride > 0)
                wattsPerSecond = runtimeMaxPowerOverride;
            else if (definition != null)
                wattsPerSecond = definition.maxPowerWatts;

            connectRadius = 28f;
            requireGridAlignedNeighbours = false;
        }

        protected virtual void Update()
        {
            if (!isOn)
            {
                wattsPerSecond = 0;
                return;
            }
            CalculatePower();
        }

        protected virtual void CalculatePower()
        {
            if (WindSystem.Instance == null)
            {
                wattsPerSecond = definition != null ? definition.maxPowerWatts * 0.6f : 1500000f;
                return;
            }

            float windSpeed = WindSystem.Instance.GetWindSpeed();
            float height = transform.position.y;
            bool obstructed = WindSystem.Instance.IsObstructed(transform.position + Vector3.up * 8f, 70f);

            float maxP = runtimeMaxPowerOverride > 0 ? runtimeMaxPowerOverride : (definition != null ? definition.maxPowerWatts : 2500000f);
            float eff = WindSystem.Instance.GetWindEfficiencyMultiplier(height, obstructed, definition != null ? definition.maxEffectiveHeight : 200f);

            _currentOutput = maxP * eff;
            wattsPerSecond = Mathf.Clamp(_currentOutput, 0f, maxP * 1.4f);
        }

        protected WindmillDefinition CreateDefaultDefinition()
        {
            var def = ScriptableObject.CreateInstance<WindmillDefinition>();
            def.definitionId = "default_standard";
            def.displayName = "Standard Windmill";
            def.maxPowerWatts = 2500000f;
            def.maxEffectiveHeight = 150f;
            def.heightBonusPerMeter = 0.008f;
            def.towerHeight = 90f;
            return def;
        }

        public float GetCurrentEfficiency()
        {
            if (WindSystem.Instance == null) return 0.6f;
            return WindSystem.Instance.GetWindEfficiencyMultiplier(transform.position.y, false, 200);
        }
    }
}
