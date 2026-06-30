// Assets/Scripts/VoxelEngine/Rendering/VolumetricWaterPass.cs
//
// Renderer feature intentionally removed from runtime use.
//
// Keeping only a plain helper class here avoids URP renderer-feature deserialization issues
// on this Unity/URP version while preserving the file path for future work.

using UnityEngine;

namespace VoxelEngine.Rendering
{
    public static class VolumetricWaterPass
    {
        public static Material CreatePreviewMaterial(Shader shader)
        {
            if (shader == null) return null;
            return new Material(shader) { name = "PlanetWaterPost_Runtime" };
        }
    }
}
