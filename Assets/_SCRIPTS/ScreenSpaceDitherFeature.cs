using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

public class ScreenSpaceDitherFeature : ScriptableRendererFeature
{
    public enum Pattern { Bayer8, InterleavedGradient, BlueNoise }

    [System.Serializable]
    public class Settings
    {
        public Shader shader;

        [Tooltip("AfterRenderingPostProcessing keeps the pattern locked to final display pixels.")]
        public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        [Header("Pattern")]
        public Pattern pattern = Pattern.Bayer8;
        [Tooltip("Point-filtered, no mips, wrap repeat. Required for BlueNoise.")]
        public Texture2D blueNoise;
        [Tooltip("Integer only. Non-integer scaling reintroduces aliasing in the pattern.")]
        [Range(1, 8)] public int pixelScale = 1;
        [Tooltip("Temporal offset. Do not enable with TAA.")]
        public bool animate = false;

        [Header("Quantisation")]
        [Range(0f, 1f)] public float strength = 1f;
        [Range(2, 16)] public int levels = 2;
        [Range(0f, 1f)] public float desaturate = 0f;

        [Header("Luminance fade")]
        [Tooltip("Below this perceptual luma the dither is at full strength.")]
        [Range(0f, 1f)] public float lumaLow = 0.15f;
        [Tooltip("Above this the image is left untouched.")]
        [Range(0f, 1f)] public float lumaHigh = 0.60f;
        [Tooltip("Breaks the fade boundary into stipple instead of a contour.")]
        [Range(0f, 1f)] public float maskDither = 0.35f;

        [Header("Depth")]
        public bool useDepth = true;
        [Range(0f, 1f)] public float depthInfluence = 1f;
        public float depthNear = 5f;
        public float depthFar = 60f;
        [Tooltip("On: dither the near field instead of the far field.")]
        public bool invertDepth = false;
        [Range(0f, 1f)] public float skyAmount = 1f;
    }

    public Settings settings = new Settings();

    Material m_Material;
    DitherPass m_Pass;

    public override void Create()
    {
        if (settings.shader == null)
            settings.shader = Shader.Find("Hidden/Oleo/ScreenSpaceDither");

        if (settings.shader != null && m_Material == null)
            m_Material = CoreUtils.CreateEngineMaterial(settings.shader);

        m_Pass = new DitherPass(m_Material)
        {
            renderPassEvent = settings.injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_Material == null) return;

        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection) return;

        ApplySettings();

        m_Pass.renderPassEvent = settings.injectionPoint;
        if (settings.useDepth)
            m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth);

        renderer.EnqueuePass(m_Pass);
    }

    void ApplySettings()
    {
        CoreUtils.SetKeyword(m_Material, "_PATTERN_BAYER", settings.pattern == Pattern.Bayer8);
        CoreUtils.SetKeyword(m_Material, "_PATTERN_IGN", settings.pattern == Pattern.InterleavedGradient);
        CoreUtils.SetKeyword(m_Material, "_PATTERN_BLUENOISE",
            settings.pattern == Pattern.BlueNoise && settings.blueNoise != null);
        CoreUtils.SetKeyword(m_Material, "_USE_DEPTH", settings.useDepth);

        if (settings.blueNoise != null)
            m_Material.SetTexture("_BlueNoiseTex", settings.blueNoise);

        m_Material.SetFloat("_Strength", settings.strength);
        m_Material.SetFloat("_Levels", settings.levels);
        m_Material.SetFloat("_PixelScale", settings.pixelScale);
        m_Material.SetFloat("_LumaLow", Mathf.Min(settings.lumaLow, settings.lumaHigh - 1e-3f));
        m_Material.SetFloat("_LumaHigh", settings.lumaHigh);
        m_Material.SetFloat("_MaskDither", settings.maskDither);
        m_Material.SetFloat("_Desaturate", settings.desaturate);
        m_Material.SetFloat("_DepthInfluence", settings.depthInfluence);
        m_Material.SetFloat("_DepthNear", settings.depthNear);
        m_Material.SetFloat("_DepthFar", settings.depthFar);
        m_Material.SetFloat("_DepthInvert", settings.invertDepth ? 1f : 0f);
        m_Material.SetFloat("_SkyAmount", settings.skyAmount);
        m_Material.SetFloat("_Animate", settings.animate ? 1f : 0f);
        m_Material.SetFloat("_FrameIndex", Time.frameCount % 64);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
        m_Pass?.Dispose();
    }

    // ------------------------------------------------------------------
    class DitherPass : ScriptableRenderPass
    {
        readonly Material m_Material;
        readonly ProfilingSampler m_Sampler = new ProfilingSampler("Screen Space Dither");

        public DitherPass(Material material)
        {
            m_Material = material;
            profilingSampler = m_Sampler;
        }

#if UNITY_6000_0_OR_NEWER
        class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle source = resourceData.activeColorTexture;

            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_DitherTarget";
            desc.clearBuffer = false;
            desc.depthBufferBits = 0;
            desc.msaaSamples = MSAASamples.None;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                       "Screen Space Dither", out var passData, m_Sampler))
            {
                passData.source = source;
                passData.material = m_Material;

                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                // Gives the fragment shader access to _CameraDepthTexture.
                builder.UseAllGlobalTextures(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.source,
                        new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }

            resourceData.cameraColor = destination;
        }

        public void Dispose() { }
#else
        RTHandle m_Temp;

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateIfNeeded(ref m_Temp, desc,
                FilterMode.Point, TextureWrapMode.Clamp, name: "_DitherTemp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Material == null) return;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_Sampler))
            {
                var camColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
                Blitter.BlitCameraTexture(cmd, camColor, m_Temp, m_Material, 0);
                Blitter.BlitCameraTexture(cmd, m_Temp, camColor);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            m_Temp?.Release();
        }
#endif
    }
}
