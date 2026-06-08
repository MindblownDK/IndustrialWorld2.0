// Stub for SfxLibrary - replace with full implementation
using UnityEngine;

namespace VoxelEngine.FX
{
    public static class SfxLibrary
    {
        public static AudioClip Get(Sfx sfx)
        {
            // TODO: Load from Resources or Addressables based on Sfx enum
            return null;
        }
    }

    public enum Sfx
    {
        UiClick,
        UiHover,
        // Add more as needed from the full audio system
    }
}