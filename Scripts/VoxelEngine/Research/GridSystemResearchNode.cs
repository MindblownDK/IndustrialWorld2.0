// Assets/Scripts/VoxelEngine/Research/GridSystemResearchNode.cs
//
// New research node for the entire Grid Building system.
// Unlocks all grid blocks, thrusters, and ship construction.

using UnityEngine;

namespace VoxelEngine.Research
{
    [CreateAssetMenu(menuName = "Voxel Engine/Research/Grid System Node", fileName = "Research_GridSystem")]
    public class GridSystemResearchNode : ResearchNode
    {
        private void OnEnable()
        {
            displayName = "Grid Construction";
            description = "Unlocks Small & Large grid building, thrusters (Atmospheric, Hydrogen, Ion, LiquidFuel), reactors, tanks, weapons, grinders, and full ship construction.";
            category = ResearchCategory.Environment;
            subCategory = ResearchSubCategory.Building;
            tier = 4;
            researchSeconds = 120f;
        }
    }
}