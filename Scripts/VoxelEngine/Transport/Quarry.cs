// Assets/Scripts/VoxelEngine/Transport/Quarry.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL QUARRY — automated strip-miner                     ║
// ║                                                                ║
// ║  16×16 default, mines to bedrock. 3 upgrade slots:             ║
// ║  • Range  (max 10) — +1 area per upgrade                       ║
// ║  • Speed  (max 10) — faster mining                              ║
// ║  • Efficiency (max 2) — mines more voxels per tick             ║
// ║                                                                ║
// ║  Port config for item/power I/O faces.                         ║
// ║  Dark steel 3D frame + tape preview + laser drill head.        ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Power;

namespace VoxelEngine.Transport
{
    public enum QuarryPhase { Idle, TapeFrame, BuildingFrame, Mining, Complete }

    [RequireComponent(typeof(PlacedBlock))]
    public class Quarry : MonoBehaviour
    {
        [Header("Area")]
        [Tooltip("Base square side length (before Range upgrades).")]
        public int defaultSize = 16;

        [Tooltip("Distance in front of the quarry block where mining starts.")]
        public float forwardOffset = 2f;

        [Header("Mining")]
        [Tooltip("Base seconds between each voxel mined (before Speed upgrades).")]
        public float baseMineInterval = 0.5f;

        [Tooltip("Base voxels mined per tick (before Efficiency upgrades).")]
        public int minePerTick = 1;

        [Range(0, 4)] public int quarryTier = 3;

        [Header("Power")]
        public float basePowerDraw = 500f;
        public float powerPerPerfUpgrade = 25f;
        public float powerSavePerEffUpgrade = 35f;

        [Header("Frame")]
        public float frameBuildInterval = 0.06f;
        public Color frameColor = new Color(0.18f, 0.19f, 0.22f);
        public Color frameAccentColor = new Color(0.92f, 0.52f, 0.08f, 0.85f);

        [Header("Construction Tape")]
        public float tapePreviewDuration = 2.5f;
        public Color tapeColor1 = new Color(0.95f, 0.55f, 0.05f, 0.75f);
        public Color tapeColor2 = new Color(0.92f, 0.82f, 0.08f, 0.75f);

        [Header("Output")]
        public int outputSlots = 6;

        // ── Runtime State ──────────────────────────────────────────
        public QuarryPhase Phase { get; private set; } = QuarryPhase.Idle;
        public int CurrentDepth { get; private set; }
        public int MaxDepth { get; private set; }
        public bool IsMining => Phase == QuarryPhase.Mining;
        public float MineProgress01 => _mineTimer / Mathf.Max(0.01f, EffectiveMineInterval);
        public ItemContainer Output { get { EnsureOutput(); return _output; } }
        public int AreaX { get; private set; }
        public int AreaZ { get; private set; }
        public Vector3Int AreaMin { get; private set; }
        public Vector3Int AreaMax { get; private set; }
        public int CursorX => _cursorX;
        public int CursorZ => _cursorZ;
        public bool IsOutputFull { get; private set; }

        // ── Upgrades (public for UI) ───────────────────────────────
        public int InstalledRangeLevel { get; private set; }
        public int InstalledSpeedLevel { get; private set; }
        public int InstalledEfficiencyLevel { get; private set; }
        public const int MaxRangeLevel = 10;
        public const int MaxSpeedLevel = 10;
        public const int MaxEfficiencyLevel = 5;

        /// <summary>Effective area side length after Range upgrades.</summary>
        public int EffectiveSize => defaultSize + InstalledRangeLevel;

        /// <summary>Effective mining interval after Speed upgrades.</summary>
        public float EffectiveMineInterval => Mathf.Max(0.05f, baseMineInterval - InstalledSpeedLevel * 0.04f);
        public float EffectivePowerDraw => Mathf.Max(10f,
            basePowerDraw + (InstalledRangeLevel + InstalledSpeedLevel) * powerPerPerfUpgrade
                          - InstalledEfficiencyLevel * powerSavePerEffUpgrade);

        private ItemContainer _output;
        private float _mineTimer;
        private float _frameBuildTimer;
        private float _tapeTimer;
        private int _cursorX, _cursorZ;
        private VoxelWorld _world;
        private MaterialRegistry _matReg;
        private PowerConsumer _power;
        private bool _initialized;
        private Vector3Int _originVoxel;

        // Visuals
        private GameObject _ghostObj;
        private GameObject _tapeObj;
        private List<GameObject> _frameSegments = new();
        private int _frameBuildIndex;
        private List<FrameSegment> _framePlan = new();
        private GameObject _drillHead;
        private GameObject _drillBeam;
        private float _tapeFlickerTimer;
        private bool _tapeFlickerState;

        private struct FrameSegment
        {
            public Vector3 position;
            public Vector3 scale;
            public bool isAccent;
        }

        // ── Lifecycle ──────────────────────────────────────────────
        private void Awake() => EnsureOutput();

        private void Start()
        {
            _world = VoxelWorld.Instance;
            _matReg = _world != null ? _world.materialRegistry : null;
            _power = GetComponent<PowerConsumer>();

            if (_world == null) { enabled = false; return; }

            _originVoxel = _world.WorldToVoxel(transform.position);
            ComputeDefaultArea();
            CalculateMaxDepth();
            CreateGhostPreview();
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;
            if (_power != null) _power.wattsPerSecond = EffectivePowerDraw;
            bool hasPower = _power == null || _power.IsPowered;

            switch (Phase)
            {
                case QuarryPhase.Idle:
                    if (hasPower) EnterTapePhase();
                    break;
                case QuarryPhase.TapeFrame:
                    if (!hasPower) return;
                    _tapeTimer += Time.deltaTime;
                    UpdateTapeFlicker();
                    if (_tapeTimer >= tapePreviewDuration) { DestroyTape(); EnterFramePhase(); }
                    break;
                case QuarryPhase.BuildingFrame:
                    if (!hasPower) return;
                    _frameBuildTimer += Time.deltaTime;
                    if (_frameBuildTimer >= frameBuildInterval)
                    {
                        _frameBuildTimer = 0f;
                        if (_frameBuildIndex < _framePlan.Count)
                            PlaceFrameSegment(_framePlan[_frameBuildIndex++]);
                        else
                        { Phase = QuarryPhase.Mining; CreateDrillHead(); }
                    }
                    break;
                case QuarryPhase.Mining:
                    if (!hasPower) return;
                    _mineTimer += Time.deltaTime;
                    if (_mineTimer >= EffectiveMineInterval)
                    {
                        _mineTimer -= EffectiveMineInterval;
                        for (int i = 0; i < minePerTick; i++)
                            MineNextVoxel();
                    }
                    UpdateDrillHead();
                    break;
                case QuarryPhase.Complete:
                    if (_drillHead) { Destroy(_drillHead); _drillHead = null; }
                    if (_drillBeam) { Destroy(_drillBeam); _drillBeam = null; }
                    break;
            }
        }

        // ── Phase Transitions ──────────────────────────────────────
        private void EnterTapePhase()
        {
            Phase = QuarryPhase.TapeFrame; _tapeTimer = 0f;
            DestroyGhost(); CreateTapePreview();
        }

        private void EnterFramePhase()
        {
            Phase = QuarryPhase.BuildingFrame;
            PrepareFramePlan(); _frameBuildIndex = 0; _frameBuildTimer = 0f;
        }

        // ── Area Setup ─────────────────────────────────────────────
        private void ComputeDefaultArea()
        {
            Vector3 forward = transform.forward;
            Vector3Int fwd = SnapToCardinal(forward);
            if (fwd.x == 0 && fwd.z == 0) fwd = new Vector3Int(1, 0, 0);

            int surfaceY = _originVoxel.y;
            int size = EffectiveSize;
            Vector3Int perp = new Vector3Int(fwd.z, 0, -fwd.x);
            int half = size / 2;

            Vector3Int start = _originVoxel + fwd * (int)forwardOffset - perp * half;
            Vector3Int end = start + fwd * size + perp * size;

            AreaMin = new Vector3Int(Mathf.Min(start.x, end.x), surfaceY, Mathf.Min(start.z, end.z));
            AreaMax = new Vector3Int(Mathf.Max(start.x, end.x) - 1, surfaceY, Mathf.Max(start.z, end.z) - 1);
            AreaX = Mathf.Abs(AreaMax.x - AreaMin.x) + 1;
            AreaZ = Mathf.Abs(AreaMax.z - AreaMin.z) + 1;
        }

        private Vector3Int SnapToCardinal(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), az = Mathf.Abs(v.z);
            if (ax >= az) return new Vector3Int(Mathf.RoundToInt(Mathf.Sign(v.x)), 0, 0);
            else return new Vector3Int(0, 0, Mathf.RoundToInt(Mathf.Sign(v.z)));
        }

        private void CalculateMaxDepth()
        {
            int bedrockTop = 3;
            MaxDepth = Mathf.Max(1, AreaMin.y - bedrockTop);
        }

        // ── Upgrades ───────────────────────────────────────────────
        public bool TryInstallUpgrade(QuarryUpgradeItem item)
        {
            if (item == null) return false;
            switch (item.upgradeKind)
            {
                case QuarryUpgradeKind.Range:
                    if (InstalledRangeLevel >= MaxRangeLevel) return false;
                    InstalledRangeLevel = Mathf.Min(MaxRangeLevel, InstalledRangeLevel + item.level);
                    break;
                case QuarryUpgradeKind.Speed:
                    if (InstalledSpeedLevel >= MaxSpeedLevel) return false;
                    InstalledSpeedLevel = Mathf.Min(MaxSpeedLevel, InstalledSpeedLevel + item.level);
                    break;
                case QuarryUpgradeKind.Efficiency:
                    if (InstalledEfficiencyLevel >= MaxEfficiencyLevel) return false;
                    InstalledEfficiencyLevel = Mathf.Min(MaxEfficiencyLevel, InstalledEfficiencyLevel + item.level);
                    break;
            }
            return true;
        }

        // ── Ghost Preview ──────────────────────────────────────────
        private void CreateGhostPreview()
        {
            _ghostObj = new GameObject("QuarryGhost");
            float y = AreaMin.y;
            float bottomY = AreaMin.y - MaxDepth;

            Color gc = new Color(1f, 0.8f, 0.2f, 0.5f);
            Vector3 v0 = new(AreaMin.x, y, AreaMin.z);
            Vector3 v1 = new(AreaMax.x + 1, y, AreaMin.z);
            Vector3 v2 = new(AreaMax.x + 1, y, AreaMax.z + 1);
            Vector3 v3 = new(AreaMin.x, y, AreaMax.z + 1);

            AddGhostLine(v0, v1, gc); AddGhostLine(v1, v2, gc);
            AddGhostLine(v2, v3, gc); AddGhostLine(v3, v0, gc);
            AddGhostLine(v0, new(v0.x, bottomY, v0.z), gc);
            AddGhostLine(v1, new(v1.x, bottomY, v1.z), gc);
            AddGhostLine(v2, new(v2.x, bottomY, v2.z), gc);
            AddGhostLine(v3, new(v3.x, bottomY, v3.z), gc);

            Vector3 b0 = new(AreaMin.x, bottomY, AreaMin.z);
            Vector3 b1 = new(AreaMax.x + 1, bottomY, AreaMin.z);
            Vector3 b2 = new(AreaMax.x + 1, bottomY, AreaMax.z + 1);
            Vector3 b3 = new(AreaMin.x, bottomY, AreaMax.z + 1);
            AddGhostLine(b0, b1, gc); AddGhostLine(b1, b2, gc);
            AddGhostLine(b2, b3, gc); AddGhostLine(b3, b0, gc);
        }

        private void AddGhostLine(Vector3 from, Vector3 to, Color color)
        {
            var go = new GameObject("GL"); go.transform.SetParent(_ghostObj.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2; lr.SetPositions(new[] { from, to });
            lr.startWidth = 0.06f; lr.endWidth = 0.06f;
            lr.startColor = color; lr.endColor = color; lr.useWorldSpace = true;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            lr.material = new Material(sh) { color = color };
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void DestroyGhost() { if (_ghostObj) { Destroy(_ghostObj); _ghostObj = null; } }

        // ── Construction Tape ──────────────────────────────────────
        private void CreateTapePreview()
        {
            _tapeObj = new GameObject("QuarryTape");
            float y = AreaMin.y + 0.1f;
            Vector3 v0 = new(AreaMin.x, y, AreaMin.z);
            Vector3 v1 = new(AreaMax.x + 1, y, AreaMin.z);
            Vector3 v2 = new(AreaMax.x + 1, y, AreaMax.z + 1);
            Vector3 v3 = new(AreaMin.x, y, AreaMax.z + 1);
            BuildTapeEdge(v0, v1, _tapeObj.transform);
            BuildTapeEdge(v1, v2, _tapeObj.transform);
            BuildTapeEdge(v2, v3, _tapeObj.transform);
            BuildTapeEdge(v3, v0, _tapeObj.transform);
            float postH = 2.5f;
            BuildTapePost(v0, postH); BuildTapePost(v1, postH);
            BuildTapePost(v2, postH); BuildTapePost(v3, postH);
        }

        private void BuildTapeEdge(Vector3 from, Vector3 to, Transform parent)
        {
            Vector3 dir = (to - from).normalized;
            float length = Vector3.Distance(from, to);
            int segs = Mathf.CeilToInt(length / 0.5f);
            float segLen = length / segs;
            for (int i = 0; i < segs; i++)
            {
                Vector3 pos = from + dir * (i * segLen + segLen * 0.5f);
                var go = MakePrim("Tape", pos, parent);
                if (Mathf.Abs(dir.x) > 0.5f) go.transform.localScale = new(segLen * 0.95f, 0.04f, 0.08f);
                else go.transform.localScale = new(0.08f, 0.04f, segLen * 0.95f);
                go.GetComponent<MeshRenderer>().material = CreateEmissiveMat((i % 2) == 0 ? tapeColor1 : tapeColor2, 0.6f);
            }
        }

        private void BuildTapePost(Vector3 basePos, float height)
        {
            var go = MakePrim("TapePost", basePos + Vector3.up * height * 0.5f, _tapeObj.transform);
            go.transform.localScale = new(0.1f, height, 0.1f);
            go.GetComponent<MeshRenderer>().material = CreateEmissiveMat(tapeColor1, 0.8f);
            var orb = MakePrim("TapeOrb", basePos + Vector3.up * height, _tapeObj.transform);
            orb.transform.localScale = Vector3.one * 0.2f;
            orb.GetComponent<MeshRenderer>().material = CreateEmissiveMat(new Color(0.95f, 0.55f, 0.05f, 0.9f), 0.9f);
        }

        private GameObject MakePrim(string name, Vector3 pos, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.position = pos;
            Destroy(go.GetComponent<Collider>()); return go;
        }

        private void UpdateTapeFlicker()
        {
            if (_tapeObj == null) return;
            _tapeFlickerTimer += Time.deltaTime;
            if (_tapeFlickerTimer > 0.15f)
            {
                _tapeFlickerTimer = 0f; _tapeFlickerState = !_tapeFlickerState;
                float a = _tapeFlickerState ? 1f : 0.5f;
                foreach (var mr in _tapeObj.GetComponentsInChildren<MeshRenderer>())
                { var c = mr.material.color; c.a = a * 0.75f; mr.material.color = c; }
            }
        }

        private void DestroyTape() { if (_tapeObj) { Destroy(_tapeObj); _tapeObj = null; } }

        // ── 3D Frame ───────────────────────────────────────────────
        private void PrepareFramePlan()
        {
            _framePlan.Clear();
            float yTop = AreaMin.y + 0.05f;
            float yBottom = AreaMin.y - MaxDepth; // FRAME BOTTOM = PIT BOTTOM

            Vector3 v00 = new(AreaMin.x, 0, AreaMin.z);
            Vector3 v10 = new(AreaMax.x + 1, 0, AreaMin.z);
            Vector3 v11 = new(AreaMax.x + 1, 0, AreaMax.z + 1);
            Vector3 v01 = new(AreaMin.x, 0, AreaMax.z + 1);

            float pillarH = yTop - yBottom + 0.3f;
            float pillarCY = (yTop + yBottom) * 0.5f;
            AddPillar(v00, pillarCY, pillarH); AddPillar(v10, pillarCY, pillarH);
            AddPillar(v11, pillarCY, pillarH); AddPillar(v01, pillarCY, pillarH);
            AddBeam(v00, v10, yTop); AddBeam(v10, v11, yTop);
            AddBeam(v11, v01, yTop); AddBeam(v01, v00, yTop);
            AddBeam(v00, v10, yBottom); AddBeam(v10, v11, yBottom);
            AddBeam(v11, v01, yBottom); AddBeam(v01, v00, yBottom);
            AddAccent(v00, yTop); AddAccent(v10, yTop);
            AddAccent(v11, yTop); AddAccent(v01, yTop);
        }

        private void AddPillar(Vector3 pos, float cy, float h)
        { _framePlan.Add(new() { position = new(pos.x, cy, pos.z), scale = new(0.16f, h, 0.16f), isAccent = false }); }

        private void AddBeam(Vector3 from, Vector3 to, float y)
        {
            Vector3 mid = (from + to) * 0.5f;
            float l = Vector3.Distance(from, to);
            Vector3 s = Mathf.Abs(from.x - to.x) > 0.01f ? new(l, 0.12f, 0.1f) : new(0.1f, 0.12f, l);
            _framePlan.Add(new() { position = new(mid.x, y, mid.z), scale = s, isAccent = false });
        }

        private void AddAccent(Vector3 pos, float y)
        { _framePlan.Add(new() { position = new(pos.x, y, pos.z), scale = new(0.22f, 0.22f, 0.22f), isAccent = true }); }

        private void PlaceFrameSegment(FrameSegment seg)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = seg.isAccent ? "QFrameAccent" : "QFrame";
            go.transform.position = seg.position;
            go.transform.localScale = seg.scale;
            var r = go.GetComponent<MeshRenderer>();
            if (seg.isAccent) r.material = CreateEmissiveMat(frameAccentColor, 0.7f);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var m = new Material(sh) { color = frameColor };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", frameColor);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.9f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
                r.material = m;
            }
            Destroy(go.GetComponent<BoxCollider>());
            _frameSegments.Add(go);
        }

        // ── Drill ──────────────────────────────────────────────────
        private void CreateDrillHead()
        {
            _drillHead = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _drillHead.name = "DrillHead";
            _drillHead.transform.localScale = new(0.5f, 0.25f, 0.5f);
            var c = _drillHead.GetComponent<CapsuleCollider>(); if (c) Destroy(c);
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = new Color(0.85f, 0.55f, 0.08f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.8f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(0.85f, 0.45f, 0.05f) * 0.4f);
            m.EnableKeyword("_EMISSION");
            _drillHead.GetComponent<MeshRenderer>().material = m;

            _drillBeam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _drillBeam.name = "DrillBeam";
            _drillBeam.transform.localScale = new(0.08f, 0.5f, 0.08f);
            var bc = _drillBeam.GetComponent<CapsuleCollider>(); if (bc) Destroy(bc);
            var bm = new Material(sh); bm.color = new Color(1f, 0.55f, 0.05f, 0.6f);
            if (bm.HasProperty("_BaseColor")) bm.SetColor("_BaseColor", bm.color);
            if (bm.HasProperty("_EmissionColor")) bm.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f) * 0.8f);
            bm.EnableKeyword("_EMISSION");
            _drillBeam.GetComponent<MeshRenderer>().material = bm;
        }

        private void UpdateDrillHead()
        {
            if (_drillHead == null) return;
            float dy = AreaMin.y - CurrentDepth - 0.5f;
            Vector3 tp = new(AreaMin.x + _cursorX + 0.5f, dy, AreaMin.z + _cursorZ + 0.5f);
            _drillHead.transform.position = tp;
            _drillHead.transform.Rotate(Vector3.up, 220f * Time.deltaTime);
            if (_drillBeam) { _drillBeam.transform.position = tp + Vector3.down * 0.35f; _drillBeam.transform.Rotate(Vector3.up, -180f * Time.deltaTime); }
        }

        // ── Mining ─────────────────────────────────────────────────
        private void MineNextVoxel()
        {
            if (CurrentDepth >= MaxDepth)
            {
                Phase = QuarryPhase.Complete;
                if (_drillHead) Destroy(_drillHead); if (_drillBeam) Destroy(_drillBeam);
                _drillHead = null; _drillBeam = null; return;
            }

            EnsureOutput();
            bool hasOutputSpace = false;
            for (int i = 0; i < _output.Size; i++)
                if (_output.GetSlot(i).IsEmpty) { hasOutputSpace = true; break; }
            if (!hasOutputSpace)
            {
                var hits = Physics.OverlapSphere(transform.position, 1.6f);
                foreach (var col in hits)
                {
                    if (col.gameObject == gameObject) continue;
                    var pipe = col.GetComponent<ItemPipe>();
                    if (pipe != null && pipe.GetInputCapacity(null) > 0) { hasOutputSpace = true; break; }
                }
            }
            if (!hasOutputSpace) { IsOutputFull = true; return; }
            IsOutputFull = false;

            Vector3Int target = new(AreaMin.x + _cursorX, AreaMin.y - CurrentDepth, AreaMin.z + _cursorZ);
            Voxel v = _world.GetVoxelWorld(target);
            if (v.material == (byte)MaterialId.Bedrock) { AdvanceCursor(); return; }
            if (v.density > VoxelConstants.ISO_LEVEL)
            {
                var def = _matReg != null ? _matReg.Get(v.material) : null;
                if (def == null || !def.isMineable || quarryTier >= def.miningTier)
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
            if (_cursorX >= AreaX) { _cursorX = 0; _cursorZ++;
                if (_cursorZ >= AreaZ) { _cursorZ = 0; CurrentDepth++; } }
        }

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
            if (rem > 0) { EnsureOutput(); _output.Insert(new ItemStack(item, rem)); }
        }

        private void EnsureOutput()
        {
            if (_output == null) _output = new ItemContainer("Quarry Output", outputSlots);
            else _output.Resize(outputSlots);
        }

        public void EnsureOutputPublic() => EnsureOutput();

        public void RestoreState(int depth, int cx, int cz, int phase, int rl, int sl, int el)
        {
            CurrentDepth = depth; _cursorX = cx; _cursorZ = cz;
            Phase = (QuarryPhase)phase;
            InstalledRangeLevel = rl; InstalledSpeedLevel = sl; InstalledEfficiencyLevel = el;
            if (Phase == QuarryPhase.Complete) { if (_drillHead) Destroy(_drillHead); if (_drillBeam) Destroy(_drillBeam); }
        }

        private void OnDestroy()
        {
            DestroyGhost(); DestroyTape();
            foreach (var fb in _frameSegments) if (fb) Destroy(fb);
            _frameSegments.Clear();
            if (_drillHead) Destroy(_drillHead);
            if (_drillBeam) Destroy(_drillBeam);
        }

        private static Material CreateEmissiveMat(Color color, float emissiveScale)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * emissiveScale);
            m.EnableKeyword("_EMISSION"); return m;
        }
    }
}
