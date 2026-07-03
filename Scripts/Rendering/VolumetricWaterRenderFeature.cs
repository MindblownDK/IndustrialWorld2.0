// Assets/Scripts/VoxelEngine/Rendering/VolumetricWaterRenderFeature.cs
//
// Stable renderer feature host for planet water integration.
//
// This feature intentionally performs no fullscreen blit yet. Its job is to keep
// URP renderer-feature deserialization stable while the planet water visuals are
// driven by world meshes and water materials. A future post pass can be layered on
// top once the pipeline path is fully validated against the active Unity 6 URP setup.

using UnityEngine;
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
            _waterPass = new VolumetricWaterPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents
            };

            if (blitShader == null)
                blitShader = Shader.Find("VoxelEngine/VolumetricWaterPost");

            if (blitShader != null)
                _material = new Material(blitShader) { name = "VolumetricWaterPost_Runtime" };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_waterPass == null)
                return;

            _waterPass.Setup(_material);
        }

        protected override void Dispose(bool disposing)
        {
            if (_material != null)
            {
                if (Application.isPlaying) Object.Destroy(_material);
                else Object.DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
