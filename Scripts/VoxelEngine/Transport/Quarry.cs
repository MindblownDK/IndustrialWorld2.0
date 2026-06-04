// Assets/Scripts/VoxelEngine/Transport/Quarry.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL QUARRY — BuildCraft-inspired automated strip-miner ║
// ║                                                                ║
// ║  • 16×16 default area (configurable via 2 QuarryLandmarks)     ║
// ║  • Mines straight to bedrock — frame shows full pit depth      ║
// ║  • Construction-tape holographic preview before frame build    ║
// ║  • Full 3D steel frame with corner pillars & accent beams      ║
// ║  • Sleek laser drill-head with rotating beam                   ║
// ║                                                                ║
// ║  Design: Dark steel industrial frame + orange accent glow.     ║
// ║  Clean, minimal, premium — per IndustrialWorld guidelines.     ║
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
        [Tooltip("Default square side length when no landmarks are placed.")]
        public int defaultSize = 16;

        [Tooltip("Distance in front of the quarry block where mining starts.")]
        public float forwardOffset = 2f;

        [Header("Mining")]
        [Tooltip("Seconds between each voxel mined.")]
        public float mineInterval = 0.5f;

        [Range(0, 4)] public int quarryTier = 3;

        [Header("Frame")]
        [Tooltip("Seconds between each frame segment placed.")]
        public float frameBuildInterval = 0.06f;

        [Tooltip("Main frame colour — dark steel.")]
        public Color frameColor = new Color(0.18f, 0.19f, 0.22f);

        [Tooltip("Accent glow colour on frame corners — orange hazard.")]
        public Color frameAccentColor = new Color(0.92f, 0.52f, 0.08f, 0.85f);

        [Tooltip("How tall the frame pillars are (visual depth, mining always goes to bedrock).")]
        public float frameHeight = 5f;

        [Header("Construction Tape")]
        [Tooltip("How long the tape preview animates before frame building starts.")]
        public float tapePreviewDuration = 2.5f;

        [Tooltip("Tape colour 1 — warning orange.")]
        public Color tapeColor1 = new Color(0.95f, 0.55f, 0.05f, 0.75f);

        [Tooltip("Tape colour 2 — safety yellow.")]
        public Color tapeColor2 = new Color(0.92f, 0.82f, 0.08f, 0.75f);

        [Header("Landmark Detection")]
        public float landmarkSearchRadius = 64f;

        [Header("Output")]
        public int outputSlots = 6;

        // ── Runtime State ──────────────────────────────────────────
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
        public int CursorX => _cursorX;
        public int CursorZ => _cursorZ;
        public bool IsOutputFull { get; private set; }

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

        // ── Visuals ────────────────────────────────────────────────
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
            public bool isAccent; // orange glow vs dark steel
        }

        // ── Lifecycle ──────────────────────────────────────────────
        private void Awake() => EnsureOutput();

        private void Start()
        {
            _world = VoxelWorld.Instance;
            _matReg = _world != null ? _world.materialRegistry : null;
            _power = GetComponent<PowerConsumer>();

            if (_world == null)
            {
                Debug.LogWarning("[Quarry] No VoxelWorld.Instance — disabling.");
                enabled = false;
                return;
            }

            _originVoxel = _world.WorldToVoxel(transform.position);
            DetectLandmarksOrDefault();
            CalculateMaxDepth();
            CreateGhostPreview();
            _initialized = true;

            Debug.Log($"[Quarry] Initialized. Area={AreaX}×{AreaZ}, Depth={MaxDepth}, " +
                      $"Origin={_originVoxel}");
        }

        private void Update()
        {
            if (!_initialized) return;
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
                    if (_tapeTimer >= tapePreviewDuration)
                    {
                        DestroyTape();
                        EnterFramePhase();
                    }
                    break;

                case QuarryPhase.BuildingFrame:
                    if (!hasPower) return;
                    _frameBuildTimer += Time.deltaTime;
                    if (_frameBuildTimer >= frameBuildInterval)
                    {
                        _frameBuildTimer = 0f;
                        if (_frameBuildIndex < _framePlan.Count)
                        {
                            var seg = _framePlan[_frameBuildIndex++];
                            PlaceFrameSegment(seg);
                        }
                        else
                        {
                            Phase = QuarryPhase.Mining;
                            CreateDrillHead();
                            Debug.Log("[Quarry] Frame complete — mining started.");
                        }
                    }
                    break;

                case QuarryPhase.Mining:
                    if (!hasPower) return;
                    _mineTimer += Time.deltaTime;
                    if (_mineTimer >= mineInterval) { _mineTimer -= mineInterval; MineNextVoxel(); }
                    UpdateDrillHead();
                    break;

                case QuarryPhase.Complete:
                    if (_drillHead != null) { Destroy(_drillHead); _drillHead = null; }
                    if (_drillBeam != null) { Destroy(_drillBeam); _drillBeam = null; }
                    break;
            }
        }

        // ── Phase Transitions ──────────────────────────────────────
        private void EnterTapePhase()
        {
            Phase = QuarryPhase.TapeFrame;
            _tapeTimer = 0f;
            DestroyGhost();
            CreateTapePreview();
            Debug.Log("[Quarry] Construction tape deployed.");
        }

        private void EnterFramePhase()
        {
            Phase = QuarryPhase.BuildingFrame;
            PrepareFramePlan();
            _frameBuildIndex = 0;
            _frameBuildTimer = 0f;
            Debug.Log("[Quarry] Building 3D frame...");
        }

        // ── Area Setup ─────────────────────────────────────────────
        private void DetectLandmarksOrDefault()
        {
            // Find all landmarks in the world using the static registry.
            var allLandmarks = QuarryLandmark.GetAllLandmarks();
            QuarryLandmark lm1 = null, lm2 = null;
            float best1 = landmarkSearchRadius * landmarkSearchRadius;
            float best2 = best1;

            foreach (var lm in allLandmarks)
            {
                if (lm == null || !lm.IsAvailable) continue;
                float d = (lm.transform.position - transform.position).sqrMagnitude;
                if (d > landmarkSearchRadius * landmarkSearchRadius) continue; // out of range

                if (d < best1)
                {
                    lm2 = lm1; best2 = best1;
                    lm1 = lm; best1 = d;
                }
                else if (d < best2)
                {
                    lm2 = lm; best2 = d;
                }
            }

            if (lm1 != null && lm2 != null)
            {
                // Two landmarks found — define custom rectangle.
                var p1 = _world.WorldToVoxel(lm1.transform.position);
                var p2 = _world.WorldToVoxel(lm2.transform.position);

                // Surface Y is the higher of the two landmark positions.
                int surfaceY = Mathf.Max(p1.y, p2.y);

                AreaMin = new Vector3Int(
                    Mathf.Min(p1.x, p2.x), surfaceY,
                    Mathf.Min(p1.z, p2.z));
                AreaMax = new Vector3Int(
                    Mathf.Max(p1.x, p2.x), surfaceY,
                    Mathf.Max(p1.z, p2.z));

                lm1.SetLinked(true, this);
                lm2.SetLinked(true, this);

                Debug.Log($"[Quarry] Landmark-defined area: {AreaMin} → {AreaMax}");
            }
            else
            {
                // No landmarks — default 16×16 area in front of quarry.
                ComputeDefaultArea();
                Debug.Log($"[Quarry] Default {defaultSize}×{defaultSize} area: {AreaMin} → {AreaMax}");
            }

            AreaX = Mathf.Abs(AreaMax.x - AreaMin.x) + 1;
            AreaZ = Mathf.Abs(AreaMax.z - AreaMin.z) + 1;
        }

        private void ComputeDefaultArea()
        {
            // Determine which cardinal direction the quarry is facing.
            Vector3 forward = transform.forward;
            Vector3Int fwd = SnapToCardinal(forward);

            // Ensure we have a valid forward direction.
            if (fwd.x == 0 && fwd.z == 0) fwd = new Vector3Int(1, 0, 0);

            // The mining area starts `forwardOffset` voxels in front of the quarry
            // and extends `defaultSize` voxels forward and `defaultSize` to the sides.
            int surfaceY = _originVoxel.y;

            // The "near-left" corner (closest to quarry, left side from quarry's view).
            Vector3Int perpendicular = new Vector3Int(fwd.z, 0, -fwd.x); // rotate 90° clockwise
            Vector3Int nearCenter = _originVoxel + fwd * (int)forwardOffset;

            // The area rectangle: from near-left to far-right.
            // For a 16×16 area, we go 16 forward and 16 right.
            AreaMin = new Vector3Int(
                Mathf.Min(nearCenter.x, nearCenter.x + fwd.x * defaultSize),
                surfaceY,
                Mathf.Min(nearCenter.z, nearCenter.z + fwd.z * defaultSize));
            AreaMax = new Vector3Int(
                Mathf.Max(nearCenter.x, nearCenter.x + fwd.x * defaultSize),
                surfaceY,
                Mathf.Max(nearCenter.z, nearCenter.z + fwd.z * defaultSize));

            // Now extend the "width" using the perpendicular axis.
            // The area is fwd × perp, centered on the quarry's forward line.
            int halfWidth = defaultSize / 2;
            Vector3Int perpOffset = perpendicular * halfWidth;

            // Rebuild with proper centering: the area extends `halfWidth` to
            // both sides of the quarry's forward axis.
            Vector3Int start = _originVoxel + fwd * (int)forwardOffset - perpOffset;
            Vector3Int end = start + fwd * defaultSize + perpendicular * defaultSize;

            AreaMin = new Vector3Int(
                Mathf.Min(start.x, end.x), surfaceY,
                Mathf.Min(start.z, end.z));
            AreaMax = new Vector3Int(
                Mathf.Max(start.x, end.x) - 1, surfaceY,
                Mathf.Max(start.z, end.z) - 1);

            // Clamp area to valid sizes.
            AreaX = Mathf.Abs(AreaMax.x - AreaMin.x) + 1;
            AreaZ = Mathf.Abs(AreaMax.z - AreaMin.z) + 1;
        }

        private Vector3Int SnapToCardinal(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), az = Mathf.Abs(v.z);
            if (ax >= az)
                return new Vector3Int(Mathf.RoundToInt(Mathf.Sign(v.x)), 0, 0);
            else
                return new Vector3Int(0, 0, Mathf.RoundToInt(Mathf.Sign(v.z)));
        }

        private void CalculateMaxDepth()
        {
            // Mine from surface down to bedrock (y <= 2). Bedrock is unbreakable
            // so we stop one layer above it (y = 3).
            int bedrockTop = 3;
            MaxDepth = Mathf.Max(1, AreaMin.y - bedrockTop);
        }

        // ── Ghost Preview ──────────────────────────────────────────
        private void CreateGhostPreview()
        {
            _ghostObj = new GameObject("QuarryGhost");
            float y = AreaMin.y;
            float bottomY = AreaMin.y - frameHeight;

            Color gc = new Color(1f, 0.8f, 0.2f, 0.5f); // gold translucent

            // Top rectangle perimeter.
            Vector3 v0 = new Vector3(AreaMin.x, y, AreaMin.z);
            Vector3 v1 = new Vector3(AreaMax.x + 1, y, AreaMin.z);
            Vector3 v2 = new Vector3(AreaMax.x + 1, y, AreaMax.z + 1);
            Vector3 v3 = new Vector3(AreaMin.x, y, AreaMax.z + 1);

            AddGhostLine(v0, v1, gc);
            AddGhostLine(v1, v2, gc);
            AddGhostLine(v2, v3, gc);
            AddGhostLine(v3, v0, gc);

            // Vertical corner posts.
            AddGhostLine(v0, new Vector3(v0.x, bottomY, v0.z), gc);
            AddGhostLine(v1, new Vector3(v1.x, bottomY, v1.z), gc);
            AddGhostLine(v2, new Vector3(v2.x, bottomY, v2.z), gc);
            AddGhostLine(v3, new Vector3(v3.x, bottomY, v3.z), gc);

            // Bottom rectangle.
            Vector3 b0 = new Vector3(AreaMin.x, bottomY, AreaMin.z);
            Vector3 b1 = new Vector3(AreaMax.x + 1, bottomY, AreaMin.z);
            Vector3 b2 = new Vector3(AreaMax.x + 1, bottomY, AreaMax.z + 1);
            Vector3 b3 = new Vector3(AreaMin.x, bottomY, AreaMax.z + 1);

            AddGhostLine(b0, b1, gc);
            AddGhostLine(b1, b2, gc);
            AddGhostLine(b2, b3, gc);
            AddGhostLine(b3, b0, gc);
        }

        private void AddGhostLine(Vector3 from, Vector3 to, Color color)
        {
            var go = new GameObject("GL");
            go.transform.SetParent(_ghostObj.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPositions(new[] { from, to });
            lr.startWidth = 0.06f; lr.endWidth = 0.06f;
            lr.startColor = color; lr.endColor = color;
            lr.useWorldSpace = true;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Sprites/Default");
            lr.material = new Material(sh) { color = color };
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void DestroyGhost()
        {
            if (_ghostObj != null) { Destroy(_ghostObj); _ghostObj = null; }
        }

        // ── Construction Tape ──────────────────────────────────────
        private void CreateTapePreview()
        {
            _tapeObj = new GameObject("QuarryTape");
            float y = AreaMin.y + 0.1f;

            Vector3 v0 = new Vector3(AreaMin.x, y, AreaMin.z);
            Vector3 v1 = new Vector3(AreaMax.x + 1, y, AreaMin.z);
            Vector3 v2 = new Vector3(AreaMax.x + 1, y, AreaMax.z + 1);
            Vector3 v3 = new Vector3(AreaMin.x, y, AreaMax.z + 1);

            // Build alternating tape segments along each edge.
            BuildTapeEdge(v0, v1, _tapeObj.transform); // near edge
            BuildTapeEdge(v1, v2, _tapeObj.transform); // right edge
            BuildTapeEdge(v2, v3, _tapeObj.transform); // far edge
            BuildTapeEdge(v3, v0, _tapeObj.transform); // left edge

            // Corner posts (vertical tape markers).
            float postHeight = 2.5f;
            BuildTapePost(v0, postHeight, _tapeObj.transform);
            BuildTapePost(v1, postHeight, _tapeObj.transform);
            BuildTapePost(v2, postHeight, _tapeObj.transform);
            BuildTapePost(v3, postHeight, _tapeObj.transform);
        }

        private void BuildTapeEdge(Vector3 from, Vector3 to, Transform parent)
        {
            Vector3 dir = (to - from).normalized;
            float length = Vector3.Distance(from, to);
            int segments = Mathf.CeilToInt(length / 0.5f); // 0.5m tape segments
            float segLen = length / segments;

            for (int i = 0; i < segments; i++)
            {
                Vector3 pos = from + dir * (i * segLen + segLen * 0.5f);
                bool odd = (i % 2) == 0;
                Color c = odd ? tapeColor1 : tapeColor2;

                var go = CreateTapeSegment($"Tape_{i}", pos, parent);
                // Align along the edge direction.
                if (Mathf.Abs(dir.x) > 0.5f)
                    go.transform.localScale = new Vector3(segLen * 0.95f, 0.04f, 0.08f);
                else
                    go.transform.localScale = new Vector3(0.08f, 0.04f, segLen * 0.95f);

                var mr = go.GetComponent<MeshRenderer>();
                mr.material = CreateEmissiveMaterial(c, 0.6f);
            }
        }

        private void BuildTapePost(Vector3 basePos, float height, Transform parent)
        {
            var go = CreateTapeSegment("TapePost", basePos + Vector3.up * height * 0.5f, parent);
            go.transform.localScale = new Vector3(0.1f, height, 0.1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.material = CreateEmissiveMaterial(tapeColor1, 0.8f);

            // Small glowing orb at the top.
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "TapeOrb";
            orb.transform.SetParent(parent, false);
            orb.transform.position = basePos + Vector3.up * height;
            orb.transform.localScale = Vector3.one * 0.2f;
            Destroy(orb.GetComponent<Collider>());
            orb.GetComponent<MeshRenderer>().material = CreateEmissiveMaterial(
                new Color(0.95f, 0.55f, 0.05f, 0.9f), 0.9f);
        }

        private GameObject CreateTapeSegment(string name, Vector3 pos, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        private void UpdateTapeFlicker()
        {
            if (_tapeObj == null) return;
            _tapeFlickerTimer += Time.deltaTime;
            if (_tapeFlickerTimer > 0.15f)
            {
                _tapeFlickerTimer = 0f;
                _tapeFlickerState = !_tapeFlickerState;
                float alpha = _tapeFlickerState ? 1f : 0.5f;

                foreach (var mr in _tapeObj.GetComponentsInChildren<MeshRenderer>())
                {
                    Color c = mr.material.color;
                    c.a = alpha * (mr.material.name.Contains("TapeOrb") ? 0.9f : 0.75f);
                    mr.material.color = c;
                }
            }
        }

        private void DestroyTape()
        {
            if (_tapeObj != null) { Destroy(_tapeObj); _tapeObj = null; }
        }

        // ── 3D Frame Building ──────────────────────────────────────
        private void PrepareFramePlan()
        {
            _framePlan.Clear();
            float yTop = AreaMin.y + 0.05f;           // top surface
            float yBottom = AreaMin.y - frameHeight;  // bottom of pit

            Vector3 v00 = new Vector3(AreaMin.x, 0, AreaMin.z);
            Vector3 v10 = new Vector3(AreaMax.x + 1, 0, AreaMin.z);
            Vector3 v11 = new Vector3(AreaMax.x + 1, 0, AreaMax.z + 1);
            Vector3 v01 = new Vector3(AreaMin.x, 0, AreaMax.z + 1);

            // Four corner pillars (from bottom to top).
            float pillarHeight = yTop - yBottom + 0.3f;
            float pillarCenterY = (yTop + yBottom) * 0.5f;

            AddPillar(v00, pillarCenterY, pillarHeight);
            AddPillar(v10, pillarCenterY, pillarHeight);
            AddPillar(v11, pillarCenterY, pillarHeight);
            AddPillar(v01, pillarCenterY, pillarHeight);

            // Top horizontal beams (connect pillars at top).
            AddBeam(v00, v10, yTop);
            AddBeam(v10, v11, yTop);
            AddBeam(v11, v01, yTop);
            AddBeam(v01, v00, yTop);

            // Bottom horizontal beams (connect pillars at bottom).
            AddBeam(v00, v10, yBottom);
            AddBeam(v10, v11, yBottom);
            AddBeam(v11, v01, yBottom);
            AddBeam(v01, v00, yBottom);

            // Accent corner lights (small glowing cubes at each top corner).
            AddAccentLight(v00, yTop);
            AddAccentLight(v10, yTop);
            AddAccentLight(v11, yTop);
            AddAccentLight(v01, yTop);
        }

        private void AddPillar(Vector3 basePos, float centerY, float height)
        {
            _framePlan.Add(new FrameSegment
            {
                position = new Vector3(basePos.x, centerY, basePos.z),
                scale = new Vector3(0.16f, height, 0.16f),
                isAccent = false
            });
        }

        private void AddBeam(Vector3 from, Vector3 to, float y)
        {
            Vector3 mid = (from + to) * 0.5f;
            float length = Vector3.Distance(from, to);

            Vector3 scale;
            if (Mathf.Abs(from.x - to.x) > 0.01f)
                scale = new Vector3(length, 0.12f, 0.1f);
            else
                scale = new Vector3(0.1f, 0.12f, length);

            _framePlan.Add(new FrameSegment
            {
                position = new Vector3(mid.x, y, mid.z),
                scale = scale,
                isAccent = false
            });
        }

        private void AddAccentLight(Vector3 pos, float y)
        {
            _framePlan.Add(new FrameSegment
            {
                position = new Vector3(pos.x, y, pos.z),
                scale = new Vector3(0.22f, 0.22f, 0.22f),
                isAccent = true
            });
        }

        private void PlaceFrameSegment(FrameSegment seg)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = seg.isAccent ? "QFrameAccent" : "QFrame";
            go.transform.position = seg.position;
            go.transform.localScale = seg.scale;

            var r = go.GetComponent<MeshRenderer>();
            if (seg.isAccent)
            {
                // Glowing accent block.
                r.material = CreateEmissiveMaterial(frameAccentColor, 0.7f);
            }
            else
            {
                // Dark steel frame.
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var m = new Material(sh) { color = frameColor };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", frameColor);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.9f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
                r.material = m;
            }

            var col = go.GetComponent<BoxCollider>();
            if (col != null) Destroy(col);

            _frameSegments.Add(go);
        }

        // ── Drill Head ─────────────────────────────────────────────
        private void CreateDrillHead()
        {
            // Cylinder drill body.
            _drillHead = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _drillHead.name = "DrillHead";
            _drillHead.transform.localScale = new Vector3(0.5f, 0.25f, 0.5f);
            var c = _drillHead.GetComponent<CapsuleCollider>();
            if (c) Destroy(c);

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.color = new Color(0.85f, 0.55f, 0.08f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.8f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
            if (m.HasProperty("_EmissionColor"))
                m.SetColor("_EmissionColor", new Color(0.85f, 0.45f, 0.05f) * 0.4f);
            m.EnableKeyword("_EMISSION");
            _drillHead.GetComponent<MeshRenderer>().material = m;

            // Laser beam from drill head downward.
            _drillBeam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _drillBeam.name = "DrillBeam";
            _drillBeam.transform.localScale = new Vector3(0.08f, 0.5f, 0.08f);
            var bc = _drillBeam.GetComponent<CapsuleCollider>();
            if (bc) Destroy(bc);

            var beamMat = new Material(sh);
            beamMat.color = new Color(1f, 0.55f, 0.05f, 0.6f);
            if (beamMat.HasProperty("_BaseColor"))
                beamMat.SetColor("_BaseColor", beamMat.color);
            if (beamMat.HasProperty("_EmissionColor"))
                beamMat.SetColor("_EmissionColor", new Color(1f, 0.5f, 0f) * 0.8f);
            beamMat.EnableKeyword("_EMISSION");
            _drillBeam.GetComponent<MeshRenderer>().material = beamMat;
        }

        private void UpdateDrillHead()
        {
            if (_drillHead == null) return;

            float dy = AreaMin.y - CurrentDepth - 0.5f;
            Vector3 targetPos = new Vector3(
                AreaMin.x + _cursorX + 0.5f,
                dy,
                AreaMin.z + _cursorZ + 0.5f);

            _drillHead.transform.position = targetPos;
            _drillHead.transform.Rotate(Vector3.up, 220f * Time.deltaTime);

            if (_drillBeam != null)
            {
                _drillBeam.transform.position = targetPos + Vector3.down * 0.35f;
                _drillBeam.transform.Rotate(Vector3.up, -180f * Time.deltaTime);
            }
        }

        // ── Mining ─────────────────────────────────────────────────
        private void MineNextVoxel()
        {
            if (CurrentDepth >= MaxDepth)
            {
                Phase = QuarryPhase.Complete;
                if (_drillHead != null) Destroy(_drillHead);
                if (_drillBeam != null) Destroy(_drillBeam);
                _drillHead = null;
                _drillBeam = null;
                Debug.Log("[Quarry] Mining complete.");
                return;
            }

            EnsureOutput();
            bool hasOutputSpace = false;
            for (int i = 0; i < _output.Size; i++)
                if (_output.GetSlot(i).IsEmpty) { hasOutputSpace = true; break; }

            if (!hasOutputSpace)
            {
                var pipeHits = Physics.OverlapSphere(transform.position, 1.6f);
                foreach (var col in pipeHits)
                {
                    if (col.gameObject == gameObject) continue;
                    var pipe = col.GetComponent<ItemPipe>();
                    if (pipe != null && pipe.GetInputCapacity(null) > 0)
                    { hasOutputSpace = true; break; }
                }
            }

            if (!hasOutputSpace) { IsOutputFull = true; return; }
            IsOutputFull = false;

            Vector3Int target = new Vector3Int(
                AreaMin.x + _cursorX,
                AreaMin.y - CurrentDepth,
                AreaMin.z + _cursorZ);

            Voxel v = _world.GetVoxelWorld(target);

            // Stop at bedrock — skip this column.
            if (v.material == (byte)MaterialId.Bedrock)
            {
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
                _cursorX = 0;
                _cursorZ++;
                if (_cursorZ >= AreaZ)
                {
                    _cursorZ = 0;
                    CurrentDepth++;
                }
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
                rem -= acc;
                if (rem <= 0) return;
            }
            if (rem > 0)
            {
                EnsureOutput();
                _output.Insert(new ItemStack(item, rem));
            }
        }

        private void EnsureOutput()
        {
            if (_output == null)
                _output = new ItemContainer("Quarry Output", outputSlots);
            else
                _output.Resize(outputSlots);
        }

        public void EnsureOutputPublic() => EnsureOutput();

        // ── Save / Restore ─────────────────────────────────────────
        public void RestoreState(int depth, int cx, int cz, int phase)
        {
            CurrentDepth = depth;
            _cursorX = cx;
            _cursorZ = cz;
            Phase = (QuarryPhase)phase;
            if (Phase == QuarryPhase.Complete)
            {
                if (_drillHead != null) Destroy(_drillHead);
                if (_drillBeam != null) Destroy(_drillBeam);
            }
        }

        private void OnDestroy()
        {
            DestroyGhost();
            DestroyTape();
            foreach (var fb in _frameSegments) if (fb) Destroy(fb);
            _frameSegments.Clear();
            if (_drillHead) Destroy(_drillHead);
            if (_drillBeam) Destroy(_drillBeam);
        }

        // ── Material Helpers ───────────────────────────────────────
        private static Material CreateEmissiveMaterial(Color color, float emissiveScale)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_EmissionColor"))
                m.SetColor("_EmissionColor", color * emissiveScale);
            m.EnableKeyword("_EMISSION");
            return m;
        }
    }
}
