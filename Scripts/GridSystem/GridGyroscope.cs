// Assets/Scripts/VoxelEngine/GridSystem/GridGyroscope.cs
//
// Gyroscope — provides rotational control (yaw/pitch/roll) to the grid, like a
// Space Engineers gyro. The grid sums the torque of all enabled gyroscopes.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGyroscope : GridBlock
    {
        [Header("Gyroscope")]
        [Tooltip("Rotational torque this gyro contributes.")]
        public float torquePower = 80000f;

        [Tooltip("Power drawn while spun up but idle (stabilisation).")]
        public float idleWatts = 30f;
        [Tooltip("Extra power drawn while actively rotating the ship.")]
        public float activeWatts = 120f;

        public override float PowerDraw
        {
            get
            {
                if (!Enabled) return 0f;
                bool turning = Grid != null &&
                    (Mathf.Abs(Grid.RotationYaw) + Mathf.Abs(Grid.RotationPitch) + Mathf.Abs(Grid.RotationRoll)) > 0.01f;
                return turning ? idleWatts + activeWatts : idleWatts;
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Gyroscope";
        }
    }
}
