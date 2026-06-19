// Assets/Scripts/VoxelEngine/Cosmos/QuasarRenderer.cs
//
// Renders a GIANT glowing quasar pinned to the deep-space skybox — a purely aesthetic
// background feature from the design brief. It's a billboarded, emissive disc with two
// relativistic jets that glows on the horizon, far beyond the solar system.
//
// The quasar follows the viewer (so it always appears "infinitely far away" like a real
// background object) and uses the QuasarSettings from the solar system template for its
// colour, brightness, and sky direction.
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Renders the background quasar as a glowing billboard. Attach near the SpaceBodyRenderer.
    /// </summary>
    [ExecuteAlways]
    public class QuasarRenderer : MonoBehaviour
    {
        [Tooltip("Quasar settings. If null, uses sensible defaults.")]
        public QuasarSettings settings = new QuasarSettings();

        [Tooltip("Visual distance (how far away the quasar appears).")]
        public float visualDistance = 15000f;

        private GameObject _coreGO;
        private GameObject _jet1GO;
        private GameObject _jet2GO;
        private Material _coreMat;
        private Material _jetMat;

        private void OnEnable() => EnsureObjects();
        private void OnDisable()
        {
            if (_coreGO != null) DestroyImmediate(_coreGO);
            if (_jet1GO != null) DestroyImmediate(_jet1GO);
            if (_jet2GO != null) DestroyImmediate(_jet2GO);
        }

        private void Update()
        {
            if (!settings.enabled) return;
            EnsureObjects();

            // Follow the viewer so the quasar always appears infinitely distant.
            Vector3 viewerPos = GetViewerPosition();
            Vector3 dir = settings.skyDirection.normalized;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            Vector3 center = viewerPos + dir * visualDistance;

            // Core disc: faces the viewer.
            float coreSize = visualDistance * 0.08f * settings.apparentSize;
            _coreGO.transform.position = center;
            _coreGO.transform.localScale = Vector3.one * coreSize;
            _coreGO.transform.rotation = Quaternion.LookRotation(viewerPos - center, Vector3.up);

            // Jets: two elongated planes perpendicular to the disc, pointing "up" and "down".
            Vector3 jetDir = Vector3.Cross(dir, Vector3.up).normalized;
            if (jetDir.sqrMagnitude < 0.01f) jetDir = Vector3.right;
            float jetLen = coreSize * 4f;
            float jetWidth = coreSize * 0.3f;

            Vector3 jet1Pos = center + jetDir * jetLen * 0.5f;
            _jet1GO.transform.position = jet1Pos;
            _jet1GO.transform.localScale = new Vector3(jetWidth, jetLen, 1f);
            _jet1GO.transform.rotation = Quaternion.LookRotation(viewerPos - jet1Pos, jetDir);

            Vector3 jet2Pos = center - jetDir * jetLen * 0.5f;
            _jet2GO.transform.position = jet2Pos;
            _jet2GO.transform.localScale = new Vector3(jetWidth, jetLen, 1f);
            _jet2GO.transform.rotation = Quaternion.LookRotation(viewerPos - jet2Pos, -jetDir);

            // Apply colours + brightness.
            Color coreCol = settings.coreColor * settings.brightness;
            Color jetCol = settings.jetColor * settings.brightness;
            if (_coreMat.HasProperty("_BaseColor")) _coreMat.SetColor("_BaseColor", coreCol);
            if (_coreMat.HasProperty("_Color"))     _coreMat.SetColor("_Color", coreCol);
            if (_jetMat.HasProperty("_BaseColor"))  _jetMat.SetColor("_BaseColor", jetCol);
            if (_jetMat.HasProperty("_Color"))      _jetMat.SetColor("_Color", jetCol);
        }

        private Vector3 GetViewerPosition()
        {
            var pc = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
            if (pc != null) return pc.transform.position;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : transform.position;
        }

        private void EnsureObjects()
        {
            if (_coreGO != null) return;

            // Core: a glowing disc (quad) with emissive material.
            _coreGO = CreateBillboard("Quasar_Core");
            _coreMat = CreateEmissiveMaterial(settings.coreColor, settings.brightness);
            _coreGO.GetComponent<MeshRenderer>().sharedMaterial = _coreMat;

            // Two jets.
            _jet1GO = CreateBillboard("Quasar_Jet1");
            _jetMat = CreateEmissiveMaterial(settings.jetColor, settings.brightness * 0.7f);
            _jet1GO.GetComponent<MeshRenderer>().sharedMaterial = _jetMat;

            _jet2GO = CreateBillboard("Quasar_Jet2");
            _jet2GO.GetComponent<MeshRenderer>().sharedMaterial = _jetMat;
        }

        private static GameObject CreateBillboard(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(null);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            // A simple quad (2 triangles) facing +Z.
            var mesh = new Mesh { name = name + "_mesh" };
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f,  0.5f, 0), new Vector3(0.5f,  0.5f, 0),
            };
            mesh.uv = new Vector2[] { new(0,0), new(1,0), new(0,1), new(1,1) };
            mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
            return go;
        }

        private static Material CreateEmissiveMaterial(Color color, float brightness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Unlit/Color")
                       ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "Mat_Quasar_Runtime";
            Color emit = color * brightness;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", emit);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", emit);
            // Additive blend so it glows on top of the skybox.
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 2500;  // renders after skybox
            return mat;
        }
    }
}
