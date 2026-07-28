// Assets/Scripts/VoxelEngine/Power/ElectricalPipeDefinition.cs
using UnityEngine;

namespace VoxelEngine.Power
{
    /// <summary>
    /// One electrical pipe tier (e.g. Copper / Iron / Gold / Superconductor).
    /// Used for grid-placed blocks that transmit power.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Power/Electrical Pipe Definition", fileName = "Pipe_New")]
    public class ElectricalPipeDefinition : ScriptableObject
    {
        public string displayName = "Copper Electrical Pipe";
        [Tooltip("Maximum watts per second this pipe can transmit.")]
        public float capacityWatts = 10000f;
        [Tooltip("Visual tint of placed pipes of this tier.")]
        public Color tint = new Color(0.85f, 0.45f, 0.20f);
        [Tooltip("Distance at which adjacent pipes/devices auto-connect.")]
        public float connectRadius = 1.6f;
    }
}
