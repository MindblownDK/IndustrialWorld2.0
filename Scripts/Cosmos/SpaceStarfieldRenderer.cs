// Assets/Scripts/VoxelEngine/Cosmos/SpaceStarfieldRenderer.cs
//
// Lightweight procedural vacuum starfield. It is intentionally sparse and
// stable: no noisy particle storm, no screen-space gimmick, just a camera-
// relative field of small distant stars that fades in with the existing
// atmosphere-to-space transition.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Cosmos
{
    [DefaultExecutionOrder(120)]
    public class SpaceStarfieldRenderer : MonoBehaviour
    {
        [Header("Starfield")]
        [Range(120, 900)] public int starCount = 420;
        [Tooltip("Seed used for a stable star layout across the session.")]
        public int seed = 17389;
        [Tooltip("Fraction of the active camera far clip used for the star shell.")]
        [Range(0.55f, 0.94f)] public float farClipFraction = 0.82f;

        private Transform _starRoot;
        private ParticleSystem _particles;
        private ParticleSystemRenderer _particleRenderer;
        private Material _material;
        private Camera _camera;
        private float _starRadius;
        private int _builtStarCount;
        private int _builtSeed;

        private static readonly Color StarWhite = new(0.90f, 0.94f, 1.00f, 1f);
        private static readonly Color StarWarm = new(1.00f, 0.82f, 0.58f, 1f);
        private static readonly Color StarCool = new(0.58f, 0.76f, 1.00f, 1f);

        private void Awake()
        {
            EnsureStarfield();
        }

        private void Update()
        {
            ResolveCamera();
            if (_camera == null || _particles == null || _starRoot == null) return;

            var atmosphere = AtmosphereManager.Sample(_camera.transform.position);
            float spaceBlend = atmosphere.HasAtmosphere
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.02f, atmosphere.Density01))
                : 1f;

            _starRoot.position = _camera.transform.position;
            _starRoot.rotation = Quaternion.identity;
            bool visible = spaceBlend > 0.005f;
            if (_particleRenderer != null) _particleRenderer.enabled = visible;
            if (!visible) return;

            float targetRadius = Mathf.Clamp(_camera.farClipPlane * farClipFraction, 1400f, 12000f);
            bool radiusChanged = _starRadius <= 0f
                || Mathf.Abs(targetRadius - _starRadius) > Mathf.Max(100f, _starRadius * 0.18f);
            if (radiusChanged || _builtStarCount != starCount || _builtSeed != seed)
                RebuildStars(targetRadius);

            SetStarfieldOpacity(spaceBlend);
        }

        private void ResolveCamera()
        {
            if (_camera != null) return;
            _camera = Camera.main;
            if (_camera == null)
            {
                var player = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
                if (player != null) _camera = player.playerCamera;
            }
        }

        private void EnsureStarfield()
        {
            if (_particles != null) return;

            var root = new GameObject("VacuumStarfield");
            root.transform.SetParent(transform, false);
            _starRoot = root.transform;
            _particles = root.AddComponent<ParticleSystem>();
            _particleRenderer = root.GetComponent<ParticleSystemRenderer>();

            var main = _particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 100000f;
            main.startSpeed = 0f;
            main.maxParticles = Mathf.Max(1, starCount);
            main.gravityModifier = 0f;

            var emission = _particles.emission;
            emission.enabled = false;

            var shape = _particles.shape;
            shape.enabled = false;

            if (_particleRenderer != null)
            {
                _particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                _particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _particleRenderer.receiveShadows = false;
                _particleRenderer.enabled = false; // enabled only after the local field reaches upper space
                _particleRenderer.sharedMaterial = CreateStarMaterial();
            }
        }

        private Material CreateStarMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Unlit")
                         ?? Shader.Find("Sprites/Default");
            _material = new Material(shader) { name = "Mat_VacuumStarfield_Runtime" };
            _material.renderQueue = 3000;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", Color.white);
            if (_material.HasProperty("_ZWrite")) _material.SetInt("_ZWrite", 0);
            return _material;
        }

        private void RebuildStars(float radius)
        {
            if (_particles == null) return;
            int count = Mathf.Clamp(starCount, 1, 900);
            var random = new System.Random(seed);
            var stars = new ParticleSystem.Particle[count];

            for (int i = 0; i < count; i++)
            {
                Vector3 direction = RandomUnitVector(random);
                float distance = radius * Mathf.Lerp(0.92f, 0.995f, (float)random.NextDouble());
                float size = radius * Mathf.Lerp(0.00022f, 0.00058f, (float)random.NextDouble());
                if (random.NextDouble() > 0.975) size *= 2.1f; // very occasional bright navigation star

                Color color = random.NextDouble() < 0.08 ? StarWarm
                    : random.NextDouble() < 0.15 ? StarCool
                    : StarWhite;
                color.a = Mathf.Lerp(0.38f, 0.95f, (float)random.NextDouble());

                stars[i] = new ParticleSystem.Particle
                {
                    position = direction * distance,
                    startSize = size,
                    startColor = color,
                    startLifetime = 100000f,
                    remainingLifetime = 100000f,
                };
            }

            _particles.Clear(true);
            _particles.SetParticles(stars, stars.Length);
            _particles.Play(true);
            _starRadius = radius;
            _builtStarCount = starCount;
            _builtSeed = seed;
        }

        private void SetStarfieldOpacity(float opacity)
        {
            if (_material == null) return;
            Color color = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity));
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", color);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", color);
        }

        private static Vector3 RandomUnitVector(System.Random random)
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
