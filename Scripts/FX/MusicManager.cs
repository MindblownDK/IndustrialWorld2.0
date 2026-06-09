// Assets/Scripts/VoxelEngine/FX/MusicManager.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            INDUSTRIAL WORLD — PROCEDURAL MUSIC MANAGER         ║
// ║                                                                  ║
// ║  Generative ambient music — slow evolving chord pads, zero       ║
// ║  audio files. A small pool of soft synth-pad clips (each a       ║
// ║  sustained chord) is crossfaded one into the next on a gentle    ║
// ║  timer, walking through a calm, cinematic chord progression.    ║
// ║                                                                  ║
// ║  Routed through the MUSIC mixer bus, so the Music settings       ║
// ║  slider controls it. Sits low in the mix and ducks slightly     ║
// ║  during storms so weather can breathe.                          ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.FX
{
    public class MusicManager : MonoBehaviour
    {
        private static MusicManager _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            // Only in the gameplay world (the main menu has its own vibe / silence).
            if (Core.VoxelWorld.Instance == null && Object.FindAnyObjectByType<Core.VoxelWorld>() == null)
                return;
            if (_instance != null) return;

            var go = new GameObject("~Music");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<MusicManager>();
        }

        // ── Tunables ───────────────────────────────────────────────
        private const int   SAMPLE_RATE   = 44100;
        private const float PAD_SECONDS   = 16f;   // length of each rendered chord pad
        private const float CHORD_HOLD    = 22f;   // seconds before moving to the next chord
        private const float CROSSFADE     = 6f;    // crossfade duration between chords
        private const float MUSIC_VOLUME  = 0.34f; // peak mix level (kept tasteful/low)
        private const float ROOT_HZ       = 130.81f; // C3

        // A calm, slightly wistful progression (scale degrees as semitone offsets,
        // each chord = a set of intervals over a moving root). Minor-leaning,
        // cinematic, non-fatiguing for long play sessions.
        // Roots walk: i – VI – III – VII (Aeolian colour).
        private static readonly int[] _rootWalk = { 0, -4, 3, -2, 0, 5, 3, -2 };
        // Chord voicings (semitone intervals from the current root) — add9 / sus
        // colours for that warm "industrial dusk" feel.
        private static readonly int[][] _voicings =
        {
            new[] { 0, 7, 12, 14 },   // root, fifth, octave, add9
            new[] { 0, 3, 7, 10 },    // minor7
            new[] { 0, 5, 7, 12 },    // sus4-ish
            new[] { 0, 7, 10, 15 },   // min7 + high colour
        };

        // ── State ──────────────────────────────────────────────────
        private AudioSource _a, _b;       // two voices we ping-pong between
        private bool   _useA = true;      // which voice is currently "front"
        private int    _step;             // index into the progression
        private float  _holdTimer;
        private bool   _started;

        private void Start()
        {
            _a = MakeVoice();
            _b = MakeVoice();
            _holdTimer = CHORD_HOLD;      // trigger first chord almost immediately
            _step = 0;
        }

        private AudioSource MakeVoice()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.loop         = true;
            src.playOnAwake  = false;
            src.volume       = 0f;
            src.spatialBlend = 0f;        // 2D music
            AudioManager.Route(src, music: true);   // ← MUSIC bus
            return src;
        }

        private void Update()
        {
            // Storm ducking so weather/ambience can lead during rain.
            float duck = 1f;
            var wm = VoxelEngine.Weather.WeatherManager.Instance;
            if (wm != null) duck = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(wm.Intensity));
            float targetPeak = MUSIC_VOLUME * duck;

            _holdTimer += Time.unscaledDeltaTime;
            if (!_started || _holdTimer >= CHORD_HOLD)
            {
                _started = true;
                _holdTimer = 0f;
                NextChord();
            }

            // Smooth crossfade: the "front" voice rises toward targetPeak, the
            // "back" voice falls toward zero.
            float k = Time.unscaledDeltaTime / Mathf.Max(0.1f, CROSSFADE);
            var front = _useA ? _a : _b;
            var back  = _useA ? _b : _a;
            front.volume = Mathf.MoveTowards(front.volume, targetPeak, targetPeak * k);
            back.volume  = Mathf.MoveTowards(back.volume,  0f,          MUSIC_VOLUME * k);
        }

        private void NextChord()
        {
            // Render the next chord onto the BACK voice, then swap.
            var back = _useA ? _b : _a;

            int root = _rootWalk[_step % _rootWalk.Length];
            var voicing = _voicings[_step % _voicings.Length];
            back.clip = RenderPad(root, voicing);
            back.time = 0f;
            back.Play();

            _useA = !_useA;   // back becomes front
            _step++;
        }

        // ── Pad synthesis ──────────────────────────────────────────
        /// <summary>
        /// Renders a soft, evolving chord pad: each note is a small cluster of
        /// slightly-detuned sine partials with a slow tremolo, wrapped in a long
        /// fade-in/out so the looping clip is seamless and breathing.
        /// </summary>
        private AudioClip RenderPad(int rootSemis, int[] voicing)
        {
            int n = (int)(SAMPLE_RATE * PAD_SECONDS);
            var data = new float[n];

            float rootHz = ROOT_HZ * Mathf.Pow(2f, rootSemis / 12f);

            foreach (int interval in voicing)
            {
                float f = rootHz * Mathf.Pow(2f, interval / 12f);
                // Three detuned partials per note for a warm chorus.
                float[] detune = { 0.997f, 1.0f, 1.004f };
                float tremRate = Random.Range(0.07f, 0.16f);
                float tremPhase = Random.value * 6.283f;

                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / SAMPLE_RATE;
                    float s = 0f;
                    for (int p = 0; p < detune.Length; p++)
                        s += Mathf.Sin(2f * Mathf.PI * f * detune[p] * t);
                    s /= detune.Length;
                    // Gentle breathing tremolo.
                    float trem = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * tremRate * t + tremPhase);
                    data[i] += s * trem * 0.16f;
                }
            }

            // Add a faint sub-octave for body.
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                data[i] += Mathf.Sin(2f * Mathf.PI * (rootHz * 0.5f) * t) * 0.06f;
            }

            // Long fade in/out → seamless, ambient swells.
            int fade = (int)(SAMPLE_RATE * 3.5f);
            for (int i = 0; i < fade && i < n; i++)
            {
                float g = Mathf.SmoothStep(0f, 1f, (float)i / fade);
                data[i]         *= g;
                data[n - 1 - i] *= g;
            }

            Normalize(data, 0.6f);
            var clip = AudioClip.Create($"Pad{rootSemis}", n, 1, SAMPLE_RATE, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void Normalize(float[] d, float peak)
        {
            float max = 0f;
            for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > max) max = a; }
            if (max < 1e-4f) return;
            float g = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= g;
        }
    }
}
