// Assets/Scripts/VoxelEngine/Cosmos/SphereWaterShell.cs
//
// Simple, performant water rendering for spherical voxel planets.
//
// Instead of per-chunk water meshes (which break when chunks are body-parented because the
// WaterMeshBuilder computes vertices in world space), this renders a single transparent sphere
// at the body's sea radius. The terrain (which rises above and dips below sea level) naturally
// clips through it — land pokes out, ocean basins fill with blue. This is how many spherical
// voxel games render water: one GPU draw call, no per-chunk fluid mesh overhead.
//
// The shell follows the body's transform (it's a child), fades with distance, and is tinted
// by the sun's water colour from the system template (Phase 5 will wire that).
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Renders a transparent water sphere at the body's sea radius. Attach as a child of the
    /// CelestialBody. One draw call, always correct orientation, no per-chunk dependency.
    /// </summary>
    [RequireComponent(typeof(CelestialBody))]
    [ExecuteAlways]
    public class SphereWaterShell : MonoBehaviour
    {
        [Tooltip("Water colour (deep ocean blue by default).")]
        public Color waterColor = new Color(0.08f, 0.32f, 0.55f, 0.78f);

        [Range(0f, 1f)]
        [Tooltip("Material smoothness (higher = more reflective).")]
        public float smoothness = 0.92f;

        [Range(16, 128)]
        [Tooltip("Sphere mesh resolution (subdivisions). Higher = smoother.")]
        public int resolution = 48;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _mat;
        private float _lastRadius;
        private int _lastResolution;

        private void OnEnable()
        {
            EnsureComponents();
            Rebuild();
        }

        private void OnDisable()
        {
            if (_mesh != null) { DestroyImmediate(_mesh); _mesh = null; }
            if (_mat != null) { DestroyImmediate(_mat); _mat = null; }
        }

        private void Update()
        {
            var body = GetComponentInParent<CelestialBody>();
            if (body == null || body.settings == null) return;
            body.ApplySettings();
            float r = body.SeaRadius;
            if (_mesh == null || Mathf.Abs(r - _lastRadius) > 0.5f || _lastResolution != resolution)
                Rebuild();
        }

        private void EnsureComponents()
        {
            if (_meshFilter == null)  _meshFilter  = GetComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
            if (_meshFilter == null)  _meshFilter  = gameObject.AddComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        private void Rebuild()
        {
            var body = GetComponentInParent<CelestialBody>();
            if (body == null || body.settings == null) return;
            body.ApplySettings();
            float radius = body.SeaRadius;

            // Build an icosphere at the sea radius.
            var verts = new System.Collections.Generic.List<Vector3>(IcosahedronVerts());
            var tris  = new System.Collections.Generic.List<int>(IcosahedronTris());
            int sub = 0;
            while (verts.Count < resolution * resolution && sub < 6)
            {
                Subdivide(verts, tris);
                sub++;
            }
            // Scale to sea radius.
            for (int i = 0; i < verts.Count; i++)
                verts[i] = verts[i].normalized * radius;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "SphereWaterShell" };
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (_meshFilter != null) _meshFilter.sharedMesh = _mesh;

            if (_mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                           ?? Shader.Find("Standard");
                _mat = new Material(shader);
                _mat.name = "Mat_SphereWater_Runtime";
            }
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", waterColor);
            if (_mat.HasProperty("_Color"))     _mat.SetColor("_Color", waterColor);
            if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", smoothness);
            if (_mat.HasProperty("_Metallic"))   _mat.SetFloat("_Metallic", 0f);
            // Transparent surface for URP/Lit.
            if (_mat.HasProperty("_Surface"))
            {
                _mat.SetFloat("_Surface", 1f);
                _mat.SetFloat("_Blend", 0f);
            }
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_ZWrite", 0);
            _mat.renderQueue = 3000;
            if (_meshRenderer != null) _meshRenderer.sharedMaterial = _mat;

            _lastRadius = radius;
            _lastResolution = resolution;
        }

        // ── Icosphere primitives (same as PlanetLodImpostor) ──
        private static System.Collections.Generic.List<Vector3> IcosahedronVerts()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            return new System.Collections.Generic.List<Vector3>
            {
                N(-1,  t,  0), N( 1,  t,  0), N(-1, -t,  0), N( 1, -t,  0),
                N( 0, -1,  t), N( 0,  1,  t), N( 0, -1, -t), N( 0,  1, -t),
                N( t,  0, -1), N( t,  0,  1), N(-t,  0, -1), N(-t,  0,  1),
            };
        }
        private static Vector3 N(float x, float y, float z) => new Vector3(x, y, z).normalized;

        private static System.Collections.Generic.List<int> IcosahedronTris()
        {
            return new System.Collections.Generic.List<int>
            {
                0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            };
        }

        private static void Subdivide(System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<int> tris)
        {
            var cache = new System.Collections.Generic.Dictionary<long, int>();
            var newTris = new System.Collections.Generic.List<int>(tris.Count * 4);
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                Vector3 mid = ((verts[a] + verts[b]) * 0.5f).normalized;
                idx = verts.Count; verts.Add(mid); cache[key] = idx; return idx;
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                newTris.Add(a); newTris.Add(ab); newTris.Add(ca);
                newTris.Add(b); newTris.Add(bc); newTris.Add(ab);
                newTris.Add(c); newTris.Add(ca); newTris.Add(bc);
                newTris.Add(ab); newTris.Add(bc); newTris.Add(ca);
            }
            tris.Clear(); tris.AddRange(newTris);
        }
    }
}
