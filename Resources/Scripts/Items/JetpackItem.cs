// Assets/Scripts/VoxelEngine/Items/JetpackItem.cs
//
// Player equipment item for the roadmap Jetpack Families pass. The runtime
// equipment component owns the two jetpack slots; this asset is the data-driven
// definition used by setup, inventory and PlayerController flight checks.

using UnityEngine;

namespace VoxelEngine.Items
{
    public enum JetpackFamily
    {
        HydrogenBoost = 0,
        Atmospheric = 1,
        Hybrid = 2,
    }

    [CreateAssetMenu(menuName = "Voxel Engine/Items/Jetpack Item", fileName = "Jetpack_New")]
    public class JetpackItem : ItemDefinition
    {
        [Header("Jetpack")]
        public JetpackFamily family = JetpackFamily.HydrogenBoost;
        [Tooltip("Base 6DOF movement speed multiplier while this pack enables flight.")]
        public float flightSpeedMultiplier = 1f;
        [Tooltip("Extra sprint/boost multiplier while holding Sprint in fly mode.")]
        public float boostMultiplier = 1f;
        [Tooltip("True when this pack can operate in atmosphere.")]
        public bool supportsAtmosphere = true;
        [Tooltip("True when this pack can operate without atmosphere.")]
        public bool supportsVacuum = false;
        [Tooltip("True for hydrogen-only boost packs. Fuel accounting is added in the later oxygen/fuel pass.")]
        public bool usesHydrogen = false;
        [Tooltip("True for power/ion packs. Energy accounting is added in the later armor/equipment UI pass.")]
        public bool usesPower = false;

        public override bool IsStackable => false;
    }
}
