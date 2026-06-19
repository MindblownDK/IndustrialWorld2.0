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
// Near the surface the impostor fades out (and hides) once the voxel chunks take over,
// giving a seamless space→surface descent. This is the cheap GPU billboard that makes a 40 km+
// planet affordable: one draw call from orbit, real voxels only where the player is.
//
// CRITICAL: the LOD creates its OWN material (NOT the flat-world VoxelTerrain). VoxelTerrain is
// a custom Shader Graph that (a) doesn't support the _BaseColor alpha fade property, so the LOD
// never faded, and (b) may not render vertex colours correctly at planet scale. The LOD material
// uses URP/Unlit (guaranteed vertex-colour × alpha support) so it ALWAYS renders correctly.
using System.Collections.Generic;
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
        [Tooltip("Icosphere subdivision level (higher = smoother from space, more verts).")]
        public int resolution = 48;

        [Tooltip("Optional biome registry for accurate surface colours.")]
        public BiomeRegistry biomeRegistry;

        [Tooltip("Viewer whose altitude drives the LOD fade.")]
        public Transform viewer;

        [Tooltip("Altitude (× body radius) above which the LOD is fully visible.")]
        public float showAboveAltitudeFactor = 0.6f;

        [Tooltip("Altitude (× body radius) below which the LOD is fully hidden.")]
        public float hideBelowAltitudeFactor = 0.12f;

        private Mesh _mesh;
        private Material _lodMaterial;
        private NativeArray<BiomeData> _biomes;
        private int _lastResolution;
        private int _lastSeed;

        private void OnEnable()
        {
            EnsureComponents();
            EnsureMaterial();
            Rebuild();
        }

        private void OnDisable() => Release();

        private void Update()
        {
            var body = GetComponentInParent<CelestialBody>();
            if (body == null || body.settings == null) return;

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

        /// <summary>
        /// Create the LOD's OWN material using a shader guaranteed to render vertex colours AND
        /// support alpha fading. We deliberately do NOT reuse the flat-world VoxelTerrain material
        /// (which is a custom Shader Graph that lacks alpha support and may not render vertex colours
        /// at planet scale → the "purple sphere" bug).
        /// </summary>
        private void EnsureMaterial()
        {
            if (_lodMaterial != null) return;

            // Try shaders in order of preference. URP/Unlit is ideal: it renders vertex colours
            // (multiplied by _BaseColor) AND supports alpha transparency — both critical for the LOD.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Unlit/Color")
                          ?? Shader.Find("Standard");

            _lodMaterial = new Material(shader);
            _lodMaterial.name = "Mat_PlanetLOD_Runtime";

            // White base colour so vertex colours pass through unchanged.
            if (_lodMaterial.HasProperty("_BaseColor"))
                _lodMaterial.SetColor("_BaseColor", Color.white);
            if (_lodMaterial.HasProperty("_Color"))
                _lodMaterial.SetColor("_Color", Color.white);

            // For URP/Lit: switch to transparent surface so alpha fade works.
            if (_lodMaterial.HasProperty("_Surface"))
            {
                _lodMaterial.SetFloat("_Surface", 1f);  // 1 = transparent
                _lodMaterial.SetFloat("_Blend", 0f);    // 0 = alpha blend
            }
            // For Unlit/Color: enable alpha.
            _lodMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lodMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // ZWrite ON so the opaque far-view sphere renders into the depth buffer and is
            // actually VISIBLE (ZWrite=0 made it sort behind opaque geometry → invisible).
            _lodMaterial.SetInt("_ZWrite", 1);

            if (meshRenderer != null) meshRenderer.sharedMaterial = _lodMaterial;
        }

        private void Rebuild()
        {
            var body = GetComponentInParent<CelestialBody>();
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

        private (Vector3[] verts, int[] tris, Color[] colors) BuildIcosphere(CelestialBody body, int targetVerts)
        {
            var verts = new List<Vector3>(IcosahedronVerts());
            var tris  = new List<int>(IcosahedronTris());

            int sub = 0;
            while (verts.Count < targetVerts && sub < 6)
            {
                Subdivide(verts, tris);
                sub++;
            }

            var colors = new Color[verts.Count];
            var prm = body.genParams;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 dir = verts[i].normalized;
                float3 d3 = dir;

                SphereDensity.EvaluateColumn(prm, _biomes, d3, out float surfaceR, out int biomeI);
                float alt = surfaceR - prm.MeanSurfaceRadius;
                verts[i] = dir * (prm.MeanSurfaceRadius + alt);
                float latitude = Mathf.Abs(dir.y);
                colors[i] = ColorFor(alt, latitude);
            }

            return (verts.ToArray(), tris.ToArray(), colors);
        }

        /// <summary>
        /// Surface colour for the LOD sphere based on altitude + latitude.
        /// Phase 3: latitude factor adds polar ice caps (white near poles) so the LOD matches
        /// the real terrain's snow caps.
        /// </summary>
        private static Color ColorFor(float altMetres, float latitude)
        {
            // Polar ice: near the poles, everything is white (ice/snow).
            if (latitude > 0.82f) return new Color(0.92f, 0.95f, 0.98f, 1f);

            if (altMetres < 0f)
            {
                float depth = Mathf.Clamp01(-altMetres / 40f);
                return Color.Lerp(new Color(0.20f, 0.45f, 0.70f, 1f), new Color(0.03f, 0.10f, 0.30f, 1f), depth);
            }
            if (altMetres < 2f) return new Color(0.80f, 0.74f, 0.52f, 1f);
            float h = Mathf.Clamp01(altMetres / 60f);
            // Equatorial = lush green; higher latitudes = browner/cooler.
            float greenness = Mathf.Clamp01(1f - latitude * 1.2f);
            Color lush = new Color(0.26f, 0.55f, 0.22f, 1f);
            Color dry  = new Color(0.50f, 0.45f, 0.28f, 1f);
            Color lowland = Color.Lerp(dry, lush, greenness);
            Color highland = new Color(0.50f, 0.40f, 0.28f, 1f);
            Color land = Color.Lerp(lowland, highland, h);
            // Snow caps on high peaks.
            if (h > 0.7f) land = Color.Lerp(land, Color.white, (h - 0.7f) / 0.3f);
            // Sub-polar regions get partial snow.
            if (latitude > 0.65f) land = Color.Lerp(land, new Color(0.85f, 0.88f, 0.92f, 1f), (latitude - 0.65f) / 0.17f);
            return land;
        }

        private void UpdateFade(CelestialBody body)
        {
            if (meshRenderer == null || _lodMaterial == null) return;
            // The LOD is ALWAYS visible — whole planet visible from surface (like Space Engineers).
            float a = 1f;
            if (viewer != null)
            {
                float alt = body.AltitudeAt(viewer.position);
                float r = body.SurfaceRadius;
                float surfaceFadeStart = r * 0.15f;
                float surfaceFadeEnd = r * 0.02f;
                if (alt < surfaceFadeStart)
                    a = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01((alt - surfaceFadeEnd) / Mathf.Max(0.001f, surfaceFadeStart - surfaceFadeEnd)));
            }
            if (_lodMaterial.HasProperty("_BaseColor"))
            {
                var col = _lodMaterial.GetColor("_BaseColor"); col.a = a; _lodMaterial.SetColor("_BaseColor", col);
            }
            else if (_lodMaterial.HasProperty("_Color"))
            {
                var col = _lodMaterial.GetColor("_Color"); col.a = a; _lodMaterial.SetColor("_Color", col);
            }
            meshRenderer.enabled = true;
        }

        private void Release()
        {
            if (_biomes.IsCreated) _biomes.Dispose();
            _biomes = default;
        }

        // ── Icosahedron primitives ────────────────────────────────
        private static List<Vector3> IcosahedronVerts()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            return new List<Vector3>
            {
                Normalize(-1,  t,  0), Normalize( 1,  t,  0), Normalize(-1, -t,  0), Normalize( 1, -t,  0),
                Normalize( 0, -1,  t), Normalize( 0,  1,  t), Normalize( 0, -1, -t), Normalize( 0,  1, -t),
                Normalize( t,  0, -1), Normalize( t,  0,  1), Normalize(-t,  0, -1), Normalize(-t,  0,  1),
            };
        }
        private static Vector3 Normalize(float x, float y, float z) => new Vector3(x, y, z).normalized;

        private static List<int> IcosahedronTris()
        {
            return new List<int>
            {
                0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            };
        }

        private static void Subdivide(List<Vector3> verts, List<int> tris)
        {
            var cache = new Dictionary<long, int>();
            var newTris = new List<int>(tris.Count * 4);

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
