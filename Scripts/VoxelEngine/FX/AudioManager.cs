// Stub for AudioManager - replace with full implementation from audio system when available
using UnityEngine;

namespace VoxelEngine.FX
{
    public static class AudioManager
    {
        public static void PlayUI(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            // TODO: Route through AudioMixer SFX bus
            var go = new GameObject("UI_SFX");
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.Play();
            Destroy(go, clip.length + 0.5f);
        }
    }
}