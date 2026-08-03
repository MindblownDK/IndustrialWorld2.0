// Assets/Scripts/VoxelEngine/Cosmos/CosmosBootstrap.cs
//
// Wires the Phase-2 spherical world into a scene from the Cosmos templates + the WorldSession
// seed table. Add ONE GameObject with this component to the Game scene to get a live, minable,
// radial-gravity planet you can fly to and walk on.
//
// CRITICAL INIT ORDER: Unity calls Awake()/OnEnable() the moment you AddComponent on an ACTIVE
// GameObject — BEFORE the caller can assign any public field. That caused NPEs in SphereWorld
// (materialRegistry null) and PlanetLodImpostor (body null). We defeat this by creating the
// body hierarchy INACTIVE, wiring every field, then activating it last so Awake sees a fully
// configured component graph.
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
        private SphereWorld _sphereWorld;
        private PlanetLodImpostor _terrainLod;
        private PlanetOceanLodRenderer _oceanLod;
        private GpuGrassRenderer _grass;
        private WaterfallSystem _waterfalls;
        private bool _awaitingViewerSurfacePlacement;

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

            // Build the body GameObject INACTIVE so we can configure components before their
            // Awake/OnEnable fire (the fix for the SphereWorld / PlanetLodImpostor NPEs).
            _bodyGO = new GameObject("CelestialBody_" +
                (planetTemplate.body != null ? planetTemplate.body.bodyName : "Planet"));
            _bodyGO.transform.SetParent(transform, false);
            _bodyGO.transform.position = bodyOrigin;
            _bodyGO.SetActive(false);

            // ── CelestialBody ──
            // Guarantee a non-null body: a PlanetTemplate created via the menu but never filled
            // in has body == null. Fall back to a full Earth-like body so the planet always generates.
            if (planetTemplate.body == null)
            {
                Debug.LogWarning("[CosmosBootstrap] PlanetTemplate.body is null — using a built-in " +
                                 "Earth body. Open the PlanetTemplate asset and author its Body settings.");
                planetTemplate.body = BodySettings.CreateEarthlike();
            }
            var body = _bodyGO.AddComponent<CelestialBody>();
            body.settings = planetTemplate.body;
            // Apply this world's per-planet seed. Use the SPAWN PLANET INDEX (player-chosen)
            // so the player spawns on the planet they selected in the menu.
            var session = VoxelEngine.Menu.WorldSession.Instance;
            int seed = body.settings.seed;
            int spawnIdx = session != null ? Mathf.Clamp(session.spawnPlanetIndex, 0, 99) : 0;
            if (session != null && session.seedState != null)
                seed = session.seedState.GetSeed(spawnIdx, seed);
            body.SetRuntimeSeedOverride(seed);
            // Keep the authored PlanetTemplate radius exactly. Test-radius overrides made the
            // LOD, ocean basins, gravity, and streaming disagree about the planet's size.
            body.ApplySettings();
            _awaitingViewerSurfacePlacement = placeViewerOnPlanetSurface;
            if (_awaitingViewerSurfacePlacement && viewer != null)
                AnchorViewerToAuthoredSurface(body);

            // ── SphereWorld streamer ── (fields set BEFORE Awake thanks to inactive GO)
            var world = _bodyGO.AddComponent<SphereWorld>();
            _sphereWorld = world;
            world.body = body;
            // Override the terrain material with the enhanced shader (procedural detail +
            // slope shading). Falls back to the resolved material if the shader is missing.
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
            world.enableScatter = true;  // Safe now — flat world is disabled, sphere is sole world.
            world.biomeRegistry = biomeRegistry;
            world.worldName = session != null ? session.worldName : "SphereTest";

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
            // Needs a CosmicRegistry in the scene to know where the other bodies are.
            EnsureCosmicRegistry();
            var activeSolarSystem = solarSystemTemplate;
            var spaceGO = new GameObject("SpaceRenderer");
            spaceGO.AddComponent<SpaceBodyRenderer>();
            // Sparse camera-relative stars fade in through the same atmosphere-to-vacuum
            // blend as the backdrop, so deep space reads as a real destination.
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
            // Use the system template's quasar settings if available.
            if (activeSolarSystem != null)
                quasar.settings = activeSolarSystem.quasar;

            // ── Asteroid field (Phase 5) ──
            var asteroidGO = new GameObject("AsteroidField");
            var asteroids = asteroidGO.AddComponent<AsteroidFieldRenderer>();
            // A real authored belt wins; otherwise a deterministic runtime fallback keeps
            // the automatically bootstrapped solar system visually alive.
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

            // Activate radial gravity for the whole game + wind personality.
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
            // A scene may spawn its Player after this bootstrap's Awake. Propagate a late
            // viewer immediately when it already exists, otherwise Start/Update retry safely.
            TryResolveViewerAndAnchor();

            var wind = FindAnyObjectByType<WindField>();
            if (wind != null) wind.ApplyBody(body.settings);

            // Ensure the main camera can actually SEE the body. Default far-clip planes (~1000m)
            // cull a planet placed thousands of units away — that's the "planet is invisible" bug.
            // We raise the far clip to comfortably cover bodyOrigin + planet radius + margin.
            EnsureCameraFarClip();

            Debug.Log($"[CosmosBootstrap] Spawned '{body.DisplayName}' at {_bodyGO.transform.position}, " +
                      $"seed {seed}, radius {body.settings.radiusKm:0.##} km, radial gravity ACTIVE. " +
                      $"Full-planet LOD and local editable terrain stream are ready.");
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
            // Keep the initial local frame simple and stable: viewer sits at the north radial
            // surface, while all later movement/streaming is fully body-relative.
            body.transform.position = viewer.position
                - Vector3.up * (body.SurfaceRadius + initialSurfaceClearance);
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

            if (_awaitingViewerSurfacePlacement)
            {
                var body = _bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null;
                if (body != null)
                {
                    AnchorViewerToAuthoredSurface(body);
                    EnsureCameraFarClip();
                    Debug.Log("[CosmosBootstrap] Late viewer resolved; anchored to the authored spherical surface.");
                }
            }
        }

        /// <summary>
        /// Raise the main camera's far clip plane so the (possibly distant) planet is rendered.
        /// Computes the distance from the camera to the far edge of the body + margin, and only
        /// ever INCREASES the far clip (never shrinks it below its existing value).
        /// </summary>
        private void EnsureCameraFarClip()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                // Camera.main can be null right after scene load; fall back to any camera.
                cam = FindAnyObjectByType<Camera>();
            }
            if (cam == null) return;
            var body = _bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null;
            float bodyRadiusM = body != null ? body.SurfaceRadius : 1000f;
            // Distance from camera to the far side of the body, plus a generous margin.
            Vector3 bodyCenter = body != null ? body.transform.position : bodyOrigin;
            float needed = Vector3.Distance(cam.transform.position, bodyCenter) + bodyRadiusM * 2f + 5000f;
            if (cam.farClipPlane < needed)
            {
                cam.farClipPlane = needed;
                Debug.Log($"[CosmosBootstrap] Camera far clip plane raised to {needed:0} so the body is visible.");
            }
        }

        private SolarSystemTemplate ResolveSolarSystemTemplate()
        {
            if (solarSystemTemplate != null) return solarSystemTemplate;

            // The setup-owned Resources library is the build-safe path. Direct AssetDatabase
            // lookup below remains an editor recovery path only; builds never depend on it.
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

        private static AsteroidFieldSettings ResolveAsteroidVisualSettings(SolarSystemTemplate system)
        {
            if (system != null && system.asteroidFields != null)
            {
                foreach (var field in system.asteroidFields)
                    if (field != null && field.settings != null) return field.settings;
            }

            // Visual fallback until Setup writes the shared belt asset into System_Sol.
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
                          " bodies and " + registry.Asteroids.Count + " asteroids are ready for sky rendering.");
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

        /// <summary>
        /// Resolve materialRegistry + terrainMaterial. Priority: inspector → scene's flat
        /// VoxelWorld (which has them assigned) → Resources → Editor asset path. Never null.
        /// </summary>
        private void ResolveAssets()
        {
            if (materialRegistry == null) materialRegistry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            if (terrainMaterial == null)  terrainMaterial  = Resources.Load<Material>("Mat_Terrain");

#if UNITY_EDITOR
            // In the editor the authored assets live under VoxelEngineAssets, not Resources.
            // Resolve them by path so play-mode planets do not fall back to empty registries
            // and white URP materials.
            if (materialRegistry == null)
                materialRegistry = AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/VoxelEngineAssets/MaterialRegistry.asset");
            if (terrainMaterial == null)
                terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/VoxelEngineAssets/VoxelTerrain.mat");
#endif

            // Pull the exact working material + registry from the flat VoxelWorld if present.
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
            CheckInterplanetaryFlight();
        }

        public void TransitionToPlanet(PlanetTemplate newPlanet)
        {
            if (newPlanet == null || newPlanet == planetTemplate || _sphereWorld == null) return;
            Debug.Log($"[CosmosBootstrap] Transitioning interplanetary flight to {newPlanet.name}...");

            _sphereWorld.ResetAllChunks();
            planetTemplate = newPlanet;

            var body = _bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null;
            if (body != null)
            {
                if (newPlanet.body == null) newPlanet.body = BodySettings.CreateEarthlike();
                body.settings = newPlanet.body;
                int perPlanetSeed = 1337 ^ (newPlanet.body.bodyName.GetHashCode() * 397);
                var session = VoxelEngine.Menu.WorldSession.Instance;
                if (session != null && session.seedState != null)
                    perPlanetSeed = session.seedState.GetSeed(0, newPlanet.body.seed);
                body.SetRuntimeSeedOverride(perPlanetSeed);
                body.ApplySettings();

                _sphereWorld.body = body;

                if (_terrainLod != null) _terrainLod.body = body;
                if (_oceanLod != null) _oceanLod.body = body;
                if (_grass != null) _grass.body = body;
                if (_waterfalls != null) _waterfalls.body = body;

                GravityProvider.ActiveBody = body;

                if (viewer != null)
                {
                    Vector3 orbitEntrance = body.transform.position + new Vector3(0f, body.settings.radiusKm * 1000f + 850f, 0f);
                    viewer.position = orbitEntrance;
                    var rb = viewer.GetComponentInParent<Rigidbody>();
                    if (rb != null) rb.linearVelocity = Vector3.down * 15f;
                    VoxelEngine.UI.BuildFeedbackHud.Show("Space Travel", $"Arrived in Orbit: {body.settings.bodyName}", null, new Color(0.18f, 0.72f, 0.88f));
                }
            }
        }

        private float _nextInterplanetaryCheck;
        private void CheckInterplanetaryFlight()
        {
            if (Time.unscaledTime < _nextInterplanetaryCheck || viewer == null || _bodyGO == null) return;
            _nextInterplanetaryCheck = Time.unscaledTime + 0.3f;

            var activeBody = GravityProvider.ActiveBody;
            if (activeBody == null || activeBody.settings == null) return;

            float altitude = (viewer.position - _bodyGO.transform.position).magnitude - activeBody.settings.radiusKm * 1000f;
            if (altitude < 1400f) return;

            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            var rb = viewer.GetComponentInParent<Rigidbody>();
            Vector3 vel = rb != null ? rb.linearVelocity : viewer.forward * 50f;
            if (vel.magnitude < 15f) return;

            Vector3 flyDir = vel.normalized;
            Vector3 viewerKm = Vector3.zero;
            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                if (registry.Bodies[i].settings == activeBody.settings)
                {
                    viewerKm = registry.Bodies[i].positionKm;
                    break;
                }
            }

            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                var target = registry.Bodies[i];
                if (target == null || target.settings == activeBody.settings || target.planetTemplate == null) continue;
                Vector3 toTargetKm = target.positionKm - viewerKm;
                if (toTargetKm.sqrMagnitude < 1f) continue;

                Vector3 dirToTarget = toTargetKm.normalized;
                if (Vector3.Dot(flyDir, dirToTarget) > 0.94f && altitude > 1800f)
                {
                    TransitionToPlanet(target.planetTemplate);
                    break;
                }
            }
        }

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

        private void OnDestroy()
        {
            RestoreAtmosphereSpaceCamera();
            Shader.SetGlobalFloat("_VoxelAtmosphereDensity01", 1f);
            Shader.SetGlobalFloat("_VoxelSpaceBlend", 0f);
            if (GravityProvider.ActiveBody != null &&
                GravityProvider.ActiveBody == (_bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null))
                GravityProvider.ActiveBody = null;
        }
    }
}
