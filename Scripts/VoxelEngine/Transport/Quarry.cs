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
        public Color frameColor     = new(0.18f, 0.19f, 0.22f);
        public Color frameAccentColor = new(0.92f, 0.52f, 0.08f, 0.85f);

        [Header("Tape")]
        public float tapePreviewDuration = 2.5f;
        public Color tapeColor1 = new(0.95f, 0.55f, 0.05f, 0.75f);
        public Color tapeColor2 = new(0.92f, 0.82f, 0.08f, 0.75f);

        [Header("Output")]
        public int outputSlots       = 6;
        public const int UPGRADE_SLOTS = 3;
        public ItemContainer upgradeC;

        // ── Runtime ──────────────────────────────────────
        public QuarryPhase Phase { get; private set; } = QuarryPhase.Idle;
        public int CurrentDepth { get; private set; }
        public int MaxDepth { get; private set; }
        public bool IsMining => Phase == QuarryPhase.Mining;
        public float MineProgress01 => _mineTimer / Mathf.Max(0.01f, EffInterval);
        public ItemContainer Output { get { EnsureOutput(); return _output; } }
        public int AreaX { get; private set; }
        public int AreaZ { get; private set; }
        public Vector3Int AreaMin  { get; private set; }
        public Vector3Int AreaMax  { get; private set; }
        public int CursorX => _cx;
        public int CursorZ => _cz;
        public bool IsOutputFull { get; private set; }

        public int InstalledRangeLevel { get; private set; }
        public int InstalledSpeedLevel { get; private set; }
        public int InstalledEfficiencyLevel { get; private set; }
        public const int MaxRangeLevel      = 10;
        public const int MaxSpeedLevel      = 10;
        public const int MaxEfficiencyLevel = 5;

        public int   EffSize      => defaultSize + InstalledRangeLevel;
        public float EffInterval  => Mathf.Max(0.05f, baseMineInterval - InstalledSpeedLevel * 0.04f);
        public float EffPowerDraw => Mathf.Max(10f,
            basePowerDraw + (InstalledRangeLevel + InstalledSpeedLevel) * powerPerPerfUpgrade
                          - InstalledEfficiencyLevel * powerSavePerEffUpgrade);

        // ── Model bounds (computed at Start) ─────────────
        private float _modelBotY;  // exact visual bottom of this quarry block
        private float _modelTopY;  // exact visual top

        private ItemContainer _output;
        private float _mineTimer, _frameBuildTimer, _tapeTimer;
        private int _cx, _cz, _lastSize;
        private VoxelWorld _world;
        private MaterialRegistry _matReg;
        private PowerConsumer _power;
        private bool _ok;
        private Vector3Int _org;

        private GameObject _ghost, _tape, _drill, _beam;
        private List<GameObject> _fSegs = new();
        private int _fIdx;
        private List<FS> _fPlan = new();
        private float _tfT;
        private bool _tfS;
        private struct FS { public Vector3 p, s; public bool a; }

        // ── Lifecycle ────────────────────────────────────
        void Awake() { EnsureOutput(); EnsureUpgrades(); }
        void Start()
        {
            _world = VoxelWorld.Instance;
            _matReg = _world?.materialRegistry;
            _power = GetComponent<PowerConsumer>();
            if (_world == null) { enabled = false; return; }

            // ── Compute model bottom/top from the ACTUAL visual bounds ──
            _modelBotY = _modelTopY = transform.position.y;
            var col = GetComponentInChildren<BoxCollider>(true);
            if (col != null) { _modelBotY = col.bounds.min.y; _modelTopY = col.bounds.max.y; }
            else
            {
                var r = GetComponentInChildren<MeshRenderer>(true);
                if (r != null) { _modelBotY = r.bounds.min.y; _modelTopY = r.bounds.max.y; }
            }
            // If nothing found, fall back to the prefab scale (Cube × 2.4)
            if (Mathf.Approximately(_modelBotY, transform.position.y) &&
                Mathf.Approximately(_modelTopY, transform.position.y))
            { _modelBotY = transform.position.y - 1.2f; _modelTopY = transform.position.y + 1.2f; }

            Debug.Log($"[Quarry] Model Y range: {_modelBotY:F2} → {_modelTopY:F2}");

            _org = _world.WorldToVoxel(transform.position);
            RecomputeArea();
            _lastSize = EffSize;
            CalculateMaxDepth();
            RegisterUpgradeListener();
            _ok = true;
        }

        void Update()
        {
            if (!_ok) return;
            if (_power) _power.wattsPerSecond = EffPowerDraw;
            bool hp = _power == null || _power.IsPowered;

            // ghost only in idle
            if (Phase == QuarryPhase.Idle && _ghost == null) CreateGhost();
            else if (Phase != QuarryPhase.Idle && _ghost) DestroyGhost();

            if (EffSize != _lastSize)
            {
                _lastSize = EffSize; RecomputeArea(); CalculateMaxDepth();
                if (Phase == QuarryPhase.Mining) { DestroyAll(); _cx = _cz = 0; CurrentDepth = 0; Phase = QuarryPhase.Idle; }
                else if (Phase == QuarryPhase.Idle) { DestroyGhost(); CreateGhost(); }
            }

            switch (Phase)
            {
                case QuarryPhase.Idle: if (hp) EnterTape(); break;
                case QuarryPhase.TapeFrame: if (!hp) return; _tapeTimer += Time.deltaTime; TickTape(); if (_tapeTimer >= tapePreviewDuration) { DestroyTape(); EnterFrame(); } break;
                case QuarryPhase.BuildingFrame: if (!hp) return; _frameBuildTimer += Time.deltaTime; if (_frameBuildTimer >= frameBuildInterval) { _frameBuildTimer = 0f; if (_fIdx < _fPlan.Count) PlaceSeg(_fPlan[_fIdx++]); else { Phase = QuarryPhase.Mining; CreateDrill(); } } break;
                case QuarryPhase.Mining: if (!hp) return; _mineTimer += Time.deltaTime; if (_mineTimer >= EffInterval) { _mineTimer -= EffInterval; for (int i = 0; i < minePerTick; i++) MineOne(); } TickDrill(); break;
                case QuarryPhase.Complete: if (_drill) { Destroy(_drill); _drill = null; } if (_beam) { Destroy(_beam); _beam = null; } break;
            }
        }

        void DestroyAll() { DestroyGhost(); DestroyTape(); foreach (var fb in _fSegs) if (fb) Destroy(fb); _fSegs.Clear(); _fPlan.Clear(); if (_drill) { Destroy(_drill); _drill = null; } if (_beam) { Destroy(_beam); _beam = null; } }
        void EnterTape()  { Phase = QuarryPhase.TapeFrame; _tapeTimer = 0f; DestroyGhost(); CreateTape(); }
        void EnterFrame() { Phase = QuarryPhase.BuildingFrame; PlanFrame(); _fIdx = 0; _frameBuildTimer = 0f; }

        // ── Area ─────────────────────────────────────────
        Vector3Int Fwd() { var f = transform.forward; float ax = Mathf.Abs(f.x), az = Mathf.Abs(f.z); return ax >= az ? new(Mathf.RoundToInt(Mathf.Sign(f.x)), 0, 0) : new(0, 0, Mathf.RoundToInt(Mathf.Sign(f.z))); }
        void RecomputeArea() { var fwd = Fwd(); if (fwd == default) fwd = new(1, 0, 0); int sy = _org.y, sz = EffSize; var perp = new Vector3Int(fwd.z, 0, -fwd.x); int h = sz / 2; var s = _org + fwd * (int)forwardOffset - perp * h; var e = s + fwd * sz + perp * sz; AreaMin = new(Mathf.Min(s.x, e.x), sy, Mathf.Min(s.z, e.z)); AreaMax = new(Mathf.Max(s.x, e.x) - 1, sy, Mathf.Max(s.z, e.z) - 1); AreaX = Mathf.Abs(AreaMax.x - AreaMin.x) + 1; AreaZ = Mathf.Abs(AreaMax.z - AreaMin.z) + 1; }
        void CalculateMaxDepth() { MaxDepth = Mathf.Max(1, AreaMin.y - 3); }

        public void EnsureUpgrades() { if (upgradeC == null) { upgradeC = new("Upgrades", UPGRADE_SLOTS); upgradeC.OnChanged += RecalcUpg; } else upgradeC.Resize(UPGRADE_SLOTS); }
        void RegisterUpgradeListener() { EnsureUpgrades(); upgradeC.OnChanged -= RecalcUpg; upgradeC.OnChanged += RecalcUpg; RecalcUpg(); }
        void RecalcUpg() { InstalledRangeLevel = InstalledSpeedLevel = InstalledEfficiencyLevel = 0; if (upgradeC == null) return; for (int i = 0; i < upgradeC.Size; i++) { var st = upgradeC.GetSlot(i); if (st.IsEmpty || !(st.item is QuarryUpgradeItem u)) continue; int a = u.level * st.count; switch (u.upgradeKind) { case QuarryUpgradeKind.Range: InstalledRangeLevel = Mathf.Min(MaxRangeLevel, a); break; case QuarryUpgradeKind.Speed: InstalledSpeedLevel = Mathf.Min(MaxSpeedLevel, a); break; case QuarryUpgradeKind.Efficiency: InstalledEfficiencyLevel = Mathf.Min(MaxEfficiencyLevel, a); break; } } }
        public bool TryInstallUpgrade(QuarryUpgradeItem item) { if (item == null) return false; EnsureUpgrades(); return upgradeC.Insert(new(item, 1)).IsEmpty; }

        // ═══ PLACEMENT PREVIEW (BuildSystem) ════════════
        static GameObject _ppv;
        public static void ShowPlacementPreview(Vector3 wp, Quaternion rot, int sz, float fo)
        {
            HidePlacementPreview(); _ppv = new("QPP");
            var vox = VoxelWorld.Instance?.WorldToVoxel(wp) ?? Vector3Int.zero;
            var f = rot * Vector3.forward; float ax = Mathf.Abs(f.x), az = Mathf.Abs(f.z);
            var d = ax >= az ? new Vector3Int(Mathf.RoundToInt(Mathf.Sign(f.x)), 0, 0) : new(0, 0, Mathf.RoundToInt(Mathf.Sign(f.z)));
            if (d == default) d = new(1, 0, 0);
            var p = new Vector3Int(d.z, 0, -d.x); int h = sz / 2;
            var s = vox + d * (int)fo - p * h; var e = s + d * sz + p * sz;
            var amin = new Vector3Int(Mathf.Min(s.x, e.x), vox.y, Mathf.Min(s.z, e.z));
            var amax = new Vector3Int(Mathf.Max(s.x, e.x) - 1, vox.y, Mathf.Max(s.z, e.z) - 1);
            float yTop = vox.y + 0.5f, yBot = wp.y - 1.2f;
            Color gc = new(1f, 0.8f, 0.2f, 0.55f);
            var v0 = V3(amin.x, yTop, amin.z); var v1 = V3(amax.x + 1, yTop, amin.z);
            var v2 = V3(amax.x + 1, yTop, amax.z + 1); var v3 = V3(amin.x, yTop, amax.z + 1);
            PPL(v0, v1, gc); PPL(v1, v2, gc); PPL(v2, v3, gc); PPL(v3, v0, gc);
            PPL(v0, V3(v0.x, yBot, v0.z), gc); PPL(v1, V3(v1.x, yBot, v1.z), gc);
            PPL(v2, V3(v2.x, yBot, v2.z), gc); PPL(v3, V3(v3.x, yBot, v3.z), gc);
            var b0 = V3(amin.x, yBot, amin.z); var b1 = V3(amax.x + 1, yBot, amin.z);
            var b2 = V3(amax.x + 1, yBot, amax.z + 1); var b3 = V3(amin.x, yBot, amax.z + 1);
            PPL(b0, b1, gc); PPL(b1, b2, gc); PPL(b2, b3, gc); PPL(b3, b0, gc);
        }
        public static void HidePlacementPreview() { if (_ppv) { Object.Destroy(_ppv); _ppv = null; } }
        static void PPL(Vector3 a, Vector3 b, Color c) { var go = new GameObject("L"); go.transform.SetParent(_ppv.transform, false); var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.SetPositions(new[]{a,b}); lr.startWidth = lr.endWidth = 0.06f; lr.startColor = lr.endColor = c; lr.useWorldSpace = true; var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"); lr.material = new Material(sh) { color = c }; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        // ═══ GHOST (idle) ════════════════════════════════
        void CreateGhost()
        {
            if (_ghost) Destroy(_ghost); _ghost = new("QG");
            float yTop = _modelTopY;  // visual top of this quarry block
            float yBot = _modelBotY;  // visual bottom
            Color gc = new(1f, 0.8f, 0.2f, 0.5f);
            var v0 = V3(AreaMin.x, yTop, AreaMin.z); var v1 = V3(AreaMax.x + 1, yTop, AreaMin.z);
            var v2 = V3(AreaMax.x + 1, yTop, AreaMax.z + 1); var v3 = V3(AreaMin.x, yTop, AreaMax.z + 1);
            GL(v0, v1, gc); GL(v1, v2, gc); GL(v2, v3, gc); GL(v3, v0, gc);
            GL(v0, V3(v0.x, yBot, v0.z), gc); GL(v1, V3(v1.x, yBot, v1.z), gc);
            GL(v2, V3(v2.x, yBot, v2.z), gc); GL(v3, V3(v3.x, yBot, v3.z), gc);
            var b0 = V3(AreaMin.x, yBot, AreaMin.z); var b1 = V3(AreaMax.x + 1, yBot, AreaMin.z);
            var b2 = V3(AreaMax.x + 1, yBot, AreaMax.z + 1); var b3 = V3(AreaMin.x, yBot, AreaMax.z + 1);
            GL(b0, b1, gc); GL(b1, b2, gc); GL(b2, b3, gc); GL(b3, b0, gc);
        }
        static Vector3 V3(float x, float y, float z) => new(x, y, z);
        void GL(Vector3 a, Vector3 b, Color c) { var go = new GameObject("L"); go.transform.SetParent(_ghost.transform, false); var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.SetPositions(new[]{a,b}); lr.startWidth = lr.endWidth = 0.06f; lr.startColor = lr.endColor = c; lr.useWorldSpace = true; var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default"); lr.material = new Material(sh) { color = c }; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        void DestroyGhost() { if (_ghost) { Destroy(_ghost); _ghost = null; } }

        // ═══ TAPE ════════════════════════════════════════
        void CreateTape() { _tape = new("QT"); float y = AreaMin.y + 0.1f; var v0 = V3(AreaMin.x, y, AreaMin.z); var v1 = V3(AreaMax.x + 1, y, AreaMin.z); var v2 = V3(AreaMax.x + 1, y, AreaMax.z + 1); var v3 = V3(AreaMin.x, y, AreaMax.z + 1); TE(v0, v1); TE(v1, v2); TE(v2, v3); TE(v3, v0); float ph = 2.5f; TP(v0, ph); TP(v1, ph); TP(v2, ph); TP(v3, ph); }
        void TE(Vector3 f, Vector3 t) { var d = (t - f).normalized; float l = Vector3.Distance(f, t); int sc = Mathf.CeilToInt(l / 0.5f); float sl = l / sc; for (int i = 0; i < sc; i++) { var p = f + d * (i * sl + sl * 0.5f); var g = MK("T", p, _tape.transform); if (Mathf.Abs(d.x) > 0.5f) g.transform.localScale = new(sl * 0.95f, 0.04f, 0.08f); else g.transform.localScale = new(0.08f, 0.04f, sl * 0.95f); g.GetComponent<MeshRenderer>().material = MatE((i % 2) == 0 ? tapeColor1 : tapeColor2, 0.6f); } }
        void TP(Vector3 bp, float h) { var g = MK("P", bp + Vector3.up * h * 0.5f, _tape.transform); g.transform.localScale = new(0.1f, h, 0.1f); g.GetComponent<MeshRenderer>().material = MatE(tapeColor1, 0.8f); var o = MK("O", bp + Vector3.up * h, _tape.transform); o.transform.localScale = Vector3.one * 0.2f; o.GetComponent<MeshRenderer>().material = MatE(new(0.95f, 0.55f, 0.05f, 0.9f), 0.9f); }
        static GameObject MK(string n, Vector3 p, Transform pt) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(pt, false); g.transform.position = p; Object.Destroy(g.GetComponent<Collider>()); return g; }
        void TickTape() { if (!_tape) return; _tfT += Time.deltaTime; if (_tfT > 0.15f) { _tfT = 0f; _tfS = !_tfS; float a = _tfS ? 1f : 0.5f; foreach (var mr in _tape.GetComponentsInChildren<MeshRenderer>()) { var c = mr.material.color; c.a = a * 0.75f; mr.material.color = c; } } }
        void DestroyTape() { if (_tape) { Destroy(_tape); _tape = null; } }

        // ═══ 3D FRAME (top = model top, bottom = model bottom) ═══
        void PlanFrame()
        {
            _fPlan.Clear();
            float yTop = _modelTopY;  // visual top of quarry block
            float yBot = _modelBotY;  // visual bottom of quarry block
            var v00 = V3(AreaMin.x, 0, AreaMin.z); var v10 = V3(AreaMax.x + 1, 0, AreaMin.z);
            var v11 = V3(AreaMax.x + 1, 0, AreaMax.z + 1); var v01 = V3(AreaMin.x, 0, AreaMax.z + 1);
            float pH = yTop - yBot + 0.3f, pCY = (yTop + yBot) * 0.5f;
            Pillar(v00, pCY, pH); Pillar(v10, pCY, pH); Pillar(v11, pCY, pH); Pillar(v01, pCY, pH);
            Beam(v00, v10, yTop); Beam(v10, v11, yTop); Beam(v11, v01, yTop); Beam(v01, v00, yTop);
            Beam(v00, v10, yBot); Beam(v10, v11, yBot); Beam(v11, v01, yBot); Beam(v01, v00, yBot);
            Accent(v00, yTop); Accent(v10, yTop); Accent(v11, yTop); Accent(v01, yTop);
        }
        void Pillar(Vector3 p, float cy, float h) => _fPlan.Add(new(){p=new(p.x, cy, p.z), s=new(0.16f, h, 0.16f), a=false});
        void Beam(Vector3 f, Vector3 t, float y) { var m = (f + t) * 0.5f; float l = Vector3.Distance(f, t); _fPlan.Add(new(){p=new(m.x, y, m.z), s=Mathf.Abs(f.x - t.x) > 0.01f ? new(l, 0.12f, 0.1f) : new(0.1f, 0.12f, l), a=false}); }
        void Accent(Vector3 p, float y) => _fPlan.Add(new(){p=new(p.x, y, p.z), s=new(0.22f, 0.22f, 0.22f), a=true});
        void PlaceSeg(FS s) { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = s.a ? "A" : "F"; go.transform.position = s.p; go.transform.localScale = s.s; var r = go.GetComponent<MeshRenderer>(); if (s.a) r.material = MatE(frameAccentColor, 0.7f); else { var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); var m = new Material(sh) { color = frameColor }; m.SetColor("_BaseColor", frameColor); m.SetFloat("_Metallic", 0.9f); m.SetFloat("_Smoothness", 0.45f); r.material = m; } Destroy(go.GetComponent<BoxCollider>()); _fSegs.Add(go); }

        // ═══ DRILL ═══════════════════════════════════════
        void CreateDrill() { _drill = GameObject.CreatePrimitive(PrimitiveType.Cylinder); _drill.name = "H"; _drill.transform.localScale = new(0.5f, 0.25f, 0.5f); var c = _drill.GetComponent<CapsuleCollider>(); if (c) Destroy(c); var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); var m = new Material(sh); m.color = new(0.85f, 0.55f, 0.08f); m.SetColor("_BaseColor", m.color); m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Smoothness", 0.5f); m.SetColor("_EmissionColor", new Color(0.85f, 0.45f, 0.05f) * 0.4f); m.EnableKeyword("_EMISSION"); _drill.GetComponent<MeshRenderer>().material = m; _beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); _beam.name = "B"; _beam.transform.localScale = new(0.08f, 0.5f, 0.08f); var bc = _beam.GetComponent<CapsuleCollider>(); if (bc) Destroy(bc); var bm = new Material(sh); bm.color = new(1f, 0.55f, 0.05f, 0.6f); bm.SetColor("_BaseColor", bm.color); bm.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f) * 0.8f); bm.EnableKeyword("_EMISSION"); _beam.GetComponent<MeshRenderer>().material = bm; }
        void TickDrill() { if (!_drill) return; float dy = AreaMin.y - CurrentDepth - 0.5f; var tp = new Vector3(AreaMin.x + _cx + 0.5f, dy, AreaMin.z + _cz + 0.5f); _drill.transform.position = tp; _drill.transform.Rotate(Vector3.up, 220f * Time.deltaTime); if (_beam) { _beam.transform.position = tp + Vector3.down * 0.35f; _beam.transform.Rotate(Vector3.up, -180f * Time.deltaTime); } }

        // ═══ MINING ════════════════════════════════════════
        void MineOne() { if (CurrentDepth >= MaxDepth) { Phase = QuarryPhase.Complete; if (_drill) Destroy(_drill); if (_beam) Destroy(_beam); _drill = _beam = null; return; } EnsureOutput(); bool sp = false; for (int i = 0; i < _output.Size; i++) if (_output.GetSlot(i).IsEmpty) { sp = true; break; } if (!sp) { var hs = Physics.OverlapSphere(transform.position, 1.6f); foreach (var c in hs) { if (c.gameObject == gameObject) continue; var p = c.GetComponent<ItemPipe>(); if (p && p.GetInputCapacity(null) > 0) { sp = true; break; } } } if (!sp) { IsOutputFull = true; return; } IsOutputFull = false; var t = new Vector3Int(AreaMin.x + _cx, AreaMin.y - CurrentDepth, AreaMin.z + _cz); var v = _world.GetVoxelWorld(t); if (v.material == (byte)MaterialId.Bedrock) { Adv(); return; } if (v.density > VoxelConstants.ISO_LEVEL) { var def = _matReg?.Get(v.material); if (def == null || !def.isMineable || quarryTier >= def.miningTier) { _world.SetVoxelWorld(t, Voxel.Empty); if (def?.dropItem && def.dropAmount > 0) Out(def.dropItem, def.dropAmount); } } Adv(); }
        void Adv() { _cx++; if (_cx >= AreaX) { _cx = 0; _cz++; if (_cz >= AreaZ) { _cz = 0; CurrentDepth++; } } }
        void Out(ItemDefinition it, int n) { int rem = n; var hs = Physics.OverlapSphere(transform.position, 1.6f); foreach (var c in hs) { if (c.gameObject == gameObject) continue; var p = c.GetComponent<ItemPipe>(); if (!p) continue; int a = p.TryInsert(it, Mathf.Min(p.GetInputCapacity(it), rem)); rem -= a; if (rem <= 0) return; } if (rem > 0) { EnsureOutput(); _output.Insert(new(it, rem)); } }
        void EnsureOutput() { if (_output == null) _output = new("Output", outputSlots); else _output.Resize(outputSlots); }
        public void EnsureOutputPublic() => EnsureOutput();
        public void RestoreState(int d, int cx, int cz, int ph, int rl, int sl, int el) { CurrentDepth = d; _cx = cx; _cz = cz; Phase = (QuarryPhase)ph; InstalledRangeLevel = rl; InstalledSpeedLevel = sl; InstalledEfficiencyLevel = el; if (Phase == QuarryPhase.Complete) { if (_drill) Destroy(_drill); if (_beam) Destroy(_beam); } }
        void OnDestroy() { DestroyGhost(); DestroyTape(); HidePlacementPreview(); foreach (var fb in _fSegs) if (fb) Destroy(fb); _fSegs.Clear(); if (_drill) Destroy(_drill); if (_beam) Destroy(_beam); }
        static Material MatE(Color c, float es) { var s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); var m = new Material(s); m.color = c; m.SetColor("_BaseColor", c); m.SetColor("_EmissionColor", c * es); m.EnableKeyword("_EMISSION"); return m; }
    }
}
