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
    ///   byte[] payload           (DeflateStream-compressed Voxel[] = 2 bytes per voxel)
    /// </summary>
    public struct ChunkSaveData
    {
        public Vector3Int coord;
        public byte[]     uncompressedVoxelBytes; // 2 * VOXELS_PER_CHUNK_P bytes

        public static ChunkSaveData FromChunk(Chunk chunk)
        {
            int byteLen = VoxelConstants.VOXELS_PER_CHUNK_P * 2; // sizeof(Voxel) == 2
            var bytes = new byte[byteLen];
            unsafe
            {
                var src = (byte*)chunk.voxels.GetUnsafeReadOnlyPtr();
                System.Runtime.InteropServices.Marshal.Copy((System.IntPtr)src, bytes, 0, byteLen);
            }
            return new ChunkSaveData { coord = chunk.coord, uncompressedVoxelBytes = bytes };
        }

        public void RestoreInto(Chunk chunk)
        {
            int byteLen = VoxelConstants.VOXELS_PER_CHUNK_P * 2;
            unsafe
            {
                var dst = (byte*)chunk.voxels.GetUnsafePtr();
                System.Runtime.InteropServices.Marshal.Copy(uncompressedVoxelBytes, 0, (System.IntPtr)dst, byteLen);
            }
        }
    }
}
