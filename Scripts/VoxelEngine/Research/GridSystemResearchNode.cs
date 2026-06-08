// Assets/Scripts/VoxelEngine/Research/GridSystemResearchNode.cs
//
// New research node for the entire Grid Building system.
// Unlocks all grid blocks, thrusters, and ship construction.

using UnityEngine;

namespace VoxelEngine.Research
{
    [CreateAssetMenu(menuName = "Voxel Engine/Research/Grid System Node")]
    public class GridSystemResearchNode : ResearchNode
    {
        public override string DisplayName => "Grid Construction";
        public override string Description => "Unlocks Small & Large grid building, thrusters, reactors, tanks, weapons, and full ship construction.";

        public override void OnResearched()
        {
            base.OnResearched();
            Debug.Log("[Research] Grid System unlocked! All grid blocks and ship systems are now available.");
        }
    }
}