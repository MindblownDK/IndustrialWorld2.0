// Assets/Scripts/VoxelEngine/Storage/PatternTerminal.cs
//
// Lets the player define crafting patterns for the auto-crafter.
// Player places recipe ingredients in a grid → creates a pattern stored in server RAM.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class PatternTerminal : MonoBehaviour
    {
        public float searchRadius = 10f;
        public ServerRack ConnectedRack { get; private set; }

        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 2f) return;
            _timer = 0;
            FindRack();
        }

        private void FindRack()
        {
            var racks = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude);
            ServerRack best = null; float bestD = searchRadius * searchRadius;
            foreach (var r in racks)
            {
                if (!r.IsOnline) continue;
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = r; }
            }
            ConnectedRack = best;
        }

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
