using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Navigation;

namespace IndustrialWorld.Navigation
{
    /// <summary>Explicit coordinate frame for local routes; legacy saves remain cosmic kilometres.</summary>
    public static class RouteCoordinates
    {
        public static bool CanResolve(ShipRoute route) => route != null
            && (route.sceneCoordinates ? SpaceOrigin.Instance == null : SpaceOrigin.Instance != null);

        public static RouteWaypoint Capture(Vector3 position, bool scene)
        {
            var origin = SpaceOrigin.Instance;
            if (scene) return new RouteWaypoint(new double3(position.x, position.y, position.z) / 1000d, null);
            var registry = CosmicRegistry.Instance;
            var body = origin != null && origin.FrameBody != null && registry != null
                ? RouteWaypoint.FindBody(registry, origin.FrameBody.name) : null;
            return new RouteWaypoint(origin.GetCosmicKm(position), body, null, registry);
        }

        public static bool TryResolve(ShipRoute route, int index, out Vector3 position)
        {
            position = Vector3.zero;
            if (!CanResolve(route) || route.waypoints == null || index < 0 || index >= route.waypoints.Count) return false;
            var point = route.waypoints[index];
            if (!route.sceneCoordinates && !string.IsNullOrEmpty(point.bodyId)
                && RouteWaypoint.FindBody(CosmicRegistry.Instance, point.bodyId) == null) return false;
            double3 km = route.sceneCoordinates ? point.positionKm : point.ResolvedPositionKm(CosmicRegistry.Instance);
            if (!math.all(math.isfinite(km))) return false;
            position = route.sceneCoordinates ? (Vector3)(float3)(km * 1000d) : SpaceOrigin.Instance.GetScenePos(km);
            return Finite(position);
        }

        public static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
