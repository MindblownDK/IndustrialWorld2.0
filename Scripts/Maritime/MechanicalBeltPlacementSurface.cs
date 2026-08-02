// Assets/Scripts/VoxelEngine/Maritime/MechanicalBeltPlacementSurface.cs
//
// Invisible trigger surface generated over a Mechanical Belt run. GridBuilder
// queries this surface only while the player holds a shaft-style grid block,
// letting the player aim directly at the belt to add a new powered take-off.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    [DisallowMultipleComponent]
    public sealed class MechanicalBeltPlacementSurface : MonoBehaviour
    {
        private MechanicalBeltNetwork _network;
        private Vector3Int _endpointA;
        private Vector3Int _endpointB;

        public void Configure(MechanicalBeltNetwork network, Vector3Int endpointA, Vector3Int endpointB)
        {
            _network = network;
            _endpointA = endpointA;
            _endpointB = endpointB;
        }

        /// <summary>Resolves an exact empty belt-tap cell for a held shaft/housing.</summary>
        public bool TryGetShaftPlacement(GridBlockItem item, Vector3 worldHit,
            out GridEntity grid, out Vector3Int gridPos, out Vector3 worldPosition,
            out Quaternion rotation, out string failure)
        {
            if (_network != null)
            {
                return _network.TryGetBeltTapPlacement(_endpointA, _endpointB, item, worldHit,
                    out grid, out gridPos, out worldPosition, out rotation, out failure);
            }

            grid = null;
            gridPos = default;
            worldPosition = default;
            rotation = Quaternion.identity;
            failure = "That belt link is no longer available.";
            return false;
        }
    }
}
