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
    [ExecuteAlways]
    public class PlanetLodImpostor : MonoBehaviour
    {
        [Tooltip("The physical celestial body this child LOD represents. Assigned by CosmosBootstrap.")]
        public CelestialBody body;

        [Tooltip("MeshFilter that will receive the generated LOD sphere. Auto-created if missing.")]
        public MeshFilter meshFilter;

        [Tooltip("MeshRenderer for the LOD sphere. Auto-created if missing.")]
        public MeshRenderer meshRenderer;

        [Range(642, 10242)]
        [Tooltip("Highest vertex budget for the sampled full-planet surface. Runtime distance LOD selects a cheaper mesh farther out.")]
        public int resolution = 2562;

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
        private int _lastEffectiveResolution;
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
            var resolvedBody = ResolveBody();
            if (resolvedBody == null || resolvedBody.settings == null) return;

            int effectiveResolution = ResolveRuntimeResolution(resolvedBody);
            bool needRebuild = !_biomes.IsCreated ||
                               _lastEffectiveResolution != effectiveResolution ||
                               _lastSeed != resolvedBody.genParams.seed;
            if (needRebuild) Rebuild(effectiveResolution);

            UpdateFade(resolvedBody);
        }

        private CelestialBody ResolveBody()
        {
            if (body != null && body.settings != null) return body;
            foreach (var candidate in GetComponentsInParent<CelestialBody>(true))
            {
                if (candidate == null || candidate.settings == null) continue;
                body = candidate;
                return body;
            }
            return body;
        }

        private int ResolveRuntimeResolution(CelestialBody resolvedBody)
        {
            int highest = Mathf.Clamp(resolution, 642, 10242);
            if (viewer == null || resolvedBody == null) return highest;

            float altitude = Mathf.Max(0f, resolvedBody.AltitudeAt(viewer.position));
            float radius = Mathf.Max(1f, resolvedBody.SurfaceRadius);
            // The body remains a full mesh at every distance, but its sampling budget steps
            // down in orbit. This is a genuine far/mid/near planet LOD rather than chunk loading.
            if (altitude >= radius * 4f) return Mathf.Min(highest, 642);
            if (altitude >= radius * 1.2f) return Mathf.Min(highest, 2562);
            return highest;
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

            // The built-in URP materials do not reliably consume mesh vertex colours, which
            // produced the reported white hexasphere. This native shader explicitly shades the
            // sampled ocean/land colour map and supports a clean alpha hand-off to voxel chunks.
            Shader shader = Shader.Find("VoxelEngine/PlanetSurfaceLodURP")
                          ?? Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Unlit/Color")
                          ?? Shader.Find("Standard");

            _lodMaterial = new Material(shader) { name = "Mat_PlanetSurfaceLOD_Runtime" };
            if (_lodMaterial.HasProperty("_Tint")) _lodMaterial.SetColor("_Tint", Color.white);
            if (_lodMaterial.HasProperty("_AtmosphereRim")) _lodMaterial.SetColor("_AtmosphereRim", new Color(0.18f, 0.42f, 0.78f, 1f));
            _lodMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lodMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // A faded shell must never write depth over nearby real voxel terrain.
            _lodMaterial.SetInt("_ZWrite", 0);

            if (meshRenderer != null) meshRenderer.sharedMaterial = _lodMaterial;
        }

        private void Rebuild(int targetResolution = -1)
        {
            var resolvedBody = ResolveBody();
            if (resolvedBody == null || resolvedBody.settings == null) return;
            resolvedBody.ApplySettings();
            if (_lodMaterial != null && _lodMaterial.HasProperty("_AtmosphereRim"))
            {
                Color rim = resolvedBody.settings.displayColor.a > 0.01f
                    ? Color.Lerp(new Color(0.18f, 0.42f, 0.78f, 1f), resolvedBody.settings.displayColor, 0.35f)
                    : new Color(0.18f, 0.42f, 0.78f, 1f);
                _lodMaterial.SetColor("_AtmosphereRim", rim);
            }

            BiomeData[] biomeArr = resolvedBody.BuildBiomeData(biomeRegistry);
            if (_biomes.IsCreated) _biomes.Dispose();
            _biomes = new NativeArray<BiomeData>(biomeArr.Length, Allocator.Persistent);
            for (int i = 0; i < biomeArr.Length; i++) _biomes[i] = biomeArr[i];

            if (targetResolution <= 0) targetResolution = ResolveRuntimeResolution(resolvedBody);
            var (verts, tris, colors) = BuildIcosphere(resolvedBody, targetResolution);

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

            _lastEffectiveResolution = targetResolution;
            _lastSeed = resolvedBody.genParams.seed;
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
                // Keep the full-planet shell just INSIDE the sampled terrain. Nearby opaque
                // voxel chunks therefore depth-occlude it, while unstreamed terrain beyond
                // the local chunk bubble still has a continuous sampled surface instead of a
                // square horizon/missing world.
                float lodInset = Mathf.Clamp(prm.radiusWorld * 0.001f, 2f, 12f);
                verts[i] = dir * Mathf.Max(1f, surfaceR - lodInset);
                float latitude = Mathf.Abs(dir.y);
                Color baseCol = ColorFor(alt, latitude);
                // Apply the body's custom display colour as a tint if set.
                if (body != null && body.settings != null && body.settings.displayColor.a > 0.01f)
                    baseCol = Color.Lerp(baseCol, body.settings.displayColor, 0.72f);
                colors[i] = baseCol;
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
            // This sampled shell stays enabled at every altitude, but it is inset beneath
            // real voxel terrain. Detailed chunks naturally hide it nearby; outside the stream
            // bubble it fills the entire planet surface with the matching procedural LOD.
            float a = 1f;
            if (_lodMaterial.HasProperty("_Tint"))
            {
                var tint = _lodMaterial.GetColor("_Tint"); tint.a = a; _lodMaterial.SetColor("_Tint", tint);
            }
            else if (_lodMaterial.HasProperty("_BaseColor"))
            {
                var col = _lodMaterial.GetColor("_BaseColor"); col.a = a; _lodMaterial.SetColor("_BaseColor", col);
            }
            else if (_lodMaterial.HasProperty("_Color"))
            {
                var col = _lodMaterial.GetColor("_Color"); col.a = a; _lodMaterial.SetColor("_Color", col);
            }
            meshRenderer.enabled = a > 0.001f;
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
