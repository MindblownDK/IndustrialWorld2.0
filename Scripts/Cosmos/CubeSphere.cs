// Assets/Scripts/VoxelEngine/Cosmos/CubeSphere.cs
//
// Pure, Burst-compatible cube-sphere mapping.
//
// A planet is built from 6 cube faces. Each face owns a square grid of voxel columns;
// a column's local (u,v) in [-1,1] maps to a 3D point on the unit cube, which is then
// normalised to a direction on the unit sphere. Gravity, density and biome sampling all
// work in DIRECTION space, so the same generator is correct for any planet radius.
//
// Forward (face,u,v) → direction and inverse direction → (face,u,v) are consistent and
// lossless, which is what the Phase-2 face-streaming mesher relies on for seam stitching.
using Unity.Burst;
using Unity.Mathematics;

namespace VoxelEngine.Cosmos
{
    [BurstCompile]
    public static class CubeSphere
    {
        public const int FaceCount = 6;

        /// <summary>
        /// Map a face-local coordinate (u,v both in [-1,1]) to a unit direction on the
        /// sphere. Face layout:
        ///   0 = +X, 1 = -X, 2 = +Y, 3 = -Y, 4 = +Z, 5 = -Z.
        /// </summary>
        public static float3 FaceDirection(int face, float u, float v)
        {
            float3 p;
            switch (face)
            {
                case 0:  p = new float3( 1f, v, -u); break; // +X
                case 1:  p = new float3(-1f, v,  u); break; // -X
                case 2:  p = new float3( u, 1f,  v); break; // +Y
                case 3:  p = new float3( u,-1f, -v); break; // -Y
                case 4:  p = new float3( u, v,  1f); break; // +Z
                default: p = new float3(-u, v, -1f); break; // -Z  (case 5)
            }
            return math.normalizesafe(p, new float3(1f, 0f, 0f));
        }

        /// <summary>
        /// Inverse: given a unit direction, find which face it belongs to and the face-local
        /// (u,v) in [-1,1]. Picks the dominant axis (largest |component|) with its sign.
        /// Round-trips exactly with <see cref="FaceDirection"/>.
        /// </summary>
        public static void DirectionToFace(float3 dir, out int face, out float u, out float v)
        {
            float ax = math.abs(dir.x), ay = math.abs(dir.y), az = math.abs(dir.z);

            if (ax >= ay && ax >= az)
            {
                if (dir.x >= 0f) { face = 0; u = -dir.z / dir.x; v =  dir.y / dir.x; }
                else             { face = 1; u =  dir.z / -dir.x; v = dir.y / -dir.x; }
            }
            else if (ay >= ax && ay >= az)
            {
                if (dir.y >= 0f) { face = 2; u =  dir.x / dir.y; v =  dir.z / dir.y; }
                else             { face = 3; u =  dir.x / -dir.y; v = dir.z / dir.y; }
            }
            else
            {
                if (dir.z >= 0f) { face = 4; u =  dir.x / dir.z; v =  dir.y / dir.z; }
                else             { face = 5; u =  dir.x / dir.z; v = dir.y / -dir.z; }
            }
            u = math.clamp(u, -1f, 1f);
            v = math.clamp(v, -1f, 1f);
        }

        /// <summary>
        /// The outward normal (face axis) for a face — used to orient face-local tangent bases
        /// and by the radial-gravity / "up" math in <see cref="CelestialBody"/>.
        /// </summary>
        public static float3 FaceAxis(int face)
        {
            switch (face)
            {
                case 0:  return new float3( 1f, 0f, 0f);
                case 1:  return new float3(-1f, 0f, 0f);
                case 2:  return new float3( 0f, 1f, 0f);
                case 3:  return new float3( 0f,-1f, 0f);
                case 4:  return new float3( 0f, 0f, 1f);
                default: return new float3( 0f, 0f,-1f);
            }
        }
    }
}
