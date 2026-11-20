using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class TurboBlurRendererFeature : ScriptableRendererFeature
{
    private Material material;
    private TurboBlurPass pass;

    public override void Create()
    {
        CoreUtils.Destroy(material);
        Shader shader = Resources.Load<Shader>("TurboRadialBlur");
        material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
        pass = new TurboBlurPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (material == null || camera.cameraType != CameraType.Game
            || !camera.TryGetComponent(out CarFollowCamera follow)
            || !follow.isActiveAndEnabled || follow.TurboBlurAmount <= 0f)
            return;

        material.SetFloat("_Intensity", follow.TurboBlurAmount);
        pass.Setup(material);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

    private class TurboBlurPass : ScriptableRenderPass
    {
        private Material material;

        public void Setup(Material blurMaterial)
        {
            material = blurMaterial;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
                return;

            TextureHandle source = resources.activeColorTexture;
            var descriptor = renderGraph.GetTextureDesc(source);
            descriptor.name = "Turbo Radial Blur";
            descriptor.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(descriptor);
            var parameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0);
            renderGraph.AddBlitPass(parameters, passName: "Turbo Radial Blur");
            resources.cameraColor = destination;
        }
    }
}
