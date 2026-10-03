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

        private ServerRack _resolved;
        private float _resolveTime = -999f;

        /// <summary>Try to add a recipe pattern. Returns true if added.</summary>
        public bool TryAddPattern(RecipeDefinition recipe)
        {
            if (ConnectedRack == null) return false;
            var crafter = ConnectedRack.GetComponent<AutoCrafter>();
            if (crafter == null) return false;
            return crafter.AddPattern(recipe);
        }
    }
}
