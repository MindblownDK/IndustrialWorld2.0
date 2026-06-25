// Assets/Scripts/VoxelEngine/Persistence/ChunkStorage.cs
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.Persistence
{
    /// <summary>
    /// Coordinates chunk persistence:
    ///   • Only chunks marked .isModified are saved (pristine chunks regenerate from seed).
    ///   • Saving is triggered on chunk eviction, scene quit, or manual Flush() — never per frame.
    ///   • A background thread drains the write queue so the main thread never blocks on disk.
    ///   • Region files are batched: many chunk writes for the same region coalesce into one disk hit.
    ///
    /// Disk wear: in steady-state play (no edits) zero bytes are written.
    /// Each edit dirties the chunk; the chunk is only flushed when the player wanders away
    /// (eviction) or quits — typically seconds-to-minutes apart, not every frame.
    /// </summary>
    public class ChunkStorage
    {
        private readonly string _worldFolder;
        private readonly Thread _writerThread;
        private readonly BlockingCollection<WriteJob> _writeQueue = new();
        private readonly ManualResetEventSlim _idle = new(true);
        private volatile bool _running = true;

        // Read cache: avoid reopening the same region file repeatedly when many chunks load at once.
        private readonly Dictionary<Vector2Int, Dictionary<int, ChunkSaveData>> _readCache = new();
        private readonly object _readCacheLock = new();

        private struct WriteJob
        {
            public Vector2Int region;
            public Dictionary<int, ChunkSaveData> entries;
        }

        public string WorldFolder => _worldFolder;

        public ChunkStorage(string worldName)
        {
            _worldFolder = Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName);
            Directory.CreateDirectory(_worldFolder);
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "VoxelChunkWriter" };
            _writerThread.Start();
            Debug.Log($"[ChunkStorage] World folder: {_worldFolder}");
        }

        public void Shutdown()
        {
            _running = false;
            _writeQueue.CompleteAdding();
            // Block until writer drains the queue so a quit doesn't lose data.
            if (_writerThread.IsAlive) _writerThread.Join(5000);
        }

        // ----- READ -----
        public bool TryLoadChunk(Vector3Int chunkCoord, Chunk chunk)
        {
            var region = RegionFile.ChunkToRegion(chunkCoord);
            int local = RegionFile.LocalIndex(chunkCoord);

            Dictionary<int, ChunkSaveData> entries;
            lock (_readCacheLock)
            {
                if (!_readCache.TryGetValue(region, out entries))
                {
                    entries = RegionFile.ReadAll(_worldFolder, region);
                    _readCache[region] = entries;
                }
            }
            if (!entries.TryGetValue(local, out var data)) return false;
            data.RestoreInto(chunk);
            return true;
        }

        // Drop region from read cache once we've moved far enough away (saves RAM).
        public void EvictRegionFromReadCache(Vector2Int region)
        {
            lock (_readCacheLock) _readCache.Remove(region);
        }

        // ----- WRITE -----
        /// <summary>Enqueue a single dirty chunk for background save. Returns immediately.</summary>
        public void EnqueueSave(Chunk chunk)
        {
            if (!chunk.isAllocated || !chunk.voxels.IsCreated) return;

            var region  = RegionFile.ChunkToRegion(chunk.coord);
            int local   = RegionFile.LocalIndex(chunk.coord);
            var snapshot = ChunkSaveData.FromChunk(chunk);

            // Coalesce: if a job for the same region is already queued, append to it.
            // The blocking queue can't peek so we wrap in a new tiny job each time and the
            // writer dedupes per-region across the batch it pulls.
            var entries = new Dictionary<int, ChunkSaveData> { [local] = snapshot };
            _writeQueue.Add(new WriteJob { region = region, entries = entries });
            _idle.Reset();
        }

        /// <summary>Block until all outstanding writes finish (call on quit / scene unload).</summary>
        public void WaitForIdle(int timeoutMs = 5000)
        {
            _idle.Wait(timeoutMs);
        }

        // ----- BACKGROUND WRITER -----
        private void WriterLoop()
        {
            // Pull as many jobs as available, group by region, then merge-write each region once.
            var batch = new Dictionary<Vector2Int, Dictionary<int, ChunkSaveData>>();

            try
            {
                while (_running || _writeQueue.Count > 0)
                {
                    if (!_writeQueue.TryTake(out var first, 250))
                    {
                        if (batch.Count > 0) FlushBatch(batch);
                        _idle.Set();
                        continue;
                    }

                    AddToBatch(batch, first);

                    // Drain anything else queued right now (coalesce burst).
                    while (_writeQueue.TryTake(out var more)) AddToBatch(batch, more);

                    FlushBatch(batch);

                    if (_writeQueue.Count == 0) _idle.Set();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChunkStorage] Writer thread crashed: {ex}");
            }
        }

        private static void AddToBatch(Dictionary<Vector2Int, Dictionary<int, ChunkSaveData>> batch, WriteJob job)
        {
            if (!batch.TryGetValue(job.region, out var dict))
                batch[job.region] = dict = new Dictionary<int, ChunkSaveData>();
            foreach (var kv in job.entries) dict[kv.Key] = kv.Value;
        }

        private void FlushBatch(Dictionary<Vector2Int, Dictionary<int, ChunkSaveData>> batch)
        {
            foreach (var kv in batch)
            {
                try
                {
                    RegionFile.WriteMerged(_worldFolder, kv.Key, kv.Value);
                    // Update read cache so future loads see the freshest data.
                    lock (_readCacheLock)
                    {
                        if (_readCache.TryGetValue(kv.Key, out var cached))
                            foreach (var entry in kv.Value) cached[entry.Key] = entry.Value;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[ChunkStorage] Failed to flush region {kv.Key}: {ex.Message}");
                }
            }
            batch.Clear();
        }
    }
}
