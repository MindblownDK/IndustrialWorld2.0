// Assets/Scripts/VoxelEngine/Persistence/ChunkSaveData.cs
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.Persistence
{
    /// <summary>
    /// Plain serialized snapshot of a single chunk's voxel grid.
    /// We store the full padded grid so loaded chunks are immediately mesh-ready
    /// without re-running the generator (1-voxel border included).
    ///
    /// On-disk layout per chunk inside a region file:
    ///   int32  localChunkIndex   (0..255 inside a 16×WORLD_HEIGHT_CHUNKS×16 region)
    ///   int32  payloadLength     (bytes after compression)
    ///   uint32 crc32             (over the *uncompressed* voxel bytes)
    ///   byte[] payload           (deflate-compressed Voxel[] — 3 bytes per voxel:
    ///                             density, material, waterLevel)
    ///
    /// The payload is the WHOLE padded grid, and its size is taken from the struct
    /// (<see cref="VoxelBytes"/>), never from a hand-written per-voxel number. 9.5.3 through
    /// 9.59.0 wrote `VOXELS_PER_CHUNK_P * 2` bytes here — a leftover from the two-byte voxel
    /// that predates `waterLevel` — so every stored chunk was missing its last third: the
    /// file was internally consistent (the CRC covered the truncated array, so nothing ever
    /// complained) while the z-major third of the padded grid simply was not on disk. On
    /// load that third kept whatever the recycled chunk happened to hold, which is the
    /// speckled slabs, the mesh that disagrees with its collider, and the holes the player
    /// fell through.
    /// </summary>
    public struct ChunkSaveData
    {
        public Vector3Int coord;
        public byte[]     uncompressedVoxelBytes; // exactly PayloadBytes long

        /// <summary>
        /// Bytes one voxel occupies in the stored payload, asked of the struct itself.
        /// `Voxel` is `[StructLayout(Pack = 1)]` with three bytes, so this is 3 today — and
        /// if a fourth field is ever added, this number moves with it instead of being a
        /// comment that goes stale. That is the whole reason the old constant is gone.
        /// </summary>
        public static int VoxelBytes => UnsafeUtility.SizeOf<Voxel>();

        /// <summary>Size of one stored chunk: the full padded voxel grid, every byte of it.</summary>
        public static int PayloadBytes => VoxelConstants.VOXELS_PER_CHUNK_P * VoxelBytes;

        public static ChunkSaveData FromChunk(Chunk chunk)
        {
            // The chunk's own array, exactly as long as it is — never a fraction of it.
            int byteLen = chunk.voxels.Length * VoxelBytes;
            var bytes = new byte[byteLen];
            unsafe
            {
                var src = (byte*)chunk.voxels.GetUnsafeReadOnlyPtr();
                System.Runtime.InteropServices.Marshal.Copy((System.IntPtr)src, bytes, 0, byteLen);
            }
            return new ChunkSaveData { coord = chunk.coord, uncompressedVoxelBytes = bytes };
        }

        /// <summary>
        /// Write this payload into a chunk. Returns false when the payload is not exactly the
        /// size of the chunk's array — a payload from an older writer is refused rather than
        /// copied, so a short one can never leave the tail of the chunk holding whatever the
        /// recycled chunk had in it. The caller then generates the chunk normally.
        /// </summary>
        public bool RestoreInto(Chunk chunk)
        {
            int byteLen = chunk.voxels.Length * VoxelBytes;
            if (uncompressedVoxelBytes == null || uncompressedVoxelBytes.Length != byteLen) return false;
            unsafe
            {
                var dst = (byte*)chunk.voxels.GetUnsafePtr();
                System.Runtime.InteropServices.Marshal.Copy(uncompressedVoxelBytes, 0, (System.IntPtr)dst, byteLen);
            }
            return true;
        }
    }
}
