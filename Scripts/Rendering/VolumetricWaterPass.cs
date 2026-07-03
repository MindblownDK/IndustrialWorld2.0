// Assets/Scripts/VoxelEngine/Rendering/VolumetricWaterPass.cs
//
// Stable renderer pass host for planet water integration.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxelEngine.Rendering
{
    public sealed class VolumetricWaterPass : ScriptableRenderPass
    {
        private Material _material;

        public void Setup(Material material)
        {
            _material = material;
        }
    }
}
