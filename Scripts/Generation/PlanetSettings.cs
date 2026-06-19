// Assets/Scripts/VoxelEngine/Generation/PlanetSettings.cs
//
// DEPRECATED — this file previously contained the PlanetSettings class (flat-world config)
// AND the OreLayer struct. Both have been superseded:
//
//   • OreLayer      → moved to its own file: OreLayer.cs (still in active use by the sphere)
//   • PlanetSettings → REMOVED. The flat VoxelWorld now uses inline fields (flatSeed, flatSeaLevel,
//     etc.). The spherical SphereWorld uses BodySettings + CelestialBody instead.
//
// This file is kept empty so the .meta GUID is preserved (avoids broken references in existing
// scene/prefab assets that may still point to it). Do not add code here.
namespace VoxelEngine.Generation
{
    // (Intentionally empty — see header comment.)
}
