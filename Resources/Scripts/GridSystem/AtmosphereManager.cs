// Assets/Scripts/VoxelEngine/GridSystem/AtmosphereManager.cs
//
// Handles air density and gravity for realistic flight and space travel.
// Uses the radial CelestialBody system when a body is active, falling
// back to a flat-world Y-based model otherwise.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.GridSystem
{
    public static class AtmosphereManager
    {
        public static float GetAirDensity(Vector3 worldPosition)
        {
            // Use the active celestial body's atmosphere model when available.
            if (GravityProvider.ActiveBody != null)
                return GravityProvider.ActiveBody.AirDensityAt(worldPosition);

            // Flat-world fallback.
            float height = worldPosition.y;
            if (height > 9000f) return 0.02f;           // Near space
            if (height < 0f) return 1.3f;
            return Mathf.Exp(-height / 8500f) * 1.225f;
        }

        public static float GetGravityMultiplier(Vector3 worldPosition)
        {
            // When a celestial body is active, GridEntity uses GravityProvider
            // directly, so this flat-world multiplier is only called in fallback.
            // Keep it for backward compatibility with any callers that don't
            // check GravityProvider.IsRadial.
            if (GravityProvider.ActiveBody != null)
            {
                float surfaceG = GravityProvider.ActiveBody.SurfaceGravity;
                return surfaceG > 0.01f
                    ? GravityProvider.GetGravity(worldPosition).magnitude / surfaceG
                    : 0f;
            }

            float height = worldPosition.y;
            if (height > 12000f) return 0.08f;
            return Mathf.Clamp01(1f - (height / 14000f));
        }

        public static bool IsInSpace(Vector3 worldPosition)
        {
            if (GravityProvider.ActiveBody != null)
                return GravityProvider.ActiveBody.IsInSpace(worldPosition);

            return GetAirDensity(worldPosition) < 0.08f;
        }
    }
}