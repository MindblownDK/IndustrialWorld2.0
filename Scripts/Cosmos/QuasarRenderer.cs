// Assets/Scripts/VoxelEngine/Cosmos/QuasarRenderer.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                    THE QUASAR — maximum visual effort                 ║
// ║                                                                       ║
// ║  A fully layered, billboarded quasar composed of:                      ║
// ║                                                                       ║
// ║  1. OUTER HALO     — soft radial glow (QuasarGlow.shader)             ║
// ║  2. ACCRETION DISC — swirling procedural disc with Doppler beaming    ║
// ║                      photon ring, black hole shadow                   ║
// ║                      (QuasarAccretionDisc.shader)                      ║
// ║  3. POLAR JET (×2) — volumetric flowing jets perpendicular to disc    ║
// ║                      with knotted turbulence (QuasarJet.shader)        ║
// ║                                                                       ║
// ║  All layers billboard toward the viewer so the quasar always looks    ║
// ║  correct from any angle. The structure is pinned to a sky direction   ║
// ║  and positioned far beyond the solar system (visualDistance).         ║
// ║                                                                       ║
// ║  This is the "wow" moment when a player looks up at the night sky.   ║
// ╚══════════════════════════════════════════════════════════════════════╝
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [ExecuteAlways]
    public class QuasarRenderer : MonoBehaviour
    {
        [Header("Settings")]
        public QuasarSettings settings = new QuasarSettings();

        [Tooltip("How far away the quasar appears (metres). Large = small on screen.")]
        public float visualDistance = 12000f;

        [Tooltip("Overall visual scale multiplier.")]
        public float overallScale = 1f;

        [Header("Layer Sizes (relative to overall)")]
        [Range(0.5f, 5f)] public float haloSize = 2.5f;
        [Range(0.5f, 5f)] public float discSize = 1.5f;
        [Range(0.5f, 8f)] public float jetLength = 4f;
        [Range(0.1f, 3f)] public float jetWidth = 0.5f;

        [Header("Animation")]
        [Range(0f, 2f)] public float discSpeed = 0.3f;
        [Range(0f, 2f)] public float jetSpeed = 0.5f;

        // ── Layer objects ──
        private GameObject _haloGO, _discGO, _jet1GO, _jet2GO;
        private Material _haloMat, _discMat, _jetMat;
        private Mesh _quadMesh, _jetMesh;

        private void OnEnable() => EnsureObjects();
        private void OnDisable() => Cleanup();

        private void Update()
        {
            if (!settings.enabled)
            {
                SetAllActive(false);
                return;
            }
            SetAllActive(true);
            EnsureObjects();

            Vector3 viewerPos = GetViewerPosition();
            Vector3 dir = settings.skyDirection.sqrMagnitude > 0.01f
                ? settings.skyDirection.normalized : Vector3.forward;
            Vector3 center = viewerPos + dir * visualDistance;

            // Billboard rotation: face the viewer.
            Quaternion billboard = Quaternion.LookRotation(viewerPos - center, Vector3.up);

            // The disc is tilted within the billboard plane (looks 3D).
            Quaternion discTilt = billboard * Quaternion.Euler(90f * 0.35f, 0, 0);

            float baseSize = visualDistance * 0.06f * settings.apparentSize * overallScale;

            // ── Halo ──
            _haloGO.transform.position = center;
            _haloGO.transform.rotation = billboard;
            _haloGO.transform.localScale = Vector3.one * baseSize * haloSize;

            // ── Accretion disc ──
            _discGO.transform.position = center;
            _discGO.transform.rotation = discTilt;
            _discGO.transform.localScale = new Vector3(baseSize * discSize, baseSize * discSize, 1);

            // ── Polar jets: perpendicular to the disc, pointing "up" and "down" ──
            Vector3 jetAxis = discTilt * Vector3.up;  // perpendicular to the disc plane
            float jLen = baseSize * jetLength;
            float jWid = baseSize * jetWidth;

            // Jet 1 (above the disc).
            Vector3 j1Pos = center + jetAxis * jLen * 0.5f;
            _jet1GO.transform.position = j1Pos;
            // Orient so the mesh's X axis aligns with the jet axis (the jet shader uses X = along).
            _jet1GO.transform.rotation = Quaternion.LookRotation(jetAxis, Vector3.up) * Quaternion.Euler(0, -90, 0);
            _jet1GO.transform.localScale = new Vector3(jLen, jWid, 1);

            // Jet 2 (below the disc, flipped).
            Vector3 j2Pos = center - jetAxis * jLen * 0.5f;
            _jet2GO.transform.position = j2Pos;
            _jet2GO.transform.rotation = Quaternion.LookRotation(-jetAxis, Vector3.up) * Quaternion.Euler(0, -90, 0);
            _jet2GO.transform.localScale = new Vector3(jLen, jWid, 1);

            // ── Push animation params to the shaders ──
            if (_discMat != null)
            {
                _discMat.SetFloat("_TimeScale", discSpeed);
                _discMat.SetFloat("_Brightness", settings.brightness);
                _discMat.SetColor("_CoreColor", settings.coreColor);
            }
            if (_jetMat != null)
            {
                _jetMat.SetFloat("_TimeScale", jetSpeed);
                _jetMat.SetFloat("_Brightness", settings.brightness * 0.8f);
                _jetMat.SetColor("_CoreColor", settings.jetColor);
            }
            if (_haloMat != null)
            {
                _haloMat.SetFloat("_Brightness", settings.brightness * 0.5f);
                _haloMat.SetColor("_InnerColor", settings.coreColor * 0.9f);
                _haloMat.SetColor("_OuterColor", settings.jetColor * 0.6f);
            }
        }

        // ── Setup ──
        private void EnsureObjects()
        {
            if (_haloGO != null) return;

            _quadMesh = CreateQuad();
            _jetMesh = CreateJetQuad();

            // Halo (largest, renders first / behind).
            _haloGO = CreateLayer("Quasar_Halo", _quadMesh);
            _haloMat = CreateMaterial("QuasarGlow");
            _haloGO.GetComponent<MeshRenderer>().sharedMaterial = _haloMat;

            // Accretion disc (the centrepiece).
            _discGO = CreateLayer("Quasar_Disc", _quadMesh);
            _discMat = CreateMaterial("QuasarAccretionDisc");
            _discGO.GetComponent<MeshRenderer>().sharedMaterial = _discMat;

            // Jets (two elongated quads).
            _jet1GO = CreateLayer("Quasar_Jet1", _jetMesh);
            _jetMat = CreateMaterial("QuasarJet");
            _jet1GO.GetComponent<MeshRenderer>().sharedMaterial = _jetMat;

            _jet2GO = CreateLayer("Quasar_Jet2", _jetMesh);
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
                // Fallback — should never happen if shaders are in the project.
                shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
            }
            return new Material(shader) { name = "Mat_" + shaderName };
        }

        // A centered quad (2 triangles), UVs 0..1.
        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "QuasarQuad" };
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
            var mesh = new Mesh { name = "QuasarJetQuad" };
            mesh.vertices = new Vector3[]
            {
                new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0),
                new(-0.5f,  0.5f, 0), new(0.5f,  0.5f, 0),
            };
            // UV: x = 0 at the core side, x = 1 at the tip. y = across.
            mesh.uv = new Vector2[] { new(0,0), new(1,0), new(0,1), new(1,1) };
            mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Vector3 GetViewerPosition()
        {
            // Anchor to the ACTIVE BODY (static in the scene) so the backdrop never moves
            // when the player walks; fall back to the scene origin in deep space.
            var active = GravityProvider.ActiveBody;
            if (active != null) return active.transform.position;
            return Vector3.zero;
        }

        private void SetAllActive(bool active)
        {
            if (_haloGO != null) _haloGO.SetActive(active);
            if (_discGO != null) _discGO.SetActive(active);
            if (_jet1GO != null) _jet1GO.SetActive(active);
            if (_jet2GO != null) _jet2GO.SetActive(active);
        }

        private void Cleanup()
        {
            DestroyImmediate(_haloGO); _haloGO = null;
            DestroyImmediate(_discGO); _discGO = null;
            DestroyImmediate(_jet1GO); _jet1GO = null;
            DestroyImmediate(_jet2GO); _jet2GO = null;
            DestroyImmediate(_haloMat);
            DestroyImmediate(_discMat);
            DestroyImmediate(_jetMat);
            DestroyImmediate(_quadMesh);
            DestroyImmediate(_jetMesh);
        }
    }
}
