// Assets/Scripts/VoxelEngine/Cosmos/CosmosBootstrap.cs
//
// Wires the Phase-2 spherical world into a scene from the Cosmos templates + the WorldSession
// seed table. Add ONE GameObject with this component to the Game scene to get a live, minable,
// radial-gravity planet you can fly to and walk on.
//
// Design choice for Phase 2: this builds the sphere at a configurable test position and does
// NOT disable the existing flat VoxelWorld — so the proven flat game keeps running while you
// validate the sphere (fly over to it; gravity reorients you onto its surface; the LOD sphere
// shows the same continents from far away). Phase 2.1 promotes the sphere to the primary spawn.
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    public class CosmosBootstrap : MonoBehaviour
    {
        [Tooltip("Planet template to spawn. LEAVE EMPTY to auto-use a built-in Earth body " +
                 "(gravity 1g, oxygen, grass, full ore catalogue). To customise, run " +
                 "Tools ▸ Voxel Engine ▸ Author Earth Planet Template, which creates the correct " +
                 "PlanetTemplate asset. NOTE: the old Planet_Earthlike.asset is a DIFFERENT type " +
                 "(flat-world PlanetSettings) and won't fit this slot.")]
        public PlanetTemplate planetTemplate;

        [Tooltip("Where to place the body's core in world space.")]
        public Vector3 bodyOrigin = new Vector3(4000f, 800f, 4000f);

        [Tooltip("Player/camera transform that the sphere streams around and gravity follows.")]
        public Transform viewer;

        [Tooltip("Optional biome registry for accurate biomes + scatter.")]
        public BiomeRegistry biomeRegistry;

        [Tooltip("Material registry (auto-loaded from Resources if null).")]
        public MaterialRegistry materialRegistry;

        [Tooltip("Terrain material (auto-loaded from Resources if null).")]
        public Material terrainMaterial;

        [Header("Tuning (overrides template for fast iteration)")]
        [Range(0.2f, 6f)] public float testRadiusKm = 0.5f;   // small enough to fly to quickly
        [Range(8, 16)] public int viewDistance = 8;

        private GameObject _bodyGO;

        private void Awake()
        {
            // Resolve template. Priority: inspector-assigned → Resources → Editor asset →
            // an in-memory default. The in-memory default is the KEY fix: it means you never
            // have to hand-author an asset just to test the sphere — add the component and play.
            ResolvePlanetTemplate();

            if (materialRegistry == null) materialRegistry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            if (terrainMaterial == null)  terrainMaterial  = Resources.Load<Material>("Mat_Terrain");
            if (viewer == null)
            {
                var pc = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
                if (pc != null) viewer = pc.transform;
            }

            EnsureGravityProvider();

            // Build the body GameObject.
            _bodyGO = new GameObject("CelestialBody_" + (planetTemplate.body != null ? planetTemplate.body.bodyName : "Planet"));
            _bodyGO.transform.SetParent(transform, false);
            _bodyGO.transform.position = bodyOrigin;

            var body = _bodyGO.AddComponent<CelestialBody>();
            body.settings = planetTemplate.body;

            // Apply this world's per-planet seed (index 0 = the home planet) if present.
            var session = VoxelEngine.Menu.WorldSession.Instance;
            int seed = body.settings.seed;
            if (session != null && session.seedState != null)
                seed = session.seedState.GetSeed(0, seed);
            body.settings.seed = seed;
            body.settings.radiusKm = testRadiusKm;
            body.ApplySettings();

            // Sphere world streamer.
            var world = _bodyGO.AddComponent<SphereWorld>();
            world.body = body;
            world.materialRegistry = materialRegistry;
            world.terrainMaterial = terrainMaterial;
            world.viewer = viewer;
            world.viewDistance = viewDistance;
            world.enableScatter = true;
            world.worldName = session != null ? session.worldName + "_sphere" : "SphereTest";

            // Far LOD (space view).
            var lodGO = new GameObject("LOD");
            lodGO.transform.SetParent(_bodyGO.transform, false);
            lodGO.transform.localPosition = Vector3.zero;
            var lod = lodGO.AddComponent<PlanetLodImpostor>();
            lod.viewer = viewer;
            lod.biomeRegistry = biomeRegistry;
            // Give the LOD a distinct bright material if none assigned (vertex-colour driven).
            var lodMr = lodGO.GetComponent<MeshRenderer>();
            if (lodMr != null && lodMr.sharedMaterial == null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.name = "Mat_PlanetLOD";
                lodMr.sharedMaterial = mat;
            }

            // Activate radial gravity for the whole game.
            GravityProvider.ActiveBody = body;

            // Drop the wind personality from the body.
            var wind = FindAnyObjectByType<WindField>();
            if (wind != null) wind.ApplyBody(body.settings);

            Debug.Log($"[CosmosBootstrap] Spawned '{body.DisplayName}' at {bodyOrigin}, " +
                      $"seed {seed}, radius {testRadiusKm} km, radial gravity ACTIVE. Fly to it!");
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
        /// Resolve the planet template by priority: inspector-assigned → Resources asset →
        /// Editor asset → in-memory default. The in-memory default is the no-setup fallback so
        /// the sphere is playable immediately WITHOUT any asset authoring.
        ///
        /// IMPORTANT: the old "Planet_Earthlike.asset" is a PlanetSettings (flat-world class),
        /// NOT a PlanetTemplate — so it will not drag into this slot. That's expected. Either
        /// leave the slot empty (uses the in-memory Earth default) or run the authoring tool.
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
                // No PlanetTemplate asset anywhere → synthesize a full Earth-like body in memory.
                // Works at runtime, zero asset files required.
                planetTemplate = CreateDefaultEarthTemplate();
                Debug.Log("[CosmosBootstrap] No PlanetTemplate asset found — using an in-memory " +
                          "Earth default (gravity 1g, oxygen, grass, full ore catalogue incl. Lithium). " +
                          "To customise, run Tools ▸ Voxel Engine ▸ Author Earth Planet Template.");
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

        private void OnDestroy()
        {
            if (GravityProvider.ActiveBody != null && GravityProvider.ActiveBody == (_bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null))
                GravityProvider.ActiveBody = null;
        }
    }
}
