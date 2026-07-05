// Assets/Scripts/VoxelEngine/Power/Wind/WindmillDefinition.cs
using UnityEngine;

namespace VoxelEngine.Power.Wind
{
    [CreateAssetMenu(menuName = "Voxel Engine/Power/Windmill Definition", fileName = "WindmillDef_New")]
    public class WindmillDefinition : ScriptableObject
    {
        public enum WindmillType { Standard, HelixVertical }
        public enum SizeCategory { Small, Medium, Large }
        public enum WingSize { Small, Large }

        [Header("Identity")]
        public string definitionId = "standard_small";
        public string displayName = "Small Standard Windmill";
        public WindmillType type = WindmillType.Standard;
        public SizeCategory size = SizeCategory.Small;

        [Header("Power & Efficiency")]
        [Tooltip("Base max power in Watts (Vestas inspired)")]
        public float maxPowerWatts = 2500000f;
        public float maxEffectiveHeight = 120f;
        public float heightBonusPerMeter = 0.008f;

        [Header("Physical")]
        public float towerHeight = 80f;
        public float nacelleScale = 1f;
        public float rotorDiameter = 80f;

        [Header("Assembly")]
        public bool requiresTower = true;
        public bool requiresNacelle = true;
        public bool requiresHub = true;
        public int requiredWings = 3;
        public bool requiresGearbox = true;
        public bool requiresGenerator = true;

        [Header("Placement & Interior")]
        public bool supportsWaterPlacement = false;
        public bool hasClimbableInterior = false;

        public float GetEffectiveMaxPower(float windSpeed, float worldHeight, bool obstructed)
        {
            float speedFactor = Mathf.Pow(Mathf.Clamp(windSpeed, 0f, 30f) / 12f, 3f);
            float heightMult = 1f + Mathf.Min(worldHeight, maxEffectiveHeight) * heightBonusPerMeter;
            float obstructionMult = obstructed ? 0.45f : 1f;
            return maxPowerWatts * speedFactor * heightMult * obstructionMult;
        }
    }
}
