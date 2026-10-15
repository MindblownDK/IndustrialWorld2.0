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
// Here the probe starts high and keeps the SHALLOWEST walkable hit, and finally returns
// a stand point with the creature's own collider bottom a hand's width clear of the
// ground - so the body drops onto the surface instead of into it.
//
// 17.27.7-dev - the world's own ground sample became a CHECK instead of a gate. It used
// to be required ("is this hit the analytic terrain, to within 20 cm?") and that single
// rule refused every real hillside: the mesh raycast and the chunk raycast land half a
// metre apart on one slope while describing the same ground, and a chunk with no mesh
// yet - a fresh load, or one being remeshed right after digging - could not answer at
// all. The rule is now a signed height band along the body's up (MaxAboveGround /
// MaxBelowGround): a prop, a deck or a canopy is metres out, ordinary terrain is not.
// The dry-footing test is now IsSubmerged - fluids only. IsDryFooting is the SCATTER
// rule ("no solid voxel 0.5-2 m overhead"), so using it here refused every slope under
// rock, every cliff and every tree. And a spawner that refuses everything and says
// nothing is unfixable, so refusals are counted and named (one line per ten).
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
        /// <summary>How far ABOVE the world's own ground a candidate may sit and still count
        /// as terrain. A prop, a deck, a hull or a canopy sits a metre or more up; a voxel
        /// stair-step on a hillside sits within one of the slope it belongs to. Measured along
        /// the body's up axis, not as a 3D distance: on steep ground the two raycasts can land
        /// laterally apart while describing the same slope (17.27.7-dev).</summary>
        public const float MaxAboveGround = 0.9f;
        /// <summary>How far BELOW the world's own ground a candidate may sit. Generous, because
        /// a cave mouth or an overhang legitimately answers lower than the analytic sample.</summary>
        public const float MaxBelowGround = 3.0f;
        /// <summary>Local-up component of the surface normal that still counts as ground
        /// a creature can stand on. Below it the hit is a wall, a ceiling or an underside.</summary>
        public const float MinWalkableNormal = 0.55f;
        /// <summary>Clearance kept under the creature's collider bottom, in metres.</summary>
        public const float StandClearance = 0.35f;

        private static readonly RaycastHit[] s_probe = new RaycastHit[32];

        /// <summary>Why the last attempt returned false. Public so a caller can explain it.</summary>
        public enum SpawnRefusal { None, NoUp, NoSurface, NotTerrain, Submerged, NoClearVolume }

        public static SpawnRefusal LastRefusal { get; private set; } = SpawnRefusal.None;

        // ── diagnostics ────────────────────────────────────────────────────────────
        // A spawner that refuses every attempt produces nothing and says nothing, which is
        // how "mobs stopped spawning" becomes an afternoon of guessing. Ten consecutive
        // refusals earn ONE console line naming what was wrong; the moment anything spawns,
        // the counters go quiet again. Ten at the hostile spawner's ten-second cadence is a
        // line every hundred seconds, in a state that is already broken.
        private static readonly int[] s_refusals = new int[6];
        private static int s_consecutiveRefusals;
        private const int RefusalsBeforeReport = 10;

        private static void NoteRefusal(SpawnRefusal reason, Vector3 anchor)
        {
            LastRefusal = reason;
            s_refusals[(int)reason]++;
            s_consecutiveRefusals++;
            if (s_consecutiveRefusals % RefusalsBeforeReport != 0) return;

            string worst = "something else";
            int worstCount = 0;
            for (int i = 0; i < s_refusals.Length; i++)
            {
                if (s_refusals[i] <= worstCount) continue;
                worstCount = s_refusals[i];
                worst = ReasonText((SpawnRefusal)i);
            }
            Debug.LogWarning("[CreatureSpawnGround] No creature has found a stand point in "
                + RefusalsBeforeReport + " attempts (last anchor " + anchor.ToString("F0")
                + "). Most common reason: " + worst + " (" + worstCount + " of "
                + RefusalsBeforeReport + "). " + RefusalHint(worst));
            System.Array.Clear(s_refusals, 0, s_refusals.Length);
        }

        private static void NoteSuccess()
        {
            LastRefusal = SpawnRefusal.None;
            s_consecutiveRefusals = 0;
            System.Array.Clear(s_refusals, 0, s_refusals.Length);
        }

        private static string ReasonText(SpawnRefusal reason) => reason switch
        {
            SpawnRefusal.NoUp => "a degenerate up axis",
            SpawnRefusal.NoSurface => "no walkable surface under the anchor",
            SpawnRefusal.NotTerrain => "the surface was not the world's own ground",
            SpawnRefusal.Submerged => "the footing was under water",
            SpawnRefusal.NoClearVolume => "the body did not fit clear of geometry",
            _ => "something else"
        };

        private static string RefusalHint(string worst)
            => worst == "the surface was not the world's own ground"
                ? "That usually means a machine, a deck or a canopy is being hit first."
                : worst == "no walkable surface under the anchor"
                    ? "That usually means nothing solid within the probe band - open air, a ship, or deep water."
                    : worst == "the footing was under water"
                        ? "Creatures need dry ground; a refusal on a shoreline is normal."
                        : worst == "the body did not fit clear of geometry"
                            ? "The prefab's collider may be larger than the spot allows - try open ground."
                            : "Check that the world is loaded and gravity is active at the anchor.";

        /// <summary>Resolves the terrain under <paramref name="anchor"/> along
        /// <paramref name="up"/>. Returns false when there is no world ground there.</summary>
        public static bool TryResolve(Vector3 anchor, Vector3 up, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = anchor;
            surfaceNormal = up;
            if (up.sqrMagnitude < 0.0001f) { NoteRefusal(SpawnRefusal.NoUp, anchor); return false; }
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
            if (bestDistance == float.PositiveInfinity) { NoteRefusal(SpawnRefusal.NoSurface, anchor); return false; }

            surfacePoint = best.point;
            Vector3 hitNormal = best.normal.normalized;
            if (Vector3.Dot(hitNormal, up) > 0.05f) surfaceNormal = hitNormal;

            if (VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld sphere && sphere.body != null)
            {
                Vector3 local = sphere.body.transform.InverseTransformPoint(best.point);
                Vector3 normal = local.normalized;
                // 17.27.7-dev: the world's own answer is used when it HAS one. Two faults used
                // to hide behind this call. It refused the hit whenever the chunk under the
                // point had no mesh yet (a fresh load, or a chunk being remeshed right after
                // digging), and it compared a 3D distance against a 20 cm tolerance - so real
                // a real hillside, where the mesh raycast and the chunk raycast can land half a metre
                // apart while describing the same slope, was rejected as "not the terrain".
                // Both made the spawner produce nothing and say nothing. The rule is now a
                // signed height band along up: metres above the ground is a prop or a canopy,
                // within a voxel of it is the ground, and when the world cannot answer at all
                // the hit stands (the body-fit test below is what keeps a creature out of the
                // geometry, and it runs either way).
                if (sphere.TryGroundSurface(ref local, ref normal))
                {
                    Vector3 terrainPoint = sphere.body.transform.TransformPoint(local);
                    float above = Vector3.Dot(best.point - terrainPoint, up);
                    if (above > MaxAboveGround || above < -MaxBelowGround)
                    {
                        NoteRefusal(SpawnRefusal.NotTerrain, anchor);
                        return false;
                    }
                    if (sphere.IsSubmerged(terrainPoint))
                    {
                        NoteRefusal(SpawnRefusal.Submerged, anchor);
                        return false;
                    }

                    surfacePoint = terrainPoint;
                    Vector3 terrainNormal = sphere.body.transform.TransformDirection(normal).normalized;
                    if (Vector3.Dot(terrainNormal, up) > 0.05f) surfaceNormal = terrainNormal;
                }
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
            if (up.sqrMagnitude < 0.0001f) { NoteRefusal(SpawnRefusal.NoUp, surfacePoint); return false; }
            up = up.normalized;

            GetBodySize(prefab, out float radius, out float height);
            float baseOffset = StandOffsetFor(prefab);

            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector3 candidate = surfacePoint + up * (baseOffset + attempt * 0.4f);
                if (IsBodyVolumeClear(candidate, up, radius, height))
                {
                    standPoint = candidate;
                    NoteSuccess();
                    return true;
                }
            }
            NoteRefusal(SpawnRefusal.NoClearVolume, surfacePoint);
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
