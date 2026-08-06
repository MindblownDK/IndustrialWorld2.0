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

        [Tooltip("When true, this body always renders at the HIGH-DETAIL budget — the 'whole planet' " +
                 "surface the player is currently on / approaching (full planet, proper LOD). " +
                 "Built progressively over several frames so there is no spawn hitch. " +
                 "Every OTHER body picks its own budget from the distance ladder below.")]
        public bool highDetail;

        [Range(10242, 163842)]
        [Tooltip("Vertex budget used when highDetail is enabled (the active body's full surface).")]
        public int highDetailVertexBudget = 40962;

        // ── True-LOD window shared with SpaceBodyRenderer's sky proxies ──
        // Bodies closer than this (metres, true scene distance) render their REAL
        // sampled-surface LOD instead of the compressed sky proxy. 2,500 km keeps
        // the approached planet's real surface visible for the whole interplanetary
        // crossing (min planet separation is 2,000 km) — the LOD ladder below then
        // upgrades the surface continuously as you close in.
        public const double TrueLodWindowMeters = 2500000d;

        /// <summary>Distance band (metres) OUTSIDE the window over which the LOD crossfades
        /// with the sky proxy (proxy fades out while this LOD fades in).</summary>
        public const double TrueLodFadeBandMeters = 300000d;

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

        // ── Real-voxel bridge (7.15.0) ────────────────────────────────
        // This sampled sphere is ONLY a short bridge: PlanetVoxelLod generates the body's
        // REAL voxel surface (whole planet, LOD levels). Once that surface is ready the
        // impostor's visual steps aside — the safety colliders below stay forever.
        private PlanetVoxelLod _voxelSurface;

        // ── Safety colliders (solid planet, no flying through) ─────
        private MeshCollider _safetyMeshCollider;
        private SphereCollider _safetySphere;
        private Mesh _safetyMesh;

        [Tooltip("Altitude above the surface where the safety mesh collider stays engaged (metres). " +
                 "Below it, the shell steps aside ONLY when real streamed terrain colliders exist " +
                 "under the player — so there is never a fall-through gap for fast players.")]
        public float safetyBubbleMeters = 45f;

        [Tooltip("Safety shell offset above the VISIBLE sampled surface (metres). The collider " +
                 "hugs the real terrain shape so what you hit is what you see.")]
        public float safetyInflationMeters = 0.3f;

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
            // Late-resolved viewers (players spawned after bootstrap) must reach every
            // body's LOD, not just the active one — otherwise distant planets never
            // upgrade past their cheap budget.
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;

            var resolvedBody = ResolveBody();
            if (resolvedBody == null || resolvedBody.settings == null) return;

            // ── Real-voxel bridge: once this body's REAL voxel surface (PlanetVoxelLod)
            // is ready, this sampled sphere hides and the safety colliders remain the only
            // job here. The voxel surface is what you see from orbit to the ground. ──
            if (_voxelSurface == null) _voxelSurface = GetComponentInChildren<PlanetVoxelLod>(true);
            if (_voxelSurface != null && _voxelSurface.SurfaceReady)
            {
                if (meshRenderer != null) meshRenderer.enabled = false;
                UpdateBodyCenter(resolvedBody);
                UpdateSafetyColliders(resolvedBody);
                return;
            }

            int effectiveResolution = ResolveRuntimeResolution(resolvedBody);

            // Continue an in-flight progressive build before anything else.
            if (_buildInProgress)
            {
                // The target tier changed mid-build (player closing in / moving away):
                // abandon the stale build and restart at the new tier immediately —
                // never finish a 160k-vertex surface nobody is looking at.
                if (effectiveResolution != _lastEffectiveResolution)
                {
                    CancelProgressiveBuild();
                    _lastEffectiveResolution = -1;
                }
                else if (ContinueProgressiveBuild(resolvedBody))
                {
                    UpdateBodyCenter(resolvedBody);
                    UpdateFade(resolvedBody);
                    UpdateSafetyColliders(resolvedBody);
                    return;
                }
            }

            bool needRebuild = !_biomes.IsCreated ||
                               _lastEffectiveResolution != effectiveResolution ||
                               _lastSeed != resolvedBody.genParams.seed;
            if (needRebuild) Rebuild(effectiveResolution);

            UpdateBodyCenter(resolvedBody);
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

        /// <summary>
        /// Continuous distance-based LOD ladder (Space-Engineers style): every body in
        /// the system renders its REAL sampled surface, and the vertex budget grows as
        /// you get closer — 642 verts for a distant dot up to the full high-detail
        /// surface when you arrive. Tiers are relative to the body's own radius so the
        /// same ladder works for 2 km moons and 30 km planets.
        /// </summary>
        private int ResolveRuntimeResolution(CelestialBody resolvedBody)
        {
            // The ACTIVE body (the one the player is on / entering) always renders at
            // the full high-detail budget — one continuous planet from ground to orbit.
            if (highDetail)
                return Mathf.Clamp(highDetailVertexBudget, 10242, 163842);

            if (viewer == null)
                return Mathf.Clamp(resolution, 642, 10242);

            float distM = Vector3.Distance(viewer.position, transform.position);
            float radii = distM / Mathf.Max(1f, resolvedBody.SurfaceRadius);

            if (radii < 1.35f) return Mathf.Clamp(highDetailVertexBudget, 10242, 163842); // on / very near the surface
            if (radii < 3f)    return 40962;   // atmosphere / low orbit — near-complete surface
            if (radii < 8f)    return 10242;   // mid approach
            if (radii < 25f)   return 2562;    // high approach
            return Mathf.Clamp(resolution, 642, 2562); // far away — cheap proxy budget
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
            // The PlanetSafetyCollider marker lets interaction raycasts skip them.
            var marker = gameObject.GetComponent<PlanetSafetyCollider>();
            if (marker == null) gameObject.AddComponent<PlanetSafetyCollider>();
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

            // Progressive (batched across frames) for any surface that would take a
            // visible main-thread hit; the cheap tiers build synchronously in one frame.
            if (targetResolution > 2562)
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

        /// <summary>Abandon an in-flight progressive build (target tier changed).</summary>
        private void CancelProgressiveBuild()
        {
            _buildVerts = null;
            _buildTris = null;
            _buildColors = null;
            _buildIndex = 0;
            _buildInProgress = false;
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
                Color baseCol = SphereSurfaceColor.For(alt, Mathf.Abs(dir.y));
                if (body.settings != null)
                    baseCol = SphereSurfaceColor.WithDisplayTint(baseCol, body.settings.displayColor, 0.18f);
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

            // The collision shell only matters for the body the player is actually on /
            // approaching — every distant body would otherwise cook a 10k-vertex collider
            // mesh on every LOD tier change during an interplanetary crossing.
            if (GravityProvider.ActiveBody == body) RebuildSafetyCollider(body);
            _lastEffectiveResolution = targetResolution;
            _lastSeed = body.genParams.seed;
        }

        /// <summary>
        /// (Re)build the safety collision shell from the REAL sampled terrain surface
        /// (not a flat sphere): the same density field the visible LOD uses, pushed a
        /// hair outward so what you collide with is exactly what you see. Capped at 10242
        /// verts so cooking stays cheap. Only the active body's collider is ever enabled.
        /// </summary>
        private void RebuildSafetyCollider(CelestialBody body)
        {
            if (_safetyMeshCollider == null || body == null) return;
            int budget = Mathf.Min(10242, Mathf.Max(642, _lastEffectiveResolution));
            var (verts, tris, _) = BuildIcosphere(body, budget);

            // BuildIcosphere returns verts AT the visible LOD surface (surfaceR − lodInset).
            // Nudge them a hair outward so the collider sits exactly on the surface you SEE —
            // a player lands on the visible planet, never floating above an invisible shell.
            float push = Mathf.Max(0.2f, safetyInflationMeters);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 dir = verts[i].normalized;
                verts[i] = dir * (verts[i].magnitude + push);
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
        /// The shell stays ON down to safetyBubbleMeters, and below that only steps aside
        /// when REAL streamed terrain colliders exist under the player — the planet is solid
        /// everywhere, at every speed, with no fall-through gap.
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
            bool shellOn = alt > safetyBubbleMeters;
            if (!shellOn)
            {
                // Near the surface: the shell steps aside only when the real voxel terrain
                // under the player already has a collider. Otherwise keep it solid.
                var sphere = SphereWorld.Instance;
                if (sphere == null || sphere.body != body || !sphere.HasColliderAt(viewer.position))
                    shellOn = true;
            }
            _safetyMeshCollider.enabled = shellOn;
            _safetySphere.enabled = alt < -64f;

            // Keep the core sphere centred + sized to this body (it may have changed).
            float targetRadius = body.SurfaceRadius - 12f;
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
                Color baseCol = SphereSurfaceColor.For(alt, latitude);
                // Apply the body's custom display colour as a SUBTLE personality tint —
                // never a wash (the old 0.72 lerp turned the whole planet flat).
                if (body != null && body.settings != null)
                    baseCol = SphereSurfaceColor.WithDisplayTint(baseCol, body.settings.displayColor, 0.18f);
                colors[i] = baseCol;
            }

            return (verts.ToArray(), tris.ToArray(), colors);
        }

        /// <summary>
        /// Keeps the shader's per-material body center in sync with THIS body. The
        /// surface-detail noise needs the radial direction from this body's own core —
        /// the global _VoxelTerrainBodyCenter only describes the active streaming body.
        /// </summary>
        private void UpdateBodyCenter(CelestialBody body)
        {
            if (_lodMaterial == null || body == null) return;
            if (_lodMaterial.HasProperty("_BodyCenter"))
            {
                Vector3 center = body.transform.position;
                _lodMaterial.SetVector("_BodyCenter", new Vector4(center.x, center.y, center.z, 1f));
            }
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
            // Non-active bodies crossfade with the compressed sky proxy at the edge of the
            // true-LOD window: invisible far away, fully opaque once the real sampled
            // surface is worth rendering. The proxy fades out over the same band, so the
            // swap reads as a smooth resolution upgrade — no popping sphere.
            if (!highDetail && viewer != null && _lodMaterial.HasProperty("_Tint"))
            {
                float distM = Vector3.Distance(viewer.position, transform.position);
                a = 1f - Mathf.Clamp01((float)((distM - TrueLodWindowMeters) / TrueLodFadeBandMeters));
            }
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
