// Assets/Scripts/VoxelEngine/GridSystem/GridLandingGear.cs
//
// Landing gear with deploy/retract functionality.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridLandingGear : GridBlock
    {
        [Header("Landing Gear")]
        public bool isDeployed = false;

        public override float PowerDraw => isDeployed ? 15f : 0f;

        public void Deploy()
        {
            isDeployed = true;
            // Add visual/collision changes
        }

        public void Retract()
        {
            isDeployed = false;
        }

        public void Toggle()
        {
            if (isDeployed) Retract();
            else Deploy();
        }
    }
}