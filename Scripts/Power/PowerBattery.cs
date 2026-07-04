// Assets/Scripts/VoxelEngine/Power/PowerBattery.cs
using UnityEngine;

namespace VoxelEngine.Power
{
    public class PowerBattery : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Battery;

        public float capacityWattHours = 1000f;
        public float charge;
        [Tooltip("Max watts/sec the battery can charge or discharge.")]
        public float ioRate = 200f;
    }
}
