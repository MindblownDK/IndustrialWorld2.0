// Assets/Scripts/VoxelEngine/Environment/IceFrictionUtility.cs
//
// Shared ice-surface detection used by movement systems. Kept tiny and stateless
// so future rigidbody/grid friction passes can use the exact same voxel sampling.

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Materials;

namespace VoxelEngine.Environment
{
    public static class IceFrictionUtility
    {
        /// <summary>
        /// Returns true when the active voxel material directly below/around the world
        /// position is Ice. Samples a short line along gravity-down so it works on flat
        /// worlds and spherical planets.
        /// </summary>
        public static bool IsIceBelow(Vector3 worldPosition, Vector3 up, float probeDepth = 1.0f)
        {
            var world = ActiveWorld.Current;
            if (world == null) return false;

            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            int samples = Mathf.Max(2, Mathf.CeilToInt(probeDepth / 0.25f));
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 p = worldPosition - up * (probeDepth * t);
                var voxel = world.GetVoxelWorld(world.WorldToVoxel(p));
                if (voxel.material == (byte)MaterialId.Ice && voxel.density > VoxelConstants.ISO_LEVEL)
                    return true;
            }

            return false;
        }
    }
}
