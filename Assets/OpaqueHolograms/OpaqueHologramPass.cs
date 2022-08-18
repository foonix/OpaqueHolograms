using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

class OpaqueHologramPass : CustomPass
{
    public enum EffectOrientation { ScreenSpace, WorldSpace, ObjectSpace }

    [Space]
    public EffectOrientation effectOrientation = EffectOrientation.WorldSpace;
    [Range(0f, 1f)] public float scanlineMinRemap = 0f;
    [Range(0f, 1f)] public float scanlineMaxRemap = 1f;
    public LayerMask hologramObjectLayers = 0;
    [ColorUsage(true, true)] public Color tint = Color.white;
    public GraphicsFormat colorBufferFormat = GraphicsFormat.B10G11R11_UFloatPack32;
    public DepthBits depthBits = DepthBits.Depth24;

    private Shader applyToCamera;
    Material toCamera;
    RTHandle holoObjectBuffer;
    RTHandle holoObjectBufferDepth;

    // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
    // When empty this render pass will render to the active camera render target.
    // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
    // The render pipeline will ensure target setup and clearing happens in an performance manner.
    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        applyToCamera = Shader.Find("OpaqueHolograms/ApplyToCamera");

        toCamera = CoreUtils.CreateEngineMaterial(applyToCamera);

        // Define the outline buffer
        holoObjectBuffer = RTHandles.Alloc(
            Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
            //colorFormat: GraphicsFormat.B10G11R11_UFloatPack32,
            colorFormat: colorBufferFormat,
            //colorFormat: GraphicsFormat.R32G32B32A32_SFloat,
            //depthBufferBits: depthBits,
            useDynamicScale: true, name: "Hologram Object Buffer"

        );
        holoObjectBufferDepth = RTHandles.Alloc(
            Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
            //colorFormat: GraphicsFormat.R16_UInt,
            depthBufferBits: depthBits,
            useDynamicScale: true, name: "Hologram Object Buffer Depth"
        );
    }

    // https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@15.0/manual/Custom-Pass-Troubleshooting.html#culling-issues
    protected override void AggregateCullingParameters(ref ScriptableCullingParameters cullingParameters, HDCamera camera)
    {
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableCullingParameters.html
        cullingParameters.cullingMask = (uint)hologramObjectLayers.value;
    }

    protected override void Execute(CustomPassContext ctx)
    {
        // Render meshes we want to apply the outline effect to in the outline buffer
        //CoreUtils.SetRenderTarget(ctx.cmd, holoObjectBuffer, holoObjectBufferDepth, ClearFlag.Color | ClearFlag.Depth, clearColor: new Color(0,0,0,0));
        CoreUtils.SetRenderTarget(ctx.cmd, holoObjectBuffer, holoObjectBufferDepth, ClearFlag.All);
        //CustomPassUtils.DrawRenderers(ctx, new ShaderTagId[] { new("ForwardOnly") }, hologramObjectLayers, RenderQueueType.All);
        //CustomPassUtils.DrawRenderers(ctx, hologramObjectLayers/*, RenderQueueType.AllOpaque*/);
        CustomPassUtils.DrawRenderers(ctx, hologramObjectLayers,
            overrideRenderState: new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(true, CompareFunction.Less)
            }
        );

        // Set up outline effect properties
        //ctx.propertyBlock.SetColor("_OutlineColor", outlineColor);
        //ctx.propertyBlock.SetTexture("_OutlineBuffer", outlineBuffer);
        //ctx.propertyBlock.SetFloat("_Threshold", threshold);

        ctx.propertyBlock.SetColor("_Tint", tint);
        ctx.propertyBlock.SetTexture("_HologramObjectBuffer", holoObjectBuffer);
        ctx.propertyBlock.SetTexture("_HologramObjectBufferDepth", holoObjectBufferDepth);

        // Render the outline buffer fullscreen
        CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
        CoreUtils.DrawFullScreen(ctx.cmd, toCamera, ctx.propertyBlock, shaderPassId: 0);
    }

    protected override void Cleanup()
    {
        CoreUtils.Destroy(toCamera);
        holoObjectBuffer.Release();
        holoObjectBufferDepth.Release();
    }
}