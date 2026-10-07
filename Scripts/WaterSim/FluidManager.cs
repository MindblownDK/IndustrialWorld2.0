// Assets/Scripts/VoxelEngine/WaterSim/FluidManager.cs
//
// Manages simulated volumetric voxel liquids across chunks.
// Authoritative conservative hydraulic-head solver; optional GPU assist is not authoritative.
// Maintains strict save-compatibility with Voxel.waterLevel bytes while utilizing
// compute buffers for real-time parallel neighbor pressure advection.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;

namespace VoxelEngine.WaterSim
{
    public class FluidManager : MonoBehaviour
    {
        public static FluidManager Instance { get; private set; }

        [Header("Simulation")]
        [Tooltip("Fluid ticks per second. 9.16.0 flow remake: 10 Hz with queued edit-wake.")]
        public float tickRate = 10f;
        [Tooltip("Max chunks to simulate per tick. All edits and pumps use this bounded work queue.")]
        public int maxChunksPerTick = 8;
        [Tooltip("Chunks within this radius of the player are active.")]
        public int activeRadius = 4;
        [Tooltip("Compute solver iterations dispatched per rendered frame.")]
        public int computeIterationsPerFrame = 1;

        [Header("Native Volumetric Assist")]
        [Tooltip("Skip optional native volumetric compute every N frames. 1 = every frame, 2 = every other frame.")]
        public int computeFrameSkip = 2;
        [Tooltip("Optional GPU density assist. The authoritative liquid simulation and native surface renderer remain voxel-driven either way.")]
        public bool useNativeVolumetricAssist = false;
        private int _computeFrameCounter;

        private readonly HashSet<Vector3Int> _activeChunks = new();
        private readonly LinkedList<Vector3Int> _workQueue = new();
        private readonly Dictionary<Vector3Int, LinkedListNode<Vector3Int>> _queuedWork = new();
        private float _timer;
        private int _simulationStep;
        private int _tickRemaining;
        private static readonly Unity.Profiling.ProfilerMarker StepMarker = new("Voxel.FluidStep");

        // Compute Volumetric Layer
        private ComputeShader _fluidSimShader;
        private GraphicsBuffer _fluidGpuBuffer;
        private int _kernelPressureSolve = -1;
        private int _kernelUpdateFlow = -1;
        private bool _isComputeInitialized;
        private readonly Dictionary<Vector3Int, SparseWaterChunk> _sparseChunks = new();
        private const int MaxActiveGpuChunks = 64;
        private int _nextGpuSlot;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (useNativeVolumetricAssist) InitializeComputeSystem();
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
        }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("FluidManager");
            Instance = go.AddComponent<FluidManager>();
            DontDestroyOnLoad(go);
        }

        private void InitializeComputeSystem()
        {
            if (_isComputeInitialized) return;
            _fluidSimShader = Resources.Load<ComputeShader>("FluidSim");
            if (_fluidSimShader != null)
            {
                _kernelPressureSolve = _fluidSimShader.FindKernel("PressureSolve");
                _kernelUpdateFlow = _fluidSimShader.FindKernel("UpdateFlow");

                int cellsPerChunk = VoxelConstants.CHUNK_SIZE * VoxelConstants.CHUNK_SIZE * VoxelConstants.CHUNK_SIZE;
                int totalCells = MaxActiveGpuChunks * cellsPerChunk;
                _fluidGpuBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalCells, MarshalSizeOfFluidCell());

                _isComputeInitialized = true;
                Debug.Log("[FluidManager] ✓ Volumetric Compute Fluid Buffer allocated successfully.");
            }
        }

        private static int MarshalSizeOfFluidCell() => 16;

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Native voxel surfaces are authoritative. The optional compute pass only adds
            // density-assist data for nearby gameplay/render sampling; it never owns the ocean.
            if (!useNativeVolumetricAssist) return;
            if (!_isComputeInitialized) InitializeComputeSystem();
            if (!_isComputeInitialized || _fluidSimShader == null || cameras == null || cameras.Count == 0) return;

            _computeFrameCounter++;
            if (_computeFrameCounter % Mathf.Max(1, computeFrameSkip) != 0) return;

            Camera mainCam2 = cameras[0];
            UpdateLodAndSparseAllocation(mainCam2 != null ? mainCam2.transform.position : Vector3.zero);

            if (_sparseChunks.Count > 0 && _kernelPressureSolve >= 0 && _kernelUpdateFlow >= 0)
            {
                _fluidSimShader.SetBuffer(_kernelPressureSolve, "_FluidBuffer", _fluidGpuBuffer);
                _fluidSimShader.SetBuffer(_kernelUpdateFlow, "_FluidBuffer", _fluidGpuBuffer);
                _fluidSimShader.SetInt("_ChunkSize", VoxelConstants.CHUNK_SIZE);

                Vector3 tideDir = PlanetWaterUtility.CurrentTideDirectionLocal();
                _fluidSimShader.SetVector("_GravityDir", tideDir);

                for (int i = 0; i < Mathf.Clamp(computeIterationsPerFrame,1,2); i++)
                {
                    _fluidSimShader.Dispatch(_kernelPressureSolve, VoxelConstants.CHUNK_SIZE / 4, VoxelConstants.CHUNK_SIZE / 4, VoxelConstants.CHUNK_SIZE / 4);
                    _fluidSimShader.Dispatch(_kernelUpdateFlow, VoxelConstants.CHUNK_SIZE / 4, VoxelConstants.CHUNK_SIZE / 4, VoxelConstants.CHUNK_SIZE / 4);
                }
            }
        }

        private void UpdateLodAndSparseAllocation(Vector3 camPos)
        {
            var world = ActiveWorld.Current;
            if (world == null) return;

            foreach (var coord in _activeChunks)
            {
                Vector3 chunkLocalPos = (Vector3)(coord * VoxelConstants.CHUNK_SIZE);
                Vector3 chunkWorldPos = world is VoxelEngine.Cosmos.SphereWorld sphere && sphere.body != null
                    ? sphere.body.transform.TransformPoint(chunkLocalPos)
                    : chunkLocalPos;
                float dist = Vector3.Distance(camPos, chunkWorldPos);

                if (!_sparseChunks.TryGetValue(coord, out var sparse))
                {
                    sparse = new SparseWaterChunk { chunkCoord = coord };
                    _sparseChunks[coord] = sparse;
                }

                if (dist < 50f) sparse.currentLod = WaterLodTier.FullVolumetric_60Hz;
                else if (dist < 200f) sparse.currentLod = WaterLodTier.SWE_Gerstner_30Hz;
                else if (dist < 1000f) sparse.currentLod = WaterLodTier.SimplifiedSWE_10Hz;
                else sparse.currentLod = WaterLodTier.StaticHeightmap_1Hz;

                float seaDist = PlanetWaterUtility.SignedDistanceToSea(chunkWorldPos);
                if (seaDist < -VoxelConstants.CHUNK_SIZE * 1.5f)
                {
                    sparse.isDeepInteriorConstant = true;
                }
                else
                {
                    sparse.isDeepInteriorConstant = false;
                    if (sparse.bufferOffsetIndex < 0)
                    {
                        sparse.bufferOffsetIndex = (_nextGpuSlot++) % MaxActiveGpuChunks;
                    }
                }
            }
        }

        public bool TryGetVolumetricDensity(Vector3Int worldVoxel, out float density)
        {
            density = 0f;
            var world = ActiveWorld.Current;
            if (world == null || !TryGetChunkAndLocal(world, worldVoxel, out var coord, out var ch, out int lx, out int ly, out int lz)) return false;

            if (_sparseChunks.TryGetValue(coord, out var sparse))
            {
                if (sparse.isDeepInteriorConstant)
                {
                    density = 1f;
                    return true;
                }
            }

            if (ch != null)
            {
                // A chunk can still have its SphereChunkGenJob in flight, and that job owns
                // the voxel array this reads. Every other voxel access in this file completes
                // the pending job first; this path did not, so a water probe over a chunk
                // that was still generating threw the job-safety check out of
                // MaritimePropulsionSystem.FixedUpdate every physics step. Completing is a
                // no-op once the job has landed, which is the common case.
                world.CompleteGenJobForChunk(ch);
                var v = ch.GetVoxelLocal(lx, ly, lz);
                density = v.waterLevel / 255f;
                return true;
            }
            return false;
        }

        public void MarkActive(Vector3Int chunkCoord) => QueueActive(chunkCoord, urgent: false);

        private void MarkUrgent(Vector3Int chunkCoord) => QueueActive(chunkCoord, urgent: true);

        private void QueueActive(Vector3Int chunkCoord, bool urgent)
        {
            _activeChunks.Add(chunkCoord);
            if (_queuedWork.TryGetValue(chunkCoord, out LinkedListNode<Vector3Int> queued))
            {
                if (urgent && queued != _workQueue.First)
                {
                    _workQueue.Remove(queued);
                    _queuedWork[chunkCoord] = _workQueue.AddFirst(chunkCoord);
                }
                return;
            }

            LinkedListNode<Vector3Int> node = urgent
                ? _workQueue.AddFirst(chunkCoord)
                : _workQueue.AddLast(chunkCoord);
            _queuedWork.Add(chunkCoord, node);
        }

        // ── Mountain springs (9.6.0 — Phase 3 liquid flow) ─────────────────
        // Deterministic spring voxels (registered by SphereWorld during chunk
        // finalisation) act as infinite sources: while their chunk is loaded they
        // refill to full every few ticks and the cellular sim carries the water
        // downhill — real streams that run down mountainsides, pool in dips and
        // feed waterfalls. No save data: the same world seed always re-derives the
        // same springs. Buried or built-over springs go dormant automatically.
        [Header("Springs")]
        [Tooltip("Spring voxels refilled per fluid tick (keeps stream sources flowing without hitching).")]
        public int maxSpringRefillsPerTick = 24;

        private readonly HashSet<Vector3Int> _springs = new();
        private readonly List<Vector3Int> _springScratch = new();
        private int _springCursor;

        /// <summary>Register an infinite water source at a body-local voxel.</summary>
        public void RegisterSpring(Vector3Int worldVoxel) => _springs.Add(worldVoxel);

        /// <summary>Forget every spring (world/body switch).</summary>
        public void ClearSprings()
        {
            _springs.Clear();
            _springScratch.Clear();
            _springCursor = 0;
            _activeChunks.Clear();
            _workQueue.Clear();
            _queuedWork.Clear();
            ConservativeFluidSolver.Reset();
            _tickRemaining = 0;
            _timer = 0f;
        }

        private void RefillSprings(IVoxelWorld world)
        {
            if (_springs.Count == 0) return;
            if (_springScratch.Count != _springs.Count)
            {
                _springScratch.Clear();
                _springScratch.AddRange(_springs);
            }

            int budget = maxSpringRefillsPerTick;
            int scanned = 0;
            const int S = VoxelConstants.CHUNK_SIZE;
            while (budget > 0 && scanned < _springScratch.Count)
            {
                scanned++;
                _springCursor = (_springCursor + 1) % _springScratch.Count;
                Vector3Int s = _springScratch[_springCursor];
                var chunkCoord = new Vector3Int(
                    Mathf.FloorToInt(s.x / (float)S),
                    Mathf.FloorToInt(s.y / (float)S),
                    Mathf.FloorToInt(s.z / (float)S));
                if (!world.TryGetChunk(chunkCoord, out var chunk) || chunk == null || !chunk.isGenerated)
                    continue;

                var v = world.GetVoxelWorld(s);
                if (v.IsSolid) continue;              // buried / built over → dormant
                if (v.waterLevel >= 200) continue;    // already full

                FluidMaterialUtility.SetLiquid(ref v, VoxelEngine.Items.LiquidType.Water, 255);
                world.SetVoxelWorld(s, v, remesh: false);
                // Liquid state does not invalidate the terrain mesh.
                MarkUrgent(chunkCoord);
                WaterMeshBuilder.Schedule(chunk);
                budget--;
            }
        }

        private void Update()
        {
            var world = ActiveWorld.Current;
            if (world == null) return;
            _timer += Time.deltaTime;
            float interval = 1f / Mathf.Max(0.1f, tickRate);
            if (_tickRemaining <= 0 && _timer >= interval)
            {
                _timer = Mathf.Min(_timer - interval, interval);
                RefillSprings(world);
                _simulationStep++;
                _tickRemaining = Mathf.Min(maxChunksPerTick, _workQueue.Count);
            }
            if (_tickRemaining <= 0 || _workQueue.First == null) { _tickRemaining = 0; return; }
            // One chunk per rendered frame, never a catch-up burst. The priority linked list
            // moves player-edited water to the front without duplicating queue entries.
            _tickRemaining--;
            LinkedListNode<Vector3Int> next = _workQueue.First;
            var coord = next.Value;
            _workQueue.RemoveFirst();
            _queuedWork.Remove(coord);
            if (!world.TryGetChunk(coord, out var chunk) || !chunk.isGenerated)
            { _activeChunks.Remove(coord); return; }
            using (StepMarker.Auto())
            {
                if (StepChunkNow(world, coord, chunk, _simulationStep)) QueueActive(coord, urgent: false);
                else _activeChunks.Remove(coord);
            }
        }

        /// <summary>
        /// Runs ONE synchronous fluid step for a chunk (gravity, spread, density layering)
        /// and propagates the result: conservative boundary transfers, neighbour wake-up, dirty flag and
        /// mesh schedule. Returns true when any liquid moved, so callers can keep the chunk
        /// queued or sleep it.
        /// </summary>
        private bool StepChunkNow(IVoxelWorld world, Vector3Int coord, Chunk chunk, int simulationStep)
        {
            world.CompleteGenJobForChunk(chunk);
            world.CompleteMeshJobForChunk(chunk);

            bool didChange = ConservativeFluidSolver.Step(world, chunk, simulationStep);

            if (didChange)
            {
                // Liquid state does not invalidate the terrain mesh.
                WakeNeighbour(world, coord + new Vector3Int(1, 0, 0));
                WakeNeighbour(world, coord + new Vector3Int(-1, 0, 0));
                WakeNeighbour(world, coord + new Vector3Int(0, 0, 1));
                WakeNeighbour(world, coord + new Vector3Int(0, 0, -1));
                WakeNeighbour(world, coord + new Vector3Int(0, -1, 0));
                WakeNeighbour(world, coord + new Vector3Int(0, 1, 0));
                WaterMeshBuilder.Schedule(chunk);
            }
            return didChange;
        }

        /// <summary>
        /// Voxel-edit wake (9.16.0 flow remake). The voxel editor calls this after ANY
        /// terrain change — mining or building. Edits queue the affected neighbourhood for the next budgeted tick,
        /// avoiding dozens of synchronous whole-chunk solves in the edit frame.
        /// </summary>
        public void NotifyVoxelEdited(Vector3Int centerWorldVoxel, int radius)
        {
            var world = ActiveWorld.Current;
            if (world == null) return;

            const int cs = VoxelConstants.CHUNK_SIZE;
            Vector3Int chunkCenter = new Vector3Int(
                Mathf.FloorToInt(centerWorldVoxel.x / (float)cs),
                Mathf.FloorToInt(centerWorldVoxel.y / (float)cs),
                Mathf.FloorToInt(centerWorldVoxel.z / (float)cs));
            // Liquid can't travel further than one chunk in a single step, so the synchronous
            // neighbourhood is clamped: 3×3×3 for normal edits, 5×5×5 for massive brushes.
            // Insert outer rings first because urgent work is placed at the list head; the edited
            // chunk then runs before its neighbours, while older distant flow work yields fairly.
            int chunkR = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(1, radius) / (float)cs), 1, 2);
            for (int ring = chunkR; ring >= 0; ring--)
            for (int z = -ring; z <= ring; z++)
            for (int y = -ring; y <= ring; y++)
            for (int x = -ring; x <= ring; x++)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) != ring) continue;
                var coord = chunkCenter + new Vector3Int(x, y, z);
                if (!world.TryGetChunk(coord, out var chunk) || chunk == null || !chunk.isGenerated) continue;
                MarkUrgent(coord);
            }

            // The next regular tick fires immediately so the flow wave keeps pace with edits.
            _timer = 1f / Mathf.Max(0.1f, tickRate);
        }

        private void WakeNeighbour(IVoxelWorld world, Vector3Int coord, bool urgent = false)
        {
            if (!world.TryGetChunk(coord, out var chunk) || chunk == null || !chunk.isGenerated) return;
            if (urgent) MarkUrgent(coord);
            else MarkActive(coord);
        }



        public void PlaceWater(Vector3Int worldVoxel, byte level = 255) => PlaceLiquid(worldVoxel, LiquidType.Water, level);
        public void PlaceOil(Vector3Int worldVoxel, byte level = 255) => PlaceLiquid(worldVoxel, LiquidType.CrudeOil, level);

        public void PlaceLiquid(Vector3Int worldVoxel, LiquidType liquid, byte level = 255)
        {
            var world = ActiveWorld.Current;
            if (world == null || !TryGetChunkAndLocal(world, worldVoxel, out var coord, out var ch, out int lx, out int ly, out int lz)) return;

            world.CompleteGenJobForChunk(ch);
            world.CompleteMeshJobForChunk(ch);

            var v = ch.GetVoxelLocal(lx, ly, lz);
            if (v.IsSolid) return;

            FluidMaterialUtility.SetLiquid(ref v, liquid, level);
            ch.SetVoxelLocal(lx, ly, lz, v);
            ch.isDirty = true;
            ch.isModified = true;
            MarkUrgent(coord);
            WakeNeighbour(world, coord + Vector3Int.right, urgent: true);
            WakeNeighbour(world, coord - Vector3Int.right, urgent: true);
            WakeNeighbour(world, coord + Vector3Int.up, urgent: true);
            WakeNeighbour(world, coord - Vector3Int.up, urgent: true);
            WakeNeighbour(world, coord + new Vector3Int(0, 0, 1), urgent: true);
            WakeNeighbour(world, coord - new Vector3Int(0, 0, 1), urgent: true);
            WaterMeshBuilder.Schedule(ch);
        }

        public bool DrainWater(Vector3Int worldVoxel) => DrainLiquid(worldVoxel, LiquidType.Water, 255) > 0;
        public bool DrainOil(Vector3Int worldVoxel) => DrainLiquid(worldVoxel, LiquidType.CrudeOil, 255) > 0;

        public byte PumpFromLiquid(Vector3Int worldVoxel, LiquidType liquid, byte maxLevel = 255, float suctionRadius = 3f)
        {
            byte drained = DrainLiquid(worldVoxel, liquid, maxLevel);
            if (drained == 0) return 0;

            var world = ActiveWorld.Current;
            if (world == null) return drained;

            int r = Mathf.Clamp(Mathf.CeilToInt(suctionRadius), 1, 8);
            for (int z = -r; z <= r; z++)
            for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                var p = worldVoxel + new Vector3Int(x, y, z);
                if ((p - worldVoxel).sqrMagnitude > r * r) continue;
                if (!TryGetChunkAndLocal(world, p, out var coord, out _, out _, out _, out _)) continue;
                MarkActive(coord);
            }

            return drained;
        }

        public byte DrainLiquid(Vector3Int worldVoxel, LiquidType liquid, byte maxLevel = 255)
        {
            var world = ActiveWorld.Current;
            if (world == null || !TryGetChunkAndLocal(world, worldVoxel, out var coord, out var ch, out int lx, out int ly, out int lz)) return 0;

            world.CompleteGenJobForChunk(ch);
            world.CompleteMeshJobForChunk(ch);

            var v = ch.GetVoxelLocal(lx, ly, lz);
            if (!FluidMaterialUtility.Matches(v, liquid)) return 0;

            byte drained = v.waterLevel < maxLevel ? v.waterLevel : maxLevel;
            v.waterLevel = (byte)(v.waterLevel - drained);
            if (v.waterLevel == 0) FluidMaterialUtility.ClearLiquid(ref v);
            ch.SetVoxelLocal(lx, ly, lz, v);
            ch.isDirty = true;
            ch.isModified = true;
            MarkUrgent(coord);
            WakeNeighbour(world, coord + Vector3Int.right, urgent: true);
            WakeNeighbour(world, coord - Vector3Int.right, urgent: true);
            WakeNeighbour(world, coord + Vector3Int.up, urgent: true);
            WakeNeighbour(world, coord - Vector3Int.up, urgent: true);
            WakeNeighbour(world, coord + new Vector3Int(0, 0, 1), urgent: true);
            WakeNeighbour(world, coord - new Vector3Int(0, 0, 1), urgent: true);
            WaterMeshBuilder.Schedule(ch);
            return drained;
        }

        public byte GetWaterLevel(Vector3Int worldVoxel) => GetLiquidLevel(worldVoxel, LiquidType.Water);

        public byte GetLiquidLevel(Vector3Int worldVoxel, LiquidType liquid)
        {
            var world = ActiveWorld.Current;
            if (world == null) return 0;
            var v = world.GetVoxelWorld(worldVoxel);
            return FluidMaterialUtility.Matches(v, liquid) ? v.waterLevel : (byte)0;
        }

        public LiquidType GetLiquidType(Vector3Int worldVoxel)
        {
            var world = ActiveWorld.Current;
            if (world == null) return LiquidType.Water;
            return FluidMaterialUtility.LiquidFromVoxel(world.GetVoxelWorld(worldVoxel));
        }

        public (int voxels, float litres, bool isInfinite) ScanPool(
            Vector3Int seed, LiquidType liquid, float reachRadius, int infiniteThreshold, int maxScan,
            List<Vector3Int> capturedCells = null)
        {
            capturedCells?.Clear();
            var world = ActiveWorld.Current;
            if (world == null) return (0, 0, false);
            if (!FluidMaterialUtility.Matches(world.GetVoxelWorld(seed), liquid)) return (0, 0, false);

            var seen = new HashSet<Vector3Int>();
            var q = new Queue<Vector3Int>();
            q.Enqueue(seed);
            seen.Add(seed);
            float litresPerLevel = 1000f / 255f;
            int count = 0;
            float litres = 0f;
            float r2 = reachRadius * reachRadius * 9f;

            while (q.Count > 0 && count < maxScan)
            {
                var p = q.Dequeue();
                var v = world.GetVoxelWorld(p);
                if (!FluidMaterialUtility.Matches(v, liquid)) continue;
                count++;
                litres += v.waterLevel * litresPerLevel;
                capturedCells?.Add(p);

                foreach (var off in NeighbourOffsets)
                {
                    var n = p + off;
                    if (seen.Contains(n)) continue;
                    if ((n - seed).sqrMagnitude > r2) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }

            bool infinite = count >= infiniteThreshold || count >= maxScan;
            return (count, litres, infinite);
        }

        private static readonly Vector3Int[] NeighbourOffsets =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.forward,
            Vector3Int.back, Vector3Int.up, Vector3Int.down
        };

        private static bool TryGetChunkAndLocal(IVoxelWorld world, Vector3Int worldVoxel, out Vector3Int coord, out Chunk ch, out int lx, out int ly, out int lz)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            coord = new Vector3Int(
                Mathf.FloorToInt(worldVoxel.x / (float)S),
                Mathf.FloorToInt(worldVoxel.y / (float)S),
                Mathf.FloorToInt(worldVoxel.z / (float)S));
            ch = null;
            lx = worldVoxel.x - coord.x * S;
            ly = worldVoxel.y - coord.y * S;
            lz = worldVoxel.z - coord.z * S;
            return world.TryGetChunk(coord, out ch);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_fluidGpuBuffer != null)
            {
                _fluidGpuBuffer.Release();
                _fluidGpuBuffer = null;
            }
        }
    }
}
