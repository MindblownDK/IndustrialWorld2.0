// Assets/Scripts/VoxelEngine/Generation/OreLayer.cs
//
// Blittable POD struct describing a single ore/mineral deposit layer. Consumed by BOTH the
// flat-world ChunkGenJob and the spherical SphereChunkGenJob via Burst. Extracted from the
// old PlanetSettings.cs so the sphere ore system doesn't depend on the deprecated flat-world
// PlanetSettings class.
using System;
using VoxelEngine.Materials;

namespace VoxelEngine.Generation
{
    [Serializable]
    public struct OreLayer
    {
        public MaterialId material;
        public float scale;
        public float threshold;
        public int   minDepth;
        public int   maxDepth;
    }
}
