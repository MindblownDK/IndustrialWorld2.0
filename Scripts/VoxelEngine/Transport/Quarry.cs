// Assets/Scripts/VoxelEngine/Transport/Quarry.cs
//
// BuildCraft-style quarry. Mines a rectangular area IN FRONT of the quarry block
// (facing the quarry's forward direction). Builds a frame first, then mines
// layer by layer down to bedrock.
//
// Without landmarks: 16×16 default area in front.
// With 2 landmarks: custom rectangular area.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Power;

namespace VoxelEngine.Transport
{
    public enum QuarryPhase { Idle, BuildingFrame, Mining, Complete }

    [RequireComponent(typeof(PlacedBlock))]
    public class Quarry : MonoBehaviour
    {
        [Header("Mining")]
        public int defaultSize = 16;
        [Tooltip("Seconds between each voxel mined.")]
        public float mineInterval = 0.5f;
        [Range(0, 4)] public int quarryTier = 3;
        [Tooltip("Distance in front of the quarry where the mining area starts.")]
        public float forwardOffset = 2f;

        [Header("Frame Building")]
        public float frameBuildInterval = 0.08f;
        public Color frameColor = new Color(0.3f, 0.3f, 0.35f);

        [Header("Output")]
        public int outputSlots = 6;

        [Header("Landmark Detection")]
        public float landmarkSearchRadius = 64f;

        // ── Runtime ────────────────────────────────────────────────
        public QuarryPhase Phase { get; private set; } = QuarryPhase.Idle;
        public int CurrentDepth { get; private set; }
        public int MaxDepth { get; private set; }
        public bool IsMining => Phase == QuarryPhase.Mining;
        public float MineProgress01 => _mineTimer / Mathf.Max(0.01f, mineInterval);
        public ItemContainer Output { get { EnsureOutput(); return _output; } }
        public int AreaX { get; private set; }
        public int AreaZ { get; private set; }
        public Vector3Int AreaMin { get; private set; }
        public Vector3Int AreaMax { get; private set; }

        private ItemContainer _output;
        private float _mineTimer;
        private float _frameBuildTimer;
        private int _cursorX, _cursorZ;
        public int CursorX => _cursorX;
        public int CursorZ => _cursorZ;
        private VoxelWorld _world;
        private MaterialRegistry _matReg;
        private PowerConsumer _power;
        private bool _initialized;
        private Vector3Int _originVoxel;
        private int _bedrockY = 2; // bedrock is at y <= 2

        // Visuals
        private GameObject _ghostObj;
        private List<GameObject> _frameBlocks = new();
        private int _frameBuildIndex;
        private List<Vector3> _framePositions = new();
        private GameObject _drillHead;

        private void Awake() => EnsureOutput();

        private void Start()
        {
            _world = VoxelWorld.Instance;
            _matReg = _world != null ? _world.materialRegistry : null;
            _power = GetComponent<PowerConsumer>();

            if (_world == null) { Debug.LogWarning("[Quarry] No VoxelWorld.Instance"); return; }

            _originVoxel = _world.WorldToVoxel(transform.position);
            DetectLandmarksOrDefault();
            CalculateMaxDepth();
            CreateGhostPreview();
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;
            bool hasPower = _power == null || _power.IsPowered;

            switch (Phase)
            {
                case QuarryPhase.Idle:
                    if (hasPower)
                    {
                        Phase = QuarryPhase.BuildingFrame;
                        DestroyGhost();
                        PrepareFramePositions();
                        _frameBuildIndex = 0;
                    }
                    break;

                case QuarryPhase.BuildingFrame:
                    if (!hasPower) return;
                    _frameBuildTimer += Time.deltaTime;
                    if (_frameBuildTimer >= frameBuildInterval)
                    {
                        _frameBuildTimer = 0f;
                        if (_frameBuildIndex < _framePositions.Count)
                            PlaceFrameBlock(_framePositions[_frameBuildIndex++]);
                        else
                        {
                            Phase = QuarryPhase.Mining;
                            CreateDrillHead();
                        }
                    }
                    break;

                case QuarryPhase.Mining:
                    if (!hasPower) return;
                    _mineTimer += Time.deltaTime;
                    if (_mineTimer >= mineInterval) { _mineTimer -= mineInterval; MineNextVoxel(); }
                    UpdateDrillHead();
                    break;
            }
        }

        // ── Area Setup ─────────────────────────────────────────────
        private void DetectLandmarksOrDefault()
        {
            var landmarks = FindObjectsByType<QuarryLandmark>(FindObjectsInactive.Exclude);
            QuarryLandmark lm1 = null, lm2 = null;
            float best1 = landmarkSearchRadius * landmarkSearchRadius, best2 = best1;

            foreach (var lm in landmarks)
            {
                float d = (lm.transform.position - transform.position).sqrMagnitude;
                if (d > best1 && d > best2) continue;
                if (d < best1) { lm2 = lm1; best2 = best1; lm1 = lm; best1 = d; }
                else if (d < best2) { lm2 = lm; best2 = d; }
            }

            if (lm1 != null && lm2 != null)
            {
                var p1 = _world.WorldToVoxel(lm1.transform.position);
                var p2 = _world.WorldToVoxel(lm2.transform.position);
                int surfaceY = Mathf.Max(p1.y, p2.y);
                AreaMin = new Vector3Int(Mathf.Min(p1.x, p2.x), surfaceY, Mathf.Min(p1.z, p2.z));
                AreaMax = new Vector3Int(Mathf.Max(p1.x, p2.x), surfaceY, Mathf.Max(p1.z, p2.z));
                lm1.SetLinked(true); lm2.SetLinked(true);
            }
            else
            {
                // Default: 16×16 area IN FRONT of the quarry (forward direction).
                Vector3 forward = transform.forward;
                Vector3Int fwd = new Vector3Int(
                    Mathf.RoundToInt(forward.x),
                    0,
                    Mathf.RoundToInt(forward.z));
                // Clamp to cardinal direction.
                if (Mathf.Abs(fwd.x) >= Mathf.Abs(fwd.z)) fwd.z = 0;
                else fwd.x = 0;
                if (fwd.x == 0 && fwd.z == 0) fwd.z = 1;

                int half = defaultSize / 2;
                Vector3Int center = _originVoxel + fwd * (int)(forwardOffset + half);
                int surfaceY = _originVoxel.y;

                // Build the perpendicular axis
                Vector3Int perp = new Vector3Int(fwd.z, 0, -fwd.x); // 90° rotation

                AreaMin = new Vector3Int(
                    Mathf.Min(center.x - Mathf.Abs(perp.x) * half + fwd.x * (-half),
                              center.x + Mathf.Abs(perp.x) * half + fwd.x * (-half)),
                    surfaceY,
                    Mathf.Min(center.z - Mathf.Abs(perp.z) * half + fwd.z * (-half),
                              center.z + Mathf.Abs(perp.z) * half + fwd.z * (-half)));

                // Simpler approach: just use forward + perpendicular
                Vector3Int startCorner = _originVoxel + fwd * (int)forwardOffset;
                Vector3Int endCorner = startCorner + fwd * defaultSize + perp * defaultSize;

                AreaMin = new Vector3Int(
                    Mathf.Min(startCorner.x, endCorner.x), surfaceY,
                    Mathf.Min(startCorner.z, endCorner.z));
                AreaMax = new Vector3Int(
                    Mathf.Max(startCorner.x, endCorner.x) - 1, surfaceY,
                    Mathf.Max(startCorner.z, endCorner.z) - 1);
            }

            AreaX = Mathf.Abs(AreaMax.x - AreaMin.x) + 1;
            AreaZ = Mathf.Abs(AreaMax.z - AreaMin.z) + 1;
        }

        private void CalculateMaxDepth()
        {
            // Mine from surface down to bedrock layer (y=2).
            MaxDepth = Mathf.Max(1, AreaMin.y - _bedrockY);
        }

        // ── Ghost Preview ──────────────────────────────────────────
        private void CreateGhostPreview()
        {
            _ghostObj = new GameObject("QuarryGhost");
            float y = AreaMin.y;
            Vector3 min = new Vector3(AreaMin.x, y, AreaMin.z);
            Vector3 max = new Vector3(AreaMax.x + 1, y, AreaMax.z + 1);
            float bottomY = _bedrockY;
            Color gc = new Color(1f, 0.8f, 0.2f, 0.6f);

            // Top rectangle
            AddLine(min, new Vector3(max.x, y, min.z), gc);
            AddLine(new Vector3(max.x, y, min.z), max, gc);
            AddLine(max, new Vector3(min.x, y, max.z), gc);
            AddLine(new Vector3(min.x, y, max.z), min, gc);
            // Vertical corners
            AddLine(min, new Vector3(min.x, bottomY, min.z), gc);
            AddLine(new Vector3(max.x, y, min.z), new Vector3(max.x, bottomY, min.z), gc);
            AddLine(max, new Vector3(max.x, bottomY, max.z), gc);
            AddLine(new Vector3(min.x, y, max.z), new Vector3(min.x, bottomY, max.z), gc);
            // Bottom rectangle
            AddLine(new Vector3(min.x, bottomY, min.z), new Vector3(max.x, bottomY, min.z), gc);
            AddLine(new Vector3(max.x, bottomY, min.z), new Vector3(max.x, bottomY, max.z), gc);
            AddLine(new Vector3(max.x, bottomY, max.z), new Vector3(min.x, bottomY, max.z), gc);
            AddLine(new Vector3(min.x, bottomY, max.z), new Vector3(min.x, bottomY, min.z), gc);
        }

        private void AddLine(Vector3 from, Vector3 to, Color color)
        {
            var go = new GameObject("GL");
            go.transform.SetParent(_ghostObj.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2; lr.SetPositions(new[] { from, to });
            lr.startWidth = 0.08f; lr.endWidth = 0.08f;
            lr.startColor = color; lr.endColor = color; lr.useWorldSpace = true;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            lr.material = new Material(sh) { color = color };
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void DestroyGhost() { if (_ghostObj != null) Destroy(_ghostObj); }

        // ── Frame Building ─────────────────────────────────────────
        private void PrepareFramePositions()
        {
            _framePositions.Clear();
            float y = AreaMin.y + 0.5f;
            for (int x = AreaMin.x; x <= AreaMax.x; x++)
            {
                _framePositions.Add(new Vector3(x + 0.5f, y, AreaMin.z + 0.5f));
                _framePositions.Add(new Vector3(x + 0.5f, y, AreaMax.z + 0.5f));
            }
            for (int z = AreaMin.z + 1; z < AreaMax.z; z++)
            {
                _framePositions.Add(new Vector3(AreaMin.x + 0.5f, y, z + 0.5f));
                _framePositions.Add(new Vector3(AreaMax.x + 0.5f, y, z + 0.5f));
            }
        }

        private void PlaceFrameBlock(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "QFrame"; go.transform.position = pos;
            go.transform.localScale = new Vector3(1.02f, 0.15f, 1.02f);
            var r = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            r.material = new Material(sh) { color = frameColor };
            if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", frameColor);
            var col = go.GetComponent<BoxCollider>(); if (col) Destroy(col);
            _frameBlocks.Add(go);
        }

        // ── Drill Head ─────────────────────────────────────────────
        private void CreateDrillHead()
        {
            _drillHead = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _drillHead.name = "DrillHead";
            _drillHead.transform.localScale = new Vector3(0.6f, 0.3f, 0.6f);
            var c = _drillHead.GetComponent<CapsuleCollider>(); if (c) Destroy(c);
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = new Color(0.8f, 0.6f, 0.1f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
            _drillHead.GetComponent<MeshRenderer>().material = m;
        }

        private void UpdateDrillHead()
        {
            if (_drillHead == null) return;
            float dy = AreaMin.y - CurrentDepth - 0.5f;
            _drillHead.transform.position = new Vector3(
                AreaMin.x + _cursorX + 0.5f, dy, AreaMin.z + _cursorZ + 0.5f);
            _drillHead.transform.Rotate(Vector3.up, 180f * Time.deltaTime);
        }

        // ── Mining ─────────────────────────────────────────────────
        /// <summary>True when quarry is waiting for output space.</summary>
        public bool IsOutputFull { get; private set; }

        private void MineNextVoxel()
        {
            if (CurrentDepth >= MaxDepth) { Phase = QuarryPhase.Complete; if (_drillHead) Destroy(_drillHead); return; }

            // Check if output has space — pause mining if full.
            EnsureOutput();
            bool hasOutputSpace = false;
            for (int i = 0; i < _output.Size; i++)
            {
                if (_output.GetSlot(i).IsEmpty) { hasOutputSpace = true; break; }
            }
            // Also check if any connected pipe has capacity.
            if (!hasOutputSpace)
            {
                var pipeHits = Physics.OverlapSphere(transform.position, 1.6f);
                foreach (var col in pipeHits)
                {
                    if (col.gameObject == gameObject) continue;
                    var pipe = col.GetComponent<ItemPipe>();
                    if (pipe != null && pipe.GetInputCapacity(null) > 0) { hasOutputSpace = true; break; }
                }
            }
            if (!hasOutputSpace) { IsOutputFull = true; return; } // PAUSE — don't mine, don't void
            IsOutputFull = false;

            Vector3Int target = new Vector3Int(
                AreaMin.x + _cursorX, AreaMin.y - CurrentDepth, AreaMin.z + _cursorZ);

            Voxel v = _world.GetVoxelWorld(target);

            // Stop at bedrock.
            if (v.material == (byte)MaterialId.Bedrock)
            {
                // Skip this column — bedrock is unbreakable.
                AdvanceCursor();
                return;
            }

            if (v.density > VoxelConstants.ISO_LEVEL)
            {
                var def = _matReg != null ? _matReg.Get(v.material) : null;
                bool canMine = def == null || !def.isMineable || quarryTier >= def.miningTier;
                if (canMine)
                {
                    _world.SetVoxelWorld(target, Voxel.Empty);
                    if (def != null && def.dropItem != null && def.dropAmount > 0)
                        OutputItems(def.dropItem, def.dropAmount);
                }
            }
            AdvanceCursor();
        }

        private void AdvanceCursor()
        {
            _cursorX++;
            if (_cursorX >= AreaX)
            {
                _cursorX = 0; _cursorZ++;
                if (_cursorZ >= AreaZ) { _cursorZ = 0; CurrentDepth++; }
            }
        }

        // ── Output ─────────────────────────────────────────────────
        private void OutputItems(ItemDefinition item, int count)
        {
            int rem = count;
            var hits = Physics.OverlapSphere(transform.position, 1.6f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var pipe = col.GetComponent<ItemPipe>();
                if (pipe == null) continue;
                int acc = pipe.TryInsert(item, Mathf.Min(pipe.GetInputCapacity(item), rem));
                rem -= acc; if (rem <= 0) return;
            }
            if (rem > 0)
            {
                EnsureOutput();
                var leftover = _output.Insert(new ItemStack(item, rem));
                // If STILL overflow after pipes + buffer, items wait (mine paused next tick).
            }
        }

        private void EnsureOutput()
        {
            if (_output == null) _output = new ItemContainer("Quarry Output", outputSlots);
            else _output.Resize(outputSlots);
        }
        public void EnsureOutputPublic() => EnsureOutput();

        /// <summary>Restore quarry state from save data.</summary>
        public void RestoreState(int depth, int cx, int cz, int phase)
        {
            CurrentDepth = depth;
            _cursorX = cx;
            _cursorZ = cz;
            Phase = (QuarryPhase)phase;
            if (Phase == QuarryPhase.Complete && _drillHead != null) Destroy(_drillHead);
        }

        private void OnDestroy()
        {
            DestroyGhost();
            foreach (var fb in _frameBlocks) if (fb) Destroy(fb);
            if (_drillHead) Destroy(_drillHead);
        }
    }
}
