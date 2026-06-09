// Assets/Scripts/VoxelEngine/Power/PowerGenerator.cs
using UnityEngine;

namespace VoxelEngine.Power
{
    public class PowerGenerator : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Generator;

        [Tooltip("Watts per second this generator produces while ON.")]
        public float wattsPerSecond = 500f;

        [Tooltip("If false, the generator stops producing (e.g. out of fuel).")]
        public bool isOn = true;
    }
}
