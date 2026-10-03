// Assets/Scripts/VoxelEngine/Storage/CraftingTerminal.cs
//
// Shows current auto-crafting queue with timers. Allows requesting new crafts.
// 14.40.0: connects by Data Pipe or touch (network membership), never radius.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class CraftingTerminal : MonoBehaviour
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

        public AutoCrafter ConnectedCrafter => ConnectedRack != null
            ? ConnectedRack.GetComponent<AutoCrafter>() : null;

        private ServerRack _resolved;
        private float _resolveTime = -999f;
    }
}
