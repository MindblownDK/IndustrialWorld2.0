// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionSourceProfile.cs

using UnityEngine;

namespace VoxelEngine.Environment
{
    /// <summary>
    /// Data-driven direct emission profile. Electricity use is deliberately absent:
    /// power demand is not pollution, while combustion, process loss and routed exhaust are.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Environment/Pollution Source Profile",
        fileName = "PollutionSource_New")]
    public sealed class PollutionSourceProfile : ScriptableObject
    {
        [Header("Identity")]
        public string sourceId = "pollution_source";
        public string displayName = "Pollution Source";

        [Header("Direct output per second at full activity")]
        public PollutionLoad perSecond = new() { airborneSmog = 1f };

        [Header("Release point")]
        [Tooltip("Prefab-local offset of the exhaust/process release point.")]
        public Vector3 localOffset = new(0f, 2f, 0f);
    }
}
