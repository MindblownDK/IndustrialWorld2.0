// Assets/Scripts/VoxelEngine/FX/MachineAudio.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            INDUSTRIAL WORLD — MACHINE AUDIO EMITTER            ║
// ║                                                                  ║
// ║  A lightweight 3D looping-sound component attached to any        ║
// ║  machine. Each frame it asks a delegate "are you running, and    ║
// ║  how hard?" (0..1) and smoothly fades / pitches the loop to      ║
// ║  match — so a quarry winds up, a furnace settles, a thruster     ║
// ║  roars in proportion to thrust.                                 ║
// ║                                                                  ║
// ║  Routed through the SFX mixer bus; spatialised so sounds come    ║
// ║  from the machine's location and fall off with distance.        ║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using UnityEngine;

namespace VoxelEngine.FX
{
    [DisallowMultipleComponent]
    public class MachineAudio : MonoBehaviour
    {
        private AudioSource _src;
        private Func<float> _activity;     // returns 0..1 (off..full)
        private float _baseVolume = 0.7f;
        private float _basePitch  = 1f;
        private float _pitchSpread = 0.12f; // how much pitch rises with activity
        private float _fade = 0f;           // smoothed activity
        private float _fadeSpeed = 3.5f;

        /// <summary>
        /// Configure and start the emitter.
        /// </summary>
        /// <param name="sfx">Which looping sound to play.</param>
        /// <param name="activity">0..1 intensity provider (volume + pitch driver).</param>
        /// <param name="volume">Peak volume at full activity.</param>
        /// <param name="maxDistance">3D falloff radius.</param>
        /// <param name="basePitch">Pitch at zero activity.</param>
        /// <param name="pitchSpread">Added pitch at full activity.</param>
        public MachineAudio Configure(
            Sfx sfx, Func<float> activity,
            float volume = 0.7f, float maxDistance = 26f,
            float basePitch = 1f, float pitchSpread = 0.12f)
        {
            _activity    = activity;
            _baseVolume  = volume;
            _basePitch   = basePitch;
            _pitchSpread = pitchSpread;

            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.clip          = SfxLibrary.Get(sfx);
            _src.loop          = true;
            _src.playOnAwake   = false;
            _src.volume        = 0f;
            _src.pitch         = basePitch;
            _src.spatialBlend  = 1f;                 // full 3D
            _src.rolloffMode   = AudioRolloffMode.Linear;
            _src.minDistance   = 2.5f;
            _src.maxDistance   = maxDistance;
            _src.dopplerLevel  = 0f;                 // machines don't pitch-bend on move
            // Randomise the loop start so identical machines don't phase-align.
            _src.time = UnityEngine.Random.Range(0f, _src.clip.length);
            AudioManager.Route(_src, music: false);
            _src.Play();
            return this;
        }

        private void Update()
        {
            if (_src == null || _activity == null) return;

            float target = Mathf.Clamp01(_activity());
            _fade = Mathf.MoveTowards(_fade, target, Time.deltaTime * _fadeSpeed);

            _src.volume = _fade * _baseVolume;
            _src.pitch  = _basePitch + _fade * _pitchSpread;

            // Pause the source entirely when fully silent to save voices.
            if (_fade <= 0.001f)
            {
                if (_src.isPlaying) _src.Pause();
            }
            else if (!_src.isPlaying)
            {
                _src.UnPause();
            }
        }

        private void OnDestroy()
        {
            if (_src != null) _src.Stop();
        }
    }
}
