// Assets/Scripts/VoxelEngine/Weather/PlanetCloudLayer.cs
//
// One cloud shell wrapped around one celestial body. Created and driven by
// WeatherClouds — never authored by hand, never saved.
//
// The shell is a unit icosphere (shared mesh, no poles) parented to the body and
// scaled to surfaceRadius + cloud altitude, so it inherits the body's rotation and
// every floating-origin shift for free. Because it is REAL geometry around the
// planet, standing on the surface puts you inside it (a ceiling curving to the
// horizon) and flying to orbit puts you outside it (cloud bands wrapped over the
// world) — with nothing following the camera.

using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Runtime cloud shell for a single <see cref="CelestialBody"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlanetCloudLayer : MonoBehaviour
    {
        private static readonly int IdNoise      = Shader.PropertyToID("_NoiseTex");
        private static readonly int IdTint       = Shader.PropertyToID("_TintColor");
        private static readonly int IdBelly      = Shader.PropertyToID("_BellyColor");
        private static readonly int IdCrown      = Shader.PropertyToID("_CrownColor");
        private static readonly int IdHorizon    = Shader.PropertyToID("_HorizonColor");
        private static readonly int IdCoverage   = Shader.PropertyToID("_Coverage");
        private static readonly int IdStorm      = Shader.PropertyToID("_Storm");
        private static readonly int IdOpacity    = Shader.PropertyToID("_Opacity");
        private static readonly int IdFlash      = Shader.PropertyToID("_Flash");
        private static readonly int IdNoiseScale = Shader.PropertyToID("_NoiseScale");
        private static readonly int IdCellScale  = Shader.PropertyToID("_CellScale");
        private static readonly int IdOffset     = Shader.PropertyToID("_Offset");
        private static readonly int IdDetailOff  = Shader.PropertyToID("_DetailOffset");
        private static readonly int IdCellOff    = Shader.PropertyToID("_CellOffset");
        private static readonly int IdThickness  = Shader.PropertyToID("_ShellThickness");
        private static readonly int IdBodyCenter = Shader.PropertyToID("_BodyCenter");
        private static readonly int IdShellRadius = Shader.PropertyToID("_ShellRadius");

        private CelestialBody _body;
        private Material _material;
        private MeshRenderer _renderer;
        private float _shellRadius;
        private Vector3 _massDrift;
        private Vector3 _detailDrift;
        private Vector3 _cellDrift;
        private Vector3 _massOffset;
        private Vector3 _detailOffset;
        private Vector3 _cellOffset;
        private float _coverage;
        private float _storm;

        /// <summary>The body this shell belongs to.</summary>
        public CelestialBody Body => _body;

        /// <summary>World radius of the shell (body surface + cloud altitude).</summary>
        public float ShellRadius => _shellRadius;

        /// <summary>
        /// Cloud-base altitude above a body's surface, in metres. Shared with
        /// <see cref="WeatherManager"/> so the point where rain stops falling on the player is
        /// exactly the deck they can see above them.
        /// </summary>
        public static float CloudAltitudeFor(float surfaceRadius) =>
            Mathf.Clamp(Mathf.Max(50f, surfaceRadius) * 0.05f, 450f, 5000f);

        internal static PlanetCloudLayer Create(CelestialBody body, Mesh sphere, Texture3D noise, Shader shader)
        {
            if (body == null || sphere == null || noise == null || shader == null) return null;

            float surface = Mathf.Max(50f, body.SurfaceRadius);
            float altitude = CloudAltitudeFor(surface);
            float radius = surface + altitude;

            var go = new GameObject("CloudShell");
            go.transform.SetParent(body.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * radius;
            go.layer = body.gameObject.layer;

            var layer = go.AddComponent<PlanetCloudLayer>();
            layer.Init(body, sphere, noise, shader, radius, altitude);
            return layer;
        }

        private void Init(CelestialBody body, Mesh sphere, Texture3D noise, Shader shader,
                          float radius, float altitude)
        {
            _body = body;
            _shellRadius = radius;

            gameObject.AddComponent<MeshFilter>().sharedMesh = sphere;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            _material = new Material(shader) { name = "CloudShell_" + body.DisplayName };
            _material.SetTexture(IdNoise, noise);
            _material.SetFloat(IdShellRadius, radius);
            _material.SetFloat(IdThickness, Mathf.Clamp(altitude * 0.7f / radius, 0.002f, 0.12f));

            // Feature size stays roughly constant in kilometres across planet sizes, so a
            // small moon does not get a single blob and a big world a grey mush.
            float radiusKm = radius / 1000f;
            _material.SetFloat(IdNoiseScale, Mathf.Clamp(radiusKm * 3.2f, 10f, 52f));
            _material.SetFloat(IdCellScale, Mathf.Clamp(radiusKm * 0.30f, 1.2f, 5f));
            _renderer.sharedMaterial = _material;

            // Deterministic per-body drift so every world's sky moves differently but the
            // same world always behaves the same way.
            int seed = body.DisplayName != null ? body.DisplayName.GetHashCode() : 12345;
            var rnd = new System.Random(seed);
            _massDrift = RandomDirection(rnd) * 0.0035f;
            _detailDrift = RandomDirection(rnd) * 0.02f;
            _cellDrift = RandomDirection(rnd) * 0.0006f;
            _massOffset = RandomDirection(rnd) * 12f;
            _detailOffset = RandomDirection(rnd) * 20f;
            _cellOffset = RandomDirection(rnd) * 7f;

            gameObject.SetActive(false);
        }

        private static Vector3 RandomDirection(System.Random rnd)
        {
            var v = new Vector3((float)rnd.NextDouble() - 0.5f,
                                (float)rnd.NextDouble() - 0.5f,
                                (float)rnd.NextDouble() - 0.5f);
            return v.sqrMagnitude < 1e-5f ? Vector3.forward : v.normalized;
        }

        /// <summary>
        /// Drives this shell for one frame. <paramref name="targetCoverage"/> and
        /// <paramref name="targetStorm"/> come from the live weather on the home world, or
        /// from the body's climate personality on every other world.
        /// </summary>
        internal void Tick(float targetCoverage, float targetStorm, bool snow, float flash,
                           Color haze, float blendSpeed, float windScale)
        {
            if (_material == null) return;

            _coverage = Mathf.MoveTowards(_coverage, Mathf.Clamp01(targetCoverage), blendSpeed * Time.deltaTime);
            _storm = Mathf.MoveTowards(_storm, Mathf.Clamp01(targetStorm), blendSpeed * 2f * Time.deltaTime);

            bool visible = _coverage > 0.02f;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            float dt = Time.deltaTime * Mathf.Max(0.1f, windScale);
            _massOffset += _massDrift * dt;
            _detailOffset += _detailDrift * dt;
            _cellOffset += _cellDrift * dt;

            Color belly = snow ? new Color(0.66f, 0.70f, 0.77f) : new Color(0.40f, 0.43f, 0.49f);
            Color crown = snow ? new Color(0.98f, 0.99f, 1.00f) : new Color(1.00f, 0.99f, 0.96f);
            // A raining sky is a dark sky: the belly falls toward charcoal with storm strength.
            belly = Color.Lerp(belly, snow ? new Color(0.48f, 0.52f, 0.58f) : new Color(0.13f, 0.14f, 0.18f), _storm);

            Color tint = Color.white;
            if (flash > 0f) tint += new Color(0.55f, 0.60f, 0.75f) * flash;

            _material.SetColor(IdTint, tint);
            _material.SetColor(IdBelly, belly);
            _material.SetColor(IdCrown, crown);
            _material.SetColor(IdHorizon, haze);
            _material.SetFloat(IdCoverage, _coverage);
            _material.SetFloat(IdStorm, _storm);
            _material.SetFloat(IdFlash, flash * 0.6f);
            _material.SetFloat(IdOpacity, Mathf.Clamp01(_coverage * 4f));
            _material.SetVector(IdOffset, _massOffset);
            _material.SetVector(IdDetailOff, _detailOffset);
            _material.SetVector(IdCellOff, _cellOffset);
            _material.SetVector(IdBodyCenter, transform.position);
        }

        /// <summary>Instantly hides the shell (body left the simulation / weather disabled).</summary>
        internal void Hide()
        {
            _coverage = 0f;
            _storm = 0f;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
