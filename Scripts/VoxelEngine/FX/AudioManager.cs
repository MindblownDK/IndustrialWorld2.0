// Assets/Scripts/VoxelEngine/FX/AudioManager.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║              INDUSTRIAL WORLD — AUDIO MANAGER                  ║
// ║                                                                  ║
// ║  Single routing point for game audio. Drives an authored         ║
// ║  AudioMixer ("GameAudioMixer" in Resources) with three exposed   ║
// ║  parameters — MasterVolume, MusicVolume, SFXVolume — converting   ║
// ║  the 0..1 settings sliders into proper decibel attenuation.      ║
// ║                                                                  ║
// ║  GRACEFUL FALLBACK: if the mixer asset isn't present yet, we     ║
// ║  fall back to AudioListener.volume (master only) so audio still  ║
// ║  works exactly as before — the game never breaks waiting on the  ║
// ║  artist to author the asset.                                     ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.Audio;

namespace VoxelEngine.FX
{
    /// <summary>
    /// Static audio routing helper. Lazily loads <c>Resources/GameAudioMixer</c>
    /// and exposes mixer groups so AudioSources can be routed to Music / SFX.
    /// </summary>
    public static class AudioManager
    {
        // Exposed-parameter names the AudioMixer must declare (right-click a
        // group's Volume in the mixer → "Expose ... to script", then rename).
        public const string P_MASTER = "MasterVolume";
        public const string P_MUSIC  = "MusicVolume";
        public const string P_SFX    = "SFXVolume";

        // Mixer-group names used to look up routing targets by path.
        public const string G_MUSIC = "Music";
        public const string G_SFX   = "SFX";

        private const string ResourcePath = "GameAudioMixer";

        private static AudioMixer _mixer;
        private static bool       _resolved;        // tried to load at least once
        private static AudioMixerGroup _musicGroup, _sfxGroup, _masterGroup;

        /// <summary>True once a real AudioMixer asset has been found and bound.</summary>
        public static bool HasMixer => Mixer != null;

        private static AudioMixer Mixer
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;
                    _mixer = Resources.Load<AudioMixer>(ResourcePath);
                    if (_mixer != null)
                    {
                        _masterGroup = FindGroup("Master");
                        _musicGroup  = FindGroup(G_MUSIC);
                        _sfxGroup    = FindGroup(G_SFX);
                    }
                }
                return _mixer;
            }
        }

        private static AudioMixerGroup FindGroup(string name)
        {
            if (_mixer == null) return null;
            var groups = _mixer.FindMatchingGroups(name);
            return (groups != null && groups.Length > 0) ? groups[0] : null;
        }

        // ── Routing targets for AudioSources ───────────────────────────────
        /// <summary>Mixer group SFX sources should output to (null when no mixer).</summary>
        public static AudioMixerGroup SfxGroup   => HasMixer ? (_sfxGroup ?? _masterGroup) : null;
        /// <summary>Mixer group music sources should output to (null when no mixer).</summary>
        public static AudioMixerGroup MusicGroup => HasMixer ? (_musicGroup ?? _masterGroup) : null;

        /// <summary>
        /// Convenience: routes an <see cref="AudioSource"/> to the SFX (default)
        /// or Music group when a mixer is available. No-op without a mixer.
        /// </summary>
        public static void Route(AudioSource src, bool music = false)
        {
            if (src == null) return;
            var g = music ? MusicGroup : SfxGroup;
            if (g != null) src.outputAudioMixerGroup = g;
        }

        // ── Volume application (called by GameSettings.Apply) ───────────────
        /// <summary>
        /// Pushes the three 0..1 volumes into the mixer as decibels. Falls back
        /// to <see cref="AudioListener.volume"/> (master only) when no mixer asset
        /// exists, preserving the original behaviour.
        /// </summary>
        public static void ApplyVolumes(float master01, float music01, float sfx01)
        {
            if (HasMixer)
            {
                Mixer.SetFloat(P_MASTER, LinearToDb(master01));
                Mixer.SetFloat(P_MUSIC,  LinearToDb(music01));
                Mixer.SetFloat(P_SFX,    LinearToDb(sfx01));
                // Keep the listener at unity gain so the mixer is the single
                // source of truth (avoids double-attenuation).
                AudioListener.volume = 1f;
            }
            else
            {
                // No mixer yet — master controls everything via the listener.
                AudioListener.volume = master01;
            }
        }

        /// <summary>
        /// Converts a 0..1 linear volume to decibels for an AudioMixer.
        /// 0 → fully muted (-80 dB), 1 → 0 dB.
        /// </summary>
        public static float LinearToDb(float linear01)
        {
            linear01 = Mathf.Clamp01(linear01);
            return linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
        }
    }
}
