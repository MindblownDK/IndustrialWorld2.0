// Assets/Scripts/VoxelEngine/Weather/WeatherClouds.cs
//
// Cloud coordinator. It owns no geometry of its own any more: it builds the shared
// cloud volume + icosphere and hands one PlanetCloudLayer to every atmospheric body
// in the system, then feeds them their weather every frame.
//
//   • The home world's shell is driven by the live WeatherManager state — clear sky,
//     scattered cloud, overcast, dark raining deck, black storm ceiling, lightning.
//   • Every other atmospheric world runs on its own WeatherClimateProfile, so when you
//     look at a planet from orbit it already wears the sky its climate implies, and
//     that sky keeps evolving while you watch.
//   • Airless bodies never get a shell at all.
//
// Nothing here follows the camera: fly to space and the clouds stay wrapped around the
// planet where they belong.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Builds and drives the per-planet cloud shells. Created automatically by
    /// <see cref="WeatherManager"/> — no prefab or setup step required.
    /// </summary>
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherClouds : MonoBehaviour
    {
        [Header("Blending")]
        [Tooltip("How fast cloud coverage eases between weather states (0..1 per second).")]
        public float coverageBlendSpeed = 0.10f;
        [Tooltip("Seconds between rescans for newly streamed-in celestial bodies.")]
        public float bodyScanInterval = 2f;

        [Header("Volume Quality")]
        [Tooltip("Resolution of the procedural 3D cloud volume (64 = 1 MB, plenty).")]
        [Range(32, 96)] public int volumeResolution = 64;
        [Tooltip("Icosphere subdivisions for the shell mesh (5 = 20k tris, shared by all bodies).")]
        [Range(3, 6)] public int shellSubdivisions = 5;

        private WeatherManager _wm;
        private Mesh _sphere;
        private Texture3D _volume;
        private Shader _shader;
        private float _flash;
        private float _scanTimer;
        private bool _ready;

        private readonly Dictionary<CelestialBody, PlanetCloudLayer> _layers =
            new Dictionary<CelestialBody, PlanetCloudLayer>();
        private readonly List<CelestialBody> _stale = new List<CelestialBody>();

        private void OnEnable()
        {
            _wm = GetComponent<WeatherManager>();
            if (_wm != null) _wm.OnThunder += HandleThunder;
        }

        private void OnDisable()
        {
            if (_wm != null) _wm.OnThunder -= HandleThunder;
            foreach (var layer in _layers.Values)
                if (layer != null) layer.Hide();
        }

        private void OnDestroy()
        {
            foreach (var layer in _layers.Values)
                if (layer != null) Destroy(layer.gameObject);
            _layers.Clear();
            if (_sphere != null) Destroy(_sphere);
            if (_volume != null) Destroy(_volume);
        }

        private void Start()
        {
            _shader = Shader.Find("VoxelEngine/WeatherCloudsURP");
            if (_shader == null)
            {
                Debug.LogWarning("[Weather] WeatherCloudsURP shader not found — planetary clouds disabled.");
                enabled = false;
                return;
            }

            _sphere = BuildIcosphere(shellSubdivisions);
            _volume = BuildCloudVolume(volumeResolution);
            _ready = _sphere != null && _volume != null;
            Debug.Log($"[Weather] Cloud volume {volumeResolution}³ and shell mesh " +
                      $"({_sphere.vertexCount} verts) built — planetary cloud shells online.");
        }

        private void Update()
        {
            if (!_ready) return;

            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - Time.deltaTime * 2.4f);

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = Mathf.Max(0.5f, bodyScanInterval);
                ScanBodies();
            }

            var wm = WeatherManager.Instance;
            CelestialBody home = GravityProvider.ActiveBody;
            Color haze = RenderSettings.fog ? RenderSettings.fogColor : new Color(0.62f, 0.68f, 0.76f);
            float windScale = Mathf.Clamp(WeatherManager.WindMultiplier, 0.5f, 6f);

            foreach (var pair in _layers)
            {
                var body = pair.Key;
                var layer = pair.Value;
                if (body == null || layer == null) continue;

                bool isHome = wm != null && body == home && wm.IsWeatherActive;
                float coverage, storm;
                bool snow;

                if (isHome)
                {
                    coverage = HomeCoverage(wm.TargetState);
                    storm = HomeStorm(wm.TargetState) * Mathf.Max(0.35f, wm.Intensity + 0.35f);
                    snow = wm.IsSnowBiome;
                    layer.Tick(coverage, storm, snow, _flash, haze, coverageBlendSpeed, windScale);
                }
                else
                {
                    AmbientClimate(body, out coverage, out storm, out snow);
                    // A world you are not standing on still lives: its cells drift and its
                    // storms build and fade on a slow, deterministic cycle.
                    layer.Tick(coverage, storm, snow, 0f,
                               new Color(0.70f, 0.76f, 0.84f), coverageBlendSpeed * 0.6f, 1f);
                }
            }
        }

        // ── Weather → sky mapping ────────────────────────────────────

        private static float HomeCoverage(WeatherState state) => state switch
        {
            WeatherState.Clear     => 0.12f,   // a few lazy fair-weather wisps
            WeatherState.Overcast  => 0.64f,
            WeatherState.LightRain => 0.80f,
            WeatherState.HeavyRain => 0.96f,   // solid ceiling, horizon to horizon
            WeatherState.Snow      => 0.74f,
            WeatherState.Blizzard  => 0.96f,
            _ => 0.12f
        };

        private static float HomeStorm(WeatherState state) => state switch
        {
            WeatherState.Clear     => 0f,
            WeatherState.Overcast  => 0.20f,
            WeatherState.LightRain => 0.55f,
            WeatherState.HeavyRain => 1.00f,   // black rain-belly
            WeatherState.Snow      => 0.40f,
            WeatherState.Blizzard  => 0.90f,
            _ => 0f
        };

        /// <summary>
        /// Sky for a world the player is not standing on, from its climate personality.
        /// Slowly oscillates on a deterministic per-body cycle so distant planets are alive.
        /// </summary>
        private static void AmbientClimate(CelestialBody body, out float coverage, out float storm, out bool snow)
        {
            var settings = body != null ? body.settings : null;
            var profile = settings != null ? settings.weather : null;

            if (settings == null || !settings.HasAtmosphere || profile == null || !profile.weatherEnabled)
            {
                coverage = 0f; storm = 0f; snow = false;
                return;
            }

            float seed = settings.bodyName != null ? (settings.bodyName.GetHashCode() & 0xFFFF) * 0.01f : 3.7f;
            float cycle = Mathf.Sin(Time.time * 0.012f + seed) * 0.5f + 0.5f;

            coverage = Mathf.Clamp01(0.22f + profile.overcastBias * 0.55f + (cycle - 0.5f) * 0.24f);
            storm = Mathf.Clamp01(profile.stormChance * (0.35f + cycle * 0.55f));
            snow = profile.precipitation == WeatherClimateProfile.Precipitation.Snow;
        }

        // ── Body tracking ────────────────────────────────────────────

        private void ScanBodies()
        {
            var registry = CosmicRegistry.Instance;
            if (registry != null)
            {
                foreach (var pair in registry.SceneBodies)
                    TryRegister(pair.Value);
            }

            // The home body is guaranteed even if the registry has not published it yet.
            TryRegister(GravityProvider.ActiveBody);

            _stale.Clear();
            foreach (var pair in _layers)
                if (pair.Key == null || pair.Value == null) _stale.Add(pair.Key);
            foreach (var key in _stale)
            {
                if (key != null && _layers.TryGetValue(key, out var layer) && layer != null)
                    Destroy(layer.gameObject);
                _layers.Remove(key);
            }
        }

        private void TryRegister(CelestialBody body)
        {
            if (body == null || _layers.ContainsKey(body)) return;

            var settings = body.settings;
            // No air, no clouds. Vacuum moons and asteroids stay pristine.
            if (settings == null || !settings.HasAtmosphere) return;

            var layer = PlanetCloudLayer.Create(body, _sphere, _volume, _shader);
            if (layer == null) return;

            _layers[body] = layer;
            Debug.Log($"[Weather] Cloud shell created for '{body.DisplayName}' " +
                      $"at radius {layer.ShellRadius:F0} m.");
        }

        private void HandleThunder(Vector3 strikePosition) => _flash = 1f;

        // ── Procedural assets ────────────────────────────────────────

        /// <summary>
        /// Unit icosphere — no poles, no UV seam, evenly distributed triangles. Shared by
        /// every shell (each body just scales its transform), so this is built exactly once.
        /// </summary>
        private static Mesh BuildIcosphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
                new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
                new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;

            var faces = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };

            var midpoints = new Dictionary<long, int>();
            int steps = Mathf.Clamp(subdivisions, 1, 6);
            for (int s = 0; s < steps; s++)
            {
                var next = new List<int>(faces.Count * 4);
                for (int f = 0; f < faces.Count; f += 3)
                {
                    int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                    int ab = Midpoint(a, b, verts, midpoints);
                    int bc = Midpoint(b, c, verts, midpoints);
                    int ca = Midpoint(c, a, verts, midpoints);
                    next.Add(a); next.Add(ab); next.Add(ca);
                    next.Add(b); next.Add(bc); next.Add(ab);
                    next.Add(c); next.Add(ca); next.Add(bc);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }
                faces = next;
            }

            var mesh = new Mesh { name = "CloudShellIcosphere" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(faces, 0);
            mesh.SetNormals(verts.ToArray());       // unit sphere: position IS the normal
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            return mesh;
        }

        private static int Midpoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int existing)) return existing;
            var mid = ((verts[a] + verts[b]) * 0.5f).normalized;
            verts.Add(mid);
            int index = verts.Count - 1;
            cache[key] = index;
            return index;
        }

        /// <summary>
        /// Tileable 3D cloud volume — the whole reason the sphere has no poles, no seams and
        /// no UV pinch: density is a function of the body-local direction, sampled in 3D.
        ///   A = billowy cloud mass (rounded lobes with real gaps),
        ///   R = fast erosion detail (tears the mass edges apart in the shader),
        ///   G/B = reserved, kept at 1.
        /// </summary>
        private static Texture3D BuildCloudVolume(int size)
        {
            size = Mathf.Clamp(size, 32, 96);
            float[] mass = Fbm3D(size, seed: 90210, basePeriod: 2, octaves: 4, gain: 0.52f, billow: true);
            float[] detail = Fbm3D(size, seed: 1337, basePeriod: 4, octaves: 3, gain: 0.58f, billow: true);

            var pixels = new Color32[size * size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                // Contrast the mass so lobes stay rounded and gaps stay genuinely open —
                // a flat fBm ramp is what makes procedural clouds look like grey soup.
                float m = Mathf.Clamp01(mass[i]);
                m = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((m - 0.20f) / 0.62f));
                pixels[i] = new Color32((byte)(Mathf.Clamp01(detail[i]) * 255f), 255, 255,
                                        (byte)(m * 255f));
            }

            var tex = new Texture3D(size, size, size, TextureFormat.RGBA32, true)
            {
                name = "CloudVolume",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// Seamless 3D value-noise fBm on a wrapped lattice. <paramref name="billow"/> folds
        /// each octave around its midpoint, producing the rounded cauliflower lobes real
        /// clouds have instead of smooth hills.
        /// </summary>
        private static float[] Fbm3D(int size, int seed, int basePeriod, int octaves, float gain, bool billow)
        {
            var result = new float[size * size * size];
            var rnd = new System.Random(seed);
            int period = Mathf.Max(2, basePeriod);
            float amplitude = 0.5f;
            float total = 0f;

            for (int o = 0; o < octaves; o++)
            {
                int p = period;
                var lattice = new float[p * p * p];
                for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rnd.NextDouble();

                for (int z = 0; z < size; z++)
                {
                    float fz = (float)z / size * p;
                    int z0 = (int)fz; float tz = Smooth01(fz - z0); int z1 = (z0 + 1) % p;
                    for (int y = 0; y < size; y++)
                    {
                        float fy = (float)y / size * p;
                        int y0 = (int)fy; float ty = Smooth01(fy - y0); int y1 = (y0 + 1) % p;
                        int rowIndex = (z * size + y) * size;
                        for (int x = 0; x < size; x++)
                        {
                            float fx = (float)x / size * p;
                            int x0 = (int)fx; float tx = Smooth01(fx - x0); int x1 = (x0 + 1) % p;

                            float c000 = lattice[(z0 * p + y0) * p + x0];
                            float c100 = lattice[(z0 * p + y0) * p + x1];
                            float c010 = lattice[(z0 * p + y1) * p + x0];
                            float c110 = lattice[(z0 * p + y1) * p + x1];
                            float c001 = lattice[(z1 * p + y0) * p + x0];
                            float c101 = lattice[(z1 * p + y0) * p + x1];
                            float c011 = lattice[(z1 * p + y1) * p + x0];
                            float c111 = lattice[(z1 * p + y1) * p + x1];

                            float v = Mathf.Lerp(
                                Mathf.Lerp(Mathf.Lerp(c000, c100, tx), Mathf.Lerp(c010, c110, tx), ty),
                                Mathf.Lerp(Mathf.Lerp(c001, c101, tx), Mathf.Lerp(c011, c111, tx), ty), tz);

                            if (billow) v = 1f - Mathf.Abs(v * 2f - 1f);
                            result[rowIndex + x] += v * amplitude;
                        }
                    }
                }

                total += amplitude;
                amplitude *= gain;
                period *= 2;
            }

            if (total > 0f)
                for (int i = 0; i < result.Length; i++) result[i] /= total;

            return result;
        }

        private static float Smooth01(float t) => t * t * (3f - 2f * t);
    }
}
