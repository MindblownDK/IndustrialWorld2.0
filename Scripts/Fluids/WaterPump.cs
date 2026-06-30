// Assets/Scripts/VoxelEngine/Fluids/WaterPump.cs
//
// Powered voxel-liquid intake. Pumps Water or Crude Oil from nearby pools into an
// internal buffer, then pushes it into connected WaterTanks through the existing
// world fluid-pipe network. Large pools are treated as infinite sources so ocean
// pumps keep producing without deleting the sea.
//
// V2 enhancements:
//   • Uses FluidManager.ScanPool for accurate pool volume reporting
//   • Enhanced UI data: pool voxel count, total litres, infinite/finite status
//   • Internal tank displayed prominently in pump UI
//   • Cleaner separation of scan / pump / push phases

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Fluids
{
    [RequireComponent(typeof(PowerConsumer))]
    public class WaterPump : FluidNode
    {
        public override FluidNodeKind Kind => FluidNodeKind.Pump;

        [Header("Liquid")]
        public LiquidType liquidType = LiquidType.Water;
        [Tooltip("Litres per second pulled from the pool into the internal buffer.")]
        public float pumpLps = 120f;
        [Tooltip("Litres per second pushed from the internal buffer into connected tanks.")]
        public float outputLps = 180f;
        [Tooltip("Internal buffer size before the pipe/tank network takes over.")]
        public float internalCapacityLitres = 500f;
        public float internalLitres;

        [Header("Source Scan")]
        [Tooltip("Voxel radius around/below the pump to search for a pool.")]
        public float reach = 5f;
        [Tooltip("Connected fluid voxels required before the source is considered infinite.")]
        public int infiniteVoxelThreshold = 1400;
        [Tooltip("BFS safety cap for pool volume scans.")]
        public int maxPoolScanVoxels = 4096;

        [Header("Runtime Source Info")]
        [SerializeField] private bool _hasSource;
        [SerializeField] private bool _sourceInfinite;
        [SerializeField] private float _sourceLitres;
        [SerializeField] private int _sourceVoxels;
        [SerializeField] private Vector3Int _lastSourceVoxel;

        private PowerConsumer _power;
        private float _scanTimer;
        private readonly List<Vector3Int> _poolCells = new(1024);

        // ── Public UI accessors ─────────────────────────────────────────────

        public bool HasSource => _hasSource;
        public bool SourceInfinite => _sourceInfinite;
        public float SourceLitres => _sourceLitres;
        public int SourceVoxels => _sourceVoxels;
        public float InternalFill01 => internalCapacityLitres > 0 ? Mathf.Clamp01(internalLitres / internalCapacityLitres) : 0f;
        public bool IsPowered => _power == null || _power.IsPowered;
        public string SourceStatus => !_hasSource ? "No pool detected"
            : (_sourceInfinite ? "∞ Infinite pool" : $"Finite pool: {_sourceLitres:0} L ({_sourceVoxels} voxels)");

        /// <summary>Pool fill relative to infinite threshold (0..1+). UI progress bar.</summary>
        public float PoolInfiniteProgress => _sourceVoxels > 0
            ? Mathf.Clamp01((float)_sourceVoxels / infiniteVoxelThreshold)
            : 0f;

        private void Awake()
        {
            _power = GetComponent<PowerConsumer>();
        }

        private void Update()
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 0.75f;
                ScanSource();
            }

            if (!IsPowered || !_hasSource) { PushToNetwork(Time.deltaTime); return; }

            // Pump from pool into internal buffer
            float space = Mathf.Max(0f, internalCapacityLitres - internalLitres);
            float want = Mathf.Min(space, pumpLps * Time.deltaTime);
            if (want > 0.01f)
            {
                float gained = _sourceInfinite ? want : DrainFromFinitePool(want);
                internalLitres = Mathf.Min(internalCapacityLitres, internalLitres + gained);
            }

            PushToNetwork(Time.deltaTime);
        }

        // ── Network push ────────────────────────────────────────────────────

        private void PushToNetwork(float dt)
        {
            if (internalLitres <= 0.01f || network == null) return;
            float remaining = Mathf.Min(internalLitres, outputLps * dt);
            foreach (var node in network.nodes)
            {
                if (remaining <= 0.01f) break;
                if (node == null || node == this) continue;
                if (node is WaterTank tank)
                {
                    float accepted = tank.AddSome(liquidType, remaining);
                    remaining -= accepted;
                    internalLitres -= accepted;
                }
            }
        }

        // ── Pool scanning ───────────────────────────────────────────────────

        public void ScanSource()
        {
            _hasSource = false;
            _sourceInfinite = false;
            _sourceLitres = 0f;
            _sourceVoxels = 0;
            _poolCells.Clear();

            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return;
            FluidManager.EnsureInstance();

            Vector3Int origin = world.WorldToVoxel(transform.position + Vector3.down * 0.5f);
            if (!FindSeed(world, origin, out var seed)) return;

            _lastSourceVoxel = seed;

            // Use FluidManager's pool scanner for accurate results
            if (FluidManager.Instance != null)
            {
                var result = FluidManager.Instance.ScanPool(
                    seed, liquidType, reach, infiniteVoxelThreshold, maxPoolScanVoxels);
                _sourceVoxels = result.voxels;
                _sourceLitres = result.litres;
                _sourceInfinite = result.isInfinite;
            }
            else
            {
                // Fallback: manual BFS
                FloodPool(world, seed);
                _sourceInfinite = _sourceVoxels >= infiniteVoxelThreshold || _poolCells.Count >= maxPoolScanVoxels;
            }

            _hasSource = _sourceVoxels > 0;
        }

        private bool FindSeed(VoxelEngine.Core.IVoxelWorld world, Vector3Int origin, out Vector3Int seed)
        {
            int r = Mathf.CeilToInt(reach);
            for (int dy = 0; dy >= -r; dy--)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                var p = origin + new Vector3Int(dx, dy, dz);
                var v = world.GetVoxelWorld(p);
                if (FluidMaterialUtility.Matches(v, liquidType)) { seed = p; return true; }
            }
            seed = default;
            return false;
        }

        private void FloodPool(VoxelEngine.Core.IVoxelWorld world, Vector3Int seed)
        {
            var seen = new HashSet<Vector3Int>();
            var q = new Queue<Vector3Int>();
            q.Enqueue(seed);
            seen.Add(seed);
            float litresPerLevel = LitresPerVoxel / 255f;

            while (q.Count > 0 && _poolCells.Count < maxPoolScanVoxels)
            {
                var p = q.Dequeue();
                var v = world.GetVoxelWorld(p);
                if (!FluidMaterialUtility.Matches(v, liquidType)) continue;

                _poolCells.Add(p);
                _sourceVoxels++;
                _sourceLitres += v.waterLevel * litresPerLevel;

                Enqueue(p + Vector3Int.right);
                Enqueue(p + Vector3Int.left);
                Enqueue(p + Vector3Int.forward);
                Enqueue(p + Vector3Int.back);
                Enqueue(p + Vector3Int.up);
                Enqueue(p + Vector3Int.down);
            }

            void Enqueue(Vector3Int n)
            {
                if (seen.Contains(n)) return;
                if ((n - seed).sqrMagnitude > reach * reach * 9f) return;
                seen.Add(n);
                q.Enqueue(n);
            }
        }

        // ── Finite pool drain ───────────────────────────────────────────────

        private float DrainFromFinitePool(float litres)
        {
            if (_poolCells.Count == 0 && _sourceVoxels == 0) return 0f;
            FluidManager.EnsureInstance();
            float drained = 0f;
            float litresPerLevel = LitresPerVoxel / 255f;

            // Re-scan pool cells to get current state, then drain from highest first
            if (_poolCells.Count == 0) ScanSource();
            if (_poolCells.Count == 0) return 0f;

            _poolCells.Sort((a, b) => b.y.CompareTo(a.y));
            for (int i = 0; i < _poolCells.Count && drained < litres; i++)
            {
                float remaining = litres - drained;
                byte levels = (byte)Mathf.Clamp(Mathf.CeilToInt(remaining / litresPerLevel), 1, 255);
                byte got = FluidManager.Instance != null ? FluidManager.Instance.PumpFromLiquid(_poolCells[i], liquidType, levels, reach) : (byte)0;
                drained += got * litresPerLevel;
            }
            if (drained > 0f) ScanSource();
            return drained;
        }

        private const float LitresPerVoxel = 1000f;
    }
}
