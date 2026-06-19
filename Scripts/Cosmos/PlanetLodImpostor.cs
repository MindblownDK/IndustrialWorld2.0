// Assets/Scripts/VoxelEngine/Cosmos/PlanetLodImpostor.cs
//
// Far / space LOD for a spherical body.
//
// From orbit you can't render millions of voxels — you render ONE low-poly sphere. This builds
// that sphere by sampling the SAME SphereDensity field the voxel generator uses, so the LOD
// matches the real continents, oceans and mountain ranges (no "wrong planet" pop when you
// descend). Vertex colours paint ocean/land/peaks; the radius is perturbed so mountains are
// visible from space.
//
// Near the surface the impostor fades out (and ideally hides) once the voxel chunks take over,
// giving a seamless space→surface descent. This is the cheap GPU billboard that makes a 40 km+
// planet affordable: one draw call from orbit, real voxels only where the player is.
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Generation;

namespace VoxelEngine.Cosmos
{
    [RequireComponent(typeof(CelestialBody))]
    [ExecuteAlways]
    public class PlanetLodImpostor : MonoBehaviour
    {
        [Tooltip("MeshFilter that will receive the generated LOD sphere. Auto-created if missing.")]
        public MeshFilter meshFilter;

        [Tooltip("MeshRenderer for the LOD sphere. Auto-created if missing.")]
        public MeshRenderer meshRenderer;

        [Range(16, 256)]
        [Tooltip("Icosphere subdivision level (higher = smoother from space, more verts). 48 is a good default.")]
        public int resolution = 48;

        [Tooltip("Optional biome registry for accurate surface colours. If null a default set is used.")]
        public BiomeRegistry biomeRegistry;

        [Tooltip("Viewer whose altitude drives the LOD fade (usually the player/camera).")]
        public Transform viewer;

        [Tooltip("Altitude (× body radius) above which the LOD is fully visible. Below it fades out.")]
        public float showAboveAltitudeFactor = 0.6f;

        [Tooltip("Altitude (× body radius) below which the LOD is fully hidden (chunks take over).")]
        public float hideBelowAltitudeFactor = 0.12f;

        private Mesh _mesh;
        private NativeArray<BiomeData> _biomes;
        private int _lastResolution;
        private int _lastSeed;

        private void OnEnable()
        {
            EnsureComponents();
            Rebuild();
        }

        private void OnDisable() => Release();

        private void Update()
        {
            var body = GetComponentInParent<CelestialBody>();
            if (body == null || body.settings == null) return;

            // Rebuild if: never built yet, OR resolution/seed changed.
            bool needRebuild = !_biomes.IsCreated ||
                               _lastResolution != resolution ||
                               _lastSeed != body.genParams.seed;
            if (needRebuild) Rebuild();

            UpdateFade(body);
        }

        private void EnsureComponents()
        {
            if (meshFilter == null)  meshFilter  = GetComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshFilter == null)  meshFilter  = gameObject.AddComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }

        private void Rebuild()
        {
            var body = GetComponentInParent<CelestialBody>();
            // Skip gracefully if the body isn't configured yet (can happen during bootstrap
            // activation or in edit mode). Update() will rebuild once it becomes ready.
            if (body == null || body.settings == null) return;
            body.ApplySettings();

            BiomeData[] biomeArr = body.BuildBiomeData(biomeRegistry);
            if (_biomes.IsCreated) _biomes.Dispose();
            _biomes = new NativeArray<BiomeData>(biomeArr.Length, Allocator.Persistent);
            for (int i = 0; i < biomeArr.Length; i++) _biomes[i] = biomeArr[i];

            var (verts, tris, colors) = BuildIcosphere(body, resolution);

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "PlanetLOD" };
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.SetColors(colors);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            if (meshFilter != null) meshFilter.sharedMesh = _mesh;

            _lastResolution = resolution;
            _lastSeed = body.genParams.seed;
        }

        // Build an icosphere (subdivided icosahedron) and sample the density field per vertex.
        private (Vector3[] verts, int[] tris, Color[] colors) BuildIcosphere(CelestialBody body, int targetVerts)
        {
            // Start from an icosahedron and subdivide until we approach the target vertex count.
            var verts = new System.Collections.Generic.List<Vector3>(IcosahedronVerts());
            var tris  = new System.Collections.Generic.List<int>(IcosahedronTris());

            int sub = 0;
            while (verts.Count < targetVerts && sub < 6)
            {
                Subdivide(verts, tris);
                sub++;
            }

            // Normalise every vertex onto the unit sphere, then sample the density field to
            // displace the radius (mountains) and pick a colour (ocean/land/peaks).
            var colors = new Color[verts.Count];
            var prm = body.genParams;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 dir = verts[i].normalized;
                float3 d3 = dir;

                SphereDensity.EvaluateColumn(prm, _biomes, d3, out float surfaceR, out int biomeI);
                float alt = surfaceR - prm.MeanSurfaceRadius;   // metres above/below mean surface
                float disp = alt;                                // world metres

                // Scale displacement so it's visible from space without exceeding the mesh scale.
                verts[i] = dir * (prm.MeanSurfaceRadius + disp);
                colors[i] = ColorFor(alt);
            }

            return (verts.ToArray(), tris.ToArray(), colors);
        }

        private static Color ColorFor(float altMetres)
        {
            if (altMetres < 0f)
            {
                float depth = Mathf.Clamp01(-altMetres / 40f);
                return Color.Lerp(new Color(0.20f, 0.45f, 0.70f), new Color(0.03f, 0.10f, 0.30f), depth);
            }
            if (altMetres < 2f) return new Color(0.80f, 0.74f, 0.52f);
            float h = Mathf.Clamp01(altMetres / 60f);
            Color land = Color.Lerp(new Color(0.30f, 0.55f, 0.25f), new Color(0.50f, 0.40f, 0.28f), h);
            if (h > 0.75f) land = Color.Lerp(land, Color.white, (h - 0.75f) / 0.25f);
            return land;
        }

        private void UpdateFade(CelestialBody body)
        {
            if (meshRenderer == null) return;
            float a = 1f;
            if (viewer != null)
            {
                float alt = body.AltitudeAt(viewer.position);
                float r = body.SurfaceRadius;
                float hi = r * showAboveAltitudeFactor;
                float lo = r * hideBelowAltitudeFactor;
                // Fully visible above `hi`, fully hidden below `lo`, smooth between.
                a = Mathf.Clamp01((alt - lo) / Mathf.Max(0.001f, hi - lo));
            }
            var m = meshRenderer.sharedMaterial;
            if (m == null) return;
            // Fade via the material's alpha (works for the standard URP Lit via _BaseColor).
            if (m.HasProperty("_BaseColor"))
            {
                var c = m.GetColor("_BaseColor"); c.a = a; m.SetColor("_BaseColor", c);
            }
            meshRenderer.enabled = a > 0.01f;
        }

        private void Release()
        {
            if (_biomes.IsCreated) _biomes.Dispose();
            _biomes = default;
        }

        // ── Icosahedron primitives ────────────────────────────────
        private static System.Collections.Generic.List<Vector3> IcosahedronVerts()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            return new System.Collections.Generic.List<Vector3>
            {
                Normalize(-1,  t,  0), Normalize( 1,  t,  0), Normalize(-1, -t,  0), Normalize( 1, -t,  0),
                Normalize( 0, -1,  t), Normalize( 0,  1,  t), Normalize( 0, -1, -t), Normalize( 0,  1, -t),
                Normalize( t,  0, -1), Normalize( t,  0,  1), Normalize(-t,  0, -1), Normalize(-t,  0,  1),
            };
        }
        private static Vector3 Normalize(float x, float y, float z) => new Vector3(x, y, z).normalized;

        private static System.Collections.Generic.List<int> IcosahedronTris()
        {
            return new System.Collections.Generic.List<int>
            {
                0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                1, 5, 9,  5,11, 4, 11,10,2, 10,7, 6,  7, 1, 8,
                3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            };
        }

        // Subdivide every triangle into 4, projecting midpoints onto the unit sphere.
        private static void Subdivide(System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<int> tris)
        {
            var cache = new System.Collections.Generic.Dictionary<long, int>();
            var newTris = new System.Collections.Generic.List<int>(tris.Count * 4);

            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                Vector3 mid = ((verts[a] + verts[b]) * 0.5f).normalized;
                idx = verts.Count;
                verts.Add(mid);
                cache[key] = idx;
                return idx;
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
            tris.Clear();
            tris.AddRange(newTris);
        }
    }
}
