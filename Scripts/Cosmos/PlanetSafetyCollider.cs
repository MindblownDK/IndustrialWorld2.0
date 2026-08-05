// Assets/Scripts/VoxelEngine/Cosmos/PlanetSafetyCollider.cs
//
// Marker component for the planet-LOD SAFETY colliders (the solid shell + core sphere
// that stop players flying through a planet). Interaction raycasts (crosshair inspection,
// mining, building) must NEVER treat these as real world surfaces — the real terrain is
// the streamed voxel chunks. The marker lets every raycast filter skip them cheaply while
// physics (movement, landing) still collides with them.
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    public class PlanetSafetyCollider : MonoBehaviour
    {
    }
}
