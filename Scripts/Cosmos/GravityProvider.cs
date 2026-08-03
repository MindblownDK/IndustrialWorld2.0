// Assets/Scripts/VoxelEngine/Cosmos/GravityProvider.cs
//
// Global gravity / orientation source for the whole game.
//
// The flat world uses a constant down-vector (world -Y). Spherical worlds need RADIAL
// gravity: "down" points toward the active body's core, and "up" points away from it — and
// both rotate as the player walks around the planet.
//
// This singleton is the single source every movement system (player, vehicles, atmosphere)
// asks: "which way is down here, right now?" When no spherical body is active it reports the
// classic flat-world answer (Vector3.up / -22 m/s²), so existing flat-world code is unaffected.
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Allocation-free local gravity telemetry shared by player and cockpit HUDs.
    /// Magnitude is the actual acceleration after an optional construct scale;
    /// SurfaceFraction reads 1.0 at the active body's surface and falls with altitude.
    /// </summary>
    public readonly struct GravityFieldSample
    {
        public readonly Vector3 Acceleration;
        public readonly float Magnitude;
        public readonly float Gees;
        public readonly float SurfaceFraction;
        public readonly bool IsRadial;

        internal GravityFieldSample(Vector3 acceleration, float surfaceMagnitude, bool isRadial)
        {
            Acceleration = acceleration;
            Magnitude = acceleration.magnitude;
            Gees = Magnitude / 9.81f;
            SurfaceFraction = surfaceMagnitude > 0.0001f
                ? Mathf.Clamp01(Magnitude / surfaceMagnitude)
                : 0f;
            IsRadial = isRadial;
        }
    }

    [DefaultExecutionOrder(-55)]
    public class GravityProvider : MonoBehaviour
    {
        public static GravityProvider Instance { get; private set; }

        [Tooltip("Flat-world gravity magnitude (m/s²). Used when no celestial body is active.")]
        public float flatGravity = 22f;

        /// <summary>The body whose gravity currently applies. Null = flat-world gravity.</summary>
        public static CelestialBody ActiveBody { get; set; }

        /// <summary>True when a spherical body is driving gravity (radial mode).</summary>
        public static bool IsRadial => ActiveBody != null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        /// <summary>"Up" at a world position: away from the active body's core, or world up.</summary>
        public static Vector3 GetUp(Vector3 worldPosition)
        {
            if (ActiveBody != null) return ActiveBody.UpAt(worldPosition);
            return Vector3.up;
        }

        /// <summary>"Down" at a world position (the direction gravity pulls toward).</summary>
        public static Vector3 GetDown(Vector3 worldPosition) => -GetUp(worldPosition);

        /// <summary>Gravity acceleration vector (m/s²) at a world position.</summary>
        public static Vector3 GetGravity(Vector3 worldPosition)
        {
            if (ActiveBody != null)
            {
                // CelestialBody.GravityAt already returns a vector pointing toward the core
                // with the correct magnitude (inverse-square above the surface).
                return ActiveBody.GravityAt(worldPosition);
            }
            return Vector3.down * (Instance != null ? Instance.flatGravity : 22f);
        }

        /// <summary>
        /// Gravity magnitude as a positive number (for jump-impulse math that expects a scalar
        /// like the old PlayerController.gravity field). Always positive.
        /// </summary>
        public static float GetMagnitude(Vector3 worldPosition)
            => GetGravity(worldPosition).magnitude;

        /// <summary>Flat-world gravity magnitude (for code that only cares about the scalar).</summary>
        public static float FlatMagnitude => Instance != null ? Instance.flatGravity : 22f;

        /// <summary>
        /// Samples the real local pull at a position. Pass a construct's gravity scale for
        /// cockpit telemetry; leave the default for player/world telemetry.
        /// </summary>
        public static GravityFieldSample Sample(Vector3 worldPosition, float gravityScale = 1f)
        {
            float scale = Mathf.Max(0f, gravityScale);
            Vector3 acceleration = GetGravity(worldPosition) * scale;
            bool radial = ActiveBody != null;
            float surfaceMagnitude = radial
                ? ActiveBody.SurfaceGravity * scale
                : FlatMagnitude * scale;
            return new GravityFieldSample(acceleration, surfaceMagnitude, radial);
        }

        // ── Surface-aligned orientation helper ────────────────────────
        /// <summary>
        /// Returns a rotation whose +Y axis aligns with the local planet "up" at
        /// <paramref name="position"/> (or world-up when no celestial body is active).
        /// The +Z axis is the projection of <paramref name="referenceForward"/> onto the
        /// tangent plane, falling back to world +Z (then world +X) if the reference is
        /// parallel to the surface normal. An optional <paramref name="yaw"/> rotates the
        /// result around the local up axis.
        /// </summary>
        public static Quaternion GetSurfaceRotation(Vector3 position, float yaw = 0f, Vector3 referenceForward = default)
        {
            Vector3 up = GetUp(position);
            Vector3 forward = referenceForward.sqrMagnitude > 0.001f
                ? Vector3.ProjectOnPlane(referenceForward, up)
                : Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(Vector3.right, up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, -up); // last-resort fallback
            forward = forward.normalized;

            Quaternion baseRot = Quaternion.LookRotation(forward, up);
            return baseRot * Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
