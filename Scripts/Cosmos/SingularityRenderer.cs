// Assets/Scripts/VoxelEngine/Cosmos/SingularityRenderer.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║           SINGULARITY RENDERER — real body visuals (Phase 5)         ║
// ║                                                                      ║
// ║  One component per singularity (black hole / quasar). When the       ║
// ║  player is inside the render window, this draws the REAL body at     ║
// ║  its REAL cosmic position (via SpaceOrigin):                         ║
// ║                                                                      ║
// ║   1. EVENT HORIZON  — pure-black sphere with a lensed fresnel rim    ║
// ║   2. ACCRETION DISC — swirling polar-UV disc, Doppler-beamed,        ║
// ║                       photon ring at the inner edge (rotates)        ║
// ║   3. HALO          — soft additive glow billboarded to the viewer    ║
// ║   4. POLAR JETS ×2 — quasar only: relativistic beams along the disc  ║
// ║                       axis, billboarded around the axis              ║
// ║                                                                      ║
// ║  Beyond the window the SingularityBeacons renderer carries the view  ║
// ║  (direction-pinned, true angular size) so the body is never a fake   ║
// ║  skybox ornament — it is always AT its real location.                ║
// ╚══════════════════════════════════════════════════════════════════════╝
using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [DisallowMultipleComponent]
    public class SingularityRenderer : MonoBehaviour
    {
        [Tooltip("The registry instance this renderer visualises (assigned by CosmosBootstrap).")]
        public SingularityInstance instance;

        [Tooltip("Within this cosmic distance (km) the real geometry renders; beyond it the beacons carry the view.")]
        public float renderWindowKm = 60000f;

        // ── Layer objects ──
        private GameObject _horizonGO, _discGO, _haloGO, _jet1GO, _jet2GO;
        private Material _horizonMat, _discMat, _haloMat, _jetMat;
        private Mesh _sphereMesh, _discMesh, _jetMesh, _quadMesh;
        private float _discSpin;

        private void OnEnable() => EnsureObjects();
        private void OnDisable() => Cleanup();

        private void Update()
        {
            if (instance == null) { SetAllActive(false); return; }
            var registry = CosmicRegistry.Instance;
            var origin = SpaceOrigin.Instance;
            if (registry == null || !registry.IsReady || origin == null) { SetAllActive(false); return; }

            Transform viewer = ResolveViewer(origin);
            if (viewer == null) { SetAllActive(false); return; }

            double3 viewerCosmic = origin.GetCosmicKm(viewer.position);
            double distKm = math.length(instance.positionKmD - viewerCosmic);
            if (distKm > renderWindowKm || double.IsNaN(distKm)) { SetAllActive(false); return; }

            EnsureObjects();
            SetAllActive(true);

            Vector3 scenePos = origin.GetScenePos(instance.positionKmD);
            if (float.IsNaN(scenePos.x) || float.IsNaN(scenePos.y) || float.IsNaN(scenePos.z))
            {
                SetAllActive(false);
                return;
            }

            float horizonScale = (float)(instance.eventHorizonKm * 1000d);
            float discScale = instance.discOuterRadiusKm * 1000f;
            Vector3 axis = instance.discAxis.sqrMagnitude > 0.001f ? instance.discAxis.normalized : Vector3.up;
            bool isQuasar = instance.kind == SingularityKind.Quasar;
            float brightness = isQuasar && instance.quasarSettings != null
                ? instance.quasarSettings.brightness
                : instance.blackHoleSettings != null ? instance.blackHoleSettings.brightness : 1.5f;

            // ── Event horizon ──
            _horizonGO.transform.position = scenePos;
            _horizonGO.transform.localScale = Vector3.one * horizonScale * 2f;

            // ── Accretion disc: plane ⊥ axis, slowly spinning ──
            _discSpin += Time.deltaTime * (isQuasar && instance.quasarSettings != null
                ? instance.quasarSettings.discSpeed
                : instance.blackHoleSettings != null ? instance.blackHoleSettings.discSpeed : 0.3f);
            _discGO.transform.position = scenePos;
            _discGO.transform.rotation = Quaternion.AngleAxis(_discSpin * Mathf.Rad2Deg, axis);
            _discGO.transform.localScale = Vector3.one * discScale * 2f;

            // ── Halo: billboarded glow at the real position ──
            Vector3 toViewer = viewer.position - scenePos;
            _haloGO.transform.position = scenePos;
            _haloGO.transform.rotation = toViewer.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(toViewer, Vector3.up)
                : Quaternion.identity;
            _haloGO.transform.localScale = Vector3.one * discScale * 5.2f;

            // ── Polar jets (quasar only) ──
            if (isQuasar && instance.quasarSettings != null)
            {
                var q = instance.quasarSettings;
                float jLen = q.jetLengthKm * 1000f;
                float jWid = Mathf.Max(2f, q.jetVisualWidthKm) * 1000f;
                PlaceJet(_jet1GO, scenePos, axis, jLen, jWid, toViewer);
                PlaceJet(_jet2GO, scenePos, -axis, jLen, jWid, toViewer);
            }
            if (_jet1GO != null) _jet1GO.SetActive(isQuasar);
            if (_jet2GO != null) _jet2GO.SetActive(isQuasar);

            // ── Material parameters ──
            if (_discMat != null)
            {
                _discMat.SetFloat("_TimeScale", isQuasar && instance.quasarSettings != null
                    ? instance.quasarSettings.discSpeed * 1.6f
                    : instance.blackHoleSettings != null ? instance.blackHoleSettings.discSpeed * 1.6f : 0.5f);
                _discMat.SetFloat("_Brightness", brightness);
                _discMat.SetFloat("_PhotonRingPower", isQuasar && instance.quasarSettings != null
                    ? instance.quasarSettings.photonRingBrightness
                    : instance.blackHoleSettings != null ? instance.blackHoleSettings.photonRingBrightness : 2.2f);
                if (isQuasar && instance.quasarSettings != null)
                {
                    _discMat.SetColor("_CoreColor", instance.quasarSettings.coreColor);
                    _discMat.SetColor("_MidColor", instance.quasarSettings.midColor);
                    _discMat.SetColor("_OuterColor", instance.quasarSettings.outerColor);
                }
                else if (instance.blackHoleSettings != null)
                {
                    _discMat.SetColor("_CoreColor", instance.blackHoleSettings.coreColor);
                    _discMat.SetColor("_MidColor", instance.blackHoleSettings.midColor);
                    _discMat.SetColor("_OuterColor", instance.blackHoleSettings.outerColor);
                }
            }
            if (_haloMat != null)
            {
                _haloMat.SetFloat("_Brightness", brightness * 0.45f);
                Color inner = isQuasar && instance.quasarSettings != null
                    ? instance.quasarSettings.coreColor
                    : instance.blackHoleSettings != null ? instance.blackHoleSettings.coreColor : Color.white;
                Color outer = isQuasar && instance.quasarSettings != null
                    ? instance.quasarSettings.outerColor
                    : instance.blackHoleSettings != null ? instance.blackHoleSettings.outerColor : Color.red;
                _haloMat.SetColor("_InnerColor", inner);
                _haloMat.SetColor("_OuterColor", outer);
            }
            if (_jetMat != null)
            {
                _jetMat.SetFloat("_Brightness", brightness * 0.8f);
                _jetMat.SetFloat("_TimeScale", isQuasar && instance.quasarSettings != null
                    ? instance.quasarSettings.discSpeed * 2.2f : 0.6f);
                Color jet = isQuasar && instance.quasarSettings != null
                    ? instance.quasarSettings.jetColor : new Color(0.4f, 0.6f, 1f, 0.9f);
                _jetMat.SetColor("_CoreColor", jet);
                if (_jetMat.HasProperty("_EdgeColor"))
                    _jetMat.SetColor("_EdgeColor", jet * 0.35f);
            }
            if (_horizonMat != null)
            {
                Color rim = isQuasar && instance.quasarSettings != null
                    ? instance.quasarSettings.coreColor * 0.85f
                    : instance.blackHoleSettings != null ? instance.blackHoleSettings.midColor * 0.9f : new Color(1f, 0.5f, 0.2f);
                _horizonMat.SetColor("_RimColor", rim);
            }
        }

        private void PlaceJet(GameObject jet, Vector3 scenePos, Vector3 axis, float length, float width, Vector3 toViewer)
        {
            if (jet == null) return;
            jet.transform.position = scenePos + axis * (length * 0.5f);
            Vector3 view = toViewer.sqrMagnitude > 0.001f ? toViewer.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(axis, view);
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(axis, Vector3.up);
            Vector3 up = Vector3.Cross(right, axis).normalized;
            // Local X aligns with the jet axis (the shader reads x = along).
            jet.transform.rotation = Quaternion.LookRotation(axis, up) * Quaternion.Euler(0f, -90f, 0f);
            jet.transform.localScale = new Vector3(length, width, 1f);
        }

        // ── Setup ──
        private void EnsureObjects()
        {
            if (_horizonGO != null) return;

            _sphereMesh = CreateIcoSphere(1);
            _discMesh = CreateDiscAnnulus(96);
            _quadMesh = CreateQuad();
            _jetMesh = CreateJetQuad();

            _horizonGO = CreateLayer("Singularity_Horizon", _sphereMesh);
            _horizonMat = CreateMaterial("SingularityHorizon");
            _horizonGO.GetComponent<MeshRenderer>().sharedMaterial = _horizonMat;

            _discGO = CreateLayer("Singularity_Disc", _discMesh);
            _discMat = CreateMaterial("BlackHoleAccretionDisc");
            _discGO.GetComponent<MeshRenderer>().sharedMaterial = _discMat;

            _haloGO = CreateLayer("Singularity_Halo", _quadMesh);
            _haloMat = CreateMaterial("QuasarGlow");
            _haloGO.GetComponent<MeshRenderer>().sharedMaterial = _haloMat;

            _jet1GO = CreateLayer("Singularity_Jet1", _jetMesh);
            _jetMat = CreateMaterial("QuasarJet");
            _jet1GO.GetComponent<MeshRenderer>().sharedMaterial = _jetMat;

            _jet2GO = CreateLayer("Singularity_Jet2", _jetMesh);
            _jet2GO.GetComponent<MeshRenderer>().sharedMaterial = _jetMat;
        }

        private GameObject CreateLayer(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(null);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static Material CreateMaterial(string shaderName)
        {
            var shader = Shader.Find("VoxelEngine/" + shaderName);
            if (shader == null)
            {
                // Fallback — should never happen if the shaders are in the project.
                shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
            }
            return new Material(shader) { name = "Mat_" + shaderName };
        }

        private static Transform ResolveViewer(SpaceOrigin origin)
        {
            if (origin.viewer != null) return origin.viewer;
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        // ── Procedural meshes ─────────────────────────────────────
        private static Mesh CreateIcoSphere(int subdivisions)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] v =
            {
                new Vector3(-1, t, 0).normalized, new Vector3(1, t, 0).normalized,
                new Vector3(-1, -t, 0).normalized, new Vector3(1, -t, 0).normalized,
                new Vector3(0, -1, t).normalized, new Vector3(0, 1, t).normalized,
                new Vector3(0, -1, -t).normalized, new Vector3(0, 1, -t).normalized,
                new Vector3(t, 0, -1).normalized, new Vector3(t, 0, 1).normalized,
                new Vector3(-t, 0, -1).normalized, new Vector3(-t, 0, 1).normalized,
            };
            verts.AddRange(v);
            tris.AddRange(new[]
            {
                0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            });
            var cache = new System.Collections.Generic.Dictionary<long, int>();
            var nt = new System.Collections.Generic.List<int>(tris.Count * 4);
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                Vector3 mid = ((verts[a] + verts[b]) * 0.5f).normalized;
                idx = verts.Count; verts.Add(mid); cache[key] = idx; return idx;
            }
            for (int s = 0; s < Mathf.Max(0, subdivisions); s++)
            {
                nt.Clear(); cache.Clear();
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.Add(a); nt.Add(ab); nt.Add(ca);
                    nt.Add(b); nt.Add(bc); nt.Add(ab);
                    nt.Add(c); nt.Add(ca); nt.Add(bc);
                    nt.Add(ab); nt.Add(bc); nt.Add(ca);
                }
                var swap = tris; tris = nt; nt = swap;
            }

            var mesh = new Mesh { name = "SingularityHorizonMesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Flat annulus (unit radius outer) with POLAR UVs: x = 0 at the inner edge → 1 at
        /// the rim, y = angle 0..1 around. The renderer scales it to the disc's km radius,
        /// and the mesh's inner hole lets the event-horizon sphere show through.
        /// </summary>
        private static Mesh CreateDiscAnnulus(int segments)
        {
            const float InnerFraction = 0.30f;   // fixed mesh hole; runtime inner radius is implied by the horizon scale
            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();

            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                verts.Add(new Vector3(ca * InnerFraction, 0f, sa * InnerFraction));
                verts.Add(new Vector3(ca, 0f, sa));
                uvs.Add(new Vector2(0f, i / (float)segments));
                uvs.Add(new Vector2(1f, i / (float)segments));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }

            var mesh = new Mesh { name = "SingularityDiscMesh" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "SingularityQuad" };
            mesh.vertices = new Vector3[]
            {
                new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0),
                new(-0.5f,  0.5f, 0), new(0.5f,  0.5f, 0),
            };
            mesh.uv = new Vector2[] { new(0,0), new(1,0), new(0,1), new(1,1) };
            mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // An elongated quad for jets: wider along X (the jet axis in the shader).
        private static Mesh CreateJetQuad()
        {
            var mesh = new Mesh { name = "SingularityJetQuad" };
            mesh.vertices = new Vector3[]
            {
                new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0),
                new(-0.5f,  0.5f, 0), new(0.5f,  0.5f, 0),
            };
            mesh.uv = new Vector2[] { new(0,0), new(1,0), new(0,1), new(1,1) };
            mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SetAllActive(bool active)
        {
            if (_horizonGO != null) _horizonGO.SetActive(active);
            if (_discGO != null) _discGO.SetActive(active);
            if (_haloGO != null) _haloGO.SetActive(active);
            if (_jet1GO != null) _jet1GO.SetActive(active);
            if (_jet2GO != null) _jet2GO.SetActive(active);
        }

        private void Cleanup()
        {
            DestroyImmediate(_horizonGO); _horizonGO = null;
            DestroyImmediate(_discGO); _discGO = null;
            DestroyImmediate(_haloGO); _haloGO = null;
            DestroyImmediate(_jet1GO); _jet1GO = null;
            DestroyImmediate(_jet2GO); _jet2GO = null;
            DestroyImmediate(_horizonMat);
            DestroyImmediate(_discMat);
            DestroyImmediate(_haloMat);
            DestroyImmediate(_jetMat);
            DestroyImmediate(_sphereMesh);
            DestroyImmediate(_discMesh);
            DestroyImmediate(_quadMesh);
            DestroyImmediate(_jetMesh);
        }
    }
}
