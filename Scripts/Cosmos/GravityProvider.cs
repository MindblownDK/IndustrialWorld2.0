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
    }
}
