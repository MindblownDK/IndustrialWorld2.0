// Assets/Scripts/VoxelEngine/Power/WireDefinition.cs
using UnityEngine;

namespace VoxelEngine.Power
{
    /// <summary>
    /// One wire tier (e.g. Copper / Iron / Gold / Superconductor). Capacity = max
    /// watts that can flow through ONE cable of this tier per second.
    /// Network bottleneck = the minimum capacity along any cable in that network.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Power/Wire Definition", fileName = "Wire_New")]
    public class WireDefinition : ScriptableObject
    {
        public string displayName = "Copper Wire";
        [Tooltip("Maximum watts per second this wire can transmit.")]
        public float capacityWatts = 1000f;
        [Tooltip("Visual tint of placed cables of this tier.")]
        public Color tint = new Color(0.85f, 0.45f, 0.20f);
        [Tooltip("Distance at which adjacent cables/devices auto-connect.")]
        public float connectRadius = 1.6f;
    }
}
