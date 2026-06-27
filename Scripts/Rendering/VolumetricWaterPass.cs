// Assets/Scripts/VoxelEngine/Rendering/VolumetricWaterPass.cs
//
// Unity 6.4 URP Custom RenderGraph Pass for Volumetric Water Refraction & Post Effects.
// Enforces modern RecordRenderGraph execution and Blitter.BlitCameraTexture.
// Deprecated OnRenderImage is strictly prohibited.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace VoxelEngine.Rendering
{
    public class VolumetricWaterPass : ScriptableRenderPass
    {
        private Material _refractionBlitMaterial;
        private const string PassName = "VolumetricWaterRefractionPass";

        public VolumetricWaterPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        public void Setup(Material mat)
        {
            _refractionBlitMaterial = mat;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_refractionBlitMaterial == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle sourceHandle = resourceData.activeColorTexture;
            if (!sourceHandle.IsValid()) return;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            var td = new TextureDesc(desc.width, desc.height);
            td.colorFormat = desc.graphicsFormat;
            td.name = "_VolumetricWaterTempColor";

            TextureHandle tempColorHandle = renderGraph.CreateTexture(td);

            using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>(PassName, out var passData))
            {
                passData.source = sourceHandle;
                passData.material = _refractionBlitMaterial;

                builder.UseTexture(sourceHandle);
                builder.SetRenderAttachment(tempColorHandle, 0);

                builder.SetRenderFunc((BlitPassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>(PassName + "_Restore", out var passData))
            {
                passData.source = tempColorHandle;
                passData.material = null;

                builder.UseTexture(tempColorHandle);
                builder.SetRenderAttachment(sourceHandle, 0);

                builder.SetRenderFunc((BlitPassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_refractionBlitMaterial == null) return;
            var cmd = CommandBufferPool.Get(PassName);
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (source != null)
            {
                Blitter.BlitCameraTexture(cmd, source, source, _refractionBlitMaterial, 0);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private class BlitPassData
        {
            public TextureHandle source;
            public Material material;
        }
    }

    public class VolumetricWaterRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader blitShader;
        private Material _mat;
        private VolumetricWaterPass _waterPass;

        public override void Create()
        {
            _waterPass = new VolumetricWaterPass();
            if (blitShader != null) _mat = CoreUtils.CreateEngineMaterial(blitShader);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_mat != null)
            {
                _waterPass.Setup(_mat);
                renderer.EnqueuePass(_waterPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_mat);
        }
    }
}
