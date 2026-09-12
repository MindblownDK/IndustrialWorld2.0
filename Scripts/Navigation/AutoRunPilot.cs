using UnityEngine;
using VoxelEngine.Navigation;

namespace IndustrialWorld.Navigation
{
    /// <summary>Placeable control block for unattended road vehicles. The recorder base keeps
    /// existing navigation, placement, item persistence and interaction contracts compatible.</summary>
    public sealed class AutoRunPilot : GridRouteRecorder
    {
        [Header("Auto-Run Control")]
        [Tooltip("Additional control power while this block owns an active wheel run, including departure countdown.")]
        [Min(0f)] public float controlWatts = 40f;

        public override float PowerDraw => base.PowerDraw
            + (Enabled && Grid != null && Grid.WheelAutopilot != null
               && Grid.WheelAutopilot.IsControlledBy(this) ? Mathf.Max(0f, controlWatts) : 0f);
    }
}
