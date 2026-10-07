// Assets/Scripts/VoxelEngine/Environment/Pollution/PollutionService.cs
//
// Host/offline-authoritative, sparse, body-local pollution simulation. Cells are
// anchored to a celestial body's transform so floating-origin shifts and orbital
// motion cannot move a plume away from the factory that created it.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Menu;
using VoxelEngine.Networking;
using VoxelEngine.Power.Wind;
using VoxelEngine.Weather;

namespace VoxelEngine.Environment
{
    [DefaultExecutionOrder(-35)]
    public sealed class PollutionService : MonoBehaviour
    {
        public const float CellSizeMetres = 64f;
        public const string SidecarFileName = "pollution.json";

        private const float SimulationStepSeconds = 1f;
        private const float SaveIntervalSeconds = 60f;
        private const float LocalAirScale = 80f;
        private const float LocalRunoffScale = 110f;
        private const float BodyAirScale = 12000f;
        private const float MinimumStoredLoad = 0.001f;
        private const float MaxCellLoad = 1000000f;

        public static PollutionService Instance { get; private set; }
        public long Revision { get; private set; }

        private readonly Dictionary<string, Dictionary<Vector3Int, PollutionLoad>> _cells
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PollutionBodyRecord> _bodyRecords
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _airTrendPerMinute
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _runoffTrendPerMinute
            = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CaptureCandidate> _captureScratch = new(128);
        private readonly List<string> _bodyNameScratch = new(16);
        private static readonly Vector3Int[] s_tangentAroundX =
        {
            Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back,
        };
        private static readonly Vector3Int[] s_tangentAroundY =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.forward, Vector3Int.back,
        };
        private static readonly Vector3Int[] s_tangentAroundZ =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
        };

        private float _simulationAccumulator;
        private float _saveTimer;
        private string _loadedPath = string.Empty;
        private bool _dirty;
        private bool _saveSuppressed;

        private struct CaptureCandidate
        {
            public Vector3Int key;
            public float distanceSq;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("PollutionService");
            go.AddComponent<PollutionService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            EnsureWorldLoaded();
            if (!CanSimulate) return;

            _simulationAccumulator += Time.deltaTime;
            while (_simulationAccumulator >= SimulationStepSeconds)
            {
                _simulationAccumulator -= SimulationStepSeconds;
                Simulate(SimulationStepSeconds);
            }

            if (!_dirty) return;
            _saveTimer += Time.deltaTime;
            if (_saveTimer >= SaveIntervalSeconds) SaveNow();
        }

        private void OnApplicationQuit()
        {
            if (CanSimulate) SaveNow();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private static bool CanSimulate
        {
            get
            {
                var session = WorldSession.Instance;
                return !NetworkSession.SimulationIsRemote && (session == null || !session.IsRemoteJoin);
            }
        }

        // ── Public authority entry points ────────────────────────────────────────

        /// <summary>Adds a direct source load at one body-local position.</summary>
        public static void Emit(Vector3 worldPosition, PollutionLoad amount)
        {
            if (!CanSimulate || amount.Total <= 0f) return;
            var service = Instance;
            if (service == null) return;
            if (!service.TryResolveBodyAt(worldPosition, out var body) || body.settings == null) return;

            // Airborne pollution cannot accumulate where there is no air. Surface runoff
            // remains valid on airless bodies; climate/debris retain their extension seams.
            if (!body.settings.HasAtmosphere) amount.airborneSmog = 0f;
            if (amount.Total <= 0f) return;

            string bodyName = body.settings.bodyName;
            Vector3Int key = CellAt(body.transform.InverseTransformPoint(worldPosition));
            var cells = service.CellsFor(bodyName);
            cells.TryGetValue(key, out var existing);
            cells[key] = Clamp(existing + amount);
            service.MarkChanged();
        }

        /// <summary>
        /// Removes up to <paramref name="maxUnits"/> airborne units, nearest cells first.
        /// Only the host/offline simulation may mutate this state.
        /// </summary>
        public static float CaptureAirborne(Vector3 worldPosition, float radiusMetres, float maxUnits)
        {
            var service = Instance;
            if (!CanSimulate || service == null || maxUnits <= 0f || radiusMetres <= 0f) return 0f;
            if (!service.TryResolveBodyAt(worldPosition, out var body) || body.settings == null) return 0f;
            if (!service._cells.TryGetValue(body.settings.bodyName, out var cells) || cells.Count == 0) return 0f;

            service._captureScratch.Clear();
            float radiusSq = radiusMetres * radiusMetres;
            foreach (var pair in cells)
            {
                if (pair.Value.airborneSmog <= 0f) continue;
                Vector3 centre = body.transform.TransformPoint(CellCentre(pair.Key));
                float distanceSq = (centre - worldPosition).sqrMagnitude;
                if (distanceSq > radiusSq) continue;
                service._captureScratch.Add(new CaptureCandidate { key = pair.Key, distanceSq = distanceSq });
            }

            service.SortCaptureScratch();

            float removed = 0f;
            for (int i = 0; i < service._captureScratch.Count && removed < maxUnits; i++)
            {
                Vector3Int key = service._captureScratch[i].key;
                if (!cells.TryGetValue(key, out var load)) continue;
                float take = Mathf.Min(load.airborneSmog, maxUnits - removed);
                load.airborneSmog -= take;
                removed += take;
                if (load.Total < MinimumStoredLoad) cells.Remove(key);
                else cells[key] = load;
            }

            if (removed > 0f)
            {
                if (cells.Count == 0) service._cells.Remove(body.settings.bodyName);
                service.MarkChanged();
                service.RecalculateBodyRecords(0f);
            }
            return removed;
        }

        /// <summary>
        /// Removes persistent soil/water contamination around a powered remediation machine.
        /// Runoff is captured nearest-cell-first and remains host/offline authoritative.
        /// </summary>
        public static float CaptureRunoff(Vector3 worldPosition, float radiusMetres, float maxUnits)
        {
            var service = Instance;
            if (!CanSimulate || service == null || maxUnits <= 0f || radiusMetres <= 0f) return 0f;
            if (!service.TryResolveBodyAt(worldPosition, out var body) || body.settings == null) return 0f;
            if (!service._cells.TryGetValue(body.settings.bodyName, out var cells) || cells.Count == 0) return 0f;

            service._captureScratch.Clear();
            float radiusSq = radiusMetres * radiusMetres;
            foreach (var pair in cells)
            {
                if (pair.Value.runoff <= 0f) continue;
                Vector3 centre = body.transform.TransformPoint(CellCentre(pair.Key));
                float distanceSq = (centre - worldPosition).sqrMagnitude;
                if (distanceSq > radiusSq) continue;
                service._captureScratch.Add(new CaptureCandidate { key = pair.Key, distanceSq = distanceSq });
            }

            service.SortCaptureScratch();
            float removed = 0f;
            for (int i = 0; i < service._captureScratch.Count && removed < maxUnits; i++)
            {
                Vector3Int key = service._captureScratch[i].key;
                if (!cells.TryGetValue(key, out var load)) continue;
                float take = Mathf.Min(load.runoff, maxUnits - removed);
                load.runoff -= take;
                removed += take;
                if (load.Total < MinimumStoredLoad) cells.Remove(key);
                else cells[key] = load;
            }

            if (removed > 0f)
            {
                if (cells.Count == 0) service._cells.Remove(body.settings.bodyName);
                service.MarkChanged();
                service.RecalculateBodyRecords(0f);
            }
            return removed;
        }

        private void SortCaptureScratch()
        {
            _captureScratch.Sort((a, b) =>
            {
                int distance = a.distanceSq.CompareTo(b.distanceSq);
                if (distance != 0) return distance;
                int x = a.key.x.CompareTo(b.key.x);
                if (x != 0) return x;
                int y = a.key.y.CompareTo(b.key.y);
                return y != 0 ? y : a.key.z.CompareTo(b.key.z);
            });
        }

        // ── Queries (safe on host, offline and snapshot-driven clients) ──────────

        public static float SampleAirborne01(Vector3 worldPosition)
            => SampleLocal01(worldPosition, runoff: false);

        public static float SampleRunoff01(Vector3 worldPosition)
            => SampleLocal01(worldPosition, runoff: true);

        private static float SampleLocal01(Vector3 worldPosition, bool runoff)
        {
            var service = Instance;
            if (service == null || !service.TryResolveBodyAt(worldPosition, out var body)
                || body.settings == null) return 0f;
            if (!service._cells.TryGetValue(body.settings.bodyName, out var cells) || cells.Count == 0)
                return 0f;

            Vector3 grid = body.transform.InverseTransformPoint(worldPosition) / CellSizeMetres
                - Vector3.one * 0.5f;
            int x0 = Mathf.FloorToInt(grid.x);
            int y0 = Mathf.FloorToInt(grid.y);
            int z0 = Mathf.FloorToInt(grid.z);
            float tx = grid.x - x0;
            float ty = grid.y - y0;
            float tz = grid.z - z0;
            float units = 0f;

            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                float weight = (x == 0 ? 1f - tx : tx)
                    * (y == 0 ? 1f - ty : ty)
                    * (z == 0 ? 1f - tz : tz);
                if (!cells.TryGetValue(new Vector3Int(x0 + x, y0 + y, z0 + z), out var load)) continue;
                units += Mathf.Max(0f, runoff ? load.runoff : load.airborneSmog) * weight;
            }

            float scale = runoff ? LocalRunoffScale : LocalAirScale;
            return 1f - Mathf.Exp(-Mathf.Max(0f, units) / scale);
        }

        public static float AirborneBurdenFor(string bodyName)
        {
            if (Instance == null || string.IsNullOrEmpty(bodyName)) return 0f;
            return Instance._bodyRecords.TryGetValue(bodyName, out var record)
                ? Mathf.Clamp01(record.airborneBurden01) : 0f;
        }

        public static float RunoffBurdenFor(string bodyName)
        {
            if (Instance == null || string.IsNullOrEmpty(bodyName)) return 0f;
            return Instance._bodyRecords.TryGetValue(bodyName, out var record)
                ? Mathf.Clamp01(record.runoffBurden01) : 0f;
        }

        public static PollutionTelemetry TelemetryAt(Vector3 worldPosition)
        {
            var service = Instance;
            if (service == null || !service.TryResolveBodyAt(worldPosition, out var body)
                || body.settings == null)
                return new PollutionTelemetry(0f, 0f, 0f, 0f, 0f, 0f, 0);

            string name = body.settings.bodyName;
            float burden = AirborneBurdenFor(name);
            float runoffBurden = RunoffBurdenFor(name);
            float trend = service._airTrendPerMinute.TryGetValue(name, out float value) ? value : 0f;
            float runoffTrend = service._runoffTrendPerMinute.TryGetValue(name, out float runoffValue)
                ? runoffValue : 0f;
            int count = service._cells.TryGetValue(name, out var cells) ? cells.Count : 0;
            return new PollutionTelemetry(SampleAirborne01(worldPosition), burden,
                SampleRunoff01(worldPosition), runoffBurden, trend, runoffTrend, count);
        }

        public static void GetActiveBodyMapCells(List<PollutionMapCell> output)
        {
            if (output == null) return;
            output.Clear();
            var service = Instance;
            var body = GravityProvider.ActiveBody;
            if (service == null || body == null || body.settings == null) return;
            if (!service._cells.TryGetValue(body.settings.bodyName, out var cells)) return;

            foreach (var pair in cells)
            {
                float intensity = Concentration01(pair.Value.airborneSmog);
                float runoff = 1f - Mathf.Exp(-Mathf.Max(0f, pair.Value.runoff) / LocalRunoffScale);
                if (intensity < 0.01f && runoff < 0.01f) continue;
                Vector3 world = body.transform.TransformPoint(CellCentre(pair.Key));
                output.Add(new PollutionMapCell(world, intensity, runoff, CellSizeMetres,
                    pair.Value.airborneSmog, pair.Value.runoff));
            }
        }

        // ── Save / network snapshot ──────────────────────────────────────────────

        public void SaveNow()
        {
            if (!CanSimulate || !_dirty || _saveSuppressed || string.IsNullOrEmpty(_loadedPath)
                || _loadedPath == "<remote>") return;
            try
            {
                string folder = Path.GetDirectoryName(_loadedPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string json = ExportSnapshotJson(prettyPrint: true);
                string temporary = _loadedPath + ".tmp";
                string previous = _loadedPath + ".previous";
                File.WriteAllText(temporary, json);
                if (File.Exists(_loadedPath))
                    File.Replace(temporary, _loadedPath, previous, ignoreMetadataErrors: true);
                else
                    File.Move(temporary, _loadedPath);
                _dirty = false;
                _saveTimer = 0f;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Pollution] Save failed; existing sidecar preserved. " + ex.Message);
            }
        }

        public string ExportSnapshotJson(bool prettyPrint = false)
        {
            RecalculateBodyRecords(0f);
            var records = new List<PollutionCellRecord>(CountCells());
            foreach (var bodyPair in _cells)
            foreach (var cellPair in bodyPair.Value)
            {
                if (cellPair.Value.Total < MinimumStoredLoad) continue;
                records.Add(new PollutionCellRecord
                {
                    bodyName = bodyPair.Key,
                    x = cellPair.Key.x,
                    y = cellPair.Key.y,
                    z = cellPair.Key.z,
                    load = cellPair.Value,
                });
            }

            var bodies = new PollutionBodyRecord[_bodyRecords.Count];
            int index = 0;
            foreach (var pair in _bodyRecords)
            {
                var source = pair.Value;
                bodies[index++] = new PollutionBodyRecord
                {
                    bodyName = source.bodyName,
                    airborneBurden01 = source.airborneBurden01,
                    runoffBurden01 = source.runoffBurden01,
                    climateBurden01 = source.climateBurden01,
                    orbitalDebrisBurden01 = source.orbitalDebrisBurden01,
                };
            }

            return JsonUtility.ToJson(new PollutionStateData
            {
                formatVersion = 1,
                cellSizeMetres = CellSizeMetres,
                revision = Revision,
                cells = records.ToArray(),
                bodies = bodies,
            }, prettyPrint);
        }

        public static void ApplyRemoteSnapshotJson(string json)
        {
            if (!NetworkSession.SimulationIsRemote || string.IsNullOrEmpty(json)) return;
            if (Instance == null) EnsureRuntimeInstance();
            var service = Instance;
            if (service == null) return;
            try
            {
                var data = JsonUtility.FromJson<PollutionStateData>(json);
                if (data == null || data.formatVersion != 1) return;
                service.ReplaceFrom(data, remote: true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Pollution] Ignored malformed host snapshot: " + ex.Message);
            }
        }

        public static void ClearRemoteSnapshot()
        {
            var service = Instance;
            if (service == null || service._loadedPath != "<remote>") return;
            service.ClearState();
            service._loadedPath = string.Empty;
        }

        private void EnsureWorldLoaded()
        {
            var session = WorldSession.Instance;
            if (session == null) return;

            if (NetworkSession.SimulationIsRemote || session.IsRemoteJoin)
            {
                if (_loadedPath != "<remote>")
                {
                    ClearState();
                    _loadedPath = "<remote>";
                }
                return;
            }

            string desired = Path.Combine(session.WorldFolderPath(session.worldName), SidecarFileName);
            if (string.Equals(desired, _loadedPath, StringComparison.Ordinal)) return;
            if (_dirty) SaveNow();
            ClearState();
            _saveSuppressed = false;
            _loadedPath = desired;
            LoadFromDisk(desired);
        }

        private void LoadFromDisk(string path)
        {
            if (!File.Exists(path))
            {
                // Additive compatibility contract: old worlds have no sidecar and therefore
                // start with clean air, without changing world_state.json or its schema.
                Debug.Log("[Pollution] No sidecar found; this world starts clean.");
                return;
            }

            try
            {
                var data = JsonUtility.FromJson<PollutionStateData>(File.ReadAllText(path));
                if (data == null || data.formatVersion != 1)
                    throw new InvalidDataException("unsupported pollution sidecar format");
                ReplaceFrom(data, remote: false);
                _dirty = false;
                Debug.Log($"[Pollution] Restored {CountCells()} sparse cell(s).");
            }
            catch (Exception ex)
            {
                // The pollution sidecar is additive. A bad one must never prevent the base
                // world from loading and must never be overwritten automatically this session.
                Debug.LogError("[Pollution] Sidecar load failed; continuing with clean air and preserving the file. " + ex);
                ClearState();
                _loadedPath = path;
                _saveSuppressed = true;
            }
        }

        private void ReplaceFrom(PollutionStateData data, bool remote)
        {
            var previous = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _bodyRecords)
                previous[pair.Key] = new Vector2(pair.Value.airborneBurden01, pair.Value.runoffBurden01);

            ClearState();
            _loadedPath = remote ? "<remote>" : _loadedPath;
            Revision = Math.Max(0L, data.revision);

            if (data.cells != null)
            {
                for (int i = 0; i < data.cells.Length; i++)
                {
                    var record = data.cells[i];
                    if (record == null || string.IsNullOrWhiteSpace(record.bodyName)) continue;
                    PollutionLoad load = Clamp(record.load);
                    if (load.Total < MinimumStoredLoad) continue;
                    CellsFor(record.bodyName)[new Vector3Int(record.x, record.y, record.z)] = load;
                }
            }

            if (data.bodies != null)
            {
                for (int i = 0; i < data.bodies.Length; i++)
                {
                    var record = data.bodies[i];
                    if (record == null || string.IsNullOrWhiteSpace(record.bodyName)) continue;
                    _bodyRecords[record.bodyName] = record;
                }
            }
            if (_bodyRecords.Count == 0) RecalculateBodyRecords(0f);

            foreach (var pair in _bodyRecords)
            {
                Vector2 old = previous.TryGetValue(pair.Key, out Vector2 value)
                    ? value
                    : new Vector2(pair.Value.airborneBurden01, pair.Value.runoffBurden01);
                _airTrendPerMinute[pair.Key] = (pair.Value.airborneBurden01 - old.x) * 30f;
                _runoffTrendPerMinute[pair.Key] = (pair.Value.runoffBurden01 - old.y) * 30f;
            }
            _dirty = !remote;
        }

        // ── Simulation ───────────────────────────────────────────────────────────

        private void Simulate(float dt)
        {
            if (_cells.Count == 0) return;

            var active = GravityProvider.ActiveBody;
            string activeName = active != null && active.settings != null ? active.settings.bodyName : string.Empty;
            var wind = WindSystem.Instance;
            Vector3 windWorld = wind != null ? wind.GetWindDirection() * wind.GetWindSpeed() : Vector3.zero;
            float rain = WeatherManager.Instance != null && WeatherManager.Instance.IsPlanetPrecipitating
                ? WeatherManager.Instance.Intensity : 0f;

            _bodyNameScratch.Clear();
            foreach (string bodyName in _cells.Keys) _bodyNameScratch.Add(bodyName);
            for (int b = 0; b < _bodyNameScratch.Count; b++)
            {
                string bodyName = _bodyNameScratch[b];
                var source = _cells[bodyName];
                if (source.Count == 0)
                {
                    _cells.Remove(bodyName);
                    continue;
                }
                var next = new Dictionary<Vector3Int, PollutionLoad>(source.Count + 8);

                bool isActive = !string.IsNullOrEmpty(activeName)
                    && string.Equals(bodyName, activeName, StringComparison.OrdinalIgnoreCase);
                Vector3 localWind = isActive && active != null
                    ? active.transform.InverseTransformVector(windWorld) : Vector3.zero;
                Vector3Int windStep = DominantStep(localWind);
                float moveShare = windStep == Vector3Int.zero ? 0f
                    : Mathf.Clamp(localWind.magnitude * dt / CellSizeMetres, 0f, 0.22f);

                float airRetention = Mathf.Exp(-0.00096f * dt);
                float washShare = isActive && rain > 0f
                    ? 1f - Mathf.Exp(-0.004f * rain * dt)
                    : 0f;
                float runoffRetention = Mathf.Exp(-0.00026f * dt);
                float runoffSpreadShare = Mathf.Clamp((0.0008f + (isActive ? rain * 0.008f : 0f)) * dt,
                    0f, 0.03f);
                float climateRetention = Mathf.Exp(-0.000048f * dt);
                float debrisRetention = Mathf.Exp(-0.000016f * dt);

                foreach (var pair in source)
                {
                    float naturallyRetainedAir = pair.Value.airborneSmog * airRetention;
                    float washedFromAir = naturallyRetainedAir * washShare;
                    PollutionLoad retained = new()
                    {
                        airborneSmog = naturallyRetainedAir - washedFromAir,
                        runoff = pair.Value.runoff * runoffRetention + washedFromAir * 0.85f,
                        climateLoad = pair.Value.climateLoad * climateRetention,
                        orbitalDebris = pair.Value.orbitalDebris * debrisRetention,
                    };

                    float movedAir = retained.airborneSmog * moveShare;
                    retained.airborneSmog -= movedAir;

                    float movedRunoff = retained.runoff * runoffSpreadShare;
                    Vector3Int[] seepSteps = TangentialSteps(pair.Key);
                    float runoffPart = movedRunoff * 0.25f;
                    if (runoffPart > MinimumStoredLoad)
                    {
                        retained.runoff -= movedRunoff;
                        for (int step = 0; step < seepSteps.Length; step++)
                            Add(next, pair.Key + seepSteps[step], new PollutionLoad { runoff = runoffPart });
                    }

                    Add(next, pair.Key, retained);
                    if (movedAir > MinimumStoredLoad)
                        Add(next, pair.Key + windStep, new PollutionLoad { airborneSmog = movedAir });
                }

                source.Clear();
                foreach (var pair in next)
                    if (pair.Value.Total >= MinimumStoredLoad) source[pair.Key] = Clamp(pair.Value);
                if (source.Count == 0) _cells.Remove(bodyName);
            }

            MarkChanged();
            RecalculateBodyRecords(dt);
        }

        private void RecalculateBodyRecords(float dt)
        {
            var previous = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _bodyRecords)
                previous[pair.Key] = new Vector2(pair.Value.airborneBurden01, pair.Value.runoffBurden01);
            _bodyRecords.Clear();

            foreach (var bodyPair in _cells)
            {
                PollutionLoad total = default;
                foreach (var cellPair in bodyPair.Value) total += cellPair.Value;
                var record = new PollutionBodyRecord
                {
                    bodyName = bodyPair.Key,
                    airborneBurden01 = Burden01(total.airborneSmog, BodyAirScale),
                    runoffBurden01 = Burden01(total.runoff, BodyAirScale * 1.5f),
                    climateBurden01 = Burden01(total.climateLoad, BodyAirScale * 4f),
                    orbitalDebrisBurden01 = Burden01(total.orbitalDebris, BodyAirScale * 2f),
                };
                _bodyRecords[bodyPair.Key] = record;

                if (dt > 0f)
                {
                    Vector2 old = previous.TryGetValue(bodyPair.Key, out Vector2 value)
                        ? value
                        : new Vector2(record.airborneBurden01, record.runoffBurden01);
                    _airTrendPerMinute[bodyPair.Key]
                        = (record.airborneBurden01 - old.x) * (60f / dt);
                    _runoffTrendPerMinute[bodyPair.Key]
                        = (record.runoffBurden01 - old.y) * (60f / dt);
                }
            }

            _bodyNameScratch.Clear();
            foreach (var pair in _airTrendPerMinute)
                if (!_bodyRecords.ContainsKey(pair.Key)) _bodyNameScratch.Add(pair.Key);
            for (int i = 0; i < _bodyNameScratch.Count; i++)
            {
                _airTrendPerMinute.Remove(_bodyNameScratch[i]);
                _runoffTrendPerMinute.Remove(_bodyNameScratch[i]);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private void MarkChanged()
        {
            Revision++;
            _dirty = true;
        }

        private Dictionary<Vector3Int, PollutionLoad> CellsFor(string bodyName)
        {
            if (!_cells.TryGetValue(bodyName, out var cells))
            {
                cells = new Dictionary<Vector3Int, PollutionLoad>();
                _cells[bodyName] = cells;
            }
            return cells;
        }

        private int CountCells()
        {
            int total = 0;
            foreach (var pair in _cells) total += pair.Value.Count;
            return total;
        }

        private void ClearState()
        {
            _cells.Clear();
            _bodyRecords.Clear();
            _airTrendPerMinute.Clear();
            _runoffTrendPerMinute.Clear();
            _simulationAccumulator = 0f;
            _saveTimer = 0f;
            _dirty = false;
            Revision = 0L;
        }

        private bool TryResolveBodyAt(Vector3 worldPosition, out CelestialBody body)
        {
            body = GravityProvider.ActiveBody;
            if (IsNearBody(worldPosition, body)) return true;

            var registry = CosmicRegistry.Instance;
            if (registry?.SceneBodies == null) { body = null; return false; }
            float best = float.MaxValue;
            CelestialBody closest = null;
            foreach (var pair in registry.SceneBodies)
            {
                var candidate = pair.Value;
                if (candidate == null || candidate.settings == null) continue;
                float altitude = Mathf.Abs(Vector3.Distance(worldPosition, candidate.transform.position)
                    - candidate.SurfaceRadius);
                if (altitude < best && altitude <= Mathf.Max(3000f, candidate.AtmosphereHeight + 1000f))
                {
                    best = altitude;
                    closest = candidate;
                }
            }
            body = closest;
            return body != null;
        }

        private static bool IsNearBody(Vector3 worldPosition, CelestialBody body)
        {
            if (body == null || body.settings == null) return false;
            float altitude = Mathf.Abs(Vector3.Distance(worldPosition, body.transform.position) - body.SurfaceRadius);
            return altitude <= Mathf.Max(3000f, body.AtmosphereHeight + 1000f);
        }

        private static Vector3Int CellAt(Vector3 localPoint) => new(
            Mathf.FloorToInt(localPoint.x / CellSizeMetres),
            Mathf.FloorToInt(localPoint.y / CellSizeMetres),
            Mathf.FloorToInt(localPoint.z / CellSizeMetres));

        private static Vector3 CellCentre(Vector3Int key) => new(
            (key.x + 0.5f) * CellSizeMetres,
            (key.y + 0.5f) * CellSizeMetres,
            (key.z + 0.5f) * CellSizeMetres);

        private static Vector3Int[] TangentialSteps(Vector3Int key)
        {
            int x = Mathf.Abs(key.x);
            int y = Mathf.Abs(key.y);
            int z = Mathf.Abs(key.z);
            if (x >= y && x >= z) return s_tangentAroundX;
            return y >= z ? s_tangentAroundY : s_tangentAroundZ;
        }

        private static Vector3Int DominantStep(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return Vector3Int.zero;
            float x = Mathf.Abs(direction.x);
            float y = Mathf.Abs(direction.y);
            float z = Mathf.Abs(direction.z);
            if (x >= y && x >= z) return new Vector3Int(direction.x >= 0f ? 1 : -1, 0, 0);
            if (y >= z) return new Vector3Int(0, direction.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, direction.z >= 0f ? 1 : -1);
        }

        private static void Add(Dictionary<Vector3Int, PollutionLoad> target,
            Vector3Int key, PollutionLoad amount)
        {
            if (amount.Total < MinimumStoredLoad) return;
            target.TryGetValue(key, out var existing);
            target[key] = Clamp(existing + amount);
        }

        private static PollutionLoad Clamp(PollutionLoad value) => new()
        {
            airborneSmog = Mathf.Clamp(value.airborneSmog, 0f, MaxCellLoad),
            runoff = Mathf.Clamp(value.runoff, 0f, MaxCellLoad),
            climateLoad = Mathf.Clamp(value.climateLoad, 0f, MaxCellLoad),
            orbitalDebris = Mathf.Clamp(value.orbitalDebris, 0f, MaxCellLoad),
        };

        private static float Concentration01(float units) =>
            1f - Mathf.Exp(-Mathf.Max(0f, units) / LocalAirScale);

        private static float Burden01(float units, float scale) =>
            1f - Mathf.Exp(-Mathf.Max(0f, units) / Mathf.Max(1f, scale));
    }
}
