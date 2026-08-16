// Assets/Scripts/VoxelEngine/Cosmos/SpaceNebulaRenderer.cs
//
// Sparse, camera-relative galactic clouds that fade in with the existing
// atmosphere-to-space blend. The layout is seeded and stable — no noisy
// particle storm, just a few large soft veils that give deep space depth.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Cosmos
{
    [DefaultExecutionOrder(115)]
    public class SpaceNebulaRenderer : MonoBehaviour
    {
        [Range(6, 24)] public int cloudCount = 14;
        public int seed = 42117;
        [Range(0.55f, 0.94f)] public float farClipFraction = 0.78f;

        private Transform _root;
        private ParticleSystem _particles;
        private ParticleSystemRenderer _renderer;
        private Material _material;
        private Camera _camera;
        private float _radius;
        private int _builtCount;
        private int _builtSeed;

        private void Awake()
        {
            EnsureClouds();
        }

        private void Update()
        {
            ResolveCamera();
            if (_camera == null || _particles == null || _root == null) return;

            var atmosphere = AtmosphereManager.Sample(_camera.transform.position);
            float spaceBlend = atmosphere.HasAtmosphere
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.015f, atmosphere.Density01))
                : 1f;

            _root.position = _camera.transform.position;
            _root.rotation = Quaternion.identity;
            bool visible = spaceBlend > 0.04f;
            if (_renderer != null) _renderer.enabled = visible;
            if (!visible) return;

            float targetRadius = Mathf.Clamp(_camera.farClipPlane * farClipFraction, 1600f, 14000f);
            bool radiusChanged = _radius <= 0f
                || Mathf.Abs(targetRadius - _radius) > Mathf.Max(120f, _radius * 0.2f);
            if (radiusChanged || _builtCount != cloudCount || _builtSeed != seed)
                RebuildClouds(targetRadius);

            ApplyColors(spaceBlend);
        }

        private void ResolveCamera()
        {
            if (_camera != null) return;
            _camera = Camera.main;
            if (_camera != null) return;
            var player = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
            if (player != null) _camera = player.playerCamera;
        }

        private void EnsureClouds()
        {
            if (_particles != null) return;

            var go = new GameObject("DeepSpaceNebula");
            go.transform.SetParent(transform, false);
            _root = go.transform;
            _particles = go.AddComponent<ParticleSystem>();
            _renderer = go.GetComponent<ParticleSystemRenderer>();

            var main = _particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 100000f;
            main.startSpeed = 0f;
            main.maxParticles = Mathf.Max(1, cloudCount);
            main.gravityModifier = 0f;
            main.startSize = 400f;

            var emission = _particles.emission;
            emission.enabled = false;
            var shape = _particles.shape;
            shape.enabled = false;

            if (_renderer != null)
            {
                _renderer.renderMode = ParticleSystemRenderMode.Billboard;
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.enabled = false;
                _renderer.sharedMaterial = CreateMaterial();
            }
        }

        private Material CreateMaterial()
        {
            var template = Resources.Load<Material>("VoxelEngineRuntime/SpaceNebula");
            if (template != null && template.shader != null)
            {
                _material = new Material(template) { name = "Mat_SpaceNebula_Runtime" };
            }
            else
            {
                Shader shader = Shader.Find("VoxelEngine/SpaceNebulaURP")
                             ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Particles/Standard Unlit")
                             ?? Shader.Find("Sprites/Default");
                _material = new Material(shader) { name = "Mat_SpaceNebula_Runtime" };
            }
            _material.renderQueue = 2450;
            if (_material.HasProperty("_ZWrite")) _material.SetInt("_ZWrite", 0);
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", Color.white);
            return _material;
        }

        private void RebuildClouds(float radius)
        {
            if (_particles == null) return;
            int count = Mathf.Clamp(cloudCount, 1, 24);
            var random = new System.Random(seed);
            var clouds = new ParticleSystem.Particle[count];

            // A thin galactic band: most clouds sit near a shared plane so the
            // field reads as a ribbon, not a noisy sphere of blobs.
            Vector3 planeA = RandomUnit(random);
            Vector3 planeB = Vector3.Cross(planeA, RandomUnit(random)).normalized;
            if (planeB.sqrMagnitude < 0.01f) planeB = Vector3.right;

            for (int i = 0; i < count; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float band = ((float)random.NextDouble() * 2f - 1f) * 0.22f;
                Vector3 dir = (Mathf.Cos(angle) * planeA + Mathf.Sin(angle) * planeB + band * Vector3.Cross(planeA, planeB)).normalized;
                float distance = radius * Mathf.Lerp(0.86f, 0.98f, (float)random.NextDouble());
                float size = radius * Mathf.Lerp(0.18f, 0.42f, (float)random.NextDouble());
                Color color = Color.Lerp(
                    new Color(0.55f, 0.28f, 0.72f, 1f),
                    new Color(0.18f, 0.42f, 0.78f, 1f),
                    (float)random.NextDouble());
                color.a = Mathf.Lerp(0.10f, 0.28f, (float)random.NextDouble());

                clouds[i] = new ParticleSystem.Particle
                {
                    position = dir * distance,
                    startSize = size,
                    startColor = color,
                    startLifetime = 100000f,
                    remainingLifetime = 100000f,
                    rotation = (float)random.NextDouble() * 360f,
                };
            }

            _particles.Clear(true);
            _particles.SetParticles(clouds, clouds.Length);
            _particles.Play(true);
            _radius = radius;
            _builtCount = cloudCount;
            _builtSeed = seed;
        }

        private void ApplyColors(float spaceBlend)
        {
            if (_material == null) return;
            PlanetSkyPalette palette = PlanetSkyController.Instance != null
                ? PlanetSkyController.Instance.CurrentPalette
                : PlanetSkyCatalog.DeepSpace();

            Color primary = palette.NebulaPrimary;
            primary.a = Mathf.Clamp01(spaceBlend) * 0.55f;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", primary);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", primary);
            if (_material.HasProperty("_Primary")) _material.SetColor("_Primary", palette.NebulaPrimary);
            if (_material.HasProperty("_Secondary")) _material.SetColor("_Secondary", palette.NebulaSecondary);
            if (_material.HasProperty("_Opacity")) _material.SetFloat("_Opacity", Mathf.Clamp01(spaceBlend));
        }

        private static Vector3 RandomUnit(System.Random random)
        {
            float z = (float)random.NextDouble() * 2f - 1f;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(radial * Mathf.Cos(angle), z, radial * Mathf.Sin(angle));
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
