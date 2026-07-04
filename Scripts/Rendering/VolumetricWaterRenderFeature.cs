// Assets/Scripts/VoxelEngine/Rendering/VolumetricWaterRenderFeature.cs
//
// Stable renderer feature host for planet water integration.
//
// Uses URP CoreUtils for safe engine material lifecycle management without
// polluting serialized asset data or triggering OnValidate serialization errors.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VoxelEngine.Rendering
{
    public sealed class VolumetricWaterRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader blitShader;

        private VolumetricWaterPass _waterPass;
        private Material _material;

        public override void Create()
        {
            if (_waterPass == null)
            {
                _waterPass = new VolumetricWaterPass
                {
                    renderPassEvent = RenderPassEvent.AfterRenderingTransparents
                };
            }

            if (blitShader == null)
                blitShader = Shader.Find("VoxelEngine/VolumetricWaterPost");
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (blitShader == null || _waterPass == null)
                return;

            if (_material == null || _material.shader != blitShader)
            {
                _material = CoreUtils.CreateEngineMaterial(blitShader);
                if (_material != null) _material.name = "VolumetricWaterPost_Runtime";
            }

            if (_material != null)
            {
                _waterPass.Setup(_material);
                renderer.EnqueuePass(_waterPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }
    }
}
