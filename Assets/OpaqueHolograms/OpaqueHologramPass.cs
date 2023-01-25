using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

class OpaqueHologramPass : CustomPass
{
    [Serializable]
    struct LayerTintMapping
    {
        public LayerMask hologramObjectLayers;
        public Material tintMaterial;
    }

    [Range(0f, 1f)] public float scanlineMinRemap = 0f;
    [Range(0f, 1f)] public float scanlineMaxRemap = 1f;
    [SerializeField] List<LayerTintMapping> layerTintMappings = new List<LayerTintMapping>();
    public GraphicsFormat colorBufferFormat = GraphicsFormat.B10G11R11_UFloatPack32;
    public DepthBits depthBits = DepthBits.Depth24;
    [SerializeField] public Material hologramMaterial;
    public Quaternion effectRotation = Quaternion.identity;
    public Vector3 effectScale = Vector3.one;
    public float mipBias = 0f;
    [Tooltip("Scroll the texture in worldspace units (meters) per second.")]
    public Vector3 scrollVelocity = Vector3.up * 0.01f;

    RTHandle holoObjectBuffer;
    RTHandle holoObjectBufferDepth;
    RTHandle holoObjectTintBuffer;

    static readonly ProfilerMarker passTimer = new("OpaqueHologramPass");
    private float scrollPos = 0f;

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
        holoObjectTintBuffer = RTHandles.Alloc(
            Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
            colorFormat: colorBufferFormat,
            useDynamicScale: true, name: "Hologram Object Tint Buffer"
        );
    }

    // https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@15.0/manual/Custom-Pass-Troubleshooting.html#culling-issues
    protected override void AggregateCullingParameters(ref ScriptableCullingParameters cullingParameters, HDCamera camera)
    {
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableCullingParameters.html
        LayerMask mask = 0x0;
        for (int i = 0; i < layerTintMappings.Count; i++)
        {
            mask |= layerTintMappings[i].hologramObjectLayers;
        }
        cullingParameters.cullingMask = (uint)mask.value;
    }

    protected override void Execute(CustomPassContext ctx)
    {
        passTimer.Begin();
        // drawing "cheaply" to the tint buffer first to serve as depth prepass
        CoreUtils.SetRenderTarget(ctx.cmd, holoObjectTintBuffer, holoObjectBufferDepth, ClearFlag.All);
        foreach (var layer in layerTintMappings)
        {
            CustomPassUtils.DrawRenderers(ctx, layer.hologramObjectLayers,
                overrideRenderState: new RenderStateBlock(RenderStateMask.Depth)
                {
                    depthState = new DepthState(true, CompareFunction.LessEqual),
                },
                overrideMaterial: layer.tintMaterial
            );
        }
        // Draw objects to the buffer
        CoreUtils.SetRenderTarget(ctx.cmd, holoObjectBuffer, holoObjectBufferDepth, ClearFlag.Color);
        foreach (var layer in layerTintMappings)
        {
            CustomPassUtils.DrawRenderers(ctx, layer.hologramObjectLayers,
                overrideRenderState: new RenderStateBlock(RenderStateMask.Depth)
                {
                    depthState = new DepthState(true, CompareFunction.LessEqual),
                }
            );
        }

        var orientation = Matrix4x4.TRS(Vector3.zero, effectRotation, effectScale);

        var linesTexture = hologramMaterial.GetTexture("_LinesTexture");
        linesTexture.mipMapBias = mipBias;
        scrollPos = (scrollPos + Time.deltaTime) % (1f / scrollVelocity.magnitude); // deltaTime so it stops when paused

        // Set up effect properties
        var properties = new MaterialPropertyBlock();
        properties.SetTexture("_HologramObjectBuffer", holoObjectBuffer);
        properties.SetTexture("_HologramObjectBufferDepth", holoObjectBufferDepth);
        properties.SetTexture("_HologramObjectTintBuffer", holoObjectTintBuffer);
        properties.SetMatrix("_EffectOrientation", orientation);
        properties.SetVector("_LineOffset", Vector3.LerpUnclamped(Vector3.zero, scrollVelocity, scrollPos));

        // Apply the buffer contents to the screen, doing the hologram effect at the same time.
        CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
        CoreUtils.DrawFullScreen(ctx.cmd, hologramMaterial, properties, shaderPassId: 0);
        passTimer.End();
    }

    protected override void Cleanup()
    {
        holoObjectBuffer.Release();
        holoObjectBufferDepth.Release();
        holoObjectTintBuffer.Release();
    }
}