// Assets/Scripts/VoxelEngine/Weather/WeatherClouds.cs
//
// The visible SKY half of the weather: a horizon-fitted, world-anchored cloud
// ceiling that hovers radially overhead and follows the player. Coverage, colour
// and drift are driven by WeatherManager so the sky tells the same story as the
// rain — wisps on Clear, a brooding rain-belly ceiling on storms — plus a bright
// pulse through the deck on every thunder strike.
//
// Geometry note (this is what makes it read as SKY and not as a disc):
// each layer's mesh is built in VIEW-ELEVATION space. A ring at elevation e sits
// at distance height / sin(e) — exactly where a flat layer at that height would
// be — clamped to a maximum sight distance, and the rim ring is pushed a few
// degrees BELOW the eye line. The ceiling therefore runs continuously from the
// zenith down past the horizon and dissolves into the haze, instead of ending in
// a circular edge somewhere overhead.
//
// Spherical-world safe: the layers align to the active body's radial up (never
// world axes), yaw-free so turning the camera never spins the sky, and the
// texture is anchored to the world so walking gives real parallax between decks.

using UnityEngine;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Builds and drives the weather cloud ceiling. Created automatically by
    /// <see cref="WeatherManager"/> — no prefab or setup step required.
    /// </summary>
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherClouds : MonoBehaviour
    {
        [Header("Low Deck (the rain ceiling)")]
        [Tooltip("Height of the low rain deck above the player, in metres.")]
        public float lowerHeight = 240f;
        [Tooltip("Furthest the low deck is drawn before it melts into the haze, in metres.")]
        public float lowerViewDistance = 5200f;
        [Tooltip("Metres of world per cloud texture tile on the low deck (bigger = bigger clouds).")]
        public float lowerCloudScale = 1150f;

        [Header("High Deck (parallax + depth)")]
        [Tooltip("Height of the high deck above the player, in metres.")]
        public float upperHeight = 820f;
        [Tooltip("Furthest the high deck is drawn, in metres.")]
        public float upperViewDistance = 11000f;
        [Tooltip("Metres of world per cloud texture tile on the high deck.")]
        public float upperCloudScale = 3400f;

        [Header("Behaviour")]
        [Tooltip("How fast cloud coverage eases between weather states (0..1 per second).")]
        public float coverageBlendSpeed = 0.09f;
        [Tooltip("Calm cloud drift speed in metres per second (scales up in wind).")]
        public float driftSpeed = 5.5f;

        // Shader property ids (cached — these are written every frame).
        private static readonly int IdTint       = Shader.PropertyToID("_TintColor");
        private static readonly int IdBase       = Shader.PropertyToID("_BaseColor");
        private static readonly int IdTop        = Shader.PropertyToID("_TopColor");
        private static readonly int IdHorizon    = Shader.PropertyToID("_HorizonColor");
        private static readonly int IdCoverage   = Shader.PropertyToID("_Coverage");
        private static readonly int IdOpacity    = Shader.PropertyToID("_Opacity");
        private static readonly int IdFlash      = Shader.PropertyToID("_Flash");
        private static readonly int IdDetailOff  = Shader.PropertyToID("_DetailOffset");
        private static readonly int IdEdgeSoft   = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int IdRelief     = Shader.PropertyToID("_Relief");
        private static readonly int IdPuff       = Shader.PropertyToID("_Puff");
        private static readonly int IdDetailTile = Shader.PropertyToID("_DetailScale");

        private WeatherManager _wm;
        private Deck _low;
        private Deck _high;
        private Texture2D _cloudTex;
        private float _coverage;
        private float _flash;
        private Vector3 _lastWorldPos;
        private bool _hasLastPos;

        /// <summary>One cloud layer: mesh, material and its own world-anchored UV offset.</summary>
        private sealed class Deck
        {
            public Transform Root;
            public Material Mat;
            public Mesh Mesh;
            public float UvPerMetre;
            public Vector2 Offset;
            public Vector2 DetailOffset;
        }

        private void OnEnable()
        {
            _wm = GetComponent<WeatherManager>();
            if (_wm != null) _wm.OnThunder += HandleThunder;
        }

        private void OnDisable()
        {
            if (_wm != null) _wm.OnThunder -= HandleThunder;
            SetDecksActive(false);
        }

        private void OnDestroy()
        {
            DestroyDeck(_low);
            DestroyDeck(_high);
            if (_cloudTex != null) Destroy(_cloudTex);
        }

        private static void DestroyDeck(Deck deck)
        {
            if (deck == null) return;
            if (deck.Root != null) Destroy(deck.Root.gameObject);
            if (deck.Mesh != null) Destroy(deck.Mesh);
            if (deck.Mat != null) Destroy(deck.Mat);
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

            _low = CreateDeck("WeatherCloudDeck_Low", shader,
                              lowerHeight, lowerViewDistance, lowerCloudScale,
                              detailTiling: 4.6f, relief: 16f, puff: 38f, edgeSoftness: 0.20f);
            _high = CreateDeck("WeatherCloudDeck_High", shader,
                               upperHeight, upperViewDistance, upperCloudScale,
                               detailTiling: 3.1f, relief: 8f, puff: 70f, edgeSoftness: 0.30f);
        }

        private Deck CreateDeck(string deckName, Shader shader, float height, float viewDistance,
                                float cloudScale, float detailTiling, float relief, float puff,
                                float edgeSoftness)
        {
            float uvPerMetre = 1f / Mathf.Max(1f, cloudScale);

            var go = new GameObject(deckName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var mesh = GenerateLayerMesh(height, viewDistance, uvPerMetre);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mat = new Material(shader) { name = deckName + "_Mat" };
            mat.mainTexture = _cloudTex;
            mat.SetFloat(IdOpacity, 0f);
            mat.SetFloat(IdCoverage, 0f);
            mat.SetFloat(IdDetailTile, detailTiling);
            mat.SetFloat(IdRelief, relief);
            mat.SetFloat(IdPuff, puff);
            mat.SetFloat(IdEdgeSoft, edgeSoftness);

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            go.SetActive(false);
            return new Deck { Root = go.transform, Mat = mat, Mesh = mesh, UvPerMetre = uvPerMetre };
        }

        private void Update()
        {
            if (_low == null || _high == null) return;
            var wm = WeatherManager.Instance;
            if (wm == null) return;

            // ── Coverage target from the weather story ──
            float targetCoverage = 0f;
            if (wm.IsWeatherActive)
            {
                switch (wm.TargetState)
                {
                    case WeatherState.Clear:     targetCoverage = 0.16f; break; // a few lazy wisps
                    case WeatherState.Overcast:  targetCoverage = 0.74f; break;
                    case WeatherState.LightRain: targetCoverage = 0.88f; break;
                    case WeatherState.HeavyRain: targetCoverage = 1.00f; break;
                    case WeatherState.Snow:      targetCoverage = 0.84f; break;
                    case WeatherState.Blizzard:  targetCoverage = 1.00f; break;
                }
            }

            _coverage = Mathf.MoveTowards(_coverage, targetCoverage, coverageBlendSpeed * Time.deltaTime);

            // Lightning brightens the deck from within (decays fast — keep decaying even
            // while the deck is faded out so an old flash can't linger).
            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - Time.deltaTime * 2.4f);

            bool visible = _coverage > 0.02f;
            SetDecksActive(visible);
            if (!visible) { _hasLastPos = false; return; }

            // ── Colour: bright fair-weather cloud → heavy storm belly, snow stays pale ──
            bool snow = wm.IsSnowBiome;
            float storm = Mathf.Clamp01(wm.Intensity);

            Color calmBelly  = snow ? new Color(0.72f, 0.75f, 0.81f) : new Color(0.62f, 0.65f, 0.72f);
            Color stormBelly = snow ? new Color(0.52f, 0.56f, 0.63f) : new Color(0.20f, 0.22f, 0.27f);
            Color calmCrown  = snow ? new Color(0.98f, 0.99f, 1.00f) : new Color(1.00f, 0.99f, 0.96f);
            Color stormCrown = snow ? new Color(0.86f, 0.89f, 0.94f) : new Color(0.55f, 0.58f, 0.66f);

            Color belly = Color.Lerp(calmBelly, stormBelly, storm);
            Color crown = Color.Lerp(calmCrown, stormCrown, storm);

            // Blend the deck into whatever haze the scene is actually using so the far
            // edge is invisible: weather fog when it owns fog, otherwise the sky's fog.
            Color haze = RenderSettings.fog
                ? RenderSettings.fogColor
                : Color.Lerp(new Color(0.62f, 0.68f, 0.76f), belly, 0.5f);

            ApplyDeckLook(_low, belly, crown, haze, _coverage, 0.98f, storm);
            // The high deck is thinner, paler and lags on coverage — it reads as distance.
            ApplyDeckLook(_high, Color.Lerp(belly, crown, 0.35f), crown, haze,
                          _coverage * 0.85f, 0.62f, storm);

            // ── World-anchored drift: wind moves the deck, walking gives parallax ──
            UpdateDrift(storm);
        }

        private void ApplyDeckLook(Deck deck, Color belly, Color crown, Color haze,
                                   float coverage, float opacity, float storm)
        {
            if (deck?.Mat == null) return;

            Color tint = Color.white;
            if (_flash > 0f) tint += new Color(0.55f, 0.60f, 0.75f) * _flash;

            deck.Mat.SetColor(IdTint, tint);
            deck.Mat.SetColor(IdBase, belly);
            deck.Mat.SetColor(IdTop, crown);
            deck.Mat.SetColor(IdHorizon, haze);
            deck.Mat.SetFloat(IdCoverage, coverage);
            deck.Mat.SetFloat(IdOpacity, Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, coverage * 3.2f)) * opacity);
            deck.Mat.SetFloat(IdFlash, _flash * (0.35f + 0.35f * storm));
        }

        /// <summary>
        /// Scrolls each deck's UVs by (a) the wind and (b) the player's own horizontal
        /// movement, so the clouds stay pinned to the world instead of sliding along with
        /// the camera. The two decks move at different rates → honest parallax.
        /// </summary>
        private void UpdateDrift(float storm)
        {
            Quaternion rot = DeckRotation();
            Vector3 pos = transform.position;
            Vector3 delta = _hasLastPos ? pos - _lastWorldPos : Vector3.zero;
            _lastWorldPos = pos;
            _hasLastPos = true;

            // Ignore teleports / floating-origin shifts: a huge jump would smear the sky.
            if (delta.sqrMagnitude > 40000f) delta = Vector3.zero;

            Vector3 localDelta = Quaternion.Inverse(rot) * delta;
            Vector2 travel = new Vector2(localDelta.x, localDelta.z);

            Vector3 windWorld = Vector3.zero;
            var wind = VoxelEngine.Cosmos.WindField.Instance;
            if (wind != null) windWorld = wind.Direction;
            if (windWorld.sqrMagnitude < 0.0001f) windWorld = rot * Vector3.forward;

            Vector3 windLocal = Quaternion.Inverse(rot) * windWorld;
            Vector2 windDir = new Vector2(windLocal.x, windLocal.z);
            if (windDir.sqrMagnitude > 0.0001f) windDir.Normalize();

            float speed = driftSpeed * (1f + 2.2f * storm);
            Vector2 windMetres = windDir * (speed * Time.deltaTime);

            AdvanceDeck(_low, windMetres, travel, 1f);
            AdvanceDeck(_high, windMetres, travel, 0.55f);   // far deck lags behind
        }

        private static void AdvanceDeck(Deck deck, Vector2 windMetres, Vector2 travelMetres, float windScale)
        {
            if (deck?.Mat == null) return;

            deck.Offset += (windMetres * windScale - travelMetres) * deck.UvPerMetre;
            deck.Offset = Wrap(deck.Offset);
            deck.Mat.mainTextureOffset = deck.Offset;

            // The erosion octave crawls slightly faster and sideways, so cloud shapes
            // evolve and dissolve instead of sliding rigidly across the sky.
            deck.DetailOffset += new Vector2(windMetres.y * -0.8f, windMetres.x * 0.8f) * deck.UvPerMetre * 3.2f
                                 + (windMetres * windScale * 0.35f - travelMetres) * deck.UvPerMetre * 4.6f;
            deck.DetailOffset = Wrap(deck.DetailOffset);
            deck.Mat.SetVector(IdDetailOff, new Vector4(deck.DetailOffset.x, deck.DetailOffset.y, 0f, 0f));
        }

        /// <summary>Keeps UV offsets in [0,1) so float precision never degrades over a long session.</summary>
        private static Vector2 Wrap(Vector2 v) =>
            new Vector2(v.x - Mathf.Floor(v.x), v.y - Mathf.Floor(v.y));

        /// <summary>
        /// Keep the decks radial-up aligned but YAW-FREE: the parent weather frame rotates
        /// with the camera, and a sky that spins when you turn your head would feel wrong.
        /// </summary>
        private void LateUpdate()
        {
            if (_low?.Root == null) return;
            Quaternion rot = DeckRotation();
            _low.Root.rotation = rot;
            if (_high?.Root != null) _high.Root.rotation = rot;
        }

        private Quaternion DeckRotation() => Quaternion.FromToRotation(Vector3.up, RadialUp());

        private void HandleThunder(Vector3 strikePosition) => _flash = 1f;

        private void SetDecksActive(bool active)
        {
            SetDeckActive(_low, active);
            SetDeckActive(_high, active);
        }

        private static void SetDeckActive(Deck deck, bool active)
        {
            if (deck?.Root != null && deck.Root.gameObject.activeSelf != active)
                deck.Root.gameObject.SetActive(active);
        }

        private Vector3 RadialUp()
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            return body != null ? body.UpAt(transform.position) : Vector3.up;
        }

        // ── Procedural assets ────────────────────────────────────────

        /// <summary>
        /// Horizon-fitted cloud layer: rings are placed by VIEW ELEVATION (dense near the
        /// horizon, sparse at the zenith) at the distance a flat layer of the given height
        /// would actually occupy, clamped to <paramref name="viewDistance"/>. The final ring
        /// sits below the eye line so the deck never shows a circular edge. UVs are planar
        /// world metres — no polar pinch at the zenith.
        /// </summary>
        private static Mesh GenerateLayerMesh(float height, float viewDistance, float uvPerMetre)
        {
            const int rings = 40;                 // zenith → below the horizon
            const int segs = 96;                  // around
            const float rimElevationDeg = -5f;    // last ring dips under the eye line
            const float bias = 2.6f;              // pack rings toward the horizon

            height = Mathf.Max(1f, height);
            viewDistance = Mathf.Max(height * 3f, viewDistance);
            float sinLimit = height / viewDistance;

            int vertsPerRing = segs + 1;
            var verts = new Vector3[(rings + 1) * vertsPerRing];
            var uv = new Vector2[verts.Length];
            var uv2 = new Vector2[verts.Length];

            int vi = 0;
            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                float elevDeg = r == rings
                    ? rimElevationDeg
                    : Mathf.Lerp(90f, rimElevationDeg, 1f - Mathf.Pow(1f - t, bias));

                float elev = elevDeg * Mathf.Deg2Rad;
                float sinE = Mathf.Sin(elev);
                float cosE = Mathf.Cos(elev);
                float dist = sinE > sinLimit ? height / sinE : viewDistance;

                for (int s = 0; s <= segs; s++)
                {
                    float theta = 2f * Mathf.PI * s / segs;
                    var p = new Vector3(cosE * Mathf.Sin(theta) * dist,
                                        sinE * dist,
                                        cosE * Mathf.Cos(theta) * dist);
                    verts[vi] = p;
                    uv[vi] = new Vector2(p.x, p.z) * uvPerMetre;
                    uv2[vi] = new Vector2(t, 0f);
                    vi++;
                }
            }

            var tris = new int[rings * segs * 6];
            int ti = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segs; s++)
                {
                    int a = r * vertsPerRing + s;
                    int b = a + 1;
                    int c = a + vertsPerRing;
                    int d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }

            var mesh = new Mesh { name = "WeatherCloudLayer" };
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.uv2 = uv2;
            mesh.triangles = tris;
            // The deck rides the camera, so a generous fixed bound keeps it from being
            // frustum-culled by a stale bounding volume.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (viewDistance * 4f));
            return mesh;
        }

        /// <summary>
        /// Tileable cloud noise, seamless in both axes.
        ///   A = billowy cloud MASS (rounded lobes with real gaps — cumulus, not fog),
        ///   R = fast erosion detail (tears the mass edges apart in the shader),
        ///   G = mid-frequency variance (subtle internal brightness breakup),
        ///   B = 1.
        /// </summary>
        private static Texture2D GenerateCloudTexture(int size)
        {
            float[] mass = Fbm(size, seed: 90210, basePeriod: 3, octaves: 5, gain: 0.52f, billow: true);
            float[] detail = Fbm(size, seed: 1337, basePeriod: 8, octaves: 4, gain: 0.58f, billow: true);
            float[] variance = Fbm(size, seed: 4242, basePeriod: 5, octaves: 3, gain: 0.5f, billow: false);

            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                // Contrast the mass so lobes stay rounded and the gaps stay open —
                // a flat fBm ramp is what makes procedural clouds look like grey soup.
                float m = Mathf.Clamp01(mass[i]);
                m = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((m - 0.18f) / 0.64f));
                m = Mathf.Pow(m, 1.15f);

                pixels[i] = new Color(Mathf.Clamp01(detail[i]),
                                      Mathf.Clamp01(variance[i]),
                                      1f,
                                      m);
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "WeatherCloudTexture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 8
            };
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// Seamless value-noise fBm on a wrapped lattice. <paramref name="billow"/> folds each
        /// octave around its midpoint, which produces the rounded, cauliflower-like lobes real
        /// clouds have instead of smooth hills.
        /// </summary>
        private static float[] Fbm(int size, int seed, int basePeriod, int octaves, float gain, bool billow)
        {
            var result = new float[size * size];
            var rnd = new System.Random(seed);
            int period = Mathf.Max(2, basePeriod);
            float amplitude = 0.5f;
            float total = 0f;

            for (int o = 0; o < octaves; o++)
            {
                var lattice = new float[period * period];
                for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rnd.NextDouble();

                for (int y = 0; y < size; y++)
                {
                    float fy = (float)y / size * period;
                    int y0 = (int)fy;
                    float ty = Smooth01(fy - y0);
                    int y1 = (y0 + 1) % period;
                    int rowA = y0 * period, rowB = y1 * period;

                    for (int x = 0; x < size; x++)
                    {
                        float fx = (float)x / size * period;
                        int x0 = (int)fx;
                        float tx = Smooth01(fx - x0);
                        int x1 = (x0 + 1) % period;

                        float v = Mathf.Lerp(Mathf.Lerp(lattice[rowA + x0], lattice[rowA + x1], tx),
                                             Mathf.Lerp(lattice[rowB + x0], lattice[rowB + x1], tx), ty);
                        if (billow) v = 1f - Mathf.Abs(v * 2f - 1f);
                        result[y * size + x] += v * amplitude;
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
