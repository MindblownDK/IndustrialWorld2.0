using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Navigation;

namespace IndustrialWorld.Navigation
{
    public static class RoadNavigationAnchor
    {
        // The construction pivot is not the vehicle position: it can be underground or far
        // outside the finished chassis. Tyre contacts are authoritative for road recording.
        public static Vector3 ForGrid(GridEntity grid, RouteTravelMode mode)
        {
            if (grid == null) return Vector3.zero;
            if (mode == RouteTravelMode.Road || mode == RouteTravelMode.RoadNetwork)
            {
                Vector3 sum = Vector3.zero; int count = 0;
                foreach (var block in grid.AllBlocks)
                    if (block is GridWheel wheel && wheel.Enabled && wheel.IsGrounded && wheel.GroundRoad != null)
                    { sum += wheel.GroundPoint; count++; }
                if (count > 0) return sum / count;
            }
            return grid.Body != null ? grid.Body.worldCenterOfMass : grid.transform.position;
        }

        public static Vector3 SurfaceCentre(AsphaltRoad road)
        {
            if (road == null) return Vector3.zero;
            Vector3 point = road.hasExplicitFootprint
                ? road.transform.TransformPoint((road.quadSW + road.quadSE + road.quadNE + road.quadNW) * 0.25f)
                : road.transform.position;
            float offset = road.SurfaceOffset(point, road.transform.up);
            return float.IsNaN(offset) || float.IsInfinity(offset) ? point : point - road.transform.up * offset;
        }

        public static Vector3 SurfacePoint(AsphaltRoad road, Vector3 point)
        {
            float offset = road.SurfaceOffset(point, road.transform.up);
            return float.IsNaN(offset) || float.IsInfinity(offset) ? SurfaceCentre(road) : point - road.transform.up * offset;
        }
    }
}
