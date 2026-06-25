// Assets/Scripts/VoxelEngine/GridSystem/AtmosphereManager.cs
//
// Handles air density and gravity for realistic flight and space travel.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class AtmosphereManager
    {
        public static float GetAirDensity(Vector3 worldPosition)
        {
            float height = worldPosition.y;

            if (height > 9000f) return 0.02f;           // Near space
            if (height < 0f) return 1.3f;

            return Mathf.Exp(-height / 8500f) * 1.225f;
        }

        public static float GetGravityMultiplier(Vector3 worldPosition)
        {
            float height = worldPosition.y;
            if (height > 12000f) return 0.08f;
            return Mathf.Clamp01(1f - (height / 14000f));
        }

        public static bool IsInSpace(Vector3 worldPosition)
        {
            return GetAirDensity(worldPosition) < 0.08f;
        }
    }
}