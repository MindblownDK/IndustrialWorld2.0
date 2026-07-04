using System.Collections.Generic;
using Crest;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    /// <summary>
    /// Pools Crest sphere-water interaction probes on modular ship grids so ships
    /// create visible wakes/ripples only while the grid is actually touching water.
    /// Gameplay buoyancy remains handled by IndustrialWorld's maritime jobs.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridEntity))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CrestMaritimeWakeEmitter : MonoBehaviour
    {
        [Header("Probe Layout")]
        [UnityEngine.Range(4, 128)] public int maxInteractionProbes = 48;
        [UnityEngine.Range(0.15f, 2.5f)] public float radiusMultiplier = 0.62f;
        [UnityEngine.Range(-10f, 10f)] public float wakeWeight = 1.15f;
        public bool boostLargeWakes = true;
        [UnityEngine.Range(0.25f, 5f)] public float rebuildIntervalSeconds = 1.25f;

        [Header("Activation")]
        public bool requireWaterContact = true;
        [UnityEngine.Range(0.05f, 1f)] public float waterContactCheckInterval = 0.18f;

        private GridEntity _grid;
        private readonly List<SphereWaterInteraction> _probes = new(64);
        private readonly List<GridBlock> _sourceBlocks = new(128);
        private int _lastBlockCount = -1;
        private float _nextRebuildTime;
        private float _nextWaterContactCheck;
        private bool _waterActive;
        private Transform _probeRoot;

        private void Awake() => _grid = GetComponent<GridEntity>();

        private void OnEnable()
        {
            RebuildProbes(force: true);
            EvaluateWaterContact(force: true);
        }

        private void OnDisable() => SetProbeObjectsActive(false);

        private void OnDestroy() => ClearProbes();

        public void MarkDirty() => RebuildProbes(force: true);

        private void LateUpdate()
        {
            if (_grid == null) return;

            EvaluateWaterContact(force: false);
            if (!_waterActive)
            {
                SetProbeObjectsActive(false);
                return;
            }

            if (_grid.BlockCount != _lastBlockCount || Time.unscaledTime >= _nextRebuildTime)
                RebuildProbes(force: _grid.BlockCount != _lastBlockCount);

            SetProbeObjectsActive(true);
            SyncProbePositions();
        }

        private void EvaluateWaterContact(bool force)
        {
            if (!force && Time.unscaledTime < _nextWaterContactCheck) return;
            _nextWaterContactCheck = Time.unscaledTime + waterContactCheckInterval;

            if (!CrestOceanAvailable())
            {
                _waterActive = false;
                return;
            }

            if (!requireWaterContact)
            {
                _waterActive = true;
                return;
            }

            float probeRadius = Mathf.Max(0.5f, (_grid != null ? _grid.gridSize.CellSize() : 1f) * 1.25f);
            _waterActive = WaterProbeSystem.GetSubmergence(transform.position, probeRadius) > 0.03f;
        }

        private static bool CrestOceanAvailable()
        {
            var oceanType = System.Type.GetType("Crest.OceanRenderer, Crest");
            var instanceProperty = oceanType?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return instanceProperty?.GetValue(null) != null;
        }

        private void RebuildProbes(bool force)
        {
            if (_grid == null) return;
            if (!force && Time.unscaledTime < _nextRebuildTime) return;

            _nextRebuildTime = Time.unscaledTime + rebuildIntervalSeconds;
            _lastBlockCount = _grid.BlockCount;
            CollectSourceBlocks();
            EnsureProbeRoot();
            ResizeProbePool(Mathf.Min(maxInteractionProbes, _sourceBlocks.Count));
            SyncProbePositions();
        }

        private void CollectSourceBlocks()
        {
            _sourceBlocks.Clear();
            if (_grid == null || _grid.Blocks == null) return;

            foreach (var kv in _grid.Blocks)
            {
                var block = kv.Value;
                if (block == null || !block.isActiveAndEnabled) continue;
                if (block is GridHullBlock || block is MaritimeBlockBase)
                    _sourceBlocks.Add(block);
            }

            if (_sourceBlocks.Count > 0) return;

            foreach (var kv in _grid.Blocks)
            {
                var block = kv.Value;
                if (block != null && block.isActiveAndEnabled)
                    _sourceBlocks.Add(block);
            }
        }

        private void EnsureProbeRoot()
        {
            if (_probeRoot != null) return;
            var root = new GameObject("Crest Wake Probes");
            _probeRoot = root.transform;
            _probeRoot.SetParent(transform, false);
            _probeRoot.localPosition = Vector3.zero;
            _probeRoot.localRotation = Quaternion.identity;
            _probeRoot.localScale = Vector3.one;
        }

        private void ResizeProbePool(int targetCount)
        {
            while (_probes.Count < targetCount)
            {
                var go = new GameObject($"Wake Probe {_probes.Count + 1:00}");
                go.transform.SetParent(_probeRoot, false);
                var probe = go.AddComponent<SphereWaterInteraction>();
                ConfigureProbe(probe);
                _probes.Add(probe);
            }

            for (int i = 0; i < _probes.Count; i++)
            {
                if (_probes[i] == null) continue;
                bool active = _waterActive && i < targetCount;
                _probes[i].gameObject.SetActive(active);
                if (i < targetCount) ConfigureProbe(_probes[i]);
            }
        }

        private void ConfigureProbe(SphereWaterInteraction probe)
        {
            if (probe == null) return;
            float cellSize = _grid != null ? _grid.gridSize.CellSize() : 1f;
            probe._radius = Mathf.Max(0.15f, cellSize * radiusMultiplier);
            probe._weight = wakeWeight;
            probe._weightUpDownMul = 0.35f;
            probe._boostLargeWaves = boostLargeWakes;
            probe._velocityOffset = 0.08f;
            probe._compensateForWaveMotion = 0.35f;
        }

        private void SetProbeObjectsActive(bool active)
        {
            for (int i = 0; i < _probes.Count; i++)
            {
                if (_probes[i] != null && _probes[i].gameObject.activeSelf != active)
                    _probes[i].gameObject.SetActive(active);
            }
        }

        private void SyncProbePositions()
        {
            if (_sourceBlocks.Count == 0 || _probes.Count == 0) return;

            int activeProbeCount = Mathf.Min(_probes.Count, _sourceBlocks.Count, maxInteractionProbes);
            float step = Mathf.Max(1f, _sourceBlocks.Count / (float)activeProbeCount);

            for (int i = 0; i < _probes.Count; i++)
            {
                var probe = _probes[i];
                if (probe == null || !probe.gameObject.activeSelf) continue;

                int blockIndex = Mathf.Clamp(Mathf.FloorToInt(i * step), 0, _sourceBlocks.Count - 1);
                var block = _sourceBlocks[blockIndex];
                if (block == null)
                {
                    probe.gameObject.SetActive(false);
                    continue;
                }

                Vector3 up = VoxelEngine.WaterSim.PlanetWaterUtility.WorldUp(block.transform.position);
                float cellSize = _grid != null ? _grid.gridSize.CellSize() : 1f;
                probe.transform.position = block.transform.position - up * (cellSize * 0.18f);
            }
        }

        private void ClearProbes()
        {
            for (int i = 0; i < _probes.Count; i++)
            {
                if (_probes[i] != null) Destroy(_probes[i].gameObject);
            }

            _probes.Clear();
            _sourceBlocks.Clear();

            if (_probeRoot != null) Destroy(_probeRoot.gameObject);
            _probeRoot = null;
        }
    }
}
