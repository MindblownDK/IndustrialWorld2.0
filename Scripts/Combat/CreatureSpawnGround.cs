// Assets/Scripts/VoxelEngine/Combat/CreatureSpawnGround.cs
//
// 17.27.0-dev - ONE surface solve for everything that spawns a body on the ground.
//
// Hostile waves and passive herds both used to do this themselves: a short ray from
// just above the anchor, first hit wins, then stand the prefab 1.2 m along the radial
// up from that point. That combination is why mobs appeared under the surface -
//   1. "first hit" could be a cave ceiling, a deck, a hull or a ledge above the anchor,
//   2. the 12 m ray could not reach the real surface at all, so a cave floor answered,
//   3. a fixed 1.2 m radial offset on a slope or a cliff face is a point INSIDE the
//      terrain, so the capsule spawned embedded and got pushed out downward.
// Here the probe starts high, keeps the SHALLOWEST walkable hit, insists that the hit
// is the world's own terrain (on sphere worlds, to within 20 cm of the analytic
// ground sample) and that the footing is dry, and finally returns a stand point with
// the creature's own collider bottom a hand's width clear of the ground - so the body
// drops onto the surface instead of into it.
using UnityEngine;

namespace VoxelEngine.Combat
{
    public static class CreatureSpawnGround
    {
        /// <summary>How far above the requested anchor the probe starts. Tall enough to
        /// clear a cave layer or a tree canopy, so the surface - not a roof - is found.</summary>
        public const float ProbeAbove = 24f;
        /// <summary>How far below the anchor the probe is allowed to search for ground.</summary>
        public const float ProbeBelow = 16f;
        /// <summary>A surface this far off the world's own ground sample is not the
        /// terrain: it is a prop, a machine or an interior floor. Rejected on planets.</summary>
        public const float TerrainMatchTolerance = 0.2f;
        /// <summary>Local-up component of the surface normal that still counts as ground
        /// a creature can stand on. Below it the hit is a wall, a ceiling or an underside.</summary>
        public const float MinWalkableNormal = 0.55f;
        /// <summary>Clearance kept under the creature's collider bottom, in metres.</summary>
        public const float StandClearance = 0.35f;

        private static readonly RaycastHit[] s_probe = new RaycastHit[32];

        /// <summary>Resolves the terrain under <paramref name="anchor"/> along
        /// <paramref name="up"/>. Returns false when there is no world ground there.</summary>
        public static bool TryResolve(Vector3 anchor, Vector3 up, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = anchor;
            surfaceNormal = up;
            if (up.sqrMagnitude < 0.0001f) return false;
            up = up.normalized;
            surfaceNormal = up;

            Vector3 origin = anchor + up * ProbeAbove;
            float reach = ProbeAbove + ProbeBelow;
            int count = Physics.RaycastNonAlloc(origin, -up, s_probe, reach, ~0, QueryTriggerInteraction.Ignore);

            float bestDistance = float.PositiveInfinity;
            RaycastHit best = default;
            for (int i = 0; i < count; i++)
            {
                var hit = s_probe[i];
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<VoxelEngine.GridSystem.GridEntity>() != null) continue;
                if (Vector3.Dot(hit.normal, up) < MinWalkableNormal) continue;
                if (hit.distance >= bestDistance) continue;      // shallowest wins: that IS the surface
                bestDistance = hit.distance;
                best = hit;
            }
            if (bestDistance == float.PositiveInfinity) return false;

            surfacePoint = best.point;
            Vector3 hitNormal = best.normal.normalized;
            if (Vector3.Dot(hitNormal, up) > 0.05f) surfaceNormal = hitNormal;

            if (VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld sphere && sphere.body != null)
            {
                Vector3 local = sphere.body.transform.InverseTransformPoint(best.point);
                Vector3 normal = local.normalized;
                if (!sphere.TryGroundSurface(ref local, ref normal)) return false;
                Vector3 terrainPoint = sphere.body.transform.TransformPoint(local);
                if (Vector3.Distance(terrainPoint, best.point) > TerrainMatchTolerance) return false;
                if (!sphere.IsDryFooting(terrainPoint)) return false;

                surfacePoint = terrainPoint;
                Vector3 terrainNormal = sphere.body.transform.TransformDirection(normal).normalized;
                if (Vector3.Dot(terrainNormal, up) > 0.05f) surfaceNormal = terrainNormal;
            }

            return true;
        }

        /// <summary>How far above its feet a prefab's pivot must sit so that its lowest
        /// real collider clears the ground. Prefabs pivoted at the feet return a small
        /// drop, centre-pivoted ones return half a body - instead of a flat 1.2 m for both.</summary>
        public static float StandOffsetFor(GameObject prefab)
        {
            if (prefab == null) return 1.2f;
            var colliders = prefab.GetComponentsInChildren<Collider>(true);
            bool any = false;
            float lowest = 0f;
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null || !col.enabled || col.isTrigger) continue;
                float minY = col.bounds.min.y;
                if (!any || minY < lowest) { lowest = minY; any = true; }
            }
            if (!any) return 1.2f;
            return Mathf.Clamp(-lowest + StandClearance, 0.15f, 3f);
        }

        /// <summary>Finds a stand point at or above <paramref name="surfacePoint"/> where
        /// the creature's own body fits clear of geometry. Slopes and steps used to eat
        /// half the capsule; a body that cannot fit anywhere within a metre and a half is
        /// refused rather than buried.</summary>
        public static bool TryFindClearStandPoint(GameObject prefab, Vector3 surfacePoint, Vector3 up,
            out Vector3 standPoint)
        {
            standPoint = surfacePoint;
            if (up.sqrMagnitude < 0.0001f) return false;
            up = up.normalized;

            GetBodySize(prefab, out float radius, out float height);
            float baseOffset = StandOffsetFor(prefab);

            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector3 candidate = surfacePoint + up * (baseOffset + attempt * 0.4f);
                if (IsBodyVolumeClear(candidate, up, radius, height))
                {
                    standPoint = candidate;
                    return true;
                }
            }
            return false;
        }

        private static bool IsBodyVolumeClear(Vector3 standPoint, Vector3 up, float radius, float height)
        {
            float half = Mathf.Max(0.05f, height * 0.5f - radius);
            Vector3 bottom = standPoint + up * radius;
            Vector3 top = standPoint + up * (radius + 2f * half);
            return !Physics.CheckCapsule(bottom, top, radius * 0.95f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Body size from the prefab's own colliders, with a humanoid default.</summary>
        private static void GetBodySize(GameObject prefab, out float radius, out float height)
        {
            radius = 0.4f;
            height = 1.8f;
            if (prefab == null) return;

            var capsule = prefab.GetComponentInChildren<CapsuleCollider>(true);
            if (capsule != null && capsule.enabled && !capsule.isTrigger)
            {
                float scale = Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x),
                                        Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.y),
                                                  Mathf.Abs(capsule.transform.lossyScale.z)));
                radius = Mathf.Max(0.15f, capsule.radius * scale);
                height = Mathf.Max(0.5f, capsule.height * scale);
                return;
            }

            var colliders = prefab.GetComponentsInChildren<Collider>(true);
            bool any = false;
            Bounds bounds = default;
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null || !col.enabled || col.isTrigger) continue;
                if (!any) { bounds = col.bounds; any = true; }
                else bounds.Encapsulate(col.bounds);
            }
            if (!any) return;
            radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.15f, 1.5f);
            height = Mathf.Clamp(bounds.size.y, 0.5f, 4f);
        }
    }
}
