// Assets/Scripts/VoxelEngine/Storage/CraftingTerminal.cs
//
// Shows current auto-crafting queue with timers. Allows requesting new crafts.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(PlacedBlock))]
    public class CraftingTerminal : MonoBehaviour
    {
        public float searchRadius = 10f;
        public ServerRack ConnectedRack { get; private set; }
        public AutoCrafter ConnectedCrafter { get; private set; }

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
            var racks = FindObjectsByType<ServerRack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ServerRack best = null; float bestD = searchRadius * searchRadius;
            foreach (var r in racks)
            {
                if (!r.IsOnline) continue;
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = r; }
            }
            ConnectedRack = best;
            ConnectedCrafter = best?.GetComponent<AutoCrafter>();
        }
    }
}
