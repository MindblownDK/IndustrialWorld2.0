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

        [Tooltip("Idle power draw while providing stabilisation.")]
        public float powerDraw = 50f;

        public override float PowerDraw => Enabled ? powerDraw : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Gyroscope";
        }
    }
}
