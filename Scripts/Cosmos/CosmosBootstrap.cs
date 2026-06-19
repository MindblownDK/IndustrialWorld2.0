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
using VoxelEngine.Biomes;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    public class CosmosBootstrap : MonoBehaviour
    {
        [Tooltip("Planet template to spawn. LEAVE EMPTY to auto-use a built-in Earth body " +
                 "(gravity 1g, oxygen, grass, full ore catalogue). To customise, run " +
                 "Tools ▸ Voxel Engine ▸ Author Earth Planet Template. NOTE: the old " +
                 "Planet_Earthlike.asset is a DIFFERENT type (flat-world PlanetSettings) and " +
                 "won't fit this slot.")]
        public PlanetTemplate planetTemplate;

        [Tooltip("Solar system template (for seeing other planets/moons/sun in the sky). " +
                 "If null, auto-loads System_Sol from the project.")]
        public SolarSystemTemplate solarSystemTemplate;

        [Tooltip("Where to place the body's core in world space. Keep it within the camera's far " +
                 "clip plane so it's visible/reachable. (0,700,400) puts it high above the flat " +
                 "terrain ceiling (~256m) and in front of the spawn — look up and fly to it.)")]
        public Vector3 bodyOrigin = new Vector3(0f, 700f, 400f);

        [Tooltip("Player/camera transform that the sphere streams around and gravity follows.")]
        public Transform viewer;

        [Tooltip("Optional biome registry for accurate biomes + scatter.")]
        public BiomeRegistry biomeRegistry;

        [Tooltip("Material registry (auto-resolved if null).")]
        public MaterialRegistry materialRegistry;

        [Tooltip("Terrain material (auto-resolved if null).")]
        public Material terrainMaterial;

        [Header("Tuning (overrides template for fast iteration)")]
        [Range(0.2f, 6f)] public float testRadiusKm = 0.5f;   // small enough to fly to quickly
        [Range(3, 16)] public int viewDistance = 5;   // 3D-ball streaming: 5 = ~520 chunks (~62MB), keep modest

        private GameObject _bodyGO;

        private void Awake()
        {
            ResolvePlanetTemplate();
            ResolveAssets();
            if (viewer == null)
            {
                var pc = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
                if (pc != null) viewer = pc.transform;
            }

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
            // Apply this world's per-planet seed (index 0 = home planet) if present.
            var session = VoxelEngine.Menu.WorldSession.Instance;
            int seed = body.settings.seed;
            if (session != null && session.seedState != null)
                seed = session.seedState.GetSeed(0, seed);
            body.settings.seed = seed;
            body.settings.radiusKm = testRadiusKm;
            body.ApplySettings();

            // ── SphereWorld streamer ── (fields set BEFORE Awake thanks to inactive GO)
            var world = _bodyGO.AddComponent<SphereWorld>();
            world.body = body;
            world.materialRegistry = materialRegistry;
            world.terrainMaterial = terrainMaterial;
            world.viewer = viewer;
            world.viewDistance = viewDistance;
            world.enableScatter = true;  // Safe now — flat world is disabled, sphere is sole world.
            world.biomeRegistry = biomeRegistry;
            world.worldName = session != null ? session.worldName + "_sphere" : "SphereTest";

            // ── Far LOD (space view), as a CHILD of the body ──
            var lodGO = new GameObject("LOD");
            lodGO.transform.SetParent(_bodyGO.transform, false);
            lodGO.transform.localPosition = Vector3.zero;
            var lod = lodGO.AddComponent<PlanetLodImpostor>();
            lod.viewer = viewer;
            lod.biomeRegistry = biomeRegistry;
            // The LOD creates its OWN material internally (URP/Unlit with vertex-colour + alpha
            // support). We deliberately do NOT assign VoxelTerrain here — VoxelTerrain is a custom
            // Shader Graph that doesn't support alpha fade and renders purple at planet scale.

            // ── Distant planet renderer (like Space Engineers — see other planets in the sky) ──
            // Needs a CosmicRegistry in the scene to know where the other bodies are.
            EnsureCosmicRegistry();
            var spaceGO = new GameObject("SpaceRenderer");
            spaceGO.AddComponent<SpaceBodyRenderer>();

            // ── Sun directional light + day/night cycle (Phase 5) ──
            var sunLightGO = new GameObject("SunLightController");
            sunLightGO.AddComponent<SunLightController>();

            // ── Background quasar (Phase 5) ──
            var quasarGO = new GameObject("Quasar");
            var quasar = quasarGO.AddComponent<QuasarRenderer>();
            // Use the system template's quasar settings if available.
            if (solarSystemTemplate != null)
                quasar.settings = solarSystemTemplate.quasar;

            // ── Asteroid field (Phase 5) ──
            var asteroidGO = new GameObject("AsteroidField");
            var asteroids = asteroidGO.AddComponent<AsteroidFieldRenderer>();
            // Use the system template's asteroid field settings if available.
            if (solarSystemTemplate != null && solarSystemTemplate.asteroidFields != null && solarSystemTemplate.asteroidFields.Length > 0)
                asteroids.settings = solarSystemTemplate.asteroidFields[0].settings;

            // ── GPU grass renderer (Phase 4) ──
            var grassGO = new GameObject("GrassRenderer");
            grassGO.transform.SetParent(_bodyGO.transform, false);
            var grass = grassGO.AddComponent<GpuGrassRenderer>();
            grass.body = body;
            grass.viewer = viewer;

            // ── Waterfall system (Phase 4) ──
            var waterfallGO = new GameObject("Waterfalls");
            waterfallGO.transform.SetParent(_bodyGO.transform, false);
            var waterfalls = waterfallGO.AddComponent<WaterfallSystem>();
            waterfalls.body = body;
            waterfalls.viewer = viewer;

            // Apply the current graphics preset to the visual systems.
            world.viewDistance = GraphicsPreset.ViewDistance;
            grass.qualityDensityMul = new float[] { 0f, GraphicsPreset.GrassDensityMul * 0.5f, GraphicsPreset.GrassDensityMul, GraphicsPreset.GrassDensityMul * 1.5f };
            if (lod != null) lod.resolution = GraphicsPreset.LodResolution;
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
            var flatWorld = FindAnyObjectByType<VoxelEngine.Core.VoxelWorld>();
            if (flatWorld != null && flatWorld != this)
            {
                flatWorld.enabled = false;
                Debug.Log("[CosmosBootstrap] Flat VoxelWorld disabled — sphere is now the sole world.");
            }

            // ── Activate LAST: now every Awake/OnEnable sees a fully-wired component graph. ──
            _bodyGO.SetActive(true);

            var wind = FindAnyObjectByType<WindField>();
            if (wind != null) wind.ApplyBody(body.settings);

            // Ensure the main camera can actually SEE the body. Default far-clip planes (~1000m)
            // cull a planet placed thousands of units away — that's the "planet is invisible" bug.
            // We raise the far clip to comfortably cover bodyOrigin + planet radius + margin.
            EnsureCameraFarClip();

            Debug.Log($"[CosmosBootstrap] Spawned '{body.DisplayName}' at {bodyOrigin}, " +
                      $"seed {seed}, radius {testRadiusKm} km, radial gravity ACTIVE. " +
                      $"Camera far clip raised — look up toward the body and fly to it!");
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
            float bodyRadiusM = testRadiusKm * 1000f;
            // Distance from camera to the far side of the body, plus a generous margin.
            float needed = Vector3.Distance(cam.transform.position, bodyOrigin) + bodyRadiusM * 2f + 5000f;
            if (cam.farClipPlane < needed)
            {
                cam.farClipPlane = needed;
                Debug.Log($"[CosmosBootstrap] Camera far clip plane raised to {needed:0} so the body is visible.");
            }
        }

        /// <summary>
        /// Ensure a CosmicRegistry exists and is populated with the solar system template.
        /// Without this, SpaceBodyRenderer has nothing to render (no moon/planets in the sky).
        /// </summary>
        private void EnsureCosmicRegistry()
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null)
            {
                var regGO = new GameObject("CosmicRegistry");
                registry = regGO.AddComponent<CosmicRegistry>();
            }
            if (!registry.IsReady)
            {
                // Use the inspector-assigned template, or auto-load System_Sol.
                var sys = solarSystemTemplate;
                if (sys == null) sys = Resources.Load<SolarSystemTemplate>("System_Sol");
#if UNITY_EDITOR
                if (sys == null)
                    sys = UnityEditor.AssetDatabase.LoadAssetAtPath<SolarSystemTemplate>(
                        "Assets/VoxelEngineAssets/Planets/System_Sol.asset");
#endif
                if (sys != null)
                {
                    var session = VoxelEngine.Menu.WorldSession.Instance;
                    int seed = session != null ? session.seed : 1337;
                    registry.GenerateLayout(sys, seed);
                    Debug.Log("[CosmosBootstrap] CosmicRegistry populated with " + sys.systemName +
                              " — " + registry.Bodies.Count + " bodies (planets + moons visible in sky).");
                }
                else
                {
                    Debug.LogWarning("[CosmosBootstrap] No SolarSystemTemplate found. Run " +
                                     "Tools ▸ Voxel Engine ▸ Create Solar System (Sol) to see other planets.");
                }
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

            planetTemplate = Resources.Load<PlanetTemplate>("Planet_Earth");
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
                          "To customise: Tools ▸ Voxel Engine ▸ Author Earth Planet Template.");
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

            // Pull the EXACT working material + registry from the flat VoxelWorld (inspector-
            // assigned, guaranteed correct). This is a RUNTIME API — no #if UNITY_EDITOR guard —
            // so it works in builds too. This is the key fix for the "purple planet" (magenta =
            // missing shader): we reuse the flat world's proven vertex-colour URP material.
            if (materialRegistry == null || terrainMaterial == null)
            {
                var flat = FindAnyObjectByType<VoxelEngine.Core.VoxelWorld>();
                if (flat != null)
                {
                    if (materialRegistry == null) materialRegistry = flat.materialRegistry;
                    if (terrainMaterial  == null) terrainMaterial  = flat.terrainMaterial;
                }
            }
#if UNITY_EDITOR
            if (materialRegistry == null)
                materialRegistry = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialRegistry>("Assets/VoxelEngineAssets/MaterialRegistry.asset");
            if (terrainMaterial == null)
                terrainMaterial  = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/VoxelEngineAssets/Materials/Mat_Terrain.mat");
#endif
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

        private void OnDestroy()
        {
            if (GravityProvider.ActiveBody != null &&
                GravityProvider.ActiveBody == (_bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null))
                GravityProvider.ActiveBody = null;
        }
    }
}
