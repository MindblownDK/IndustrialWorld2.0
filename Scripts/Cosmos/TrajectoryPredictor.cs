// Assets/Scripts/VoxelEngine/Cosmos/TrajectoryPredictor.cs
//
// Forward physics integration of a grid's coast path.
//
// OrbitalTelemetry answers "what orbit am I on right now" analytically. That is the
// right tool for a flight-computer readout, but it cannot answer "where do I hit the
// ground", because a two-body conic ignores atmospheric drag and cannot tell you what
// terrain is in the way. This service answers the pilot's question instead by stepping
// the SAME forces GridEntity applies in FixedUpdate and then probing the world.
//
// It never touches the rigidbody. It reads state, integrates a copy, and reports.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Cosmos
{
    public enum TrajectoryOutcome
    {
        /// <summary>No solution (no grid, no physics, or the grid is at rest on the ground).</summary>
        None = 0,
        /// <summary>The path runs into terrain or a collider inside the prediction horizon.</summary>
        Impact = 1,
        /// <summary>The path stays clear for the whole horizon and the coast is a bound orbit.</summary>
        Orbit = 2,
        /// <summary>The path stays clear and the grid is leaving the gravity well.</summary>
        Escape = 3,
        /// <summary>Clear for the whole horizon, but not a resolved orbit — still climbing or coasting.</summary>
        Clear = 4,
    }

    /// <summary>Result of one prediction pass. Points are world-space and owned by the predictor.</summary>
    public readonly struct TrajectorySolution
    {
        public readonly TrajectoryOutcome Outcome;
        /// <summary>Seconds of simulated flight covered by <see cref="TrajectoryPredictor.Points"/>.</summary>
        public readonly float Duration;
        /// <summary>World point of the predicted impact. Only meaningful when Outcome == Impact.</summary>
        public readonly Vector3 ImpactPoint;
        public readonly Vector3 ImpactNormal;
        /// <summary>Seconds until impact. Only meaningful when Outcome == Impact.</summary>
        public readonly float TimeToImpact;
        /// <summary>Speed along the path at the impact point, in m/s.</summary>
        public readonly float ImpactSpeed;

        public TrajectorySolution(TrajectoryOutcome outcome, float duration, Vector3 impactPoint,
            Vector3 impactNormal, float timeToImpact, float impactSpeed)
        {
            Outcome = outcome;
            Duration = duration;
            ImpactPoint = impactPoint;
            ImpactNormal = impactNormal;
            TimeToImpact = timeToImpact;
            ImpactSpeed = impactSpeed;
        }

        public bool HasImpact => Outcome == TrajectoryOutcome.Impact;
    }

    public static class TrajectoryPredictor
    {
        // ── Tuning ───────────────────────────────────────────────────────────────
        /// <summary>Seconds of flight simulated at most. Long enough to see an orbit bend away.</summary>
        public const float Horizon = 45f;
        /// <summary>Integration step. Coarser than physics: the path only has to look right.</summary>
        private const float Step = 0.25f;
        private const int MaxPoints = (int)(Horizon / Step) + 2;
        /// <summary>Below this speed a grounded grid has no interesting path to draw.</summary>
        private const float MinimumSpeed = 1.5f;
        private const float DragCoefficient = 0.85f;

        private static readonly List<Vector3> _points = new List<Vector3>(MaxPoints);
        private static readonly RaycastHit[] _hits = new RaycastHit[8];

        /// <summary>The path from the most recent <see cref="Solve"/>. Do not retain across calls.</summary>
        public static IReadOnlyList<Vector3> Points => _points;

        // ── Cache ────────────────────────────────────────────────────────────────
        // A prediction is only rebuilt when the ship's motion actually changed, because a
        // coasting ship re-solves to the same curve every frame. Thrust, gravity turns and
        // collisions all move the velocity, so velocity change is the honest dirty signal.
        private static GridEntity _cachedGrid;
        private static Vector3 _cachedPosition;
        private static Vector3 _cachedVelocity;
        private static float _cachedAt = -999f;
        private static TrajectorySolution _cachedSolution;

        private const float MinRefreshInterval = 0.05f;
        private const float MaxCacheAge = 0.5f;
        private const float VelocityEpsilonSq = 0.25f;   // 0.5 m/s
        private const float PositionEpsilonSq = 4f;      // 2 m

        /// <summary>
        /// Predicts the coast path for <paramref name="grid"/>, reusing the previous solution
        /// while the ship's motion is unchanged. The returned points live in <see cref="Points"/>.
        /// </summary>
        public static TrajectorySolution Solve(GridEntity grid)
        {
            if (grid == null || grid.Body == null || grid.Body.isKinematic)
            {
                _cachedGrid = null;
                _points.Clear();
                return default;
            }

            Vector3 position = grid.Body.worldCenterOfMass;
            Vector3 velocity = grid.Body.linearVelocity;

            float now = Time.unscaledTime;
            bool sameGrid = _cachedGrid == grid;
            float age = now - _cachedAt;
            if (sameGrid && age < MinRefreshInterval) return _cachedSolution;
            if (sameGrid && age < MaxCacheAge
                && (velocity - _cachedVelocity).sqrMagnitude < VelocityEpsilonSq
                && (position - _cachedPosition).sqrMagnitude < PositionEpsilonSq)
                return _cachedSolution;

            _cachedGrid = grid;
            _cachedPosition = position;
            _cachedVelocity = velocity;
            _cachedAt = now;
            _cachedSolution = Integrate(grid, position, velocity);
            return _cachedSolution;
        }

        /// <summary>Drops the cache so the next Solve rebuilds from scratch.</summary>
        public static void Invalidate()
        {
            _cachedGrid = null;
            _cachedAt = -999f;
            _points.Clear();
        }

        // ── Integration ──────────────────────────────────────────────────────────
        private static TrajectorySolution Integrate(GridEntity grid, Vector3 position, Vector3 velocity)
        {
            _points.Clear();

            if (velocity.sqrMagnitude < MinimumSpeed * MinimumSpeed)
                return new TrajectorySolution(TrajectoryOutcome.None, 0f, Vector3.zero, Vector3.up, 0f, 0f);

            float gravityScale = Mathf.Max(0f, grid.gravityScale);
            float mass = Mathf.Max(0.001f, grid.Body.mass);

            // The same frontal-area estimate GridEntity.ApplyAtmosphericDrag uses, so the
            // predicted path matches the drag the ship will actually feel on the way down.
            float cellSize = grid.gridSize.CellSize();
            float baseArea = Mathf.Max(1f, cellSize * cellSize * 0.55f);
            float frontalArea = baseArea * Mathf.Max(1f, grid.BlockCount * 0.32f);

            // Skip terrain probing until the path has cleared the hull, otherwise the very
            // first segment reports an "impact" with the ship we are predicting for.
            float selfClearance = Mathf.Max(2f, cellSize * 2.5f);
            float travelled = 0f;

            _points.Add(position);

            for (int i = 0; i < MaxPoints - 1; i++)
            {
                Vector3 acceleration = GravityAt(position, gravityScale);

                // Drag: F = ½ρv²CdA, then divide by mass to get acceleration.
                float airDensity = AtmosphereManager.GetAirDensity(position);
                if (airDensity > 0.0001f)
                {
                    float speedSq = velocity.sqrMagnitude;
                    if (speedSq > 0.0025f)
                    {
                        float dragForce = 0.5f * airDensity * speedSq * DragCoefficient * frontalArea;
                        acceleration -= velocity.normalized * (dragForce / mass);
                    }
                }

                // Semi-implicit Euler: same integrator family as the physics step, and it does
                // not spiral outwards on a circular orbit the way explicit Euler does.
                velocity += acceleration * Step;
                Vector3 next = position + velocity * Step;

                Vector3 segment = next - position;
                float segmentLength = segment.magnitude;
                travelled += segmentLength;

                if (travelled > selfClearance && segmentLength > 0.0001f
                    && TryHit(position, segment / segmentLength, segmentLength, grid, out RaycastHit hit))
                {
                    _points.Add(hit.point);
                    float t = i * Step + Step * Mathf.Clamp01(hit.distance / Mathf.Max(0.0001f, segmentLength));
                    return new TrajectorySolution(TrajectoryOutcome.Impact, t, hit.point,
                        hit.normal, t, velocity.magnitude);
                }

                position = next;
                _points.Add(position);
            }

            // Nothing in the way for the whole horizon — classify the coast so the readout can
            // say WILL ORBIT rather than just "no impact found".
            var sample = OrbitalTelemetry.Sample(position, velocity, gravityScale);
            TrajectoryOutcome outcome = sample.State switch
            {
                OrbitalFlightState.Orbiting => TrajectoryOutcome.Orbit,
                OrbitalFlightState.Escape => TrajectoryOutcome.Escape,
                OrbitalFlightState.DeepSpace => TrajectoryOutcome.Escape,
                _ => TrajectoryOutcome.Clear,
            };
            return new TrajectorySolution(outcome, Horizon, Vector3.zero, Vector3.up, 0f, velocity.magnitude);
        }

        private static Vector3 GravityAt(Vector3 position, float gravityScale)
        {
            if (GravityProvider.IsRadial)
                return GravityProvider.GetGravity(position) * gravityScale;
            return Physics.gravity * AtmosphereManager.GetGravityMultiplier(position) * gravityScale;
        }

        /// <summary>
        /// Probes one path segment, ignoring the predicted grid itself and the pilot. Without
        /// that filter every prediction impacts the ship it belongs to on the first step.
        /// </summary>
        private static bool TryHit(Vector3 origin, Vector3 direction, float distance,
            GridEntity self, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(new Ray(origin, direction), _hits, distance,
                ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null) continue;
                if (hit.distance >= nearest) continue;
                if (hit.collider.GetComponentInParent<GridEntity>() == self) continue;
                if (hit.collider.GetComponentInParent<VoxelEngine.Player.PlayerController>() != null) continue;
                nearest = hit.distance;
                best = hit;
                found = true;
            }
            return found;
        }
    }
}
