// Assets/Scripts/VoxelEngine/Core/IVoxelWorld.cs
//
// Shared interface for anything that owns a voxel terrain the player can mine/build on.
// Implemented by the spherical SphereWorld (the flat VoxelWorld was removed in 8.0.0 —
// this game is planets-only). Systems that need to read/write terrain (pumps, drills,
// farms, map, weather, audio) target ActiveWorld.Current, so they work on whichever
// world the player is currently in.
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Core
{
    /// <summary>
    /// Voxel-world contract: read/write voxels, look up chunks, remesh, plus shared assets.
    /// The spherical SphereWorld implements this.
    /// </summary>
    public interface IVoxelWorld
    {
        Voxel GetVoxelWorld(Vector3Int worldVoxel);
        void SetVoxelWorld(Vector3Int worldVoxel, Voxel v, bool remesh = true);
        bool TryGetChunk(Vector3Int coord, out Chunk chunk);
        Vector3Int WorldToVoxel(Vector3 worldPos);
        Vector3Int WorldToChunk(Vector3 worldPos);
        /// <summary>Force a chunk to rebuild its mesh (used by editing tools after voxel writes).</summary>
        void ScheduleMeshJob(Chunk chunk);
        /// <summary>Complete any in-flight gen job for this chunk (fluid sim safety).</summary>
        void CompleteGenJobForChunk(Chunk chunk);
        /// <summary>Complete any in-flight mesh job for this chunk (fluid sim safety).</summary>
        void CompleteMeshJobForChunk(Chunk chunk);

        /// <summary>Material registry (colors, hardness, mining tier) for this world.</summary>
        MaterialRegistry MaterialRegistry { get; }
        /// <summary>Transform the world streams around (usually the player).</summary>
        Transform Viewer { get; }
        /// <summary>Voxel-space sea level (water fills below this).</summary>
        int SeaLevel { get; }
        /// <summary>World generation seed (for deterministic biome/climate sampling).</summary>
        int Seed { get; }
    }

    /// <summary>
    /// Static pointer to the world the player is currently interacting with. Set by the
    /// scene bootstrap (CosmosBootstrap sets the SphereWorld — the flat world is removed
    /// since 8.0.0). Systems read Current to target whichever world the player is in.
    /// </summary>
    public static class ActiveWorld
    {
        private static IVoxelWorld _current;

        /// <summary>The active voxel world (the sphere). Null until a bootstrap sets it.</summary>
        public static IVoxelWorld Current
        {
            get
            {
                // A destroyed world can linger behind this interface reference: the C# `??`
                // operator only tests reference-null, NOT Unity's overloaded "destroyed ==
                // null", so a torn-down SphereWorld would otherwise be handed back to callers
                // and its dead CelestialBody would throw MissingReferenceException (e.g. the
                // dropped-item ice probe). Re-validate the backing Unity object before return.
                // (8.0.0: the old VoxelWorld.Instance fallback is gone — the sphere is the
                // only world and re-registers itself through CosmosBootstrap.)
                if (!IsAlive(_current)) _current = null;
                return IsAlive(_current) ? _current : null;
            }
            set => _current = value;
        }

        /// <summary>Clears the pointer if it targets <paramref name="world"/> (called from a
        /// world's OnDestroy so the static never dangles at a destroyed world).</summary>
        public static void ClearIfCurrent(IVoxelWorld world)
        {
            if (ReferenceEquals(_current, world)) _current = null;
        }

        private static bool IsAlive(IVoxelWorld world)
            => world is UnityEngine.Object uo ? uo != null : world != null;
    }
}
