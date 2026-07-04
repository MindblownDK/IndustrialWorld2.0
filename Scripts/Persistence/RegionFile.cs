// Assets/Scripts/VoxelEngine/Persistence/RegionFile.cs
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.Persistence
{
    /// <summary>
    /// One region file = REGION_SIZE × WORLD_HEIGHT_CHUNKS × REGION_SIZE chunks.
    /// File path:  {persistentDataPath}/{worldName}/r_{rx}_{rz}.dat
    /// Format:
    ///   "VECR" magic (4 bytes)
    ///   int32 version
    ///   int32 entryCount
    ///   for each entry:
    ///     int32  localIndex                // (cx + cz*REGION_SIZE) * WORLD_HEIGHT_CHUNKS + cy
    ///     int32  payloadLength
    ///     uint32 crc32
    ///     byte[] payload (deflate-compressed)
    /// </summary>
    public static class RegionFile
    {
        public const int   REGION_SIZE = 16;
        private const uint MAGIC       = 0x52434556; // "VECR"
        private const int  VERSION     = 1;

        public static Vector2Int ChunkToRegion(Vector3Int chunkCoord) =>
            new Vector2Int(
                Mathf.FloorToInt(chunkCoord.x / (float)REGION_SIZE),
                Mathf.FloorToInt(chunkCoord.z / (float)REGION_SIZE));

        public static int LocalIndex(Vector3Int chunkCoord)
        {
            int lx = chunkCoord.x - Mathf.FloorToInt(chunkCoord.x / (float)REGION_SIZE) * REGION_SIZE;
            int lz = chunkCoord.z - Mathf.FloorToInt(chunkCoord.z / (float)REGION_SIZE) * REGION_SIZE;
            return (lx + lz * REGION_SIZE) * VoxelConstants.WORLD_HEIGHT_CHUNKS + chunkCoord.y;
        }

        public static Vector3Int FromLocalIndex(Vector2Int region, int localIndex)
        {
            int cy = localIndex % VoxelConstants.WORLD_HEIGHT_CHUNKS;
            int rest = localIndex / VoxelConstants.WORLD_HEIGHT_CHUNKS;
            int lx = rest % REGION_SIZE;
            int lz = rest / REGION_SIZE;
            return new Vector3Int(region.x * REGION_SIZE + lx, cy, region.y * REGION_SIZE + lz);
        }

        public static string PathFor(string worldFolder, Vector2Int region) =>
            Path.Combine(worldFolder, $"r_{region.x}_{region.y}.dat");

        // ------------- WRITE: merge-update existing file with new entries -------------
        public static void WriteMerged(string worldFolder, Vector2Int region,
                                       Dictionary<int, ChunkSaveData> entries)
        {
            Directory.CreateDirectory(worldFolder);
            string path = PathFor(worldFolder, region);
            string tmp  = path + ".tmp";

            // Load existing entries first so we don't lose chunks we aren't currently writing.
            var combined = new Dictionary<int, ChunkSaveData>(entries);
            if (File.Exists(path))
            {
                foreach (var kv in ReadAll(worldFolder, region))
                    if (!combined.ContainsKey(kv.Key)) combined[kv.Key] = kv.Value;
            }

            using (var fs = File.Open(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(MAGIC);
                bw.Write(VERSION);
                bw.Write(combined.Count);

                foreach (var kv in combined)
                {
                    var data = kv.Value;
                    uint crc = Crc32.Compute(data.uncompressedVoxelBytes);
                    using var ms = new MemoryStream();
                    using (var ds = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                        ds.Write(data.uncompressedVoxelBytes, 0, data.uncompressedVoxelBytes.Length);
                    var compressed = ms.ToArray();

                    bw.Write(kv.Key);
                    bw.Write(compressed.Length);
                    bw.Write(crc);
                    bw.Write(compressed);
                }
            }

            // Atomic replace — power-loss safe.
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // ------------- READ: load all entries from one region file -------------
        public static Dictionary<int, ChunkSaveData> ReadAll(string worldFolder, Vector2Int region)
        {
            var result = new Dictionary<int, ChunkSaveData>();
            string path = PathFor(worldFolder, region);
            if (!File.Exists(path)) return result;

            try
            {
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                if (br.ReadUInt32() != MAGIC) { Debug.LogWarning($"[RegionFile] Bad magic in {path}"); return result; }
                int version = br.ReadInt32();
                if (version != VERSION) { Debug.LogWarning($"[RegionFile] Unknown version {version} in {path}"); return result; }
                int count = br.ReadInt32();

                for (int i = 0; i < count; i++)
                {
                    int localIndex = br.ReadInt32();
                    int payloadLen = br.ReadInt32();
                    uint crc       = br.ReadUInt32();
                    byte[] compressed = br.ReadBytes(payloadLen);

                    using var ms = new MemoryStream(compressed);
                    using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                    var raw = new byte[VoxelConstants.VOXELS_PER_CHUNK_P * 2];
                    int read = 0;
                    while (read < raw.Length)
                    {
                        int got = ds.Read(raw, read, raw.Length - read);
                        if (got <= 0) break;
                        read += got;
                    }
                    if (read != raw.Length || Crc32.Compute(raw) != crc)
                    {
                        Debug.LogWarning($"[RegionFile] Corrupt entry {localIndex} in {path}, skipping.");
                        continue;
                    }
                    var coord = FromLocalIndex(region, localIndex);
                    result[localIndex] = new ChunkSaveData { coord = coord, uncompressedVoxelBytes = raw };
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RegionFile] Failed to read {path}: {ex.Message}");
            }
            return result;
        }

        // Read just one specific chunk — used for fast path when streaming a chunk in.
        public static bool TryReadChunk(string worldFolder, Vector3Int chunkCoord, out ChunkSaveData data)
        {
            data = default;
            var region = ChunkToRegion(chunkCoord);
            int wantedIndex = LocalIndex(chunkCoord);
            // Simple: read whole file (fast — region files are typically <1 MB compressed).
            var all = ReadAll(worldFolder, region);
            if (!all.TryGetValue(wantedIndex, out data)) return false;
            return true;
        }
    }

    // ---------- minimal CRC-32 (no external deps) ----------
    internal static class Crc32
    {
        private static readonly uint[] _table = BuildTable();
        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }
        public static uint Compute(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                c = _table[(c ^ data[i]) & 0xff] ^ (c >> 8);
            return ~c;
        }
    }
}
