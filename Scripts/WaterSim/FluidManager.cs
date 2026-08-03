// Assets/Scripts/VoxelEngine/WaterSim/FluidManager.cs
//
// Manages simulated volumetric voxel liquids across chunks.
// Integrates spherical volumetric compute solver running hybrid pressure-gravity mechanics.
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
        [Tooltip("Ticks per second for chunk sleep tracking.")]
        public float tickRate = 8f;
        [Tooltip("Max chunks to process CPU sync per tick.")]
        public int maxChunksPerTick = 6;
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
        private readonly Queue<Vector3Int> _workQueue = new();
        // Runtime marker for player/bucket-placed water. It lets a local legacy-water cleanup
        // distinguish deliberate placed liquid from the old cave-fill generation bug.
        private readonly HashSet<Vector3Int> _playerPlacedLiquid = new();
        private IVoxelWorld _placementWorld;
        private float _timer;
        private int _simulationStep;

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
                var v = ch.GetVoxelLocal(lx, ly, lz);
                density = v.waterLevel / 255f;
                return true;
            }
            return false;
        }

        public void MarkActive(Vector3Int chunkCoord)
        {
            if (_activeChunks.Add(chunkCoord)) _workQueue.Enqueue(chunkCoord);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            float interval = 1f / Mathf.Max(0.1f, tickRate);
            if (_timer < interval) return;
            _timer -= interval;

            var world = ActiveWorld.Current;
            if (world == null) return;

            int budget = maxChunksPerTick;
            int processed = 0;
            int simulationStep = ++_simulationStep;
            var toRemove = new List<Vector3Int>();

            int queueSize = _workQueue.Count;
            for (int q = 0; q < queueSize && processed < budget; q++)
            {
                var coord = _workQueue.Dequeue();
                if (!world.TryGetChunk(coord, out var chunk) || !chunk.isGenerated)
                {
                    _activeChunks.Remove(coord);
                    continue;
                }

                world.CompleteGenJobForChunk(chunk);
                world.CompleteMeshJobForChunk(chunk);

                var changed = new NativeArray<int>(1, Allocator.TempJob);
                bool didChange;
                try
                {
                    int downX = 0, downY = -1, downZ = 0;
                    Vector3Int chunkCenterVoxel = chunk.coord * VoxelConstants.CHUNK_SIZE + new Vector3Int(
                        VoxelConstants.CHUNK_SIZE / 2,
                        VoxelConstants.CHUNK_SIZE / 2,
                        VoxelConstants.CHUNK_SIZE / 2);
                    Vector3 radialDown = PlanetWaterUtility.LocalGravityDirection(chunkCenterVoxel);
                    if (Mathf.Abs(radialDown.x) > Mathf.Abs(radialDown.y) && Mathf.Abs(radialDown.x) > Mathf.Abs(radialDown.z))
                    {
                        downX = radialDown.x >= 0f ? 1 : -1; downY = 0; downZ = 0;
                    }
                    else if (Mathf.Abs(radialDown.z) > Mathf.Abs(radialDown.y))
                    {
                        downZ = radialDown.z >= 0f ? 1 : -1; downX = 0; downY = 0;
                    }
                    else
                    {
                        downY = radialDown.y >= 0f ? 1 : -1; downX = 0; downZ = 0;
                    }

                    var job = new FluidSimJob
                    {
                        voxels     = chunk.voxels,
                        chunkSize  = VoxelConstants.CHUNK_SIZE,
                        chunkSizeP = VoxelConstants.CHUNK_SIZE_P,
                        downX      = downX,
                        downY      = downY,
                        downZ      = downZ,
                        changed    = changed,
                        simulationStep = simulationStep
                    };
                    job.Run();
                    didChange = changed[0] != 0;
                }
                finally
                {
                    if (changed.IsCreated) changed.Dispose();
                }

                if (didChange)
                {
                    FlushPaddingFlowsToNeighbours(world, chunk);
                    _workQueue.Enqueue(coord);
                    chunk.isDirty = true;
                    WakeNeighbour(world, coord + new Vector3Int(1, 0, 0));
                    WakeNeighbour(world, coord + new Vector3Int(-1, 0, 0));
                    WakeNeighbour(world, coord + new Vector3Int(0, 0, 1));
                    WakeNeighbour(world, coord + new Vector3Int(0, 0, -1));
                    WakeNeighbour(world, coord + new Vector3Int(0, -1, 0));
                    WakeNeighbour(world, coord + new Vector3Int(0, 1, 0));
                    WaterMeshBuilder.Schedule(chunk);
                }
                else toRemove.Add(coord);

                processed++;
            }

            foreach (var c in toRemove) _activeChunks.Remove(c);
        }

        private void WakeNeighbour(IVoxelWorld world, Vector3Int coord)
        {
            if (world.TryGetChunk(coord, out var ch) && ch.isGenerated) MarkActive(coord);
        }

        private void FlushPaddingFlowsToNeighbours(IVoxelWorld world, Chunk chunk)
        {
            FlushFace(world, chunk, new Vector3Int( 1, 0, 0));
            FlushFace(world, chunk, new Vector3Int(-1, 0, 0));
            FlushFace(world, chunk, new Vector3Int( 0, 1, 0));
            FlushFace(world, chunk, new Vector3Int( 0,-1, 0));
            FlushFace(world, chunk, new Vector3Int( 0, 0, 1));
            FlushFace(world, chunk, new Vector3Int( 0, 0,-1));
        }

        private void FlushFace(IVoxelWorld world, Chunk source, Vector3Int dir)
        {
            const int S = VoxelConstants.CHUNK_SIZE;
            var nCoord = source.coord + dir;
            if (!world.TryGetChunk(nCoord, out var target) || target == null || !target.isGenerated) return;
            world.CompleteGenJobForChunk(target);
            world.CompleteMeshJobForChunk(target);

            bool changed = false;
            int sx = dir.x > 0 ? S : (dir.x < 0 ? -1 : 0);
            int sy = dir.y > 0 ? S : (dir.y < 0 ? -1 : 0);
            int sz = dir.z > 0 ? S : (dir.z < 0 ? -1 : 0);
            int tx = dir.x > 0 ? 0 : (dir.x < 0 ? S - 1 : 0);
            int ty = dir.y > 0 ? 0 : (dir.y < 0 ? S - 1 : 0);
            int tz = dir.z > 0 ? 0 : (dir.z < 0 ? S - 1 : 0);

            for (int z = 0; z < S; z++)
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int px = dir.x == 0 ? x : sx;
                int py = dir.y == 0 ? y : sy;
                int pz = dir.z == 0 ? z : sz;
                int lx = dir.x == 0 ? x : tx;
                int ly = dir.y == 0 ? y : ty;
                int lz = dir.z == 0 ? z : tz;

                var pad = source.GetVoxelLocal(px, py, pz);
                if (!FluidMaterialUtility.IsFluid(pad)) continue;
                var dst = target.GetVoxelLocal(lx, ly, lz);
                if (dst.IsSolid) continue;

                bool same = dst.waterLevel == 0 || FluidMaterialUtility.LiquidFromVoxel(dst) == FluidMaterialUtility.LiquidFromVoxel(pad);
                if (!same || pad.waterLevel <= dst.waterLevel) continue;

                dst.density = -1;
                dst.material = pad.material;
                dst.waterLevel = pad.waterLevel;
                target.SetVoxelLocal(lx, ly, lz, dst);

                pad.waterLevel = 0;
                FluidMaterialUtility.ClearLiquid(ref pad);
                source.SetVoxelLocal(px, py, pz, pad);
                changed = true;
            }

            if (!changed) return;
            target.isModified = true;
            MarkActive(target.coord);
            WaterMeshBuilder.Schedule(target);
        }

        private void EnsurePlacementWorld(IVoxelWorld world)
        {
            if (object.ReferenceEquals(_placementWorld, world)) return;
            _placementWorld = world;
            _playerPlacedLiquid.Clear();
        }

        /// <summary>
        /// Removes legacy auto-generated water around a freshly mined dry cave. Real ocean basins
        /// and bucket/pump-placed liquid are preserved. New SphereDensity output no longer creates
        /// this water; this is a local migration repair for already-generated terrain.
        /// </summary>
        public void PruneLegacyDryCaveWater(Vector3Int center, int radius = 2)
        {
            var world = ActiveWorld.Current;
            if (world is not SphereWorld sphere || sphere.body == null) return;
            EnsurePlacementWorld(world);
            radius = Mathf.Clamp(radius, 1, 6);
            for (int z = -radius; z <= radius; z++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                var cell = center + new Vector3Int(x, y, z);
                if ((cell - center).sqrMagnitude > radius * radius) continue;
                if (_playerPlacedLiquid.Contains(cell) || sphere.IsNaturalOceanBasinAt(cell)) continue;
                Voxel voxel = world.GetVoxelWorld(cell);
                if (!FluidMaterialUtility.Matches(voxel, LiquidType.Water)) continue;
                world.SetVoxelWorld(cell, Voxel.Empty, remesh: true);
                _playerPlacedLiquid.Remove(cell);
            }
        }

        public void PlaceWater(Vector3Int worldVoxel, byte level = 255) => PlaceLiquid(worldVoxel, LiquidType.Water, level);
        public void PlaceOil(Vector3Int worldVoxel, byte level = 255) => PlaceLiquid(worldVoxel, LiquidType.CrudeOil, level);

        public void PlaceLiquid(Vector3Int worldVoxel, LiquidType liquid, byte level = 255)
        {
            var world = ActiveWorld.Current;
            if (world == null || !TryGetChunkAndLocal(world, worldVoxel, out var coord, out var ch, out int lx, out int ly, out int lz)) return;
            EnsurePlacementWorld(world);

            world.CompleteGenJobForChunk(ch);
            world.CompleteMeshJobForChunk(ch);

            var v = ch.GetVoxelLocal(lx, ly, lz);
            if (v.IsSolid) return;

            FluidMaterialUtility.SetLiquid(ref v, liquid, level);
            ch.SetVoxelLocal(lx, ly, lz, v);
            _playerPlacedLiquid.Add(worldVoxel);
            ch.isDirty = true;
            MarkActive(coord);
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
            if (v.waterLevel == 0)
            {
                FluidMaterialUtility.ClearLiquid(ref v);
                EnsurePlacementWorld(world);
                _playerPlacedLiquid.Remove(worldVoxel);
            }
            ch.SetVoxelLocal(lx, ly, lz, v);
            ch.isDirty = true;
            MarkActive(coord);
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
