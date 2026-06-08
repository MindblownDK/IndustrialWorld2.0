// Assets/Scripts/VoxelEngine/GridSystem/AtmosphereManager.cs
//
// Handles air density and gravity per planet + space.
// This is the foundation for realistic thruster performance and flight.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class AtmosphereManager
    {
        /// <summary>
        /// Returns air density at a given world position (0 = vacuum, 1 = sea level Earth-like).
        /// </summary>
        public static float GetAirDensity(Vector3 worldPosition)
        {
            // TODO: Replace with proper planet-specific atmosphere data
            // For now we use a simple height-based model
            float height = worldPosition.y;

            if (height > 8000f) return 0f;                    // Space / very high altitude
            if (height < 0f) return 1.2f;                     // Below sea level (dense)

            // Simple exponential atmosphere
            return Mathf.Exp(-height / 8500f) * 1.225f;       // Earth-like sea level density
        }

        /// <summary>
        /// Returns local gravity multiplier (1.0 = Earth, 0 = space).
        /// </summary>
        public static float GetGravityMultiplier(Vector3 worldPosition)
        {
            float height = worldPosition.y;

            if (height > 10000f) return 0.05f;   // Near zero in high orbit / space
            if (height < -100f) return 1.5f;     // Stronger near core

            return Mathf.Clamp01(1f - (height / 12000f));
        }

        /// <summary>
        /// Returns true if we are considered "in space" (very low air density).
        /// </summary>
        public static bool IsInSpace(Vector3 worldPosition)
        {
            return GetAirDensity(worldPosition) < 0.05f;
        }
    }
}