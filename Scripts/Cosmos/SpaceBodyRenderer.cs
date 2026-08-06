// Assets/Scripts/VoxelEngine/Cosmos/SpaceBodyRenderer.cs
//
// Renders distant celestial bodies (planets, moons, sun) as LOD spheres in the sky.
//
// The player can see the whole solar system around you — other planets hang in the
// distance, the sun glows, moons orbit.
//
// REAL SURFACES (7.14.0): every sky proxy is now a SAMPLED TERRAIN sphere — its vertex
// colours are baked from the same SphereDensity field the voxel generator uses, so the
// planet in the sky shows its actual continents, oceans and ice caps, not a flat
// colored ball. As the player approaches, the proxy CONVERGES to the body's true
// scene position and size and crossfades into the real PlanetLodImpostor surface
// (which continues upgrading through its distance-based LOD ladder) — a seamless
// space-to-surface descent, Space-Engineers style.
//
// Cosmic km-scale positions are compressed to a manageable visual range so you can
// actually SEE the other planets without floating-origin.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Biomes;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Renders the solar system's bodies as distant LOD spheres. Attach anywhere in the scene;
    /// it reads CosmicRegistry.Instance for positions and spawns one sphere per body.
    /// </summary>
    public class SpaceBodyRenderer : MonoBehaviour
    {
        [Header("Scaling")]
        [Tooltip("Cosmic distances (km) are compressed to this visual range (metres) so other " +
                 "planets are actually visible in the sky without floating-origin.")]
        public float visualRange = 6500f;

        [Tooltip("Base visual size of a planet (metres at 1× scale).")]
        public float planetVisualScale = 260f;

        [Tooltip("Base visual size of a moon.")]
        public float moonVisualScale = 60f;

        [Tooltip("Visual size of the sun (always large + glowing).")]
        public float sunVisualScale = 800f;

        [Header("Quality")]
        [Tooltip("Sampled-terrain sky proxy resolution (higher = crisper continents in the sky).")]
        [Range(642, 10242)] public int proxyResolution = 2562;

        [Tooltip("Rebuild body positions every N frames (lower = smoother orbits, higher = cheaper).")]
        public int updateEveryNFrames = 3;

        [Tooltip("Bodies closer than this (metres, true scene distance) render their real LOD " +
                 "instead of the compressed sky proxy. 60,000 km keeps EVERY planet's real voxel surface visible — the whole " +
                 "system renders real surfaces at all times; the proxy remains only for the far edge.")]
        public float trueLodDistanceMeters = 60000000f;

        /// <summary>Shared window constant (metres) — also consumed by CosmosBootstrap's far clip
        /// and PlanetLodImpostor's crossfade, so every system agrees on where real LOD begins.</summary>
        public const double TrueLodWindowMeters = 60000000d;

        /// <summary>Distance band (metres) OUTSIDE the window over which the proxy fades out
        /// while the real LOD fades in.</summary>
        public const double TrueLodFadeBandMeters = 300000d;

        [Tooltip("True distance (metres) at which the sky proxy starts converging from its " +
                 "compressed sky position/size toward the body's true scene position/size, so " +
                 "the proxy → real-LOD swap happens at the same apparent size.")]
        public float proxyConvergeDistanceMeters = 65000000f;

        [Tooltip("Optional biome registry for accurate surface colours on the sky proxies.")]
        public BiomeRegistry biomeRegistry;

        private struct BodyVisual
        {
            public GameObject go;
            public MeshFilter mf;
            public MeshRenderer mr;
            public Mesh flatMesh;      // fallback plain sphere (no terrain bake available)
            public Mesh terrainMesh;   // sampled terrain sphere (baked once per body)
            public BodyInstance bakeKey;
            public int bakeSeed;
            public bool bakePending;   // a bake is queued/in-flight for this body
        }

        private readonly List<BodyVisual> _sunVisuals = new();
        private readonly List<BodyVisual> _bodyVisuals = new();
        private int _frameCount;

        // ── Terrain baking ─────────────────────────────────────────────
        // The sky proxy's vertex colours are sampled from the body's own density field
        // (identical to the real LOD), baked lazily in small batches so spawning or
        // entering a system never hitches. One shared icosphere topology; each body
        // owns its baked colour mesh.
        private Vector3[] _sharedDirs;
        private int[] _sharedTris;
        private int _sharedTopologyResolution;

        private class ProxyBake
        {
            public BodyInstance body;
            public CelestialBody sceneBody;
            public SphereGenParams prm;
            public NativeArray<BiomeData> biomes;
            public Color[] colors;
            public int index;
            public bool done;
        }

        private readonly List<ProxyBake> _bakeQueue = new();
        private ProxyBake _activeBake;
        private readonly Dictionary<CelestialBody, NativeArray<BiomeData>> _biomeCache = new();

        private void Update()
        {
            _frameCount++;
            PumpBakes(); // terrain baking continues every frame, independent of the render throttle

            if (_frameCount % updateEveryNFrames != 0) return;

            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            // Player position in cosmic space (real space: the true viewer cosmic position;
            // legacy fallback: the active body's position).
            Vector3 viewerKm = Vector3.zero;
            var activeBody = GravityProvider.ActiveBody;
            var spaceOrigin = SpaceOrigin.Instance;
            if (spaceOrigin != null && spaceOrigin.viewer != null)
            {
                viewerKm = (Vector3)(float3)spaceOrigin.GetCosmicKm(spaceOrigin.viewer.position);
            }
            else if (activeBody != null)
            {
                // Find this body in the registry to get its cosmic position.
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    if (registry.Bodies[i].settings == activeBody.settings)
                    {
                        viewerKm = registry.Bodies[i].positionKm;
                        break;
                    }
                }
            }

            // Render the sun(s).
            EnsureCount(_sunVisuals, registry.Sun != null ? 1 : 0, "SpaceSun");
            // Origin-anchored fallback position for the sky-proxy hierarchy (used by the
            // planet/moon sky projection below, which orbits bodies around this point).
            Vector3 sunPos = GetViewerPosition() + Vector3.up * (visualRange * 1.5f);
            if (registry.Sun != null)
            {
                var sun = registry.Sun;
                float intensity = sun.settings != null ? sun.settings.intensity : 1f;
                Color glow = sun.settings != null ? sun.settings.glowColor : new Color(1f, 0.9f, 0.7f);

                // Real sun distance (scene metres): when it's close enough to render at its
                // TRUE position (SolarHazard's emissive sphere), hide this fake sprite —
                // one sun only, and the one you fly toward is the real hazard.
                bool realSunVisible = spaceOrigin != null && spaceOrigin.viewer != null
                    ? math.length(registry.Sun.positionKmD - spaceOrigin.GetCosmicKm(spaceOrigin.viewer.position)) * 1000d < trueLodDistanceMeters
                    : false;
                if (realSunVisible)
                {
                    if (_sunVisuals.Count > 0 && _sunVisuals[0].go != null)
                        _sunVisuals[0].go.SetActive(false);
                }
                else
                {
                    Vector3 sunDirKm = sun.positionKm - viewerKm;
                    Vector3 sunDir = sunDirKm.sqrMagnitude < 1f ? Vector3.up : sunDirKm.normalized;
                    sunPos = GetViewerPosition() + sunDir * visualRange * 1.5f;
                    if (_sunVisuals.Count > 0 && _sunVisuals[0].go != null && !_sunVisuals[0].go.activeSelf)
                        _sunVisuals[0].go.SetActive(true);
                    PositionBody(_sunVisuals[0], sunPos, sunVisualScale * intensity, glow, emissive: true, alpha: 1f);
                }
            }

            // Render planets + moons.
            int bodyCount = registry.Bodies.Count;
            EnsureCount(_bodyVisuals, bodyCount, "SpaceBody");
            EnsureSharedTopology();

            for (int i = 0; i < bodyCount; i++)
            {
                var b = registry.Bodies[i];
                if (b == null || b.settings == null) continue;

                // The active body already has a physical PlanetLodImpostor around its
                // real core. Do not draw a second compressed sky proxy for it.
                if (activeBody != null && b.settings == activeBody.settings)
                {
                    if (_bodyVisuals[i].go != null) _bodyVisuals[i].go.SetActive(false);
                    continue;
                }

                // True distance to the body (scene metres via cosmic positions).
                double distM = double.MaxValue;
                if (spaceOrigin != null && spaceOrigin.viewer != null)
                {
                    double3 bodyAbs = registry.CosmicPositionOf(b);
                    double3 viewerCosmic = spaceOrigin.GetCosmicKm(spaceOrigin.viewer.position);
                    distM = math.length(bodyAbs - viewerCosmic) * 1000d;
                }

                // REAL SPACE: bodies inside the true-LOD window render their real sampled
                // LOD (placed by SpaceOrigin) — the sky proxy must step aside.
                if (distM < trueLodDistanceMeters)
                {
                    if (_bodyVisuals[i].go != null) _bodyVisuals[i].go.SetActive(false);
                    continue;
                }

                var visual = _bodyVisuals[i];
                if (visual.go != null && !visual.go.activeSelf)
                    visual.go.SetActive(true);

                // Queue a lazy terrain bake for this body's sky proxy (once per settings+seed).
                EnsureBakeQueued(b, registry);

                // ── Proxy → real-LOD convergence ──────────────────────────
                // Outside the window the proxy starts at its compressed sky position/size;
                // as the true distance drops it morphs toward the body's TRUE scene
                // position/size, so the hand-off to the real LOD at the window edge happens
                // at the exact same apparent size — no popping sphere, just detail.
                float convergeT = Mathf.Clamp01((float)((proxyConvergeDistanceMeters - distM)
                                              / (proxyConvergeDistanceMeters - trueLodDistanceMeters)));
                Vector3 visualPos = Vector3.Lerp(
                    GetVisualPositionFor(b, registry, sunPos, GetViewerPosition(), activeBody),
                    GetTrueScenePosition(b, spaceOrigin),
                    convergeT);

                float radiusKm = b.settings.radiusKm;
                float stylizedSize = (b.isPlanet ? planetVisualScale : moonVisualScale) *
                                     Mathf.Clamp01(radiusKm / 8f);
                if (!b.isPlanet && b.parentBody != null && !b.parentBody.isPlanet)
                    stylizedSize *= 0.6f; // Sub-moonlets appear slightly smaller
                float size = Mathf.Lerp(stylizedSize, radiusKm * 1000f, convergeT);

                // Crossfade with the real LOD over the same band the LOD uses to fade in:
                // fully opaque beyond the band, fully transparent at the window edge.
                float alpha = Mathf.Clamp01((float)((distM - trueLodDistanceMeters) / TrueLodFadeBandMeters));

                PositionBody(visual, visualPos, size, GetBodyColor(b), emissive: false, alpha: alpha);
            }
        }

        /// <summary>
        /// True scene position of a body (metres). SpaceOrigin places every registered body
        /// every tick, so the scene transform is the exact, float-precise answer.
        /// </summary>
        private static Vector3 GetTrueScenePosition(BodyInstance b, SpaceOrigin spaceOrigin)
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null) return Vector3.zero;
            if (registry.SceneBodies != null && registry.SceneBodies.TryGetValue(b, out var cb) && cb != null)
                return cb.transform.position;
            if (spaceOrigin != null) return spaceOrigin.GetScenePos(registry.CosmicPositionOf(b));
            return Vector3.zero;
        }

        private Vector3 GetVisualPositionFor(BodyInstance b, CosmicRegistry registry, Vector3 sunPos, Vector3 viewerPosition, CelestialBody activeBody)
        {
            if (b == null) return viewerPosition;
            if (activeBody != null && b.settings == activeBody.settings)
            {
                return activeBody.transform.position;
            }

            if (b.isPlanet || b.parentBody == null)
            {
                Vector3 fromSunKm = b.positionKm - (registry.Sun != null ? registry.Sun.positionKm : Vector3.zero);
                float scaleKmToSky = (visualRange * 0.45f) / 4500f;
                return sunPos + fromSunKm * scaleKmToSky;
            }

            Vector3 parentPos = GetVisualPositionFor(b.parentBody, registry, sunPos, viewerPosition, activeBody);
            Vector3 fromParentKm = b.positionKm - b.parentBody.positionKm;
            float scale = b.parentBody.isPlanet ? 18f : 26f;
            return parentPos + fromParentKm * scale;
        }

        private static Color GetBodyColor(BodyInstance b)
        {
            if (b.settings == null) return Color.gray;
            // Custom display colour wins if set (alpha > 0 means user-chosen).
            if (b.settings.displayColor.a > 0.01f) return b.settings.displayColor;
            // Otherwise infer from climate.
            if (!b.settings.HasOxygen) return new Color(0.5f, 0.5f, 0.55f);  // moon/airless grey
            if (b.settings.temperature < -5f) return new Color(0.8f, 0.85f, 0.9f);  // ice world
            if (b.settings.temperature > 30f) return new Color(0.8f, 0.65f, 0.4f);  // desert
            return new Color(0.2f, 0.4f, 0.6f);  // earth-like blue
        }


        /// <summary>
        /// Sky-proxy anchor: the ACTIVE BODY's scene position (falling back to the scene
        /// origin in deep space). The active body is perfectly static in the scene (the
        /// floating origin tracks it), so anchoring here means the sky never moves when the
        /// player walks — walk 100 m and the planets stay put. It also keeps the proxies
        /// outside the planet. In deep space the star-frame origin anchors the sky.
        /// </summary>
        private Vector3 GetViewerPosition()
        {
            var active = GravityProvider.ActiveBody;
            if (active != null) return active.transform.position;
            return Vector3.zero;
        }

        // ── Sampled-terrain baking ────────────────────────────────────

        /// <summary>Queue a one-time terrain bake for a body's sky proxy if not already done.</summary>
        private void EnsureBakeQueued(BodyInstance b, CosmicRegistry registry)
        {
            int index = -1;
            for (int i = 0; i < registry.Bodies.Count; i++)
                if (registry.Bodies[i] == b) { index = i; break; }
            if (index < 0 || index >= _bodyVisuals.Count) return;

            if (registry.SceneBodies == null || !registry.SceneBodies.TryGetValue(b, out var sceneBody) || sceneBody == null)
                return; // no scene body yet — keep the flat fallback colour

            // Asteroid belts are procedural rock fields, not sampled surfaces — keep the
            // flat fallback for them.
            if (sceneBody.genParams.isAsteroidBelt == 1) return;

            var visual = _bodyVisuals[index];
            if (visual.terrainMesh != null && visual.bakeKey == b && visual.bakeSeed == sceneBody.genParams.seed)
                return; // already baked for this body + seed
            if (visual.bakePending && visual.bakeKey == b && visual.bakeSeed == sceneBody.genParams.seed)
                return; // a bake is already queued/in-flight

            // (Re)queue: new body, or its seed changed since the last bake.
            visual.bakeKey = b;
            visual.bakeSeed = sceneBody.genParams.seed;
            visual.bakePending = true;
            _bodyVisuals[index] = visual;

            // Drop any previously queued bake for this body.
            for (int i = _bakeQueue.Count - 1; i >= 0; i--)
                if (_bakeQueue[i].body == b) _bakeQueue.RemoveAt(i);

            if (_sharedDirs == null || _sharedDirs.Length == 0) return; // topology not ready yet — retried next frame

            var bake = new ProxyBake
            {
                body = b,
                sceneBody = sceneBody,
                prm = sceneBody.genParams,
                biomes = GetBiomesFor(sceneBody),
                colors = new Color[_sharedDirs.Length],
                index = 0,
                done = false
            };
            _bakeQueue.Add(bake);
            Debug.Log($"[SpaceBodyRenderer] Queued sampled-terrain bake for '{b.DisplayName}' " +
                      $"({_sharedDirs.Length} verts) — used only beyond the {trueLodDistanceMeters / 1000f:0} km real-voxel window.");
        }

        private NativeArray<BiomeData> GetBiomesFor(CelestialBody sceneBody)
        {
            if (_biomeCache.TryGetValue(sceneBody, out var cached) && cached.IsCreated) return cached;
            var arr = sceneBody.BuildBiomeData(biomeRegistry);
            var native = new NativeArray<BiomeData>(arr.Length, Allocator.Persistent);
            for (int i = 0; i < arr.Length; i++) native[i] = arr[i];
            _biomeCache[sceneBody] = native;
            return native;
        }

        /// <summary>Bake a small batch of proxy vertex colours per frame (never a hitch).</summary>
        private void PumpBakes()
        {
            const int Batch = 1024;
            for (int pass = 0; pass < 1; pass++)
            {
                if (_activeBake == null)
                {
                    while (_bakeQueue.Count > 0)
                    {
                        _activeBake = _bakeQueue[0];
                        _bakeQueue.RemoveAt(0);
                        if (_activeBake.done) { _activeBake = null; continue; }
                        break;
                    }
                    if (_activeBake == null) return;
                }

                var bake = _activeBake;
                // The shared topology may have been rebuilt (proxy resolution changed) —
                // a stale bake whose colour array no longer matches is dropped.
                if (_sharedDirs == null || bake.colors.Length != _sharedDirs.Length)
                {
                    bake.done = true;
                    _activeBake = null;
                    continue;
                }
                int end = Mathf.Min(bake.index + Batch, bake.colors.Length);
                var prm = bake.prm;
                for (int i = bake.index; i < end; i++)
                {
                    Vector3 dir = _sharedDirs[i];
                    SphereDensity.EvaluateColumn(prm, bake.biomes, (float3)dir, out float surfaceR, out _);
                    float alt = surfaceR - prm.MeanSurfaceRadius;
                    Color c = SphereSurfaceColor.For(alt, Mathf.Abs(dir.y));
                    if (bake.body != null && bake.body.settings != null)
                        c = SphereSurfaceColor.WithDisplayTint(c, bake.body.settings.displayColor, 0.3f);
                    bake.colors[i] = c;
                }
                bake.index = end;

                if (bake.index >= bake.colors.Length)
                {
                    FinishBake(bake);
                    _activeBake = null;
                }
            }
        }

        private void FinishBake(ProxyBake bake)
        {
            bake.done = true;
            var registry = CosmicRegistry.Instance;
            if (registry == null) return;

            int index = -1;
            for (int i = 0; i < registry.Bodies.Count; i++)
                if (registry.Bodies[i] == bake.body) { index = i; break; }
            if (index < 0 || index >= _bodyVisuals.Count) return;

            var visual = _bodyVisuals[index];
            if (visual.terrainMesh == null)
            {
                var mesh = new Mesh { name = "PlanetSkyProxy_" + bake.body.DisplayName };
                mesh.SetVertices(_sharedDirs);
                mesh.SetTriangles(_sharedTris, 0);
                mesh.SetColors(bake.colors);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                visual.terrainMesh = mesh;
                visual.bakePending = false;
                _bodyVisuals[index] = visual;
            }

            if (visual.mr != null && visual.terrainMesh != null && visual.mf != null)
            {
                visual.mf.sharedMesh = visual.terrainMesh;
            }
            Debug.Log($"[SpaceBodyRenderer] Sky proxy terrain ready for '{bake.body.DisplayName}' ({bake.colors.Length} verts).");
        }

        private void EnsureSharedTopology()
        {
            int target = Mathf.Clamp(proxyResolution, 642, 10242);
            if (_sharedDirs != null && _sharedTopologyResolution == target) return;

            var dirs = new List<Vector3>(IcosphereVerts());
            var tris = new List<int>(IcosphereTris());
            int sub = 0;
            while (dirs.Count < target && sub < 6)
            {
                Subdivide(dirs, tris);
                sub++;
            }
            _sharedDirs = dirs.ToArray();
            _sharedTris = tris.ToArray();
            _sharedTopologyResolution = target;
        }

        private void EnsureCount(List<BodyVisual> list, int count, string namePrefix)
        {
            while (list.Count < count)
            {
                var go = new GameObject(namePrefix + "_" + list.Count);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                var mesh = CreateSphere(24);
                mf.sharedMesh = mesh;
                list.Add(new BodyVisual { go = go, mf = mf, mr = mr, flatMesh = mesh });
            }
            while (list.Count > count)
            {
                var v = list[list.Count - 1];
                if (v.flatMesh != null) Destroy(v.flatMesh);
                if (v.terrainMesh != null) Destroy(v.terrainMesh);
                if (v.go != null) Destroy(v.go);
                list.RemoveAt(list.Count - 1);
            }
        }

        private void PositionBody(BodyVisual v, Vector3 pos, float size, Color color, bool emissive, float alpha)
        {
            if (v.go == null) return;
            v.go.transform.position = pos;
            v.go.transform.localScale = Vector3.one * size;

            // Ensure material exists.
            if (v.mr.sharedMaterial == null)
            {
                // The dedicated proxy shader renders the baked sampled-terrain vertex
                // colours with real-sun lighting; built-in URP materials are only a
                // last-resort fallback (they ignore vertex colours).
                var shader = Shader.Find("VoxelEngine/PlanetSkyProxyURP")
                          ?? Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard");
                v.mr.sharedMaterial = new Material(shader) { name = "Mat_SpaceBody" };
            }
            var mat = v.mr.sharedMaterial;

            bool hasTerrain = v.terrainMesh != null;
            if (mat.HasProperty("_Tint"))
            {
                // Terrain bakes carry their own vertex colours (tint = white); bodies
                // without a bake (belts, sun, not-yet-baked) tint through the flat colour.
                // (UnityEngine.Color has no .rgb accessor — channels are set individually.)
                var tint = mat.GetColor("_Tint");
                Color tintColor = hasTerrain ? Color.white : color;
                tint.r = tintColor.r;
                tint.g = tintColor.g;
                tint.b = tintColor.b;
                tint.a = hasTerrain ? alpha : 1f;
                mat.SetColor("_Tint", tint);
            }

            // Flat-colour properties only matter for the fallback (built-in) shaders.
            if (!hasTerrain)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
            }
            if (emissive)
            {
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * 2f);
                }
            }
        }

        private static Mesh CreateSphere(int resolution)
        {
            var verts = new List<Vector3>(IcosphereVerts());
            var tris = new List<int>(IcosphereTris());
            int sub = 0;
            while (verts.Count < resolution * resolution && sub < 5)
            {
                Subdivide(verts, tris);
                sub++;
            }
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var mesh = new Mesh { name = "SpaceSphere" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            foreach (var kv in _biomeCache)
            {
                if (kv.Value.IsCreated) kv.Value.Dispose();
            }
            _biomeCache.Clear();
            _bakeQueue.Clear();
            _activeBake = null;
        }

        private static List<Vector3> IcosphereVerts()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            return new List<Vector3>
            {
                N(-1,  t,  0), N( 1,  t,  0), N(-1, -t,  0), N( 1, -t,  0),
                N( 0, -1,  t), N( 0,  1,  t), N( 0, -1, -t), N( 0,  1, -t),
                N( t,  0, -1), N( t,  0,  1), N(-t,  0, -1), N(-t,  0,  1),
            };
        }
        private static Vector3 N(float x, float y, float z) => new Vector3(x, y, z).normalized;
        private static List<int> IcosphereTris()
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
            var nt = new List<int>(tris.Count * 4);
            int Mid(int a, int b)
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(k, out int idx)) return idx;
                Vector3 m = ((verts[a] + verts[b]) * 0.5f).normalized;
                idx = verts.Count; verts.Add(m); cache[k] = idx; return idx;
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nt.Add(a); nt.Add(ab); nt.Add(ca);
                nt.Add(b); nt.Add(bc); nt.Add(ab);
                nt.Add(c); nt.Add(ca); nt.Add(bc);
                nt.Add(ab); nt.Add(bc); nt.Add(ca);
            }
            tris.Clear(); tris.AddRange(nt);
        }
    }
}
