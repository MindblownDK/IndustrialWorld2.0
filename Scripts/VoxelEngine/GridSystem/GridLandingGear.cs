// Assets/Scripts/VoxelEngine/GridSystem/GridLandingGear.cs
//
// Landing gear for safe planetary landings.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridLandingGear : GridBlock
    {
        [Header("Landing Gear")]
        public bool isDeployed = false;

        public override float PowerDraw => isDeployed ? 15f : 0f;

        public void Toggle()
        {
            isDeployed = !isDeployed;
            // Add visual/collision changes here
        }
    }
}