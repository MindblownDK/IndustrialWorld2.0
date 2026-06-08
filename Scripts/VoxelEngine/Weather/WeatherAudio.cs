// Assets/Scripts/VoxelEngine/Weather/WeatherAudio.cs
//
// Procedural rain/snow audio. Generates AudioClips at runtime from pure code —
// NO audio files needed. Creates realistic rain sounds by mixing:
//   1) Ambient rain loop (pink noise filtered to sound like rain)
//   2) Surface-hit patter (different pitch/tone for metal, wood, stone, ground)
//   3) Indoor dampening (when under a roof/foundation, rain sounds muffled + cozy)
//   4) Thunder rumbles during heavy rain
//   5) Wind during snow/blizzard

using UnityEngine;
using VoxelEngine.Building.Tiered;
using VoxelEngine.Core;

namespace VoxelEngine.Weather
{
    [RequireComponent(typeof(WeatherManager))]
    public class WeatherAudio : MonoBehaviour
    {
        // Audio sources
        private AudioSource _ambientRain;
        private AudioSource _metalPatter;
        private AudioSource _woodPatter;
        private AudioSource _groundPatter;
        private AudioSource _thunder;
        private AudioSource _wind;
        private AudioSource _indoorMuffle;

        // Generated clips
        private AudioClip _rainClip;
        private AudioClip _metalClip;
        private AudioClip _woodClip;
        private AudioClip _groundClip;
        private AudioClip _thunderClip;
        private AudioClip _windClip;
        private AudioClip _indoorClip;

        private float _thunderTimer;
        private float _nextThunder;
        private bool _isUnderRoof;
        private float _roofCheckTimer;
        private float _surfaceScanTimer;
        private float _cachedMetalAmount;
        private float _cachedWoodAmount;

        private const int SAMPLE_RATE = 44100;

        private void Start()
        {
            // Generate all audio clips procedurally.
            _rainClip    = GenerateRainLoop(4f);
            _metalClip   = GenerateMetalPatter(3f);
            _woodClip    = GenerateWoodPatter(3f);
            _groundClip  = GenerateGroundPatter(3f);
            _thunderClip = GenerateThunder(6f);
            _windClip    = GenerateWind(5f);
            _indoorClip  = GenerateIndoorRain(4f);

            // Create audio sources.
            _ambientRain  = CreateSource(_rainClip, true, 0f);
            _metalPatter  = CreateSource(_metalClip, true, 0f);
            _woodPatter   = CreateSource(_woodClip, true, 0f);
            _groundPatter = CreateSource(_groundClip, true, 0f);
            _thunder      = CreateSource(_thunderClip, false, 0f);
            _wind         = CreateSource(_windClip, true, 0f);
            _indoorMuffle = CreateSource(_indoorClip, true, 0f);

            _nextThunder = Random.Range(15f, 45f);
        }

        private void Update()
        {
            var wm = WeatherManager.Instance;
            if (wm == null) return;

            float intensity = wm.Intensity;
            bool isSnow = wm.IsSnowBiome && (wm.CurrentState == WeatherState.Snow ||
                          wm.CurrentState == WeatherState.Blizzard);
            bool isRain = !isSnow && intensity > 0.02f;

            // Check if under a roof.
            _roofCheckTimer += Time.deltaTime;
            if (_roofCheckTimer >= 0.5f)
            {
                _roofCheckTimer = 0f;
                _isUnderRoof = CheckUnderRoof();
            }

            // ── Rain audio ──
            if (isRain)
            {
                if (_isUnderRoof)
                {
                    // Indoor cozy rain — muffled ambient + enhanced metal patter (rain on roof).
                    _ambientRain.volume = Mathf.Lerp(_ambientRain.volume, intensity * 0.12f, Time.deltaTime * 3f);
                    _indoorMuffle.volume = Mathf.Lerp(_indoorMuffle.volume, intensity * 0.45f, Time.deltaTime * 3f);
                    _metalPatter.volume = Mathf.Lerp(_metalPatter.volume, intensity * 0.55f, Time.deltaTime * 3f);
                    _woodPatter.volume = Mathf.Lerp(_woodPatter.volume, intensity * 0.15f, Time.deltaTime * 3f);
                    _groundPatter.volume = Mathf.Lerp(_groundPatter.volume, intensity * 0.05f, Time.deltaTime * 3f);

                    // Lower pitch slightly for indoor dampening.
                    _ambientRain.pitch = 0.85f;
                    _indoorMuffle.pitch = 0.9f;
                }
                else
                {
                    // Outdoor — full rain ambiance.
                    _ambientRain.volume = Mathf.Lerp(_ambientRain.volume, intensity * 0.50f, Time.deltaTime * 3f);
                    _indoorMuffle.volume = Mathf.Lerp(_indoorMuffle.volume, 0f, Time.deltaTime * 5f);
                    _groundPatter.volume = Mathf.Lerp(_groundPatter.volume, intensity * 0.35f, Time.deltaTime * 3f);
                    _ambientRain.pitch = 1f;

                    // Surface patter based on what's nearby.
                    // Throttled surface scan (expensive OverlapSphere).
                    _surfaceScanTimer += Time.deltaTime;
                    if (_surfaceScanTimer >= 1.5f)
                    {
                        _surfaceScanTimer = 0f;
                        ScanSurfaceMaterials();
                    }
                    _metalPatter.volume = Mathf.Lerp(_metalPatter.volume, intensity * _cachedMetalAmount * 0.50f, Time.deltaTime * 3f);
                    _woodPatter.volume = Mathf.Lerp(_woodPatter.volume, intensity * _cachedWoodAmount * 0.30f, Time.deltaTime * 3f);
                }

                // Play if not already playing.
                if (!_ambientRain.isPlaying) _ambientRain.Play();
                if (!_indoorMuffle.isPlaying) _indoorMuffle.Play();
                if (!_metalPatter.isPlaying) _metalPatter.Play();
                if (!_woodPatter.isPlaying) _woodPatter.Play();
                if (!_groundPatter.isPlaying) _groundPatter.Play();
            }
            else
            {
                // Fade out rain.
                _ambientRain.volume = Mathf.Lerp(_ambientRain.volume, 0f, Time.deltaTime * 2f);
                _indoorMuffle.volume = Mathf.Lerp(_indoorMuffle.volume, 0f, Time.deltaTime * 2f);
                _metalPatter.volume = Mathf.Lerp(_metalPatter.volume, 0f, Time.deltaTime * 2f);
                _woodPatter.volume = Mathf.Lerp(_woodPatter.volume, 0f, Time.deltaTime * 2f);
                _groundPatter.volume = Mathf.Lerp(_groundPatter.volume, 0f, Time.deltaTime * 2f);
            }

            // ── Thunder ──
            if (isRain && intensity > 0.6f)
            {
                _thunderTimer += Time.deltaTime;
                if (_thunderTimer >= _nextThunder)
                {
                    _thunderTimer = 0f;
                    _nextThunder = Random.Range(10f, 40f);
                    _thunder.volume = Random.Range(0.15f, 0.45f) * intensity;
                    _thunder.pitch = Random.Range(0.7f, 1.1f);
                    _thunder.Play();
                }
            }

            // ── Wind (snow/blizzard) ──
            if (isSnow && intensity > 0.05f)
            {
                _wind.volume = Mathf.Lerp(_wind.volume, intensity * 0.40f, Time.deltaTime * 2f);
                if (!_wind.isPlaying) _wind.Play();

                // During blizzard, play subtle indoor audio too if under roof.
                if (_isUnderRoof && wm.CurrentState == WeatherState.Blizzard)
                {
                    _indoorMuffle.volume = Mathf.Lerp(_indoorMuffle.volume, 0.2f, Time.deltaTime * 2f);
                    if (!_indoorMuffle.isPlaying) _indoorMuffle.Play();
                }
            }
            else
            {
                _wind.volume = Mathf.Lerp(_wind.volume, 0f, Time.deltaTime * 2f);
            }
        }

        // ── Surface detection ──────────────────────────────────────

        private void ScanSurfaceMaterials()
        {
            float metalAmount = 0f; // local accumulation
            float woodAmount = 0f; // local accumulation

            // Scan nearby placed blocks for material type.
            var hits = Physics.OverlapSphere(transform.position, 12f);
            foreach (var col in hits)
            {
                if (col == null) continue;
                var tiered = col.GetComponentInParent<PlacedTieredBlock>();
                if (tiered != null)
                {
                    float dist = Vector3.Distance(col.transform.position, transform.position);
                    float falloff = Mathf.Clamp01(1f - dist / 12f);
                    if (tiered.tier == BuildTier.Iron || tiered.tier == BuildTier.Steel)
                        metalAmount += falloff;
                    else if (tiered.tier == BuildTier.Wood)
                        woodAmount += falloff;
                }
            }

            metalAmount = Mathf.Clamp01(metalAmount);
            woodAmount = Mathf.Clamp01(woodAmount);

            _cachedMetalAmount = Mathf.Clamp01(metalAmount);
            _cachedWoodAmount = Mathf.Clamp01(woodAmount);
        }

        private bool CheckUnderRoof()
        {
            // Check 1: Raycast up for building pieces (roof/foundation).
            if (Physics.Raycast(transform.position, Vector3.up, out var hit, 10f))
            {
                var tiered = hit.collider.GetComponentInParent<PlacedTieredBlock>();
                var placed = hit.collider.GetComponentInParent<Building.PlacedBlock>();
                if (tiered != null || placed != null) return true;
            }

            // Check 2: Are we underground in a cave? Check if there's solid terrain above.
            var world = VoxelEngine.Core.VoxelWorld.Instance;
            if (world != null)
            {
                var pos = world.WorldToVoxel(transform.position);
                // Check several blocks above for solid terrain (cave ceiling).
                int solidAbove = 0;
                for (int dy = 2; dy <= 8; dy++)
                {
                    var v = world.GetVoxelWorld(new UnityEngine.Vector3Int(pos.x, pos.y + dy, pos.z));
                    if (v.density > 0) solidAbove++;
                }
                // If 3+ of 7 blocks above are solid, we're in a cave.
                if (solidAbove >= 3) return true;
            }

            return false;
        }

        // ── Audio Generation ───────────────────────────────────────
        // All clips are generated from code — no audio files needed.

        private AudioSource CreateSource(AudioClip clip, bool loop, float vol)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.volume = vol;
            src.spatialBlend = 0f; // 2D (ambient)
            src.playOnAwake = false;
            // Route through the SFX bus so the audio settings sliders affect it
            // (no-op when no AudioMixer asset is present yet).
            VoxelEngine.FX.AudioManager.Route(src, music: false);
            return src;
        }

        /// <summary>Rain ambience — filtered pink noise with gentle variation.</summary>
        private AudioClip GenerateRainLoop(float duration)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            float[] data = new float[samples];
            float b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;

            for (int i = 0; i < samples; i++)
            {
                float white = Random.Range(-1f, 1f);
                // Pink noise filter (Voss-McCartney approximation).
                b0 = 0.99886f * b0 + white * 0.0555179f;
                b1 = 0.99332f * b1 + white * 0.0750759f;
                b2 = 0.96900f * b2 + white * 0.1538520f;
                b3 = 0.86650f * b3 + white * 0.3104856f;
                b4 = 0.55000f * b4 + white * 0.5329522f;
                b5 = -0.7616f * b5 - white * 0.0168980f;
                float pink = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362f) * 0.06f;
                b6 = white * 0.115926f;

                // Add gentle modulation for natural variation.
                float t = (float)i / SAMPLE_RATE;
                float mod = 1f + 0.15f * Mathf.Sin(t * 0.3f) + 0.08f * Mathf.Sin(t * 0.7f);
                data[i] = pink * mod * 0.5f;
            }

            return MakeClip("RainAmbient", data, duration);
        }

        /// <summary>Metal patter — high-pitched pings with resonance (tin roof sound).</summary>
        private AudioClip GenerateMetalPatter(float duration)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            float[] data = new float[samples];

            // Pre-generate random drop events.
            int numDrops = (int)(duration * 60); // 60 drops/sec
            for (int d = 0; d < numDrops; d++)
            {
                int startSample = Random.Range(0, samples - 2000);
                float freq = Random.Range(2800f, 5500f); // high metallic ping
                float amplitude = Random.Range(0.05f, 0.15f);
                float decay = Random.Range(600f, 1500f); // samples to decay

                for (int j = 0; j < (int)decay && startSample + j < samples; j++)
                {
                    float t = (float)j / SAMPLE_RATE;
                    float env = Mathf.Exp(-j / (decay * 0.3f)); // fast exponential decay
                    float resonance = Mathf.Sin(2f * Mathf.PI * freq * t) * env;
                    // Add a slight ring (overtone).
                    resonance += Mathf.Sin(2f * Mathf.PI * freq * 2.7f * t) * env * 0.3f;
                    data[startSample + j] += resonance * amplitude;
                }
            }

            return MakeClip("MetalPatter", data, duration);
        }

        /// <summary>Wood patter — softer thunks, lower frequency.</summary>
        private AudioClip GenerateWoodPatter(float duration)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            float[] data = new float[samples];

            int numDrops = (int)(duration * 40);
            for (int d = 0; d < numDrops; d++)
            {
                int start = Random.Range(0, samples - 1500);
                float freq = Random.Range(400f, 1200f);
                float amp = Random.Range(0.04f, 0.10f);
                float decay = Random.Range(300f, 800f);

                for (int j = 0; j < (int)decay && start + j < samples; j++)
                {
                    float t = (float)j / SAMPLE_RATE;
                    float env = Mathf.Exp(-j / (decay * 0.25f));
                    // Duller thunk — lower harmonics, more noise.
                    float thunk = Mathf.Sin(2f * Mathf.PI * freq * t) * env;
                    thunk += Random.Range(-0.02f, 0.02f) * env; // texture
                    data[start + j] += thunk * amp;
                }
            }

            return MakeClip("WoodPatter", data, duration);
        }

        /// <summary>Ground patter — soft splats with noise.</summary>
        private AudioClip GenerateGroundPatter(float duration)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            float[] data = new float[samples];

            int numDrops = (int)(duration * 30);
            for (int d = 0; d < numDrops; d++)
            {
                int start = Random.Range(0, samples - 800);
                float amp = Random.Range(0.02f, 0.06f);
                int len = Random.Range(200, 600);

                for (int j = 0; j < len && start + j < samples; j++)
                {
                    float env = 1f - (float)j / len;
                    env *= env; // quadratic falloff — soft splat
                    data[start + j] += Random.Range(-1f, 1f) * env * amp;
                }
            }

            return MakeClip("GroundPatter", data, duration);
        }

        /// <summary>Thunder — low rumble with crack.</summary>
        private AudioClip GenerateThunder(float duration)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            float[] data = new float[samples];

            // Initial crack (white noise burst).
            int crackLen = SAMPLE_RATE / 4; // 0.25s
            int crackStart = SAMPLE_RATE / 10;
            for (int i = 0; i < crackLen && crackStart + i < samples; i++)
            {
                float env = Mathf.Exp(-i / (float)(crackLen * 0.15f));
                data[crackStart + i] = Random.Range(-1f, 1f) * env * 0.6f;
            }

            // Low rumble (filtered noise).
            float b = 0;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float env = Mathf.Sin(t * Mathf.PI); // bell envelope
                env *= env;
                float white = Random.Range(-1f, 1f);
                b = b * 0.97f + white * 0.03f; // low-pass
                float rumble = b * env * 0.4f;
                // Sub-bass sine
                rumble += Mathf.Sin(2f * Mathf.PI * 35f * t * duration) * env * 0.3f;
                data[i] += rumble;
            }

            return MakeClip("Thunder", data, duration);
        }

        /// <summary>Wind — filtered noise with slow modulation.</summary>
        private AudioClip GenerateWind(float duration)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            float[] data = new float[samples];
            float b0 = 0, b1 = 0;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                b0 = b0 * 0.985f + white * 0.015f;
                b1 = b1 * 0.95f + b0 * 0.05f;
                float mod = 0.5f + 0.5f * Mathf.Sin(t * 0.2f + Mathf.Sin(t * 0.07f) * 3f);
                data[i] = b1 * mod * 0.35f;
            }

            return MakeClip("Wind", data, duration);
        }

        /// <summary>Indoor rain — muffled low-frequency rain (cozy under-roof sound).</summary>
        private AudioClip GenerateIndoorRain(float duration)
        {
            int samples = (int)(SAMPLE_RATE * duration);
            float[] data = new float[samples];
            float b0 = 0, b1 = 0, b2 = 0;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                // Heavy low-pass for muffled sound.
                b0 = b0 * 0.995f + white * 0.005f;
                b1 = b1 * 0.990f + b0 * 0.010f;
                b2 = b2 * 0.985f + b1 * 0.015f;
                float mod = 1f + 0.1f * Mathf.Sin(t * 0.25f);
                data[i] = b2 * mod * 0.6f;
            }

            // Add occasional muffled drop impacts.
            int drops = (int)(duration * 12);
            for (int d = 0; d < drops; d++)
            {
                int start = Random.Range(0, samples - 1000);
                float freq = Random.Range(200f, 600f);
                float amp = Random.Range(0.02f, 0.05f);
                for (int j = 0; j < 800 && start + j < samples; j++)
                {
                    float env = Mathf.Exp(-j / 200f);
                    data[start + j] += Mathf.Sin(2f * Mathf.PI * freq * (float)j / SAMPLE_RATE) * env * amp;
                }
            }

            return MakeClip("IndoorRain", data, duration);
        }

        private AudioClip MakeClip(string name, float[] data, float duration)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SAMPLE_RATE, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
