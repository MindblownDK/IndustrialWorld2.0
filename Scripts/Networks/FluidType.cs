// Assets/Scripts/VoxelEngine/Networks/FluidType.cs
//
// ScriptableObject defining a fluid or gas type. Used by Fluid and Gas pipes.
// Create via: Right-click > Create > Voxel Engine > Networks > Fluid Type

using UnityEngine;

namespace VoxelEngine.Networks
{
    [CreateAssetMenu(menuName = "Voxel Engine/Networks/Fluid Type", fileName = "Fluid_New")]
    public class FluidType : ScriptableObject
    {
        [Header("Identity")]
        public string fluidName = "Water";
        public Color color = new Color(0.15f, 0.55f, 0.85f, 0.8f);

        [Header("Properties")]
        [Tooltip("Lower viscosity = faster flow. Water=1, Oil=3, Lava=8.")]
        [Range(0.1f, 10f)]
        public float viscosity = 1f;

        [Tooltip("True for gases (Steam, Hydrogen, etc). False for liquids.")]
        public bool isGas;
    }
}
