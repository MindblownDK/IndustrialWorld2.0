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
        [Tooltip("Planet template to spawn. If null, loads Planet_Earth from the project.")]
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
            // Resolve template.
            if (planetTemplate == null)
                planetTemplate = Resources.Load<PlanetTemplate>("Planet_Earth");
#if UNITY_EDITOR
            if (planetTemplate == null)
                planetTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetTemplate>(
                    "Assets/VoxelEngineAssets/Planets/Planet_Earth.asset");
#endif
            if (planetTemplate == null)
            {
                Debug.LogError("[CosmosBootstrap] No PlanetTemplate assigned and Planet_Earth not found. " +
                               "Run Tools ▸ Voxel Engine ▸ Author Earth Planet Template, then assign it here.");
                return;
            }
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

        private void OnDestroy()
        {
            if (GravityProvider.ActiveBody != null && GravityProvider.ActiveBody == (_bodyGO != null ? _bodyGO.GetComponent<CelestialBody>() : null))
                GravityProvider.ActiveBody = null;
        }
    }
}
