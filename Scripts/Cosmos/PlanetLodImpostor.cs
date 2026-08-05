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

        [Tooltip("When true, this body renders at the HIGH-DETAIL budget — the 'whole planet' " +
                 "surface the player is currently on / approaching (full planet, proper LOD). " +
                 "Built progressively over several frames so there is no spawn hitch. " +
                 "Distant bodies keep the cheap proxy.")]
        public bool highDetail;

        [Range(10242, 163842)]
        [Tooltip("Vertex budget used when highDetail is enabled (the active body's full surface).")]
        public int highDetailVertexBudget = 40962;

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

        // ── Safety colliders (solid planet, no flying through) ─────
        private MeshCollider _safetyMeshCollider;
        private SphereCollider _safetySphere;
        private Mesh _safetyMesh;

        [Tooltip("Altitude above the surface where the safety mesh collider engages (metres). " +
                 "Inside this shell the streamed voxel colliders own collision.")]
        public float safetyBubbleMeters = 220f;

        [Tooltip("Safety shell inflation above the sampled surface (metres).")]
        public float safetyInflationMeters = 8f;

        // ── Progressive (batched) high-detail build state ─────────────
        // Building a 40k–160k-vertex sampled surface in ONE frame would hitch the game.
        // Instead the vertex loop runs in small batches inside Update() until done, then
        // the mesh is finalised once — no spawn/frame-entry stall.
        private List<Vector3> _buildVerts;
        private List<int> _buildTris;
        private Color[] _buildColors;
        private int _buildIndex;
        private bool _buildInProgress;

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

            // Continue an in-flight progressive build before anything else.
            if (_buildInProgress)
            {
                if (ContinueProgressiveBuild(resolvedBody))
                {
                    UpdateFade(resolvedBody);
                    UpdateSafetyColliders(resolvedBody);
                }
                return;
            }

            int effectiveResolution = ResolveRuntimeResolution(resolvedBody);
            bool needRebuild = !_biomes.IsCreated ||
                               _lastEffectiveResolution != effectiveResolution ||
                               _lastSeed != resolvedBody.genParams.seed;
            if (needRebuild) Rebuild(effectiveResolution);

            UpdateFade(resolvedBody);
            UpdateSafetyColliders(resolvedBody);
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
            // High-detail (active body): the FULL planet surface with real continents and
            // mountains visible from ground to orbit. Distant bodies keep the cheap proxy.
            if (highDetail)
                return Mathf.Clamp(highDetailVertexBudget, 10242, 163842);
            return Mathf.Clamp(resolution, 642, 10242);
        }

        private void EnsureComponents()
        {
            if (meshFilter == null)  meshFilter  = GetComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshFilter == null)  meshFilter  = gameObject.AddComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
            // Remove stale colliders from earlier experimental shells, then re-add the
            // managed SAFETY colliders below (they are the ones that stop fast players
            // from flying through the planet).
            foreach (var collider in GetComponents<Collider>())
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);

            // ── SAFETY COLLIDERS (7.13.10) ─────────────────────────────
            // The streamed voxel bubble only covers the player's vicinity — beyond it the
            // LOD shell is the planet, and it must be SOLID or a fast player flies through
            // the planet. Two safety nets:
            //   • MeshCollider: the LOD surface inflated +8 m, catches orbital approaches.
            //   • SphereCollider: a solid core sphere, catches players who somehow ended
            //     up deep inside the body and pushes them back to the surface shell.
            // Both are disabled in the thin surface shell where real voxel colliders rule.
            _safetyMeshCollider = gameObject.AddComponent<MeshCollider>();
            _safetyMeshCollider.enabled = false;
            _safetySphere = gameObject.AddComponent<SphereCollider>();
            _safetySphere.enabled = false;
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

            // High-detail surface: build progressively (batched across frames) so the
            // whole-planet upgrade never hitches the game.
            if (targetResolution > 10242)
            {
                BeginProgressiveBuild(resolvedBody, targetResolution);
                return;
            }

            var (verts, tris, colors) = BuildIcosphere(resolvedBody, targetResolution);

            FinalizeMesh(verts, tris, colors, targetResolution, resolvedBody);
        }

        /// <summary>Start a batched high-detail build (vertex loop runs in Update()).</summary>
        private void BeginProgressiveBuild(CelestialBody body, int targetVerts)
        {
            _buildVerts = new List<Vector3>(IcosahedronVerts());
            _buildTris  = new List<int>(IcosahedronTris());

            int sub = 0;
            while (_buildVerts.Count < targetVerts && sub < 8)
            {
                Subdivide(_buildVerts, _buildTris);
                sub++;
            }

            _buildColors = new Color[_buildVerts.Count];
            _buildIndex = 0;
            _buildInProgress = true;
            _lastEffectiveResolution = targetVerts;
            _lastSeed = body.genParams.seed;
        }

        /// <summary>
        /// Sample a batch of vertices (keeps the main-thread hit tiny), finalise the mesh
        /// when done. Returns true while the build is still running.
        /// </summary>
        private bool ContinueProgressiveBuild(CelestialBody body)
        {
            if (_buildVerts == null || _buildColors == null)
            {
                _buildInProgress = false;
                return false;
            }

            const int Batch = 4096;
            int end = Mathf.Min(_buildIndex + Batch, _buildVerts.Count);
            var prm = body.genParams;
            for (int i = _buildIndex; i < end; i++)
            {
                Vector3 dir = _buildVerts[i].normalized;
                SphereDensity.EvaluateColumn(prm, _biomes, (float3)dir, out float surfaceR, out _);
                float alt = surfaceR - prm.MeanSurfaceRadius;
                // Keep the full-planet shell just INSIDE the sampled terrain so streamed
                // voxel chunks depth-occlude it, while everything beyond the chunk bubble
                // stays a continuous sampled surface.
                float lodInset = Mathf.Clamp(prm.radiusWorld * 0.001f, 2f, 12f);
                _buildVerts[i] = dir * Mathf.Max(1f, surfaceR - lodInset);
                Color baseCol = ColorFor(alt, Mathf.Abs(dir.y));
                if (body.settings != null && body.settings.displayColor.a > 0.01f)
                    baseCol = Color.Lerp(baseCol, body.settings.displayColor, 0.72f);
                _buildColors[i] = baseCol;
            }
            _buildIndex = end;

            if (_buildIndex < _buildVerts.Count) return true;

            // Done — finalise on this frame.
            FinalizeMesh(_buildVerts.ToArray(), _buildTris.ToArray(), _buildColors,
                _lastEffectiveResolution, body);
            _buildVerts = null;
            _buildTris = null;
            _buildColors = null;
            _buildIndex = 0;
            _buildInProgress = false;
            Debug.Log($"[PlanetLodImpostor] High-detail planet surface ready: {_lastEffectiveResolution} verts ('{body.DisplayName}').");
            return false;
        }

        private void FinalizeMesh(Vector3[] verts, int[] tris, Color[] colors, int targetResolution, CelestialBody body)
        {
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

            RebuildSafetyCollider(body);
            _lastEffectiveResolution = targetResolution;
            _lastSeed = body.genParams.seed;
        }

        /// <summary>
        /// (Re)build the safety collision shell: the sampled surface inflated slightly
        /// outward, capped at 10242 verts so cooking stays cheap. Only the active body's
        /// collider is ever enabled, so other planets cost nothing.
        /// </summary>
        private void RebuildSafetyCollider(CelestialBody body)
        {
            if (_safetyMeshCollider == null || body == null) return;
            int budget = Mathf.Min(10242, Mathf.Max(642, _lastEffectiveResolution));
            var (verts, tris, _) = BuildIcosphere(body, budget);

            // Inflate outward by the safety margin.
            float inflate = Mathf.Max(1f, safetyInflationMeters);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 dir = verts[i].normalized;
                float r = body.SurfaceRadius + inflate;
                verts[i] = dir * r;
            }

            if (_safetyMesh == null) _safetyMesh = new Mesh { name = "PlanetSafetyShell" };
            _safetyMesh.Clear();
            _safetyMesh.SetVertices(verts);
            _safetyMesh.SetTriangles(tris, 0);
            _safetyMesh.RecalculateBounds();
            _safetyMeshCollider.sharedMesh = _safetyMesh;
        }

        /// <summary>
        /// Toggle the safety colliders based on the viewer's altitude relative to this body.
        /// Only the ACTIVE body's colliders are ever enabled (cheap for distant planets).
        /// </summary>
        private void UpdateSafetyColliders(CelestialBody body)
        {
            if (_safetyMeshCollider == null || _safetySphere == null || viewer == null || body == null) return;

            bool isActiveBody = GravityProvider.ActiveBody == body;
            if (!isActiveBody)
            {
                _safetyMeshCollider.enabled = false;
                _safetySphere.enabled = false;
                return;
            }

            float alt = body.AltitudeAt(viewer.position);
            bool outsideShell = alt > safetyBubbleMeters;
            bool deepInside = alt < -64f;
            _safetyMeshCollider.enabled = outsideShell;
            _safetySphere.enabled = deepInside;

            // Keep the core sphere centred + sized to this body (it may have changed).
            float targetRadius = body.SurfaceRadius + safetyInflationMeters;
            if (Mathf.Abs(_safetySphere.radius - targetRadius) > 0.5f)
                _safetySphere.radius = targetRadius;
            _safetySphere.center = Vector3.zero;
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
            bool isBelt = body != null && body.settings != null &&
                          (body.settings.bodyName.IndexOf("Asteroid", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                           body.settings.bodyName.IndexOf("Belt", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (isBelt)
            {
                meshRenderer.enabled = false;
                return;
            }
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
            _buildVerts = null;
            _buildTris = null;
            _buildColors = null;
            _buildIndex = 0;
            _buildInProgress = false;
            if (_safetyMesh != null) { Destroy(_safetyMesh); _safetyMesh = null; }
            if (_safetyMeshCollider != null) _safetyMeshCollider.sharedMesh = null;
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
