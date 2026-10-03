// Assets/Scripts/VoxelEngine/Storage/PatternTerminal.cs
//
// Lets the player define crafting patterns for the auto-crafter.
// 14.40.0: connects by Data Pipe or touch (network membership), never radius.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class PatternTerminal : MonoBehaviour
    {
        // 14.40.0: legacy radius kept for prefab/setup compatibility -
        // connectivity is network membership (Data Pipes / touching) now.
        [HideInInspector] public float searchRadius = 10f;

        public ServerRack ConnectedRack
        {
            get
            {
                if (Time.time - _resolveTime > 1f)
                {
                    _resolved = StorageNetwork.ControllerOf(this);
                    _resolveTime = Time.time;
                }
                return _resolved;
            }
        }

        /// <summary>The controller's auto-craft engine (14.43.0: owns the
        /// pattern bank the encode flow files into).</summary>
        public AutoCrafter ConnectedCrafter => ConnectedRack != null
            ? ConnectedRack.GetComponent<AutoCrafter>() : null;

        private ServerRack _resolved;
        private float _resolveTime = -999f;
    }
}
