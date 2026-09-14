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
        /// <summary>What happened to the store's terrain identity when it was opened.</summary>
        public enum StoreStatus
        {
            /// <summary>No identity was supplied — the store behaves exactly as it did before this guard existed.</summary>
            Unmanaged,
            /// <summary>The stored identity matched the live body: the stored chunks belong to this field.</summary>
            Verified,
            /// <summary>No identity file yet (a store written before the guard, or a brand-new one). Kept as-is.</summary>
            Adopted,
            /// <summary>The store described a different field. It was moved aside and this body regenerates fresh.</summary>
            Quarantined
        }

        private readonly string _worldFolder;
        private readonly Thread _writerThread;
        private readonly BlockingCollection<WriteJob> _writeQueue = new();
        private readonly ManualResetEventSlim _idle = new(true);
        private volatile bool _running = true;

        /// <summary>Result of the identity check performed when this store was opened.</summary>
        public StoreStatus Status { get; private set; } = StoreStatus.Unmanaged;

        /// <summary>Region files present in the store folder (after any quarantine).</summary>
        public int RegionFileCount { get; private set; }

        /// <summary>The identity this store currently describes, when one is being kept.</summary>
        public ChunkStoreIdentity Identity { get; private set; }

        // Read cache: avoid reopening the same region file repeatedly when many chunks load at once.
        private readonly Dictionary<Vector2Int, Dictionary<int, ChunkSaveData>> _readCache = new();
        private readonly object _readCacheLock = new();

        private struct WriteJob
        {
            public Vector2Int region;
            public Dictionary<int, ChunkSaveData> entries;
        }

        public string WorldFolder => _worldFolder;

        public ChunkStorage(string worldName) : this(worldName, null) { }

        /// <summary>
        /// Open (or create) the per-body chunk store. When an <paramref name="identity"/> is
        /// supplied the store is checked against it first: stored chunks that were generated
        /// from a different field are moved aside instead of loaded, because a chunk from
        /// another field renders as an island of wrong terrain inside this one.
        /// </summary>
        public ChunkStorage(string worldName, ChunkStoreIdentity identity)
        {
            _worldFolder = Path.Combine(Application.persistentDataPath, "VoxelWorlds", worldName);
            Directory.CreateDirectory(_worldFolder);
            VerifyIdentity(identity);
            RegionFileCount = CountRegionFiles();
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "VoxelChunkWriter" };
            _writerThread.Start();
            Debug.Log($"[ChunkStorage] World folder: {_worldFolder}");
        }

        /// <summary>
        /// Compare the store against the live body's field identity and act on the answer.
        /// Nothing is ever deleted: a store that describes another field is moved into a
        /// `stale_&lt;utc&gt;` folder next to it, so the data is still on disk to look at.
        /// </summary>
        private void VerifyIdentity(ChunkStoreIdentity identity)
        {
            if (identity == null) { Status = StoreStatus.Unmanaged; return; }
            Identity = identity;

            if (!ChunkStoreIdentity.TryRead(_worldFolder, out var stored))
            {
                // First open with the guard: a brand-new store, or one written by an older
                // build. Both are kept — there is nothing to compare against, and this is
                // also the path a world takes the first time it runs this build.
                int existing = CountRegionFiles();
                Status = identity.TryWrite(_worldFolder) ? StoreStatus.Adopted : StoreStatus.Unmanaged;
                if (existing > 0)
                {
                    Debug.LogWarning($"[ChunkStorage] Store '{_worldFolder}' predates the identity guard " +
                                     $"({existing} region file(s)) — adopted as {identity.Summary}. " +
                                     "If this body's terrain looks wrong, move that folder aside once and let it regenerate.");
                }
                else
                {
                    Debug.Log($"[ChunkStorage] New store '{_worldFolder}' created for {identity.Summary}.");
                }
                return;
            }

            if (stored.Matches(identity, out string difference))
            {
                Status = StoreStatus.Verified;
                Debug.Log($"[ChunkStorage] Store '{_worldFolder}' verified against the live body: {identity.Summary}.");
                return;
            }

            int moved = QuarantineStoredRegions();
            bool rewritten = identity.TryWrite(_worldFolder);
            Status = rewritten ? StoreStatus.Quarantined : StoreStatus.Unmanaged;
            Debug.LogWarning($"[ChunkStorage] Store '{_worldFolder}' was written against a different terrain " +
                             $"({difference}) — moved {moved} stored region file(s) to a 'stale_' folder and " +
                             $"regenerating this body from {identity.Summary}. Blocks, machines, grids and the " +
                             "inventory live in world_state.json and are not affected; voxel edits made in the " +
                             "stored chunks are what regenerates.");
        }

        /// <summary>
        /// Move every region file (and its .previous / .tmp siblings) into a timestamped
        /// `stale_` folder. Returns how many region files were moved. Never throws: a failed
        /// move leaves the file where it is and logs, because a broken store is still better
        /// than a half-deleted one.
        /// </summary>
        private int QuarantineStoredRegions()
        {
            int moved = 0;
            try
            {
                string staleFolder = Path.Combine(_worldFolder, "stale_" + System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                foreach (var file in Directory.GetFiles(_worldFolder, "r_*.dat*"))
                {
                    try
                    {
                        Directory.CreateDirectory(staleFolder);
                        File.Move(file, Path.Combine(staleFolder, Path.GetFileName(file)));
                        moved++;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ChunkStorage] Could not move '{file}' aside: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ChunkStorage] Could not quarantine the store in '{_worldFolder}': {ex.Message}");
            }
            return moved;
        }

        private int CountRegionFiles()
        {
            try { return Directory.GetFiles(_worldFolder, "r_*.dat").Length; }
            catch { return 0; }
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
            // A payload that is not the size of the chunk's array is refused (and the stale
            // read-cache entry dropped) so the chunk regenerates instead of being marked
            // generated with a third of it still holding the recycled chunk's data.
            if (!data.RestoreInto(chunk))
            {
                Debug.LogWarning($"[ChunkStorage] Stored chunk {chunkCoord} in {_worldFolder} has a " +
                                 "partial payload (written before 10.0.0); regenerating it from the seed.");
                return false;
            }
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
