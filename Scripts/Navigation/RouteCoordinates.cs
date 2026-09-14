using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Navigation;

namespace IndustrialWorld.Navigation
{
    /// <summary>Explicit coordinate frame for local routes; legacy saves remain cosmic kilometres.</summary>
    public static class RouteCoordinates
    {
        public static bool CanResolve(ShipRoute route)
        {
            if (route == null) return false;
            // v9.56.5-dev: local scene routes (Road/RoadNetwork/Water/Flight) are always scene-local
            // and must resolve even when SpaceOrigin exists (planet with cosmic registry loaded).
            // Legacy cosmic routes try to resolve with SpaceOrigin, but old planet cosmic saves
            // (pre-9.56.4) fallback to scene km*1000 so overlay and diagnostics can show large offset.
            return route.waypoints != null && route.waypoints.Count > 0;
        }

        public static RouteWaypoint Capture(Vector3 position, bool scene)
        {
            var origin = SpaceOrigin.Instance;
            if (scene) return new RouteWaypoint(new double3(position.x, position.y, position.z) / 1000d, null);
            var registry = CosmicRegistry.Instance;
            var body = origin != null && origin.FrameBody != null && registry != null
                ? RouteWaypoint.FindBody(registry, origin.FrameBody.name) : null;
            if (origin == null) return new RouteWaypoint(new double3(position.x, position.y, position.z) / 1000d, null);
            return new RouteWaypoint(origin.GetCosmicKm(position), body, null, registry);
        }

        public static bool TryResolve(ShipRoute route, int index, out Vector3 position)
        {
            position = Vector3.zero;
            if (route == null || route.waypoints == null || index < 0 || index >= route.waypoints.Count) return false;
            var point = route.waypoints[index];
            double3 km;
            try
            {
                if (route.sceneCoordinates)
                {
                    km = point.positionKm;
                }
                else
                {
                    if (!string.IsNullOrEmpty(point.bodyId) && CosmicRegistry.Instance != null)
                    {
                        var body = RouteWaypoint.FindBody(CosmicRegistry.Instance, point.bodyId);
                        if (body != null)
                            km = point.ResolvedPositionKm(CosmicRegistry.Instance);
                        else
                            km = point.positionKm;
                    }
                    else
                    {
                        km = point.positionKm;
                    }
                }
            }
            catch
            {
                km = point.positionKm;
            }
            if (!math.all(math.isfinite(km))) return false;
            try
            {
                if (route.sceneCoordinates)
                    position = (Vector3)(float3)(km * 1000d);
                else
                {
                    var origin = SpaceOrigin.Instance;
                    if (origin != null)
                        position = origin.GetScenePos(km);
                    else
                        position = (Vector3)(float3)(km * 1000d);
                }
            }
            catch
            {
                position = (Vector3)(float3)(km * 1000d);
            }
            return Finite(position);
        }

        public static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
