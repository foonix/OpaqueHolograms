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
    public Material hologramMaterial;

    RTHandle holoObjectBuffer;
    RTHandle holoObjectBufferDepth;

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        holoObjectBuffer = RTHandles.Alloc(
            Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
            colorFormat: colorBufferFormat,
            useDynamicScale: true, name: "Hologram Object Buffer"

        );
        holoObjectBufferDepth = RTHandles.Alloc(
            Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
            colorFormat: GraphicsFormat.R32_SFloat,
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
        // Draw objects to the buffer
        CoreUtils.SetRenderTarget(ctx.cmd, holoObjectBuffer, holoObjectBufferDepth, ClearFlag.All);
        CustomPassUtils.DrawRenderers(ctx, hologramObjectLayers,
            overrideRenderState: new RenderStateBlock(RenderStateMask.Depth)
            {
                depthState = new DepthState(true, CompareFunction.LessEqual),
            }
        );

        // Set up effect properties
        var properties = new MaterialPropertyBlock();
        properties.SetTexture("_HologramObjectBuffer", holoObjectBuffer);
        properties.SetTexture("_HologramObjectBufferDepth", holoObjectBufferDepth);

        // Apply the buffer contents to the screen, doing the hologram effect at the same time.
        CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
        CoreUtils.DrawFullScreen(ctx.cmd, hologramMaterial, properties, shaderPassId: 0);
    }

    protected override void Cleanup()
    {
        holoObjectBuffer.Release();
        holoObjectBufferDepth.Release();
    }
}