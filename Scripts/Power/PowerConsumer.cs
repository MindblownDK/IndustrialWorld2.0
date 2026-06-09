// Assets/Scripts/VoxelEngine/Power/PowerConsumer.cs
using UnityEngine;

namespace VoxelEngine.Power
{
    public class PowerConsumer : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Consumer;

        [Tooltip("Watts per second this consumer needs to be considered powered.")]
        public float wattsPerSecond = 100f;

        // Read by other systems (e.g. Furnace) to pause when no power is available.
        [System.NonSerialized] public bool IsPowered;
    }
}
