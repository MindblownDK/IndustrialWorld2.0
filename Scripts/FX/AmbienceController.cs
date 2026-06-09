// Assets/Scripts/VoxelEngine/FX/AmbienceController.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            INDUSTRIAL WORLD — AMBIENCE CONTROLLER              ║
// ║                                                                  ║
// ║  Layered, crossfading 2D soundscape that makes the world feel    ║
// ║  ALIVE. Picks an environment around the player and blends the    ║
// ║  matching beds in/out smoothly:                                  ║
// ║                                                                  ║
// ║   • Surface   → soft wind + (day) birds / (night) crickets       ║
// ║   • Cave/deep → low rumble + dripping water                      ║
// ║                                                                  ║
// ║  Ambience automatically ducks while it's raining/snowing so the  ║
// ║  weather audio owns the mix during storms.                      ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.FX
{
    public class AmbienceController : MonoBehaviour
    {
        private Transform _listener;

        // Layers.
        private AudioSource _wind;       // surface neutral bed
        private AudioSource _birds;      // day
        private AudioSource _crickets;   // night
        private AudioSource _caveRumble; // underground bed
        private AudioSource _caveDrips;  // underground detail

        // Target volumes (lerped toward each frame).
        private float _tWind, _tBirds, _tCrickets, _tCaveRumble, _tCaveDrips;

        private float _envTimer;
        private bool  _underground;
        private float _dayFactor = 1f;   // 1 = full day, 0 = night (smoothed)

        // Peak mix levels per layer.
        private const float WIND_VOL   = 0.30f;
        private const float BIRDS_VOL  = 0.40f;
        private const float CRICK_VOL  = 0.45f;
        private const float RUMBLE_VOL = 0.50f;
        private const float DRIPS_VOL  = 0.55f;

        private void Start()
        {
            _wind       = Make(Sfx.AmbWindLight);
            _birds      = Make(Sfx.AmbDayBirds);
            _crickets   = Make(Sfx.AmbNightCrickets);
            _caveRumble = Make(Sfx.AmbCaveRumble);
            _caveDrips  = Make(Sfx.AmbCaveDrips);
        }

        private AudioSource Make(Sfx sfx)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip         = SfxLibrary.Get(sfx);
            src.loop         = true;
            src.playOnAwake  = false;
            src.volume       = 0f;
            src.spatialBlend = 0f;   // 2D ambience
            src.time         = Random.Range(0f, src.clip.length);
            AudioManager.Route(src, music: false);
            src.Play();
            return src;
        }

        private void Update()
        {
            if (_listener == null)
            {
                var cam = Camera.main;
                if (cam != null) _listener = cam.transform;
            }

            // Re-evaluate the environment a few times a second (cheap, but no need per-frame).
            _envTimer += Time.deltaTime;
            if (_envTimer >= 0.4f) { _envTimer = 0f; EvaluateEnvironment(); }

            // Weather ducking: when it's precipitating, pull surface ambience down
            // so the rain/wind audio leads.
            float weatherDuck = 1f;
            var wm = VoxelEngine.Weather.WeatherManager.Instance;
            if (wm != null) weatherDuck = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(wm.Intensity));

            // Decide target volumes from environment.
            if (_underground)
            {
                _tWind = _tBirds = _tCrickets = 0f;
                _tCaveRumble = RUMBLE_VOL;
                _tCaveDrips  = DRIPS_VOL;
            }
            else
            {
                _tCaveRumble = _tCaveDrips = 0f;
                _tWind     = WIND_VOL * weatherDuck;
                _tBirds    = BIRDS_VOL * _dayFactor * weatherDuck;
                _tCrickets = CRICK_VOL * (1f - _dayFactor) * weatherDuck;
            }

            // Smoothly approach targets.
            float k = Time.deltaTime * 1.2f;
            Approach(_wind,       _tWind,       k);
            Approach(_birds,      _tBirds,      k);
            Approach(_crickets,   _tCrickets,   k);
            Approach(_caveRumble, _tCaveRumble, k);
            Approach(_caveDrips,  _tCaveDrips,  k);
        }

        private static void Approach(AudioSource s, float target, float k)
        {
            if (s == null) return;
            s.volume = Mathf.Lerp(s.volume, target, k);
        }

        private void EvaluateEnvironment()
        {
            if (_listener == null) return;
            Vector3 p = _listener.position;

            // ── Underground detection: solid terrain stacked above the player. ──
            bool under = false;
            var world = VoxelWorld.Instance;
            if (world != null)
            {
                var vpos = world.WorldToVoxel(p);
                int solidAbove = 0;
                for (int dy = 3; dy <= 14; dy++)
                {
                    var v = world.GetVoxelWorld(new Vector3Int(vpos.x, vpos.y + dy, vpos.z));
                    if (v.density > 0) solidAbove++;
                }
                under = solidAbove >= 5;
            }
            _underground = under;

            // ── Day/night factor. No dedicated cycle in the project yet, so we
            // derive a smooth value from the scene's main directional light if
            // present (its intensity tracks daylight); otherwise assume day. ──
            float day = 1f;
            var sun = RenderSettings.sun;
            if (sun != null)
                day = Mathf.Clamp01(Mathf.InverseLerp(0.05f, 0.6f, sun.intensity));
            _dayFactor = Mathf.Lerp(_dayFactor, day, 0.5f);
        }
    }
}
