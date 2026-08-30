// Assets/Scripts/VoxelEngine/Weather/WeatherAudio.cs
//
// Procedural + SPATIAL weather audio. Everything is synthesised at runtime from
// pure code (no audio files needed), then rendered as a proper 3D soundscape:
//
//   1) OUTDOOR RAIN BED — a wide stereo loop: pink-noise downpour body + hundreds
//      of tiny droplet transients per second + heavier ground splats. It reads as
//      RAIN, not as filtered static. Low-passed when you step behind walls.
//   2) INDOOR BED — the sheltered mix: rain muffled through the structure, deep
//      building shudder, and settling drips with a little pitch sag.
//   3) RAIN ON SURFACES — 3D POSITIONAL pattering. Rain on your metal roof plays
//      from the actual roof point above you (pan + distance correct); rain on
//      wood comes from the wooden roof/wall. Stand in a cave and neither plays.
//   4) THUNDER WITH PHYSICS — the flash is instant, the rumble arrives
//      speed-of-sound late (343 m/s), from the strike's real direction: near
//      strikes crack, distant ones only roll.
//   5) WIND — gusting stereo wind for snow/blizzards (and a whisper in storms).
//
// Spherical-world safe: every "up" is the active body's radial up.

using System.Collections;
using UnityEngine;
using VoxelEngine.Building.Tiered;

namespace VoxelEngine.Weather
{
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherAudio : MonoBehaviour
    {
        // ── Audio sources (each on its own host so filters can't bleed) ──
        private AudioSource _outdoor;     // 2D wide stereo rain bed
        private AudioSource _indoor;      // 2D muffled sheltered mix
        private AudioSource _wind;        // 2D stereo wind
        private AudioSource _metal;       // 3D — rain hammering metal (roof/structures)
        private AudioSource _wood;        // 3D — rain on wood (roof/walls)
        private AudioSource _thunder;     // 3D — repositioned per strike

        private AudioLowPassFilter _outdoorLp;
        private AudioLowPassFilter _metalLp;
        private AudioLowPassFilter _woodLp;
        private AudioLowPassFilter _thunderLp;

        private Transform _metalHost;
        private Transform _woodHost;
        private Transform _thunderHost;

        // ── Generated clips ──
        private AudioClip _outdoorClip;
        private AudioClip _indoorClip;
        private AudioClip _metalClip;
        private AudioClip _woodClip;
        private AudioClip _windClip;
        private AudioClip _thunderNearClip;
        private AudioClip _thunderFarClip;

        private WeatherManager _wm;
        private Transform _listener;

        // ── Shelter state ──
        private bool _sheltered;
        private bool _roofIsMetal;
        private bool _roofIsWood;
        private bool _inCave;
        private Vector3 _roofPoint;
        private float _indoorBlend;          // smoothed 0 (outside) → 1 (sheltered)

        // ── Nearby surface scan ──
        private float _metalAmount;
        private float _woodAmount;
        private Vector3 _metalPoint;
        private Vector3 _woodPoint;

        private float _roofCheckTimer;
        private float _surfaceScanTimer;

        private const int SR = 44100;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Start()
        {
            // Synthesis (a one-off cost at world load, well under a second total).
            BakeOutdoorRain();
            BakeIndoorRain();
            BakeMetalPatter();
            BakeWoodPatter();
            BakeWind();
            BakeThunder();

            // 2D beds.
            _outdoor = CreateSource(CreatePointHost("WeatherAudio_Outdoor"), _outdoorClip, loop: true, spatial: false);
            _outdoorLp = _outdoor.gameObject.AddComponent<AudioLowPassFilter>();
            _outdoorLp.cutoffFrequency = 22000f;

            _indoor = CreateSource(CreatePointHost("WeatherAudio_Indoor"), _indoorClip, loop: true, spatial: false);
            _wind   = CreateSource(CreatePointHost("WeatherAudio_Wind"),   _windClip,   loop: true, spatial: false);

            // 3D positional sources.
            _metalHost = CreatePointHost("WeatherAudio_Metal");
            _metal = CreateSource(_metalHost, _metalClip, loop: true, spatial: true);
            _metal.minDistance = 2f; _metal.maxDistance = 28f; _metal.spread = 55f;
            _metalLp = _metal.gameObject.AddComponent<AudioLowPassFilter>();
            _metalLp.cutoffFrequency = 22000f;

            _woodHost = CreatePointHost("WeatherAudio_Wood");
            _wood = CreateSource(_woodHost, _woodClip, loop: true, spatial: true);
            _wood.minDistance = 2f; _wood.maxDistance = 24f; _wood.spread = 55f;
            _woodLp = _wood.gameObject.AddComponent<AudioLowPassFilter>();
            _woodLp.cutoffFrequency = 22000f;

            _thunderHost = CreatePointHost("WeatherAudio_Thunder");
            _thunder = CreateSource(_thunderHost, _thunderFarClip, loop: false, spatial: true);
            _thunder.rolloffMode = AudioRolloffMode.Linear;
            _thunder.minDistance = 400f; _thunder.maxDistance = 4000f; _thunder.spread = 35f;
            _thunderLp = _thunder.gameObject.AddComponent<AudioLowPassFilter>();
            _thunderLp.cutoffFrequency = 22000f;

            _wm = GetComponent<WeatherManager>();
            if (_wm != null) _wm.OnThunder += HandleThunder;
        }

        private void OnDestroy()
        {
            if (_wm != null) _wm.OnThunder -= HandleThunder;
        }

        // ── Thunder: real direction + real speed-of-sound delay ───────

        /// <summary>
        /// A strike happened (WeatherManager.OnThunder). The flash is already out;
        /// schedule the rumble to arrive late, from the strike's direction.
        /// </summary>
        private void HandleThunder(Vector3 strike)
        {
            if (_thunder == null) return;
            Vector3 pos = ListenerPos();
            Vector3 toStrike = strike - pos;
            float dist = Mathf.Max(1f, toStrike.magnitude);
            Vector3 dir = toStrike / dist;

            float delay = Mathf.Clamp(dist / 343f, 0.3f, 6.5f);   // light first, sound later
            float intensity = _wm != null ? Mathf.Max(0.55f, _wm.Intensity) : 1f;
            StartCoroutine(ThunderAfter(delay, dir, dist, intensity));
        }

        private IEnumerator ThunderAfter(float delay, Vector3 dir, float dist, float intensity)
        {
            yield return new WaitForSeconds(delay);
            if (_thunder == null) yield break;

            // Position the source toward the strike so the stereo image points at it
            // (its rolloff window keeps it loud — we control volume by distance here).
            Vector3 up = RadialUp();
            Vector3 flat = dir - up * Vector3.Dot(dir, up);
            if (flat.sqrMagnitude < 1f) flat = Vector3.ProjectOnPlane(Random.insideUnitSphere, up);
            _thunderHost.position = ListenerPos() + flat.normalized * 180f + up * 70f;

            bool near = dist < 1400f;
            _thunder.clip = near ? _thunderNearClip : _thunderFarClip;
            float distanceFade = Mathf.Clamp01(1f - (dist - 400f) / 3200f);
            _thunder.volume = Mathf.Clamp01(0.25f + 0.75f * distanceFade) * intensity * Random.Range(0.7f, 1f);
            _thunder.pitch = Random.Range(0.85f, 1.05f);
            _thunder.Play();
        }

        // ── Per-frame mix ─────────────────────────────────────────────

        private void Update()
        {
            if (_outdoor == null) return;
            var wm = WeatherManager.Instance;
            if (wm == null) return;
            _wm = wm;

            if (_listener == null && wm.playerCamera != null) _listener = wm.playerCamera;

            float intensity = wm.Intensity;
            bool isSnow = wm.IsSnowBiome &&
                          (wm.CurrentState == WeatherState.Snow ||
                           wm.CurrentState == WeatherState.Blizzard ||
                           wm.TargetState == WeatherState.Snow ||
                           wm.TargetState == WeatherState.Blizzard);
            bool isRain = !isSnow && intensity > 0.02f;

            // Shelter probe (cheap; every 0.4 s).
            _roofCheckTimer += Time.deltaTime;
            if (_roofCheckTimer >= 0.4f)
            {
                _roofCheckTimer = 0f;
                ProbeShelter();
            }

            _indoorBlend = Mathf.MoveTowards(_indoorBlend, _sheltered ? 1f : 0f, Time.deltaTime * 1.4f);
            float inside = _indoorBlend;
            float outside = 1f - inside;

            // Nearby structure scan informs the 3D pattering (every 1.5 s while raining).
            if (isRain)
            {
                _surfaceScanTimer += Time.deltaTime;
                if (_surfaceScanTimer >= 1.5f)
                {
                    _surfaceScanTimer = 0f;
                    ScanSurfaceMaterials();
                }
            }

            float k = Time.deltaTime * 3f;

            // ── Rain beds: outdoor downpour ↔ sheltered interior ──
            // (gated by isRain — snow gets wind + the sheltered bed, never the rain bed)
            float outdoorTarget = isRain ? intensity * (0.80f * outside + 0.16f * inside) : 0f;
            float indoorTarget = isRain
                ? intensity * 0.85f * inside
                : (isSnow ? intensity * 0.30f * inside : 0f);   // blizzard sheltered bed
            _outdoor.volume = Mathf.Lerp(_outdoor.volume, outdoorTarget, k);
            _indoor.volume = Mathf.Lerp(_indoor.volume, indoorTarget, k);
            _outdoorLp.cutoffFrequency = Mathf.Lerp(22000f, 750f, inside);   // walls eat the highs
            _outdoor.pitch = Mathf.Lerp(1f, 0.93f, inside);

            // ── 3D surface pattering ──
            // Rain on YOUR roof (from the actual roof point above you) or on the
            // nearest metal/wood structures (from their direction, fading with distance).
            float metalRoofVol = _roofIsMetal ? intensity * 0.80f : 0f;
            float metalNearVol = _metalAmount * intensity * 0.55f * outside;
            float metalVol = Mathf.Max(metalRoofVol, metalNearVol);

            float woodRoofVol = _roofIsWood ? intensity * 0.60f : 0f;
            float woodNearVol = _woodAmount * intensity * 0.38f * outside;
            float woodVol = Mathf.Max(woodRoofVol, woodNearVol);

            _metal.volume = Mathf.Lerp(_metal.volume, isRain ? metalVol : 0f, k);
            _wood.volume = Mathf.Lerp(_wood.volume, isRain ? woodVol : 0f, k);
            _metalLp.cutoffFrequency = Mathf.Lerp(22000f, 1500f, inside);    // heard through the roof
            _woodLp.cutoffFrequency = Mathf.Lerp(22000f, 1200f, inside);
            _thunderLp.cutoffFrequency = Mathf.Lerp(22000f, 1800f, inside);

            // Keep the 3D hosts at the surfaces they voice.
            Vector3 upNow = RadialUp();
            Vector3 fallback = ListenerPos() + upNow * 3f;
            _metalHost.position = _roofIsMetal ? _roofPoint + upNow * 0.4f
                             : (_metalAmount > 0.01f ? _metalPoint : fallback);
            _woodHost.position = _roofIsWood ? _roofPoint + upNow * 0.4f
                            : (_woodAmount > 0.01f ? _woodPoint : fallback);

            // ── Wind: blizzards howl, storms whisper ──
            float windTarget = 0f;
            if (isSnow && intensity > 0.05f) windTarget = intensity * 0.50f;
            else if (isRain) windTarget = intensity * 0.10f;
            windTarget *= 1f - 0.55f * inside;                     // walls hush the wind too
            _wind.volume = Mathf.Lerp(_wind.volume, windTarget, k);

            // ── Play states (volumes carry the fade; silent sources cost nothing) ──
            if (isRain || (isSnow && inside > 0.05f))
            {
                EnsurePlaying(_outdoor);
                EnsurePlaying(_indoor);
            }
            if (isRain && metalVol > 0.01f) EnsurePlaying(_metal);
            if (isRain && woodVol > 0.01f) EnsurePlaying(_wood);
            if (windTarget > 0.01f) EnsurePlaying(_wind);
        }

        // ── World probes ──────────────────────────────────────────────

        /// <summary>Shelter probe: roof above (with its material) or a cave ceiling.</summary>
        private void ProbeShelter()
        {
            _sheltered = false;
            _roofIsMetal = false;
            _roofIsWood = false;
            _inCave = false;
            _roofPoint = transform.position;

            Vector3 up = RadialUp();

            // Check 1: built roof directly overhead (radial up — never world +Y).
            if (Physics.Raycast(transform.position, up, out var hit, 10f))
            {
                var tiered = hit.collider.GetComponentInParent<PlacedTieredBlock>();
                var placed = hit.collider.GetComponentInParent<Building.PlacedBlock>();
                if (tiered != null || placed != null)
                {
                    _sheltered = true;
                    _roofPoint = hit.point;
                    if (tiered != null)
                    {
                        _roofIsMetal = tiered.tier == BuildTier.Iron || tiered.tier == BuildTier.Steel;
                        _roofIsWood = tiered.tier == BuildTier.Wood;
                    }
                    return;
                }
            }

            // Check 2: natural cave — sample voxels ALONG the radial up direction
            // (a world-Y column slices through the planet as you walk a sphere).
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world != null)
            {
                int solidAbove = 0;
                for (int step = 2; step <= 8; step++)
                {
                    Vector3 sample = transform.position + up * (step * 1.5f);
                    var v = world.GetVoxelWorld(world.WorldToVoxel(sample));
                    if (v.density > 0) solidAbove++;
                }
                if (solidAbove >= 3)
                {
                    _sheltered = true;
                    _inCave = true;
                }
            }
        }

        /// <summary>Scan nearby placed structures for metal/wood (amount + closest point).</summary>
        private void ScanSurfaceMaterials()
        {
            float metal = 0f, wood = 0f;
            Vector3 pos = transform.position;
            Vector3 bestMetal = pos, bestWood = pos;
            float bestMetalD = float.MaxValue, bestWoodD = float.MaxValue;

            var hits = Physics.OverlapSphere(pos, 14f);
            foreach (var col in hits)
            {
                if (col == null) continue;
                var tiered = col.GetComponentInParent<PlacedTieredBlock>();
                if (tiered == null) continue;
                bool isMetal = tiered.tier == BuildTier.Iron || tiered.tier == BuildTier.Steel;
                bool isWood = tiered.tier == BuildTier.Wood;
                if (!isMetal && !isWood) continue;

                Vector3 c = col.transform.position;
                float d = Vector3.Distance(c, pos);
                float falloff = Mathf.Clamp01(1f - d / 14f);
                if (isMetal)
                {
                    metal += falloff;
                    if (d < bestMetalD) { bestMetalD = d; bestMetal = c; }
                }
                else
                {
                    wood += falloff;
                    if (d < bestWoodD) { bestWoodD = d; bestWood = c; }
                }
            }

            _metalAmount = Mathf.Clamp01(metal);
            _woodAmount = Mathf.Clamp01(wood);
            Vector3 up = RadialUp();
            _metalPoint = _metalAmount > 0.01f ? bestMetal : pos + up * 3f;
            _woodPoint = _woodAmount > 0.01f ? bestWood : pos + up * 3f;
        }

        private Vector3 ListenerPos() => _listener != null ? _listener.position : transform.position;

        private Vector3 RadialUp()
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            return body != null ? body.UpAt(transform.position) : Vector3.up;
        }

        // ── Source helpers ────────────────────────────────────────────

        private Transform CreatePointHost(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private AudioSource CreateSource(Transform host, AudioClip clip, bool loop, bool spatial)
        {
            var src = host.gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.volume = 0f;
            src.playOnAwake = false;
            src.spatialBlend = spatial ? 1f : 0f;
            src.dopplerLevel = 0f;
            // Route through the SFX bus so the settings sliders affect it
            // (no-op when no AudioMixer asset is present).
            VoxelEngine.FX.AudioManager.Route(src, music: false);
            return src;
        }

        private static void EnsurePlaying(AudioSource src)
        {
            if (src != null && !src.isPlaying) src.Play();
        }

        // ══ Audio synthesis (pure code — no files) ═════════════════════

        // ── Outdoor rain bed ───────────────────────────────────────────

        private void BakeOutdoorRain()
        {
            const float dur = 9f;
            int n = (int)(SR * dur);
            var l = new float[n];
            var r = new float[n];

            // Layer 1 — the broad body of the downpour (decorrelated channels = wide bed).
            var pl = new float[7];
            var pr = new float[7];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float sway = 1f + 0.12f * Mathf.Sin(t * 0.9f) + 0.06f * Mathf.Sin(t * 2.3f + 1.7f);
                l[i] += Pink(pl, Random.Range(-1f, 1f)) * 0.55f * sway;
                r[i] += Pink(pr, Random.Range(-1f, 1f)) * 0.55f * sway;
            }

            // Layer 2 — individual drops: hundreds of tiny HF transients per second with
            // random pan. This is what makes it read as RAIN instead of a waterfall.
            int grains = (int)(dur * 640f);
            for (int g = 0; g < grains; g++)
            {
                int s = Random.Range(0, n - 256);
                float pan = Random.Range(-0.85f, 0.85f);
                float lg = Mathf.Sqrt(Mathf.Clamp01((1f - pan) * 0.5f));
                float rg = Mathf.Sqrt(Mathf.Clamp01((1f + pan) * 0.5f));
                float amp = 0.05f + 0.45f * Mathf.Pow(Random.value, 3.5f);
                int len = Random.Range(24, 130);
                bool soft = Random.value < 0.35f;             // duller drops mixed in
                float pn = 0f;
                for (int j = 0; j < len; j++)
                {
                    int idx = s + j;
                    if (idx >= n) break;
                    float env = Mathf.Exp(-j * (5.5f / len));
                    float w = Random.Range(-1f, 1f);
                    float v = soft ? w * 0.5f : (w - pn) * 1.2f;
                    pn = w;
                    l[idx] += v * env * amp * lg;
                    r[idx] += v * env * amp * rg;
                }
            }

            // Layer 3 — heavier splats on the ground around the listener.
            int splats = (int)(dur * 110f);
            for (int g = 0; g < splats; g++)
            {
                int s = Random.Range(0, n - 512);
                float pan = Random.Range(-0.7f, 0.7f);
                float lg = Mathf.Sqrt(Mathf.Clamp01((1f - pan) * 0.5f));
                float rg = Mathf.Sqrt(Mathf.Clamp01((1f + pan) * 0.5f));
                float amp = 0.10f + 0.25f * Random.value;
                int len = Random.Range(90, 240);
                float f = Random.Range(130f, 260f);
                float lp = 0f;
                for (int j = 0; j < len; j++)
                {
                    int idx = s + j;
                    if (idx >= n) break;
                    float env = Mathf.Exp(-j * (6.5f / len));
                    lp += (Random.Range(-1f, 1f) - lp) * 0.25f;
                    float thump = j < 90 ? Mathf.Sin(2f * Mathf.PI * f * j / SR) * Mathf.Exp(-j / 40f) * 0.7f : 0f;
                    float v = (lp + thump) * env * amp;
                    l[idx] += v * lg;
                    r[idx] += v * rg;
                }
            }

            l = Loopify(l, 0.3f);
            r = Loopify(r, 0.3f);
            Normalize(l, r, 0.85f);
            _outdoorClip = MakeStereo("WeatherRainOutdoor", l, r);
        }

        // ── Indoor (sheltered) bed ─────────────────────────────────────

        private void BakeIndoorRain()
        {
            const float dur = 9f;
            int n = (int)(SR * dur);
            var l = new float[n];
            var r = new float[n];

            var pl = new float[7];
            var pr = new float[7];
            float lpL = 0f, lpR = 0f, lp2L = 0f, lp2R = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float sway = 1f + 0.10f * Mathf.Sin(t * 0.7f + 0.4f);
                // The downpour beyond the walls: heavily low-passed pink noise.
                lpL += (Pink(pl, Random.Range(-1f, 1f)) - lpL) * 0.055f;
                lpR += (Pink(pr, Random.Range(-1f, 1f)) - lpR) * 0.055f;
                l[i] += lpL * 1.1f * sway;
                r[i] += lpR * 1.1f * sway;
                // A deep body — the structure shuddering under the storm.
                lp2L += (lpL - lp2L) * 0.012f;
                lp2R += (lpR - lp2R) * 0.012f;
                l[i] += lp2L * 1.6f;
                r[i] += lp2R * 1.6f;
            }

            // Drips — the signature "sheltered from the rain" sound.
            int drips = (int)(dur * 2.2f);
            for (int d = 0; d < drips; d++)
            {
                int s = Random.Range(0, n - 4096);
                float pan = Random.Range(-0.7f, 0.7f);
                float lg = Mathf.Sqrt(Mathf.Clamp01((1f - pan) * 0.5f));
                float rg = Mathf.Sqrt(Mathf.Clamp01((1f + pan) * 0.5f));
                float f = Random.Range(220f, 780f);
                float f2 = f * Random.Range(1.4f, 1.8f);
                float amp = Random.Range(0.25f, 0.6f);
                int len = Random.Range((int)(SR * 0.12f), (int)(SR * 0.4f));
                for (int j = 0; j < len; j++)
                {
                    int idx = s + j;
                    if (idx >= n) break;
                    float env = Mathf.Exp(-j / (len * 0.28f));
                    float glide = 1f - 0.10f * (j / (float)len);   // pitch sags as it settles
                    float v = Mathf.Sin(2f * Mathf.PI * f * glide * j / SR)
                            + Mathf.Sin(2f * Mathf.PI * f2 * j / SR) * 0.35f;
                    l[idx] += v * env * amp * lg;
                    r[idx] += v * env * amp * rg;
                }
            }

            l = Loopify(l, 0.3f);
            r = Loopify(r, 0.3f);
            Normalize(l, r, 0.72f);
            _indoorClip = MakeStereo("WeatherRainIndoor", l, r);
        }

        // ── Rain on metal (tin-roof pings) ─────────────────────────────

        private void BakeMetalPatter()
        {
            const float dur = 7f;
            int n = (int)(SR * dur);
            var data = new float[n];

            int pings = (int)(dur * 250f);
            for (int p = 0; p < pings; p++)
            {
                int s = Random.Range(0, n - 2048);
                // Lower partials dominate; the occasional bright tink on top.
                float f = Random.value < 0.72f ? Random.Range(360f, 950f) : Random.Range(950f, 2500f);
                float amp = 0.06f + 0.55f * Mathf.Pow(Random.value, 2.6f);
                int len = (int)(SR * Random.Range(0.05f, 0.16f));
                // Inharmonic partials — a struck sheet rings in stretched ratios.
                AddDecayTone(data, s, f,         amp,        len, len * 0.28f);
                AddDecayTone(data, s, f * 2.71f, amp * 0.55f, len, len * 0.20f);
                AddDecayTone(data, s, f * 5.15f, amp * 0.26f, len, len * 0.12f);
                if (Random.value < 0.30f)
                    AddDecayTone(data, s, Random.Range(150f, 240f), amp * 0.40f, len / 2, len * 0.10f);
            }

            // Faint spray bed so the pings don't float in silence.
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                lp += (Random.Range(-1f, 1f) - lp) * 0.5f;
                data[i] += (Random.Range(-1f, 1f) - lp) * 0.035f;
            }

            data = Loopify(data, 0.25f);
            Normalize(data, 0.88f);
            _metalClip = MakeMono("WeatherRainMetal", data);
        }

        // ── Rain on wood ───────────────────────────────────────────────

        private void BakeWoodPatter()
        {
            const float dur = 7f;
            int n = (int)(SR * dur);
            var data = new float[n];

            int knocks = (int)(dur * 270f);
            for (int kk = 0; kk < knocks; kk++)
            {
                int s = Random.Range(0, n - 1024);
                float amp = 0.05f + 0.40f * Mathf.Pow(Random.value, 2.4f);
                int len = Random.Range(90, 320);
                float f = Random.Range(105f, 190f);
                float lp = 0f;
                for (int j = 0; j < len; j++)
                {
                    int idx = s + j;
                    if (idx >= n) break;
                    float env = Mathf.Exp(-j * (6f / len));
                    lp += (Random.Range(-1f, 1f) - lp) * 0.18f;   // dull knock body
                    float thump = Mathf.Sin(2f * Mathf.PI * f * j / SR);
                    data[idx] += (lp * 1.2f + thump * 0.8f) * env * amp;
                }
            }

            data = Loopify(data, 0.25f);
            Normalize(data, 0.80f);
            _woodClip = MakeMono("WeatherRainWood", data);
        }

        // ── Wind ───────────────────────────────────────────────────────

        private void BakeWind()
        {
            const float dur = 9f;
            int n = (int)(SR * dur);
            var l = new float[n];
            var r = new float[n];
            float bl = 0f, br = 0f, lpL = 0f, lpR = 0f, pnL = 0f, pnR = 0f;
            float phaseL = Random.Range(0f, 6.28f), phaseR = Random.Range(0f, 6.28f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                // Gusts: two slow sines with wandering phase.
                float gustL = 0.5f + 0.5f * Mathf.Sin(t * 0.11f + phaseL + Mathf.Sin(t * 0.043f) * 2f);
                float gustR = 0.5f + 0.5f * Mathf.Sin(t * 0.13f + phaseR + Mathf.Sin(t * 0.037f) * 2f);

                bl += Random.Range(-1f, 1f) * 0.02f; bl *= 0.997f;
                br += Random.Range(-1f, 1f) * 0.02f; br *= 0.997f;

                float kL = Mathf.Lerp(0.015f, 0.06f, gustL);   // gusts open the filter
                float kR = Mathf.Lerp(0.015f, 0.06f, gustR);
                lpL += (bl - lpL) * kL;
                lpR += (br - lpR) * kR;
                l[i] += lpL * (2.2f + 2.4f * gustL);
                r[i] += lpR * (2.2f + 2.4f * gustR);

                // High hiss riding the gusts.
                float wl = Random.Range(-1f, 1f);
                float wr = Random.Range(-1f, 1f);
                l[i] += (wl - pnL) * 0.05f * gustL * gustL;
                r[i] += (wr - pnR) * 0.05f * gustR * gustR;
                pnL = wl; pnR = wr;
            }

            l = Loopify(l, 0.35f);
            r = Loopify(r, 0.35f);
            Normalize(l, r, 0.60f);
            _windClip = MakeStereo("WeatherWind", l, r);
        }

        // ── Thunder (two variants) ─────────────────────────────────────

        private void BakeThunder()
        {
            _thunderFarClip = BakeThunderClip(near: false);
            _thunderNearClip = BakeThunderClip(near: true);
        }

        private AudioClip BakeThunderClip(bool near)
        {
            float dur = near ? 5.6f : 6.8f;
            int n = (int)(SR * dur);
            var data = new float[n];

            // The roll: brown noise through a closing low-pass, shaped by several
            // delayed booms (the rumble bouncing around the sky).
            int humps = Random.Range(3, 6);
            var humpStart = new float[humps];
            var humpWidth = new float[humps];
            var humpAmp = new float[humps];
            for (int h = 0; h < humps; h++)
            {
                humpStart[h] = Random.Range(0.02f, near ? 2.0f : 3.2f);
                humpWidth[h] = Random.Range(0.45f, 1.35f);
                humpAmp[h] = Random.Range(0.45f, 1f) * (h == 0 ? (near ? 1.25f : 1f) : 0.8f);
            }

            float b = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                b += Random.Range(-1f, 1f) * 0.055f;
                b *= 0.9965f;

                float prog = t / dur;
                float kk = Mathf.Lerp(0.035f, 0.004f, prog);    // bright early, sub-bass late
                lp += (b - lp) * kk;

                float env = 0f;
                for (int h = 0; h < humps; h++)
                {
                    float dt = (t - humpStart[h]) / humpWidth[h];
                    if (dt > -0.2f && dt < 3f)
                        env += humpAmp[h] * Mathf.Exp(-dt * dt * 0.9f);
                }
                env *= Mathf.Exp(-t / (dur * 0.55f));

                data[i] = lp * 7f * env;
            }

            if (near)
            {
                // The crack: an instant tearing snap right overhead.
                int crackLen = (int)(SR * 0.09f);
                float pn = 0f;
                for (int j = 0; j < crackLen; j++)
                {
                    float env = Mathf.Exp(-j / (SR * 0.012f));
                    float w = Random.Range(-1f, 1f);
                    data[j] += (w - pn) * env * 0.9f;
                    pn = w;
                }
                // A short mid "tear" trailing the snap.
                int tearLen = (int)(SR * 0.35f);
                float lpT = 0f;
                for (int j = 0; j < tearLen; j++)
                {
                    float env = Mathf.Exp(-j / (SR * 0.09f));
                    lpT += (Random.Range(-1f, 1f) - lpT) * 0.16f;
                    data[j] += lpT * env * 0.5f;
                }
            }

            // One-shot: just guard the tail against a click.
            int fade = SR / 20;
            for (int i = 0; i < fade; i++) data[n - 1 - i] *= (float)i / fade;

            Normalize(data, 0.95f);
            return MakeMono(near ? "WeatherThunderNear" : "WeatherThunderFar", data);
        }

        // ── DSP helpers ────────────────────────────────────────────────

        /// <summary>Voss-McCartney pink noise (state array of 7, per channel).</summary>
        private static float Pink(float[] s, float white)
        {
            s[0] = 0.99886f * s[0] + white * 0.0555179f;
            s[1] = 0.99332f * s[1] + white * 0.0750759f;
            s[2] = 0.96900f * s[2] + white * 0.1538520f;
            s[3] = 0.86650f * s[3] + white * 0.3104856f;
            s[4] = 0.55000f * s[4] + white * 0.5329522f;
            s[5] = -0.7616f * s[5] - white * 0.0168980f;
            float pink = (s[0] + s[1] + s[2] + s[3] + s[4] + s[5] + s[6] + white * 0.5362f) * 0.11f;
            s[6] = white * 0.115926f;
            return pink;
        }

        /// <summary>
        /// Fast decaying sine via the two-step recurrence (no per-sample trig).
        /// Sharp attack by design — exactly what an impact needs.
        /// </summary>
        private static void AddDecayTone(float[] data, int start, float freq, float amp, int len, float decaySamples)
        {
            double w = 2.0 * System.Math.PI * freq / SR;
            double coef = 2.0 * System.Math.Cos(w);
            double s0 = 0.0;
            double s1 = System.Math.Sin(w);
            float e = 1f;
            float dk = Mathf.Exp(-1f / Mathf.Max(1f, decaySamples));
            int n = data.Length;
            for (int j = 0; j < len; j++)
            {
                int idx = start + j;
                if (idx >= n) break;
                double v = coef * s1 - s0;
                s0 = s1;
                s1 = v;
                e *= dk;
                data[idx] += (float)v * amp * e;
            }
        }

        /// <summary>Crossfade the tail into the head so the loop wraps seamlessly.</summary>
        private static float[] Loopify(float[] data, float fadeSeconds)
        {
            int fade = (int)(SR * fadeSeconds);
            if (fade <= 8 || fade * 2 >= data.Length) return data;
            var tail = new float[fade];
            System.Array.Copy(data, data.Length - fade, tail, 0, fade);
            for (int i = 0; i < fade; i++)
            {
                float w = (i + 0.5f) / fade;
                data[i] = Mathf.Lerp(tail[i], data[i], w);
            }
            System.Array.Resize(ref data, data.Length - fade);
            return data;
        }

        private static void Normalize(float[] data, float peak)
        {
            float max = 0f;
            for (int i = 0; i < data.Length; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
            if (max < 1e-5f) return;
            float k = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= k;
        }

        private static void Normalize(float[] l, float[] r, float peak)
        {
            float max = 0f;
            for (int i = 0; i < l.Length; i++) max = Mathf.Max(max, Mathf.Abs(l[i]), Mathf.Abs(r[i]));
            if (max < 1e-5f) return;
            float k = peak / max;
            for (int i = 0; i < l.Length; i++) { l[i] *= k; r[i] *= k; }
        }

        private static AudioClip MakeMono(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeStereo(string name, float[] l, float[] r)
        {
            var clip = AudioClip.Create(name, l.Length, 2, SR, false);
            var both = new float[l.Length * 2];
            for (int i = 0; i < l.Length; i++)
            {
                both[2 * i] = l[i];
                both[2 * i + 1] = r[i];
            }
            clip.SetData(both, 0);
            return clip;
        }
    }
}
