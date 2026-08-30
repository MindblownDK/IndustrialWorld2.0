// Assets/Scripts/VoxelEngine/Weather/WeatherClouds.cs
//
// The visible SKY half of the weather: a two-layer procedural cloud dome that
// hovers radially overhead and follows the player. Coverage, colour and drift
// are driven by WeatherManager so the sky tells the same story as the rain —
// wisps on Clear, brooding grey on storms — plus a bright pulse through the
// cloud deck on every thunder strike.
//
// Spherical-world safe: the domes align to the active body's radial up
// (never world axes), yaw-free so turning the camera never spins the sky.

using UnityEngine;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Builds and drives the weather cloud domes. Created automatically by
    /// <see cref="WeatherManager"/> — no prefab or setup step required.
    /// </summary>
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherClouds : MonoBehaviour
    {
        [Header("Dome Geometry (metres, along radial up)")]
        [Tooltip("Altitude of the inner (detail) cloud dome above the player.")]
        public float innerAltitude = 150f;
        [Tooltip("Radius of the inner cloud dome.")]
        public float innerRadius = 320f;
        [Tooltip("Altitude of the outer (parallax) cloud dome above the player.")]
        public float outerAltitude = 280f;
        [Tooltip("Radius of the outer cloud dome.")]
        public float outerRadius = 540f;

        [Header("Behaviour")]
        [Tooltip("How fast cloud coverage eases between weather states (0..1 per second).")]
        public float coverageBlendSpeed = 0.06f;
        [Tooltip("Calm cloud drift speed in texture units per second (scales up in wind).")]
        public float driftSpeed = 0.0045f;

        private WeatherManager _wm;
        private Transform _inner;
        private Transform _outer;
        private Material _innerMat;
        private Material _outerMat;
        private Texture2D _cloudTex;
        private Mesh _innerMesh;
        private Mesh _outerMesh;
        private float _coverage;
        private float _flash;
        private Vector2 _drift;

        private void OnEnable()
        {
            _wm = GetComponent<WeatherManager>();
            if (_wm != null) _wm.OnThunder += HandleThunder;
        }

        private void OnDisable()
        {
            if (_wm != null) _wm.OnThunder -= HandleThunder;
            SetDomesActive(false);
        }

        private void OnDestroy()
        {
            if (_inner != null) Destroy(_inner.gameObject);
            if (_outer != null) Destroy(_outer.gameObject);
            if (_innerMesh != null) Destroy(_innerMesh);
            if (_outerMesh != null) Destroy(_outerMesh);
            if (_cloudTex != null) Destroy(_cloudTex);
            if (_innerMat != null) Destroy(_innerMat);
            if (_outerMat != null) Destroy(_outerMat);
        }

        private void Start()
        {
            var shader = Shader.Find("VoxelEngine/WeatherCloudsURP");
            if (shader == null)
            {
                Debug.LogWarning("[Weather] WeatherCloudsURP shader not found — cloud sky disabled.");
                enabled = false;
                return;
            }

            _cloudTex = GenerateCloudTexture(256);
            _inner = CreateDome("WeatherCloudDome_Inner", innerAltitude, innerRadius, shader,
                                new Vector2(3.1f, 1.35f), out _innerMat, out _innerMesh);
            _outer = CreateDome("WeatherCloudDome_Outer", outerAltitude, outerRadius, shader,
                                new Vector2(2.0f, 1.10f), out _outerMat, out _outerMesh);
        }

        private Transform CreateDome(string domeName, float altitude, float radius, Shader shader,
                                     Vector2 tiling, out Material mat, out Mesh mesh)
        {
            var go = new GameObject(domeName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, altitude, 0f);

            mesh = GenerateDomeMesh(radius);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            mat = new Material(shader) { name = domeName + "_Mat" };
            mat.mainTexture = _cloudTex;
            mat.mainTextureScale = tiling;
            mat.SetFloat("_Opacity", 0f);
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            go.SetActive(false);
            return go.transform;
        }

        private void Update()
        {
            if (_inner == null || _outer == null) return;
            var wm = WeatherManager.Instance;
            if (wm == null) return;

            // ── Coverage target from the weather story ──
            float targetCoverage = 0f;
            if (wm.IsWeatherActive)
            {
                switch (wm.TargetState)
                {
                    case WeatherState.Clear:     targetCoverage = 0.10f; break; // a few lazy wisps
                    case WeatherState.Overcast:  targetCoverage = 0.72f; break;
                    case WeatherState.LightRain: targetCoverage = 0.88f; break;
                    case WeatherState.HeavyRain: targetCoverage = 1.00f; break;
                    case WeatherState.Snow:      targetCoverage = 0.82f; break;
                    case WeatherState.Blizzard:  targetCoverage = 1.00f; break;
                }
            }

            _coverage = Mathf.MoveTowards(_coverage, targetCoverage,
                                          coverageBlendSpeed * Time.deltaTime);

            // Lightning brightens the cloud deck from within (decays fast — keep
            // decaying even while the deck is faded out so an old flash can't linger).
            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - Time.deltaTime * 2.4f);

            bool visible = _coverage > 0.015f;
            SetDomesActive(visible);
            if (!visible) return;

            // ── Colour: light overcast → storm-dark, snow stays pale ──
            bool snow = wm.IsSnowBiome;
            Color lightCloud = snow ? new Color(0.88f, 0.90f, 0.94f) : new Color(0.80f, 0.82f, 0.87f);
            Color stormCloud = snow ? new Color(0.70f, 0.73f, 0.79f) : new Color(0.28f, 0.30f, 0.37f);
            Color tint = Color.Lerp(lightCloud, stormCloud, Mathf.Clamp01(wm.Intensity));

            if (_flash > 0f)
                tint += new Color(0.80f, 0.85f, 1.00f) * _flash;

            _innerMat.SetColor("_TintColor", tint);
            _outerMat.SetColor("_TintColor", tint);
            _innerMat.SetFloat("_Opacity", _coverage * 0.95f);
            _outerMat.SetFloat("_Opacity", _coverage * 0.70f);

            // ── Drift: the deck slides with the wind, faster in storms ──
            float speed = driftSpeed * (1f + 2.5f * wm.Intensity);
            _drift += new Vector2(speed, speed * 0.35f) * Time.deltaTime;
            _innerMat.mainTextureOffset = _drift;
            _outerMat.mainTextureOffset = _drift * 0.55f; // parallax: the far deck lags behind
        }

        /// <summary>
        /// Keep the domes radial-up aligned but YAW-FREE: the parent weather frame rotates
        /// with the camera, and a sky that spins when you turn your head would feel wrong.
        /// </summary>
        private void LateUpdate()
        {
            if (_inner == null) return;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, RadialUp());
            _inner.rotation = rot;
            _outer.rotation = rot;
        }

        private void HandleThunder(Vector3 strikePosition) => _flash = 1f;

        private void SetDomesActive(bool active)
        {
            if (_inner != null && _inner.gameObject.activeSelf != active) _inner.gameObject.SetActive(active);
            if (_outer != null && _outer.gameObject.activeSelf != active) _outer.gameObject.SetActive(active);
        }

        private Vector3 RadialUp()
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            return body != null ? body.UpAt(transform.position) : Vector3.up;
        }

        // ── Procedural assets ────────────────────────────────────────

        /// <summary>Cloud dome mesh: a spherical cap around local +Y (the zenith).</summary>
        private static Mesh GenerateDomeMesh(float radius)
        {
            const int rings = 18;                       // zenith → rim
            const int segs = 56;                        // around
            const float phiMax = 82f * Mathf.Deg2Rad;   // how far down from the zenith the cap reaches

            var verts = new Vector3[(rings + 1) * (segs + 1)];
            var colors = new Color[verts.Length];
            var uv = new Vector2[verts.Length];
            var polar = new Vector2[verts.Length];

            int vi = 0;
            for (int r = 0; r <= rings; r++)
            {
                float phi = phiMax * r / rings;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);
                for (int s = 0; s <= segs; s++)
                {
                    float theta = 2f * Mathf.PI * s / segs;
                    verts[vi] = new Vector3(sinPhi * Mathf.Sin(theta), cosPhi,
                                            sinPhi * Mathf.Cos(theta)) * radius;
                    colors[vi] = Color.white;
                    uv[vi] = new Vector2((float)s / segs, (float)r / rings);
                    polar[vi] = new Vector2(0f, (float)r / rings);
                    vi++;
                }
            }

            var tris = new int[rings * segs * 6];
            int ti = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segs; s++)
                {
                    int a = r * (segs + 1) + s;
                    int b = a + 1;
                    int c = a + (segs + 1);
                    int d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }

            var mesh = new Mesh { name = "WeatherCloudDome" };
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.uv2 = polar;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Tileable fBm cloud texture. Alpha carries cloud density (billowy shapes with
        /// real gaps); RGB carries a subtle brightness variation so the deck has depth
        /// even under a flat grey tint. Wraps seamlessly in both axes.
        /// </summary>
        private static Texture2D GenerateCloudTexture(int size)
        {
            int octaves = 5;
            var fbm = new float[size * size];

            var rnd = new System.Random(90210);
            int period = 4;
            float amplitude = 0.5f;
            float total = 0f;

            for (int o = 0; o < octaves; o++)
            {
                // One wrapped random lattice per octave — wrapping the lattice makes the
                // whole octave (and therefore the fBm) tile seamlessly.
                var lattice = new float[period * period];
                for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rnd.NextDouble();

                for (int y = 0; y < size; y++)
                {
                    float fy = (float)y / size * period;
                    int y0 = (int)fy;
                    float ty = Smooth01(fy - y0);
                    int y1 = (y0 + 1) % period;
                    for (int x = 0; x < size; x++)
                    {
                        float fx = (float)x / size * period;
                        int x0 = (int)fx;
                        float tx = Smooth01(fx - x0);
                        int x1 = (x0 + 1) % period;

                        float v00 = lattice[y0 * period + x0];
                        float v01 = lattice[y0 * period + x1];
                        float v10 = lattice[y1 * period + x0];
                        float v11 = lattice[y1 * period + x1];
                        float v = Mathf.Lerp(Mathf.Lerp(v00, v01, tx),
                                             Mathf.Lerp(v10, v11, tx), ty);
                        fbm[y * size + x] += v * amplitude;
                    }
                }

                total += amplitude;
                amplitude *= 0.55f;
                period *= 2;
            }

            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float d = Mathf.Clamp01(fbm[i] / total);
                float alpha = Mathf.SmoothStep(0.40f, 0.78f, d);
                float shade = 0.80f + 0.20f * d;   // brighter on thick spots
                pixels[i] = new Color(shade, shade, shade, alpha);
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "WeatherCloudTexture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return tex;
        }

        private static float Smooth01(float t) => t * t * (3f - 2f * t);
    }
}
