// Assets/Scripts/VoxelEngine/Cosmos/CosmosBootstrap.cs
//
// Wires the spherical world into a scene from the Cosmos templates + the WorldSession
// seed table. Add ONE GameObject with this component to the Game scene to get a live,
// minable, radial-gravity planet you can fly to and walk on.
//
// CRITICAL INIT ORDER: Unity calls Awake()/OnEnable() the moment you AddComponent on an ACTIVE
// GameObject — BEFORE the caller can assign any public field. That caused NPEs in SphereWorld
// (materialRegistry null) and PlanetLodImpostor (body null). We defeat this by creating the
// body hierarchy INACTIVE, wiring every field, then activating it last so Awake sees a fully
// configured component graph.
//
// REAL SPACE (7.13.0): this bootstrap is the scene-side owner of the continuous solar
// system. CosmicRegistry runs the real Keplerian orbits; SpaceOrigin runs the floating
// origin + reference frames; THIS class owns the ACTIVE streaming body — which body the
// voxel streamer, grass, waterfalls and ocean LOD follow. When the player leaves a body's
// gravity well the streamer suspends (deep space); when another body takes over the frame,
// the streamer re-targets it — a continuous flight with NO warp (except the Warp Drive
// block, which is a deliberate, expensive grid system).
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using VoxelEngine.Biomes;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    public class CosmosBootstrap : MonoBehaviour
    {
        [Tooltip("Planet template to spawn. LEAVE EMPTY to auto-use the setup-owned home body " +
                 "(gravity 1g, oxygen, grass, full ore catalogue). Celestial content is repaired " +
                 "through Tools ▸ Voxel Engine ▸ Voxel Engine Setup ▸ Step 21. NOTE: the old " +
                 "Planet_Earthlike.asset is a DIFFERENT type (flat-world PlanetSettings) and " +
                 "won't fit this slot.")]
        public PlanetTemplate planetTemplate;

        [Tooltip("Solar system template (for seeing other planets/moons/sun in the sky). " +
                 "If null, auto-loads System_Sol from the project.")]
        public SolarSystemTemplate solarSystemTemplate;

        [Tooltip("Fallback body-core position when surface anchoring is disabled.")]
        public Vector3 bodyOrigin = new Vector3(0f, 700f, 400f);

        [Tooltip("Player/camera transform that the sphere streams around and gravity follows.")]
        public Transform viewer;

        [Header("Initial Surface Placement")]
        [Tooltip("Places the viewer just above the authored planet surface at startup. Keep enabled for full-size planets; it replaces the old tiny test-radius workflow.")]
        public bool placeViewerOnPlanetSurface = true;
        [Range(0.25f, 5f)] public float initialSurfaceClearance = 1.15f;

        [Tooltip("Optional biome registry for accurate biomes + scatter.")]
        public BiomeRegistry biomeRegistry;

        [Tooltip("Material registry (auto-resolved if null).")]
        public MaterialRegistry materialRegistry;

        [Tooltip("Terrain material (auto-resolved if null).")]
        public Material terrainMaterial;

        [Header("Streaming")]
        [Range(3, 16)] public int viewDistance = 8;   // local editable voxel detail radius; full planet uses LOD outside it

        private GameObject _bodyGO;
        private CelestialBody _body;
        private SphereWorld _sphereWorld;
        private PlanetLodImpostor _terrainLod;
        private PlanetOceanLodRenderer _oceanLod;
        private GpuGrassRenderer _grass;
        private WaterfallSystem _waterfalls;
        private SpaceOrigin _spaceOrigin;
        private SpaceAsteroidField _asteroidField;
        private WindField _wind;
        private bool _awaitingViewerSurfacePlacement;

        /// <summary>The body the streaming systems currently follow (null = deep space).</summary>
        private CelestialBody _streamingBody;

        // Camera handoff is intentionally kept here with the body bootstrap: it owns the
        // active celestial profile and can restore the scene's original camera state cleanly.
        private Camera _spaceTransitionCamera;
        private CameraClearFlags _spaceTransitionBaseClearFlags;
        private Color _spaceTransitionBaseBackground;
        private float _spaceTransitionBaseFarClip;
        private bool _spaceTransitionCaptured;

        public static CosmosBootstrap Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            ResolvePlanetTemplate();
            ResolveAssets();
            ResolveViewerReference();

            EnsureGravityProvider();

            // ── Cosmic layout FIRST (real Keplerian orbits) + floating origin ──
            EnsureCosmicRegistry();
            var registry = CosmicRegistry.Instance;

            // The home body instance (matched by settings) anchors the scene origin.
            BodyInstance homeInstance = null;
            if (planetTemplate != null && planetTemplate.body != null && registry.Bodies != null)
            {
                foreach (var b in registry.Bodies)
                {
                    if (b != null && b.settings == planetTemplate.body) { homeInstance = b; break; }
                }
            }

            // ── SpaceOrigin: floating origin + reference frames ──
            EnsureSpaceOrigin(registry, homeInstance);

            // ── Home body (the planet the player starts on) ──
            // Build the body GameObject INACTIVE so we can configure components before their
            // Awake/OnEnable fire (the fix for the SphereWorld / PlanetLodImpostor NPEs).
            _bodyGO = new GameObject("CelestialBody_" +
                (planetTemplate.body != null ? planetTemplate.body.bodyName : "Planet"));
            _bodyGO.transform.SetParent(transform, false);
            _bodyGO.transform.position = bodyOrigin;
            _bodyGO.SetActive(false);

            if (planetTemplate.body == null)
            {
                Debug.LogWarning("[CosmosBootstrap] PlanetTemplate.body is null — using a built-in " +
                                 "Earth body. Open the PlanetTemplate asset and author its Body settings.");
                planetTemplate.body = BodySettings.CreateEarthlike();
            }
            var body = _bodyGO.AddComponent<CelestialBody>();
            _body = body;
            body.settings = planetTemplate.body;
            var session = VoxelEngine.Menu.WorldSession.Instance;
            int seed = body.settings.seed;
            int spawnIdx = session != null ? Mathf.Clamp(session.spawnPlanetIndex, 0, 99) : 0;
            if (session != null && session.seedState != null)
                seed = session.seedState.GetSeed(spawnIdx, seed);
            body.SetRuntimeSeedOverride(seed);
            body.ApplySettings();
            _awaitingViewerSurfacePlacement = placeViewerOnPlanetSurface;
            if (_awaitingViewerSurfacePlacement && viewer != null)
                AnchorViewerToAuthoredSurface(body);

            // ── SphereWorld streamer ── (fields set BEFORE Awake thanks to inactive GO)
            var world = _bodyGO.AddComponent<SphereWorld>();
            _sphereWorld = world;
            world.body = body;
            var enhancedShader = Shader.Find("VoxelEngine/VoxelTerrainEnhanced");
            if (enhancedShader != null && terrainMaterial != null)
            {
                var enhancedMat = new Material(enhancedShader) { name = "Mat_Terrain_Enhanced" };
                if (terrainMaterial.HasProperty("_BaseColor"))
                {
                    var col = terrainMaterial.GetColor("_BaseColor");
                    enhancedMat.SetColor("_BaseColor", col == Color.clear || col == Color.black ? Color.white : col);
                }
                else
                {
                    enhancedMat.SetColor("_BaseColor", Color.white);
                }
                if (terrainMaterial.HasProperty("_Smoothness"))
                    enhancedMat.SetFloat("_Smoothness", terrainMaterial.GetFloat("_Smoothness"));
                if (terrainMaterial.HasProperty("_Metallic"))
                    enhancedMat.SetFloat("_Metallic", terrainMaterial.GetFloat("_Metallic"));
                terrainMaterial = enhancedMat;
            }
            world.materialRegistry = materialRegistry;
            world.terrainMaterial = terrainMaterial;
            world.viewer = viewer;
            world.viewDistance = viewDistance;
            world.enableScatter = true;
            world.biomeRegistry = biomeRegistry;
            world.worldName = session != null ? session.worldName : "SphereTest";

            bool isBelt = body.settings != null &&
                          (body.settings.bodyName.IndexOf("Asteroid", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                           body.settings.bodyName.IndexOf("Belt", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (isBelt)
            {
                world.enableScatter = false;
                placeViewerOnPlanetSurface = false;
            }

            // ── Far LOD (space view), as a CHILD of the body ──
            var lodGO = new GameObject("LOD");
            lodGO.transform.SetParent(_bodyGO.transform, false);
            lodGO.transform.localPosition = Vector3.zero;
            var lod = lodGO.AddComponent<PlanetLodImpostor>();
            _terrainLod = lod;
            lod.body = body;
            lod.viewer = viewer;
            lod.biomeRegistry = biomeRegistry;

            // Ocean LOD is a separate mesh generated only over real ocean basins. It fills
            // distant water without creating a wrapped water sphere in dry caves or on land.
            var oceanLodGO = new GameObject("OceanLOD");
            oceanLodGO.transform.SetParent(_bodyGO.transform, false);
            var oceanLod = oceanLodGO.AddComponent<PlanetOceanLodRenderer>();
            _oceanLod = oceanLod;
            oceanLod.body = body;
            oceanLod.viewer = viewer;
            oceanLod.biomeRegistry = biomeRegistry;

            // ── Distant bodies + sparse vacuum starfield ────────────────
            var spaceGO = new GameObject("SpaceRenderer");
            spaceGO.AddComponent<SpaceBodyRenderer>();
            spaceGO.AddComponent<SpaceStarfieldRenderer>();

            // ── Live quality preset applier (Phase 7) ──
            var qpaGO = new GameObject("QualityPresetApplier");
            qpaGO.AddComponent<QualityPresetApplier>();

            // ── Sun directional light + day/night cycle (Phase 5) ──
            var sunLightGO = new GameObject("SunLightController");
            sunLightGO.AddComponent<SunLightController>();

            // ── Background quasar (Phase 5) ──
            var quasarGO = new GameObject("Quasar");
            var quasar = quasarGO.AddComponent<QuasarRenderer>();
            var activeSolarSystem = ResolveSolarSystemTemplate();
            if (activeSolarSystem != null)
                quasar.settings = activeSolarSystem.quasar;
            // ONE sun only (7.13.10): with a real star in the system the quasar's bright
            // core reads as a second sun — disable it. Systems without a sun keep it.
            if (activeSolarSystem != null && activeSolarSystem.sun != null)
                quasar.settings.enabled = false;

            // ── Asteroid field (Phase 5 visual belt) ──
            var asteroidGO = new GameObject("AsteroidField");
            var asteroids = asteroidGO.AddComponent<AsteroidFieldRenderer>();
            asteroids.settings = ResolveAsteroidVisualSettings(activeSolarSystem);

            // ── GPU grass renderer (Phase 4) ──
            var grassGO = new GameObject("GrassRenderer");
            grassGO.transform.SetParent(_bodyGO.transform, false);
            var grass = grassGO.AddComponent<GpuGrassRenderer>();
            _grass = grass;
            grass.body = body;
            grass.viewer = viewer;

            // ── Waterfall system (Phase 4) ──
            var waterfallGO = new GameObject("Waterfalls");
            waterfallGO.transform.SetParent(_bodyGO.transform, false);
            var waterfalls = waterfallGO.AddComponent<WaterfallSystem>();
            _waterfalls = waterfalls;
            waterfalls.body = body;
            waterfalls.viewer = viewer;

            // Apply the current graphics preset to the visual systems.
            world.viewDistance = GraphicsPreset.ViewDistance;
            grass.qualityDensityMul = new float[] { 0f, GraphicsPreset.GrassDensityMul * 0.5f, GraphicsPreset.GrassDensityMul, GraphicsPreset.GrassDensityMul * 1.5f };
            if (lod != null) lod.resolution = GraphicsPreset.LodResolution;
            if (oceanLod != null) oceanLod.resolution = GraphicsPreset.LodResolution;
            waterfalls.scanRange = GraphicsPreset.WaterfallRange;
            world.maxJobsPerFrame = GraphicsPreset.JobsPerFrame;

            // Register the home body with the cosmic scene graph.
            homeInstance = FindInstanceFor(body);
            if (homeInstance != null)
            {
                registry.SceneBodies[homeInstance] = body;
                _spaceOrigin.RegisterRoot(_bodyGO.transform);
            }

            // CRITICAL — spawn-stability: pin the scene reference frame to the HOME body
            // immediately. Without this the frame starts as the solar (star) frame, so the
            // planet visibly races away at its orbital speed while the freshly-spawned
            // player stands still — and when the frame then switches to the planet, the
            // frame-velocity delta hurls the player into space at hundreds of m/s. Pinning
            // the frame up front means the planet is at rest in the scene from frame one
            // and the player is born standing on it (no kick, ever).
            _spaceOrigin.SetFrame(body);

            // ── Real space: spawn every OTHER body of the system as real geometry ──
            EnsureAllBodiesInScene(registry);

            // ── Deep-space procedural asteroid spawner ──
            var fieldGO = new GameObject("SpaceAsteroidField");
            _asteroidField = fieldGO.AddComponent<SpaceAsteroidField>();

            // ── Solar hazard: the star warns before it kills (no more random sun deaths) ──
            var sunHazardGO = new GameObject("SolarHazard");
            sunHazardGO.AddComponent<SolarHazard>();

            // Activate radial gravity for the whole game + wind personality.
            _streamingBody = body;
            GravityProvider.ActiveBody = body;
            // Route mining/building tools to THIS world (not the flat VoxelWorld).
            VoxelEngine.Core.ActiveWorld.Current = world;

            // CRITICAL: disable the flat VoxelWorld so ONLY the sphere streams chunks + uses the
            // shared FluidManager/WaterMeshBuilder. Running BOTH worlds simultaneously causes them
            // to fight over the same singletons and generate chunks at each other's positions → 
            // job-safety violations + scatter crashes. We disable the component (not destroy it)
            // so its inspector-assigned assets stay available for the sphere to borrow.
            var flatWorld = FindAnyObjectByType<VoxelEngine.Core.VoxelWorld>(FindObjectsInactive.Include);
            if (flatWorld != null && flatWorld != this)
            {
                flatWorld.enabled = false;
                Debug.Log("[CosmosBootstrap] Flat VoxelWorld disabled — sphere is now the sole world.");
            }

            // ── Activate LAST: now every Awake/OnEnable sees a fully-wired component graph. ──
            _bodyGO.SetActive(true);

            // ── Whole-planet surface (Space-Engineers style): the home body renders at the
            // high-detail LOD budget — one continuous sampled planet, built progressively
            // over the next frames so the spawn stays smooth. ──
            if (_terrainLod != null)
            {
                _terrainLod.highDetail = true;
                _terrainLod.highDetailVertexBudget = GraphicsPreset.ActiveBodyLodResolution;
            }

            TryResolveViewerAndAnchor();

            _wind = FindAnyObjectByType<WindField>();
            if (_wind != null) _wind.ApplyBody(body.settings);

            // Real-space frame events: switching bodies must re-target the streamer.
            SpaceOrigin.OnFrameChanged += HandleFrameChange;

            // Ensure the main camera can actually SEE the body. Default far-clip planes (~1000m)
            // cull a planet placed thousands of units away — that's the "planet is invisible" bug.
            EnsureCameraFarClip();

            Debug.Log($"[CosmosBootstrap] Spawned '{body.DisplayName}' at {_bodyGO.transform.position}, " +
                      $"seed {seed}, radius {body.settings.radiusKm:0.##} km, radial gravity ACTIVE. " +
                      $"Real Keplerian orbits: {registry.Bodies.Count} bodies, {registry.Asteroids.Count} belt rocks.");
        }

        private void OnDestroy()
        {
            SpaceOrigin.OnFrameChanged -= HandleFrameChange;
            RestoreAtmosphereSpaceCamera();
            Shader.SetGlobalFloat("_VoxelAtmosphereDensity01", 1f);
            Shader.SetGlobalFloat("_VoxelSpaceBlend", 0f);
            if (GravityProvider.ActiveBody != null &&
                GravityProvider.ActiveBody == (_bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null))
                GravityProvider.ActiveBody = null;
        }

        /// <summary>
        /// Finds the player after either bootstrap order (scene-authored player before Cosmos or
        /// setup-spawned player after Cosmos). This removes the one-frame race that otherwise
        /// leaves SphereWorld without a viewer and strands its stream at the fallback origin.
        /// </summary>
        private void ResolveViewerReference()
        {
            if (viewer != null) return;
            var player = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
            if (player != null) viewer = player.transform;
        }

        private void AnchorViewerToAuthoredSurface(CelestialBody body)
        {
            if (body == null || viewer == null) return;
            body.transform.position = viewer.position
                - Vector3.up * (body.SurfaceRadius + initialSurfaceClearance);
            // Keep the floating origin consistent with this authored placement so the
            // body stays put in the scene and the viewer stays on its surface.
            if (_spaceOrigin != null)
                _spaceOrigin.AlignAnchorToBodyScenePosition(body);
            _awaitingViewerSurfacePlacement = false;
        }

        private void PropagateViewer()
        {
            if (_sphereWorld != null) _sphereWorld.viewer = viewer;
            if (_terrainLod != null) _terrainLod.viewer = viewer;
            if (_oceanLod != null) _oceanLod.viewer = viewer;
            if (_grass != null) _grass.viewer = viewer;
            if (_waterfalls != null) _waterfalls.viewer = viewer;
        }

        private void TryResolveViewerAndAnchor()
        {
            ResolveViewerReference();
            if (viewer == null) return;
            PropagateViewer();
            if (_spaceOrigin != null)
            {
                _spaceOrigin.RegisterRoot(viewer);
                // The floating origin must track the REAL player — never a placeholder.
                _spaceOrigin.viewer = viewer;
            }

            if (_awaitingViewerSurfacePlacement)
            {
                var body = _bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null;
                if (body != null)
                {
                    // LATE resolution (player appeared after bootstrap): DO NOT move the
                    // body under the viewer — the spawner computes its surface point from
                    // the body's CURRENT position, so moving it later would strand the
                    // player inside/off the planet. Keep the body where it is, align the
                    // floating origin to it, and let PlayerSpawner place the player.
                    if (_spaceOrigin != null)
                        _spaceOrigin.AlignAnchorToBodyScenePosition(body);
                    _awaitingViewerSurfacePlacement = false;
                    EnsureCameraFarClip();
                    Debug.Log("[CosmosBootstrap] Late viewer resolved; origin aligned to the authored spherical body.");
                }
            }
        }

        /// <summary>
        /// Raise the main camera's far clip plane so the (possibly distant) planet is rendered.
        /// Computes the distance from the camera to the far edge of the body + margin, and only
        /// ever INCREASES the far clip (never shrinks it below its existing value). Capped so
        /// depth precision stays usable; farther bodies use the compressed sky renderer.
        /// </summary>
        private void EnsureCameraFarClip()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = FindAnyObjectByType<Camera>();
            }
            if (cam == null) return;

            var registry = CosmicRegistry.Instance;
            if (registry == null || _spaceOrigin == null) return;

            double3 camCosmic = _spaceOrigin.GetCosmicKm(cam.transform.position);
            double needed = 0d;

            // 1) The ACTIVE body — always visible, including the whole approach from orbit
            //    (the old 900 km cap hid the planet the moment its frame became active,
            //    because the sky proxy is hidden for the active body — the planet vanished).
            var body = GravityProvider.ActiveBody != null
                ? GravityProvider.ActiveBody
                : (_bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null);
            if (body != null)
            {
                double d = math.length(_spaceOrigin.GetCosmicKm(body.transform.position) - camCosmic) * 1000d;
                needed = math.max(needed, d + body.SurfaceRadius * 2d + 5000d);
            }

            // 2) Every other body whose real LOD renders (true-LOD window).
            if (registry.SceneBodies != null)
            {
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    var b = registry.Bodies[i];
                    if (b == null) continue;
                    if (b.settings != null && body != null && b.settings == body.settings) continue;
                    double distM = math.length(registry.CosmicPositionOf(b) - camCosmic) * 1000d;
                    if (distM < trueLodViewKm * 1000d)
                        needed = math.max(needed, distM + (b.settings != null ? b.settings.radiusKm * 1000d : 6000d) + 5000d);
                }
            }

            // 3) The STAR — the real sun must render at its true position (capped far clips
            //    previously culled it, so "flying into the sun" passed through a fake sprite).
            if (registry.Sun != null)
            {
                double sunDistM = math.length(registry.Sun.positionKmD - camCosmic) * 1000d;
                needed = math.max(needed, math.min(sunDistM + 100000d, maxFarClipMeters));
            }

            if (needed > 1000d)
            {
                float target = (float)math.min(needed, maxFarClipMeters);
                if (Mathf.Abs(cam.farClipPlane - target) > target * 0.05f)
                {
                    cam.farClipPlane = target;
                    Debug.Log($"[CosmosBootstrap] Camera far clip plane set to {cam.farClipPlane:0} so the sun, planets and LODs are visible.");
                }
            }
        }

        /// <summary>Far-plane cap (metres). 50,000 km keeps the whole system visible;
        /// URP reversed-Z depth keeps near-terrain precision intact.</summary>
        private const double maxFarClipMeters = 50000000d;

        /// <summary>Bodies within this distance (km) render their real LOD instead of the sky proxy.</summary>
        private const double trueLodViewKm = 800d;

        // ── Real-space infrastructure ──────────────────────────────

        private void EnsureSpaceOrigin(CosmicRegistry registry, BodyInstance homeInstance)
        {
            if (SpaceOrigin.Instance != null)
            {
                _spaceOrigin = SpaceOrigin.Instance;
                return;
            }
            var go = new GameObject("SpaceOrigin");
            _spaceOrigin = go.AddComponent<SpaceOrigin>();

            // Anchor the scene so the HOME body sits at bodyOrigin.
            double3 homeCosmic = homeInstance != null ? registry.CosmicPositionOf(homeInstance) : double3.zero;
            double3 anchor = homeCosmic - VoxelEngine.Cosmos.CosmicRegistry.ToDouble3(bodyOrigin) / 1000d;

            _spaceOrigin.Initialize(viewer != null ? viewer : transform, anchor);
            _spaceOrigin.RegisterRoot(go.transform);
            Debug.Log($"[CosmosBootstrap] SpaceOrigin anchored at {anchor} km (home '{planetTemplate?.body?.bodyName}').");
        }

        private BodyInstance FindInstanceFor(CelestialBody body)
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || body == null) return null;
            foreach (var kv in registry.SceneBodies)
                if (kv.Value == body) return kv.Key;
            // Match by settings reference (the home body isn't in the map yet).
            foreach (var b in registry.Bodies)
            {
                if (b == null) continue;
                if (b.settings == body.settings) return b;
            }
            return null;
        }

        /// <summary>
        /// Spawn real CelestialBody GameObjects for every body in the registry (except the
        /// home, which already exists), each with its own sampled-surface LOD. SpaceOrigin
        /// positions them every frame from their real Keplerian positions.
        /// </summary>
        private void EnsureAllBodiesInScene(CosmicRegistry registry)
        {
            if (registry == null || !registry.IsReady) return;

            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                var instance = registry.Bodies[i];
                if (instance == null || instance.settings == null) continue;
                if (registry.SceneBodies.ContainsKey(instance)) continue;
                if (instance.settings == _body.settings) continue;

                var go = new GameObject("CelestialBody_" + instance.DisplayName);
                go.transform.SetParent(transform, false);
                go.SetActive(false);

                var cb = go.AddComponent<CelestialBody>();
                cb.settings = instance.settings;
                int perBodySeed = instance.settings.seed;
                var session = VoxelEngine.Menu.WorldSession.Instance;
                if (session != null && session.seedState != null && instance.planetTemplate != null)
                {
                    // The seed table is index-aligned with the system template's planet order.
                    int planetIndex = 0;
                    if (registry.systemTemplate != null && registry.systemTemplate.planets != null)
                    {
                        for (int k = 0; k < registry.systemTemplate.planets.Length; k++)
                        {
                            if (registry.systemTemplate.planets[k] == instance.planetTemplate)
                            {
                                planetIndex = k;
                                break;
                            }
                        }
                    }
                    perBodySeed = session.seedState.GetSeed(planetIndex, instance.settings.seed);
                }
                cb.SetRuntimeSeedOverride(perBodySeed);
                cb.ApplySettings();

                var lodGO = new GameObject("LOD");
                lodGO.transform.SetParent(go.transform, false);
                var lod = lodGO.AddComponent<PlanetLodImpostor>();
                lod.body = cb;
                lod.viewer = viewer;
                lod.biomeRegistry = biomeRegistry;
                // Distant bodies get the cheap proxy; the active body is upgraded on entry.
                lod.resolution = Mathf.Min(GraphicsPreset.LodResolution, 642);

                registry.SceneBodies[instance] = cb;
                _spaceOrigin.RegisterRoot(go.transform);
                go.SetActive(true);
            }
        }

        /// <summary>
        /// Re-target all active-body systems when the scene reference frame changes:
        /// null = deep space (streaming suspended), body = fly into that body's well.
        /// The player's position is continuous — nothing teleports.
        /// </summary>
        private void HandleFrameChange(CelestialBody newBody)
        {
            if (newBody == _streamingBody) return;
            CelestialBody previousBody = _streamingBody;
            _streamingBody = newBody;

            if (newBody == null)
            {
                // ── Deep space: suspend voxel streaming + planet-side effects. ──
                if (previousBody != null)
                {
                    // Downgrade the previous body's LOD back to the cheap proxy.
                    var oldLod = previousBody.GetComponentInChildren<PlanetLodImpostor>(true);
                    if (oldLod != null)
                    {
                        oldLod.highDetail = false;
                        oldLod.resolution = Mathf.Min(GraphicsPreset.LodResolution, 642);
                    }
                }
                _sphereWorld.SetBody(null);
                SetAuxSystemsEnabled(false);
                GravityProvider.ActiveBody = null;
                Debug.Log("[CosmosBootstrap] Deep space — voxel streaming suspended, zero-g active.");
            }
            else
            {
                // ── Entering another body's frame: re-target the streamer. ──
                // The entered body becomes the high-detail WHOLE-PLANET surface.
                var newLod = newBody.GetComponentInChildren<PlanetLodImpostor>(true);
                if (newLod != null)
                {
                    newLod.highDetail = true;
                    newLod.highDetailVertexBudget = GraphicsPreset.ActiveBodyLodResolution;
                }
                if (previousBody != null)
                {
                    var prevLod = previousBody.GetComponentInChildren<PlanetLodImpostor>(true);
                    if (prevLod != null) prevLod.highDetail = false;
                }
                _sphereWorld.SetBody(newBody);
                MoveAuxSystemsUnder(newBody);
                // Belt worlds are zero-g rock fields — no grass, waterfalls or oceans.
                bool isBelt = newBody.settings != null &&
                              (newBody.settings.bodyName.IndexOf("Asteroid", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                               newBody.settings.bodyName.IndexOf("Belt", System.StringComparison.OrdinalIgnoreCase) >= 0);
                SetAuxSystemsEnabled(!isBelt);
                GravityProvider.ActiveBody = newBody;
                if (_wind != null) _wind.ApplyBody(newBody.settings);
                Debug.Log($"[CosmosBootstrap] Entered '{newBody.DisplayName}' — streaming re-targeted.");
            }

            // Propagate the new body into every celestial renderer.
            var registry = CosmicRegistry.Instance;
            if (registry != null && _spaceOrigin != null)
                EnsureCameraFarClip();
        }

        private void SetAuxSystemsEnabled(bool enabled)
        {
            if (_grass != null) _grass.gameObject.SetActive(enabled);
            if (_waterfalls != null) _waterfalls.gameObject.SetActive(enabled);
            if (_oceanLod != null) _oceanLod.gameObject.SetActive(enabled);
        }

        private void MoveAuxSystemsUnder(CelestialBody targetBody)
        {
            if (targetBody == null) return;
            if (_grass != null) _grass.body = targetBody;
            if (_waterfalls != null) _waterfalls.body = targetBody;
            if (_oceanLod != null) _oceanLod.body = targetBody;

            if (_grass != null) _grass.transform.SetParent(targetBody.transform, true);
            if (_waterfalls != null) _waterfalls.transform.SetParent(targetBody.transform, true);
            if (_oceanLod != null) _oceanLod.transform.SetParent(targetBody.transform, true);
        }

        /// <summary>
        /// Restore a saved deep-space/orbital state: re-anchor the origin at the saved cosmic
        /// position and re-enter the saved frame body (or deep space). Called by the save
        /// system after it restores the player position.
        /// </summary>
        public void RestoreCosmicState(Vector3 savedCosmicKm, string frameBodyName)
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || _spaceOrigin == null) return;

            _spaceOrigin.TeleportCosmic(VoxelEngine.Cosmos.CosmicRegistry.ToDouble3(savedCosmicKm));

            CelestialBody frame = null;
            if (!string.IsNullOrEmpty(frameBodyName))
            {
                foreach (var kv in registry.SceneBodies)
                {
                    if (kv.Key == null || kv.Key.settings == null) continue;
                    if (string.Equals(kv.Key.settings.bodyName, frameBodyName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        frame = kv.Value;
                        break;
                    }
                }
            }

            if (frame != null && frame != _streamingBody)
            {
                _spaceOrigin.SetFrame(frame);
                HandleFrameChange(frame);
            }
            else if (frame == null && _streamingBody != null)
            {
                _spaceOrigin.SetFrame(null);
                HandleFrameChange(null);
            }

            TryResolveViewerAndAnchor();
            EnsureCameraFarClip();
            Debug.Log($"[CosmosBootstrap] Space state restored at {savedCosmicKm} km, frame '{(frame != null ? frame.DisplayName : "SOL")}'.");
        }

        /// <summary>Current scene frame body (null = deep space).</summary>
        public CelestialBody CurrentFrameBody => _spaceOrigin != null ? _spaceOrigin.FrameBody : _body;

        /// <summary>
        /// Force the streaming systems (voxel world, gravity, grass, ocean, LOD) onto a
        /// specific body without waiting for an automatic frame switch — used right after
        /// a respawn/teleport pins the scene frame directly (SetFrame), so the streamer
        /// never stays on the wrong planet. Pass null for deep space.
        /// </summary>
        public void ForceStreamingBody(CelestialBody body)
        {
            if (body == _streamingBody) return;
            CelestialBody previous = _streamingBody;
            // Defeat the early-out in HandleFrameChange — but only when switching TO a
            // body; a deep-space (null) target must keep a non-null previous to pass.
            if (body != null) _streamingBody = null;
            HandleFrameChange(body);
            Debug.Log($"[CosmosBootstrap] Streaming forced to {(body != null ? "'" + body.DisplayName + "'" : "deep space")} (was {(previous != null ? "'" + previous.DisplayName + "'" : "deep space")}).");
        }

        /// <summary>True when the player is in deep space (outside every body's gravity well).</summary>
        public bool IsDeepSpace => _spaceOrigin != null && _spaceOrigin.IsDeepSpace;

        // ── Legacy asset resolution (unchanged behaviour) ──────────

        private SolarSystemTemplate ResolveSolarSystemTemplate()
        {
            if (solarSystemTemplate != null) return solarSystemTemplate;

            var library = CosmosTemplateLibrary.Load();
            if (library != null && library.systems != null)
            {
                solarSystemTemplate = library.FindByName("Sol System");
                if (solarSystemTemplate == null)
                {
                    foreach (var candidate in library.systems)
                    {
                        if (candidate == null) continue;
                        solarSystemTemplate = candidate;
                        break;
                    }
                }
            }

            if (solarSystemTemplate == null)
                solarSystemTemplate = Resources.Load<SolarSystemTemplate>("System_Sol");
#if UNITY_EDITOR
            if (solarSystemTemplate == null)
                solarSystemTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<SolarSystemTemplate>(
                    "Assets/VoxelEngineAssets/Planets/System_Sol.asset");
#endif
            return solarSystemTemplate;
        }

        private static AsteroidFieldSettings ResolveAsteroidVisualSettings(SolarSystemTemplate system)
        {
            if (system != null && system.asteroidFields != null)
            {
                foreach (var field in system.asteroidFields)
                    if (field != null && field.settings != null) return field.settings;
            }

            return new AsteroidFieldSettings
            {
                density = 1f,
                resourceCount = 3,
                possibleResources = new[]
                {
                    VoxelEngine.Materials.MaterialId.Iron,
                    VoxelEngine.Materials.MaterialId.Nickel,
                    VoxelEngine.Materials.MaterialId.Silicon,
                    VoxelEngine.Materials.MaterialId.Platinum,
                    VoxelEngine.Materials.MaterialId.Ice
                },
                sizeRangeKm = new Vector2(0.03f, 0.35f),
                shellRadiusKm = new Vector2(8000f, 12000f)
            };
        }

        /// <summary>
        /// Ensure a CosmicRegistry exists and is populated with the solar system template.
        /// The resolved system is assigned to both bootstrap and registry, so Inspector state,
        /// runtime layout, distant planets, and asteroid visuals all share one source of truth.
        /// </summary>
        private void EnsureCosmicRegistry()
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null)
            {
                var regGO = new GameObject("CosmicRegistry");
                registry = regGO.AddComponent<CosmicRegistry>();
            }

            var system = ResolveSolarSystemTemplate();
            if (system == null)
            {
                system = CreateRuntimeSolarFallback();
                solarSystemTemplate = system;
                Debug.LogWarning("[CosmosBootstrap] System_Sol was not yet setup-owned at runtime; using a temporary visual fallback. Run Step 21 to author the persistent system library and belt.");
            }

            var session = VoxelEngine.Menu.WorldSession.Instance;
            int seed = session != null ? session.seed : 1337;
            bool needsLayout = !registry.IsReady || registry.systemTemplate != system || registry.Bodies.Count == 0;
            registry.systemTemplate = system;
            registry.worldSeed = seed;
            if (needsLayout)
            {
                registry.GenerateLayout(system, seed);
                Debug.Log("[CosmosBootstrap] System_Sol assigned automatically: " + registry.Bodies.Count +
                          " bodies and " + registry.Asteroids.Count + " asteroids are ready for real Keplerian flight.");
            }
        }

        private void EnsureGravityProvider()
        {
            if (FindAnyObjectByType<GravityProvider>() == null)
            {
                var gpGO = new GameObject("GravityProvider");
                gpGO.AddComponent<GravityProvider>();
            }
        }

        /// <summary>
        /// Resolve the planet template by priority: inspector → Resources → Editor asset →
        /// in-memory default. The in-memory default is the no-setup fallback so the sphere is
        /// playable immediately WITHOUT any asset authoring.
        /// </summary>
        private void ResolvePlanetTemplate()
        {
            if (planetTemplate != null) return;

            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session != null && session.seedState != null && session.seedState.planets != null)
            {
                int spawnIdx = Mathf.Clamp(session.spawnPlanetIndex, 0, session.seedState.planets.Count - 1);
                var sys = ResolveSolarSystemTemplate();
                if (sys != null && sys.planets != null && spawnIdx < sys.planets.Length && sys.planets[spawnIdx] != null)
                {
                    planetTemplate = sys.planets[spawnIdx];
                }
            }

            if (planetTemplate == null) planetTemplate = Resources.Load<PlanetTemplate>("Planet_Earth");
#if UNITY_EDITOR
            if (planetTemplate == null)
                planetTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetTemplate>(
                    "Assets/VoxelEngineAssets/Planets/Planet_Earth.asset");
#endif
            if (planetTemplate == null)
            {
                planetTemplate = CreateDefaultEarthTemplate();
                Debug.Log("[CosmosBootstrap] No PlanetTemplate asset found — using an in-memory " +
                          "Earth default (gravity 1g, oxygen, grass, full ore catalogue). " +
                          "Run Tools ▸ Voxel Engine ▸ Voxel Engine Setup ▸ Step 21 to author/repair celestial assets.");
            }
        }

        private SolarSystemTemplate CreateRuntimeSolarFallback()
        {
            // Last-resort visual/runtime safety net for a scene that predates Step 21. It is not
            // an authored asset and is never saved; Step 21 remains the canonical way to create
            // System_Sol, its library entry, and its real asteroid-belt asset.
            var system = ScriptableObject.CreateInstance<SolarSystemTemplate>();
            system.name = "System_Sol_RuntimeFallback";
            system.hideFlags = HideFlags.DontSave;
            system.systemName = "Sol System";
            system.sun = new SunSettings
            {
                displayName = "Sol",
                sunCount = 1,
                intensity = 1.3f,
                glowColor = new Color(1f, 0.95f, 0.78f, 1f)
            };
            system.minPlanetSeparationKm = 500f;
            system.maxPlanetSeparationKm = 10000f;

            var planets = new System.Collections.Generic.List<PlanetTemplate>();
            if (planetTemplate != null) planets.Add(planetTemplate);

            PlanetTemplate Proxy(string name, float radiusKm, Color color, float orbit)
            {
                var template = ScriptableObject.CreateInstance<PlanetTemplate>();
                template.name = "Runtime_" + name;
                template.hideFlags = HideFlags.DontSave;
                template.body = BodySettings.CreateEarthlike();
                template.body.bodyName = name;
                template.body.radiusKm = radiusKm;
                template.body.displayColor = color;
                template.distanceFromSun = orbit;
                template.orbitSpeed = 0.8f;
                return template;
            }

            planets.Add(Proxy("Amber World", 6f, new Color(0.85f, 0.52f, 0.20f, 1f), 3400f));
            planets.Add(Proxy("Azure World", 7f, new Color(0.18f, 0.45f, 0.82f, 1f), 5600f));
            planets.Add(Proxy("Verdant World", 6f, new Color(0.30f, 0.70f, 0.40f, 1f), 7900f));
            planets.Add(Proxy("Violet World", 5f, new Color(0.55f, 0.36f, 0.82f, 1f), 10500f));
            system.planets = planets.ToArray();
            return system;
        }

        /// <summary>
        /// Resolve materialRegistry + terrainMaterial. Priority: inspector → scene's flat
        /// VoxelWorld (which has them assigned) → Resources → Editor asset path. Never null.
        /// </summary>
        private void ResolveAssets()
        {
            if (materialRegistry == null) materialRegistry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            if (terrainMaterial == null)  terrainMaterial  = Resources.Load<Material>("Mat_Terrain");

#if UNITY_EDITOR
            if (materialRegistry == null)
                materialRegistry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/VoxelEngineAssets/MaterialRegistry.asset");
            if (terrainMaterial == null)
                terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/VoxelEngineAssets/VoxelTerrain.mat");
#endif

            if (materialRegistry == null || terrainMaterial == null)
            {
                var flat = FindAnyObjectByType<VoxelEngine.Core.VoxelWorld>(FindObjectsInactive.Include);
                if (flat != null)
                {
                    if (materialRegistry == null) materialRegistry = flat.materialRegistry;
                    if (terrainMaterial  == null) terrainMaterial  = flat.terrainMaterial;
                }
            }
        }

        /// <summary>Build a complete, ready-to-play Earth PlanetTemplate in memory.</summary>
        private static PlanetTemplate CreateDefaultEarthTemplate()
        {
            var t = ScriptableObject.CreateInstance<PlanetTemplate>();
            t.name = "Planet_Earth_Runtime";
            t.body = BodySettings.CreateEarthlike();
            t.orbitalDistanceKm = new Vector2(2500f, 4000f);
            t.orbitSpeed = 0.6f;
            return t;
        }

        private void Start()
        {
            TryResolveViewerAndAnchor();
        }

        private void Update()
        {
            if (viewer == null || _awaitingViewerSurfacePlacement) TryResolveViewerAndAnchor();
            UpdateAtmosphereSpaceCamera();

            // Frame changes can also arrive via polling (robust against missed events).
            if (_spaceOrigin != null && _streamingBody != _spaceOrigin.FrameBody)
                HandleFrameChange(_spaceOrigin.FrameBody);

            // Keep the far clip tracking the approach every 2 s (bodies move along their
            // orbits; the sun/planet must stay visible as you travel between them).
            _farClipTimer -= Time.deltaTime;
            if (_farClipTimer <= 0f)
            {
                _farClipTimer = 2f;
                EnsureCameraFarClip();
            }
        }

        private float _farClipTimer = 1f;

        /// <summary>
        /// Gives the player an actual visual handoff from sky to deep space without replacing
        /// the scene skybox asset: upper air fades to a dark backdrop, existing cosmic meshes
        /// remain visible, and the camera far clip expands with orbital altitude.
        /// </summary>
        private void UpdateAtmosphereSpaceCamera()
        {
            var body = GravityProvider.ActiveBody;
            if (body == null || viewer == null)
            {
                RestoreAtmosphereSpaceCamera();
                return;
            }

            Camera camera = Camera.main;
            if (camera == null) camera = viewer.GetComponentInChildren<Camera>(true);
            if (camera != _spaceTransitionCamera)
            {
                RestoreAtmosphereSpaceCamera();
                _spaceTransitionCamera = camera;
            }
            if (_spaceTransitionCamera == null) return;

            var underwater = _spaceTransitionCamera.GetComponent<VoxelEngine.Player.UnderwaterEffect>();
            if (underwater != null && underwater.IsUnderwater)
            {
                RestoreAtmosphereSpaceCamera();
                return;
            }

            var atmosphere = VoxelEngine.GridSystem.AtmosphereManager.Sample(viewer.position);
            float spaceBlend = atmosphere.HasAtmosphere
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.02f, atmosphere.Density01))
                : 1f;
            Shader.SetGlobalFloat("_VoxelAtmosphereDensity01", atmosphere.Density01);
            Shader.SetGlobalFloat("_VoxelSpaceBlend", spaceBlend);

            if (spaceBlend <= 0.001f)
            {
                RestoreAtmosphereSpaceCamera();
                return;
            }

            if (!_spaceTransitionCaptured)
            {
                _spaceTransitionBaseClearFlags = _spaceTransitionCamera.clearFlags;
                _spaceTransitionBaseBackground = _spaceTransitionCamera.backgroundColor;
                _spaceTransitionBaseFarClip = _spaceTransitionCamera.farClipPlane;
                _spaceTransitionCaptured = true;
            }

            _spaceTransitionCamera.clearFlags = CameraClearFlags.SolidColor;
            Color upperAir = new Color(0.075f, 0.145f, 0.245f, 1f);
            Color deepSpace = new Color(0.002f, 0.004f, 0.012f, 1f);
            _spaceTransitionCamera.backgroundColor = Color.Lerp(upperAir, deepSpace, spaceBlend);

            float bodyDistance = Vector3.Distance(_spaceTransitionCamera.transform.position, body.transform.position);
            float requiredFarClip = bodyDistance + body.SurfaceRadius * 1.25f + 2500f;
            _spaceTransitionCamera.farClipPlane = Mathf.Max(_spaceTransitionBaseFarClip, requiredFarClip);
        }

        private void RestoreAtmosphereSpaceCamera()
        {
            if (!_spaceTransitionCaptured || _spaceTransitionCamera == null) return;
            _spaceTransitionCamera.clearFlags = _spaceTransitionBaseClearFlags;
            _spaceTransitionCamera.backgroundColor = _spaceTransitionBaseBackground;
            _spaceTransitionCamera.farClipPlane = _spaceTransitionBaseFarClip;
            _spaceTransitionCaptured = false;
        }
    }
}
