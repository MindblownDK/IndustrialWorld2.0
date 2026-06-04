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
        public int defaultSize = 16;
        public float forwardOffset = 2f;

        [Header("Mining")]
        public float baseMineInterval = 0.5f;
        public int minePerTick = 1;
        [Range(0, 4)] public int quarryTier = 3;

        [Header("Power")]
        public float basePowerDraw = 500f;
        public float powerPerPerfUpgrade = 25f;
        public float powerSavePerEffUpgrade = 35f;

        [Header("Frame")]
        public float frameBuildInterval = 0.06f;
        public Color frameColor = new(0.18f, 0.19f, 0.22f);
        public Color frameAccentColor = new(0.92f, 0.52f, 0.08f, 0.85f);

        [Header("Tape")]
        public float tapePreviewDuration = 2.5f;
        public Color tapeColor1 = new(0.95f, 0.55f, 0.05f, 0.75f);
        public Color tapeColor2 = new(0.92f, 0.82f, 0.08f, 0.75f);

        [Header("Output")]
        public int outputSlots = 6;

        public const int UPGRADE_SLOTS = 3;
        public ItemContainer upgradeC;

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

        public int InstalledRangeLevel { get; private set; }
        public int InstalledSpeedLevel { get; private set; }
        public int InstalledEfficiencyLevel { get; private set; }
        public const int MaxRangeLevel = 10;
        public const int MaxSpeedLevel = 10;
        public const int MaxEfficiencyLevel = 5;

        public int EffectiveSize => defaultSize + InstalledRangeLevel;
        public float EffectiveMineInterval => Mathf.Max(0.05f, baseMineInterval - InstalledSpeedLevel * 0.04f);
        public float EffectivePowerDraw => Mathf.Max(10f,
            basePowerDraw + (InstalledRangeLevel + InstalledSpeedLevel) * powerPerPerfUpgrade
                          - InstalledEfficiencyLevel * powerSavePerEffUpgrade);

        private ItemContainer _output;
        private float _mineTimer, _frameBuildTimer, _tapeTimer;
        private int _cursorX, _cursorZ;
        private VoxelWorld _world;
        private MaterialRegistry _matReg;
        private PowerConsumer _power;
        private bool _initialized;
        private Vector3Int _originVoxel;
        private int _lastKnownSize;

        private GameObject _ghostObj, _tapeObj, _drillHead, _drillBeam;
        private List<GameObject> _frameSegments = new();
        private int _frameBuildIndex;
        private List<FrameSegment> _framePlan = new();
        private float _tapeFlickerTimer;
        private bool _tapeFlickerState;
        private struct FrameSegment { public Vector3 position, scale; public bool isAccent; }

        private void Awake() { EnsureOutput(); EnsureUpgrades(); }
        private void Start()
        {
            _world = VoxelWorld.Instance;
            _matReg = _world?.materialRegistry;
            _power = GetComponent<PowerConsumer>();
            if (_world == null) { enabled = false; return; }
            _originVoxel = _world.WorldToVoxel(transform.position);
            RecomputeArea();
            _lastKnownSize = EffectiveSize;
            CalculateMaxDepth();
            RegisterUpgradeListener();
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized) return;
            if (_power) _power.wattsPerSecond = EffectivePowerDraw;
            bool hp = _power == null || _power.IsPowered;

            if (Phase == QuarryPhase.Idle && _ghostObj == null) CreateGhostPreview();
            else if (Phase != QuarryPhase.Idle && _ghostObj) DestroyGhost();

            if (EffectiveSize != _lastKnownSize)
            {
                _lastKnownSize = EffectiveSize; RecomputeArea(); CalculateMaxDepth();
                if (Phase == QuarryPhase.Mining) { DAV(); _cursorX = _cursorZ = 0; CurrentDepth = 0; Phase = QuarryPhase.Idle; }
                else if (Phase == QuarryPhase.Idle) { DestroyGhost(); CreateGhostPreview(); }
            }

            switch (Phase)
            {
                case QuarryPhase.Idle: if (hp) ETP(); break;
                case QuarryPhase.TapeFrame: if (!hp) return; _tapeTimer += Time.deltaTime; UTF(); if (_tapeTimer >= tapePreviewDuration) { DestroyTape(); EFP(); } break;
                case QuarryPhase.BuildingFrame: if (!hp) return; _frameBuildTimer += Time.deltaTime; if (_frameBuildTimer >= frameBuildInterval) { _frameBuildTimer = 0f; if (_frameBuildIndex < _framePlan.Count) PFS(_framePlan[_frameBuildIndex++]); else { Phase = QuarryPhase.Mining; CDH(); } } break;
                case QuarryPhase.Mining: if (!hp) return; _mineTimer += Time.deltaTime; if (_mineTimer >= EffectiveMineInterval) { _mineTimer -= EffectiveMineInterval; for (int i = 0; i < minePerTick; i++) MNV(); } UDH(); break;
                case QuarryPhase.Complete: if (_drillHead) { Destroy(_drillHead); _drillHead = null; } if (_drillBeam) { Destroy(_drillBeam); _drillBeam = null; } break;
            }
        }

        void DAV() { DestroyGhost(); DestroyTape(); foreach (var fb in _frameSegments) if (fb) Destroy(fb); _frameSegments.Clear(); _framePlan.Clear(); if (_drillHead) { Destroy(_drillHead); _drillHead = null; } if (_drillBeam) { Destroy(_drillBeam); _drillBeam = null; } }
        void ETP() { Phase = QuarryPhase.TapeFrame; _tapeTimer = 0f; DestroyGhost(); CTP(); }
        void EFP() { Phase = QuarryPhase.BuildingFrame; PFP(); _frameBuildIndex = 0; _frameBuildTimer = 0f; }

        Vector3Int FD() { var f = transform.forward; float ax = Mathf.Abs(f.x), az = Mathf.Abs(f.z); return ax >= az ? new(Mathf.RoundToInt(Mathf.Sign(f.x)), 0, 0) : new(0, 0, Mathf.RoundToInt(Mathf.Sign(f.z))); }
        void RecomputeArea() { var fwd = FD(); if (fwd == default) fwd = new(1, 0, 0); int sy = _originVoxel.y, sz = EffectiveSize; var p = new Vector3Int(fwd.z, 0, -fwd.x); int h = sz / 2; var s = _originVoxel + fwd * (int)forwardOffset - p * h; var e = s + fwd * sz + p * sz; AreaMin = new(Mathf.Min(s.x, e.x), sy, Mathf.Min(s.z, e.z)); AreaMax = new(Mathf.Max(s.x, e.x) - 1, sy, Mathf.Max(s.z, e.z) - 1); AreaX = Mathf.Abs(AreaMax.x - AreaMin.x) + 1; AreaZ = Mathf.Abs(AreaMax.z - AreaMin.z) + 1; }
        void CalculateMaxDepth() { MaxDepth = Mathf.Max(1, AreaMin.y - 3); }

        public void EnsureUpgrades() { if (upgradeC == null) { upgradeC = new("Upgrades", UPGRADE_SLOTS); upgradeC.OnChanged += RCU; } else upgradeC.Resize(UPGRADE_SLOTS); }
        void RegisterUpgradeListener() { EnsureUpgrades(); upgradeC.OnChanged -= RCU; upgradeC.OnChanged += RCU; RCU(); }
        void RCU() { InstalledRangeLevel = InstalledSpeedLevel = InstalledEfficiencyLevel = 0; if (upgradeC == null) return; for (int i = 0; i < upgradeC.Size; i++) { var s = upgradeC.GetSlot(i); if (s.IsEmpty || !(s.item is QuarryUpgradeItem u)) continue; int a = u.level * s.count; switch (u.upgradeKind) { case QuarryUpgradeKind.Range: InstalledRangeLevel = Mathf.Min(MaxRangeLevel, a); break; case QuarryUpgradeKind.Speed: InstalledSpeedLevel = Mathf.Min(MaxSpeedLevel, a); break; case QuarryUpgradeKind.Efficiency: InstalledEfficiencyLevel = Mathf.Min(MaxEfficiencyLevel, a); break; } } }
        public bool TryInstallUpgrade(QuarryUpgradeItem item) { if (item == null) return false; EnsureUpgrades(); var leftover = upgradeC.Insert(new(item, 1)); return leftover.IsEmpty; }

        // ═══ PLACEMENT PREVIEW (BuildSystem calls these) ═══
        static GameObject _ppv;
        public static void ShowPlacementPreview(Vector3 wp, Quaternion rot, int sz, float fo)
        {
            HidePlacementPreview(); _ppv = new("QPP");
            var vox = VoxelWorld.Instance != null ? VoxelWorld.Instance.WorldToVoxel(wp) : Vector3Int.zero;
            var f = rot * Vector3.forward; float ax = Mathf.Abs(f.x), az = Mathf.Abs(f.z);
            var d = ax >= az ? new Vector3Int(Mathf.RoundToInt(Mathf.Sign(f.x)), 0, 0) : new(0, 0, Mathf.RoundToInt(Mathf.Sign(f.z)));
            if (d == default) d = new(1, 0, 0);
            var perp = new Vector3Int(d.z, 0, -d.x); int h = sz / 2;
            var s = vox + d * (int)fo - perp * h; var e = s + d * sz + perp * sz;
            var amin = new Vector3Int(Mathf.Min(s.x, e.x), vox.y, Mathf.Min(s.z, e.z));
            var amax = new Vector3Int(Mathf.Max(s.x, e.x) - 1, vox.y, Mathf.Max(s.z, e.z) - 1);
            float yTop = vox.y + 0.5f, yBot = wp.y - 1.2f; Color gc = new(1f, 0.8f, 0.2f, 0.55f);
            var v0 = new Vector3(amin.x, yTop, amin.z); var v1 = new Vector3(amax.x + 1, yTop, amin.z);
            var v2 = new Vector3(amax.x + 1, yTop, amax.z + 1); var v3 = new Vector3(amin.x, yTop, amax.z + 1);
            AL(v0, v1, gc); AL(v1, v2, gc); AL(v2, v3, gc); AL(v3, v0, gc);
            AL(v0, new(v0.x, yBot, v0.z), gc); AL(v1, new(v1.x, yBot, v1.z), gc);
            AL(v2, new(v2.x, yBot, v2.z), gc); AL(v3, new(v3.x, yBot, v3.z), gc);
            var b0 = new Vector3(amin.x, yBot, amin.z); var b1 = new Vector3(amax.x + 1, yBot, amin.z);
            var b2 = new Vector3(amax.x + 1, yBot, amax.z + 1); var b3 = new Vector3(amin.x, yBot, amax.z + 1);
            AL(b0, b1, gc); AL(b1, b2, gc); AL(b2, b3, gc); AL(b3, b0, gc);
        }
        public static void HidePlacementPreview() { if (_ppv) { Object.Destroy(_ppv); _ppv = null; } }
        static void AL(Vector3 a, Vector3 b, Color c) { var go = new GameObject("L"); go.transform.SetParent(_ppv.transform, false); var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.SetPositions(new[] { a, b }); lr.startWidth = lr.endWidth = 0.06f; lr.startColor = lr.endColor = c; lr.useWorldSpace = true; var s = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"); lr.material = new Material(s) { color = c }; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        // ═══ GHOST (placed quarry idle) ═══
        void CreateGhostPreview()
        {
            if (_ghostObj) Destroy(_ghostObj);
            _ghostObj = new("QG");
            float yTop = transform.position.y + 1.2f;  // MODEL TOP
            float yBot = transform.position.y - 1.2f;  // MODEL BOTTOM
            Color gc = new(1f, 0.8f, 0.2f, 0.5f);
            var v0 = V(AreaMin.x, yTop, AreaMin.z); var v1 = V(AreaMax.x + 1, yTop, AreaMin.z);
            var v2 = V(AreaMax.x + 1, yTop, AreaMax.z + 1); var v3 = V(AreaMin.x, yTop, AreaMax.z + 1);
            GL(v0, v1, gc); GL(v1, v2, gc); GL(v2, v3, gc); GL(v3, v0, gc);
            GL(v0, V(v0.x, yBot, v0.z), gc); GL(v1, V(v1.x, yBot, v1.z), gc);
            GL(v2, V(v2.x, yBot, v2.z), gc); GL(v3, V(v3.x, yBot, v3.z), gc);
            var b0 = V(AreaMin.x, yBot, AreaMin.z); var b1 = V(AreaMax.x + 1, yBot, AreaMin.z);
            var b2 = V(AreaMax.x + 1, yBot, AreaMax.z + 1); var b3 = V(AreaMin.x, yBot, AreaMax.z + 1);
            GL(b0, b1, gc); GL(b1, b2, gc); GL(b2, b3, gc); GL(b3, b0, gc);
        }
        Vector3 V(float x, float y, float z) => new(x, y, z);
        void GL(Vector3 a, Vector3 b, Color c) { var go = new GameObject("L"); go.transform.SetParent(_ghostObj.transform, false); var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.SetPositions(new[] { a, b }); lr.startWidth = lr.endWidth = 0.06f; lr.startColor = lr.endColor = c; lr.useWorldSpace = true; var s = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"); lr.material = new Material(s) { color = c }; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        void DestroyGhost() { if (_ghostObj) { Destroy(_ghostObj); _ghostObj = null; } }

        // ═══ TAPE ═══
        void CTP() { _tapeObj = new("QT"); float y = AreaMin.y + 0.1f; var v0 = V(AreaMin.x, y, AreaMin.z); var v1 = V(AreaMax.x + 1, y, AreaMin.z); var v2 = V(AreaMax.x + 1, y, AreaMax.z + 1); var v3 = V(AreaMin.x, y, AreaMax.z + 1); TE(v0, v1); TE(v1, v2); TE(v2, v3); TE(v3, v0); float ph = 2.5f; TP(v0, ph); TP(v1, ph); TP(v2, ph); TP(v3, ph); }
        void TE(Vector3 f, Vector3 t) { var d = (t - f).normalized; float l = Vector3.Distance(f, t); int sc = Mathf.CeilToInt(l / 0.5f); float sl = l / sc; for (int i = 0; i < sc; i++) { var p = f + d * (i * sl + sl * 0.5f); var go = MK("T", p, _tapeObj.transform); if (Mathf.Abs(d.x) > 0.5f) go.transform.localScale = new(sl * 0.95f, 0.04f, 0.08f); else go.transform.localScale = new(0.08f, 0.04f, sl * 0.95f); go.GetComponent<MeshRenderer>().material = EM((i % 2) == 0 ? tapeColor1 : tapeColor2, 0.6f); } }
        void TP(Vector3 bp, float h) { var g = MK("P", bp + Vector3.up * h * 0.5f, _tapeObj.transform); g.transform.localScale = new(0.1f, h, 0.1f); g.GetComponent<MeshRenderer>().material = EM(tapeColor1, 0.8f); var o = MK("O", bp + Vector3.up * h, _tapeObj.transform); o.transform.localScale = Vector3.one * 0.2f; o.GetComponent<MeshRenderer>().material = EM(new(0.95f, 0.55f, 0.05f, 0.9f), 0.9f); }
        GameObject MK(string n, Vector3 p, Transform pt) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(pt, false); g.transform.position = p; Destroy(g.GetComponent<Collider>()); return g; }
        void UTF() { if (!_tapeObj) return; _tapeFlickerTimer += Time.deltaTime; if (_tapeFlickerTimer > 0.15f) { _tapeFlickerTimer = 0f; _tapeFlickerState = !_tapeFlickerState; float a = _tapeFlickerState ? 1f : 0.5f; foreach (var mr in _tapeObj.GetComponentsInChildren<MeshRenderer>()) { var c = mr.material.color; c.a = a * 0.75f; mr.material.color = c; } } }
        void DestroyTape() { if (_tapeObj) { Destroy(_tapeObj); _tapeObj = null; } }

        // ═══ 3D FRAME (top = model top, bottom = model bottom) ═══
        void PFP()
        {
            _framePlan.Clear();
            float yTop = transform.position.y + 1.2f;  // MODEL TOP
            float yBot = transform.position.y - 1.2f;  // MODEL BOTTOM
            var v00 = V(AreaMin.x, 0, AreaMin.z); var v10 = V(AreaMax.x + 1, 0, AreaMin.z);
            var v11 = V(AreaMax.x + 1, 0, AreaMax.z + 1); var v01 = V(AreaMin.x, 0, AreaMax.z + 1);
            float pH = yTop - yBot + 0.3f, pCY = (yTop + yBot) * 0.5f;
            AP(v00, pCY, pH); AP(v10, pCY, pH); AP(v11, pCY, pH); AP(v01, pCY, pH);
            AB(v00, v10, yTop); AB(v10, v11, yTop); AB(v11, v01, yTop); AB(v01, v00, yTop);
            AB(v00, v10, yBot); AB(v10, v11, yBot); AB(v11, v01, yBot); AB(v01, v00, yBot);
            AA(v00, yTop); AA(v10, yTop); AA(v11, yTop); AA(v01, yTop);
        }
        void AP(Vector3 p, float cy, float h) => _framePlan.Add(new() { position = new(p.x, cy, p.z), scale = new(0.16f, h, 0.16f), isAccent = false });
        void AB(Vector3 f, Vector3 t, float y) { var m = (f + t) * 0.5f; float l = Vector3.Distance(f, t); _framePlan.Add(new() { position = new(m.x, y, m.z), scale = Mathf.Abs(f.x - t.x) > 0.01f ? new(l, 0.12f, 0.1f) : new(0.1f, 0.12f, l), isAccent = false }); }
        void AA(Vector3 p, float y) => _framePlan.Add(new() { position = new(p.x, y, p.z), scale = new(0.22f, 0.22f, 0.22f), isAccent = true });
        void PFS(FrameSegment s) { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = s.isAccent ? "A" : "F"; go.transform.position = s.position; go.transform.localScale = s.scale; var r = go.GetComponent<MeshRenderer>(); if (s.isAccent) r.material = EM(frameAccentColor, 0.7f); else { var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); var m = new Material(sh) { color = frameColor }; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", frameColor); if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.9f); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f); r.material = m; } Destroy(go.GetComponent<BoxCollider>()); _frameSegments.Add(go); }

        // ═══ DRILL ═══
        void CDH() { _drillHead = GameObject.CreatePrimitive(PrimitiveType.Cylinder); _drillHead.name = "H"; _drillHead.transform.localScale = new(0.5f, 0.25f, 0.5f); var c = _drillHead.GetComponent<CapsuleCollider>(); if (c) Destroy(c); var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); var m = new Material(sh); m.color = new(0.85f, 0.55f, 0.08f); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color); if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.8f); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(0.85f, 0.45f, 0.05f) * 0.4f); m.EnableKeyword("_EMISSION"); _drillHead.GetComponent<MeshRenderer>().material = m; _drillBeam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); _drillBeam.name = "B"; _drillBeam.transform.localScale = new(0.08f, 0.5f, 0.08f); var bc = _drillBeam.GetComponent<CapsuleCollider>(); if (bc) Destroy(bc); var bm = new Material(sh); bm.color = new(1f, 0.55f, 0.05f, 0.6f); if (bm.HasProperty("_BaseColor")) bm.SetColor("_BaseColor", bm.color); if (bm.HasProperty("_EmissionColor")) bm.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f) * 0.8f); bm.EnableKeyword("_EMISSION"); _drillBeam.GetComponent<MeshRenderer>().material = bm; }
        void UDH() { if (!_drillHead) return; float dy = AreaMin.y - CurrentDepth - 0.5f; var tp = new Vector3(AreaMin.x + _cursorX + 0.5f, dy, AreaMin.z + _cursorZ + 0.5f); _drillHead.transform.position = tp; _drillHead.transform.Rotate(Vector3.up, 220f * Time.deltaTime); if (_drillBeam) { _drillBeam.transform.position = tp + Vector3.down * 0.35f; _drillBeam.transform.Rotate(Vector3.up, -180f * Time.deltaTime); } }

        // ═══ MINING ═══
        void MNV() { if (CurrentDepth >= MaxDepth) { Phase = QuarryPhase.Complete; if (_drillHead) Destroy(_drillHead); if (_drillBeam) Destroy(_drillBeam); _drillHead = _drillBeam = null; return; } EnsureOutput(); bool sp = false; for (int i = 0; i < _output.Size; i++) if (_output.GetSlot(i).IsEmpty) { sp = true; break; } if (!sp) { var hs = Physics.OverlapSphere(transform.position, 1.6f); foreach (var c in hs) { if (c.gameObject == gameObject) continue; var p = c.GetComponent<ItemPipe>(); if (p && p.GetInputCapacity(null) > 0) { sp = true; break; } } } if (!sp) { IsOutputFull = true; return; } IsOutputFull = false; var t = new Vector3Int(AreaMin.x + _cursorX, AreaMin.y - CurrentDepth, AreaMin.z + _cursorZ); var v = _world.GetVoxelWorld(t); if (v.material == (byte)MaterialId.Bedrock) { AC(); return; } if (v.density > VoxelConstants.ISO_LEVEL) { var def = _matReg?.Get(v.material); if (def == null || !def.isMineable || quarryTier >= def.miningTier) { _world.SetVoxelWorld(t, Voxel.Empty); if (def?.dropItem && def.dropAmount > 0) OI(def.dropItem, def.dropAmount); } } AC(); }
        void AC() { _cursorX++; if (_cursorX >= AreaX) { _cursorX = 0; _cursorZ++; if (_cursorZ >= AreaZ) { _cursorZ = 0; CurrentDepth++; } } }
        void OI(ItemDefinition item, int count) { int rem = count; var hs = Physics.OverlapSphere(transform.position, 1.6f); foreach (var c in hs) { if (c.gameObject == gameObject) continue; var p = c.GetComponent<ItemPipe>(); if (!p) continue; int a = p.TryInsert(item, Mathf.Min(p.GetInputCapacity(item), rem)); rem -= a; if (rem <= 0) return; } if (rem > 0) { EnsureOutput(); _output.Insert(new(item, rem)); } }
        void EnsureOutput() { if (_output == null) _output = new("Quarry Output", outputSlots); else _output.Resize(outputSlots); }
        public void EnsureOutputPublic() => EnsureOutput();
        public void RestoreState(int d, int cx, int cz, int ph, int rl, int sl, int el) { CurrentDepth = d; _cursorX = cx; _cursorZ = cz; Phase = (QuarryPhase)ph; InstalledRangeLevel = rl; InstalledSpeedLevel = sl; InstalledEfficiencyLevel = el; if (Phase == QuarryPhase.Complete) { if (_drillHead) Destroy(_drillHead); if (_drillBeam) Destroy(_drillBeam); } }
        void OnDestroy() { DestroyGhost(); DestroyTape(); HidePlacementPreview(); foreach (var fb in _frameSegments) if (fb) Destroy(fb); _frameSegments.Clear(); if (_drillHead) Destroy(_drillHead); if (_drillBeam) Destroy(_drillBeam); }
        static Material EM(Color c, float es) { var s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); var m = new Material(s); m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * es); m.EnableKeyword("_EMISSION"); return m; }
    }
}
