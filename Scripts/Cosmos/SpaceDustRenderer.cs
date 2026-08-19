// Assets/Scripts/VoxelEngine/Cosmos/SpaceDustRenderer.cs
//
// Sparse world-anchored dust motes for upper atmosphere and vacuum flight.
// Particles wrap around the camera instead of following it, creating restrained
// parallax that communicates motion without turning space into a particle storm.

using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [DefaultExecutionOrder(118)]
    public sealed class SpaceDustRenderer : MonoBehaviour
    {
        [Range(12, 96)] public int particleCount = 52;
        public int seed = 73421;
        [Range(25f, 150f)] public float nearRadius = 55f;
        [Range(120f, 600f)] public float farRadius = 280f;
        [Range(0.01f, 0.3f)] public float minimumSize = 0.035f;
        [Range(0.02f, 0.6f)] public float maximumSize = 0.14f;
        [Range(0f, 0.6f)] public float opacity = 0.24f;

        private Camera _camera;
        private ParticleSystem _particles;
        private ParticleSystemRenderer _renderer;
        private Material _material;
        private ParticleSystem.Particle[] _buffer;
        private System.Random _random;
        private int _builtCount;
        private int _builtSeed;
        private float _wrapTimer;

        private static readonly int IdTint = Shader.PropertyToID("_Tint");
        private static readonly int IdOpacity = Shader.PropertyToID("_Opacity");

        private void Awake()
        {
            EnsureSystem();
            if (_renderer != null) _renderer.enabled = false;
        }

        private void OnDisable()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        private void LateUpdate()
        {
            ResolveCamera();
            if (_camera == null || _particles == null || _renderer == null) return;

            float spaceBlend = ResolveSpaceBlend();
            float visible = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.96f, spaceBlend));
            _renderer.enabled = visible > 0.01f && _material != null;
            if (!_renderer.enabled) return;

            if (_buffer == null || _builtCount != particleCount || _builtSeed != seed)
                RebuildParticles();

            PlanetSkyPalette palette = PlanetSkyController.Instance != null
                ? PlanetSkyController.Instance.CurrentPalette
                : PlanetSkyCatalog.DeepSpace();
            Color tint = Color.Lerp(new Color(0.62f, 0.72f, 0.86f, 1f), palette.NebulaPrimary, 0.22f);
            if (_material.HasProperty(IdTint)) _material.SetColor(IdTint, tint);
            if (_material.HasProperty(IdOpacity)) _material.SetFloat(IdOpacity, visible * opacity);

            _wrapTimer -= Time.unscaledDeltaTime;
            if (_wrapTimer > 0f) return;
            _wrapTimer = 0.12f;
            WrapParticles();
        }

        private float ResolveSpaceBlend()
        {
            var sky = PlanetSkyController.Instance;
            if (sky != null) return sky.SpaceBlend;
            var sample = VoxelEngine.GridSystem.AtmosphereManager.Sample(_camera.transform.position);
            return sample.HasAtmosphere
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.02f, sample.Density01))
                : 1f;
        }

        private void ResolveCamera()
        {
            Camera candidate = Camera.main;
            if (candidate == null)
            {
                var player = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
                if (player != null) candidate = player.playerCamera;
            }
            _camera = candidate;
        }

        private void EnsureSystem()
        {
            if (_particles != null) return;
            var go = new GameObject("SpaceDustField");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            _particles = go.AddComponent<ParticleSystem>();
            _renderer = go.GetComponent<ParticleSystemRenderer>();

            var main = _particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 100000f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.maxParticles = 96;

            var emission = _particles.emission;
            emission.enabled = false;
            var shape = _particles.shape;
            shape.enabled = false;

            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.allowOcclusionWhenDynamic = false;
            _material = CreateMaterial();
            _renderer.sharedMaterial = _material;
        }

        private static Material CreateMaterial()
        {
            var template = Resources.Load<Material>("VoxelEngineRuntime/SpaceDust");
            Material material;
            if (template != null && template.shader != null)
            {
                material = new Material(template);
            }
            else
            {
                Shader shader = Shader.Find("VoxelEngine/SpaceDustURP");
                if (shader == null)
                {
                    Debug.LogError("[SpaceDustRenderer] Space dust shader is unavailable. Run Voxel Engine Setup Step 51.");
                    return null;
                }
                material = new Material(shader);
            }

            material.name = "Mat_SpaceDust_Runtime";
            material.renderQueue = 3050;
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return material;
        }

        private void RebuildParticles()
        {
            if (_camera == null || _particles == null) return;
            int count = Mathf.Clamp(particleCount, 1, 96);
            _buffer = new ParticleSystem.Particle[count];
            _random = new System.Random(seed);
            for (int i = 0; i < count; i++)
                _buffer[i] = CreateParticle(_random, _camera.transform.position);

            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _particles.SetParticles(_buffer, count);
            _particles.Play(true);
            _builtCount = particleCount;
            _builtSeed = seed;
        }

        private void WrapParticles()
        {
            if (_buffer == null || _particles == null || _camera == null) return;
            int count = _particles.GetParticles(_buffer);
            if (count <= 0)
            {
                RebuildParticles();
                return;
            }

            Vector3 cameraPosition = _camera.transform.position;
            float safeNear = Mathf.Max(5f, nearRadius);
            float safeFar = Mathf.Max(safeNear + 20f, farRadius);
            float farSq = safeFar * safeFar;
            if (_random == null) _random = new System.Random(seed);
            bool changed = false;

            for (int i = 0; i < count; i++)
            {
                Vector3 delta = _buffer[i].position - cameraPosition;
                if (delta.sqrMagnitude <= farSq) continue;
                _buffer[i] = CreateParticle(_random, cameraPosition);
                changed = true;
            }

            if (changed) _particles.SetParticles(_buffer, count);
        }

        private ParticleSystem.Particle CreateParticle(System.Random random, Vector3 center)
        {
            float safeNear = Mathf.Max(5f, nearRadius);
            float safeFar = Mathf.Max(safeNear + 20f, farRadius);
            Vector3 direction = RandomUnit(random);
            float distance = Mathf.Lerp(safeNear, safeFar, Mathf.Pow((float)random.NextDouble(), 0.55f));
            float size = Mathf.Lerp(minimumSize, maximumSize, (float)random.NextDouble());
            byte brightness = (byte)Mathf.RoundToInt(Mathf.Lerp(138f, 235f, (float)random.NextDouble()));

            return new ParticleSystem.Particle
            {
                position = center + direction * distance,
                startSize = size,
                startColor = new Color32(brightness, brightness, 255, 255),
                startLifetime = 100000f,
                remainingLifetime = 100000f,
                velocity = Vector3.zero,
                rotation = (float)random.NextDouble() * 360f,
            };
        }

        private static Vector3 RandomUnit(System.Random random)
        {
            float z = (float)random.NextDouble() * 2f - 1f;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(radial * Mathf.Cos(angle), z, radial * Mathf.Sin(angle));
        }
    }
}
