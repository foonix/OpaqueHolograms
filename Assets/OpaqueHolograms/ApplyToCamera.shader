Shader "OpaqueHolograms/ApplyToCamera"
{
    HLSLINCLUDE

    #pragma vertex Vert

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    // The PositionInputs struct allow you to retrieve a lot of useful information for your fullScreenShader:
    // struct PositionInputs
    // {
    //     float3 positionWS;  // World space position (could be camera-relative)
    //     float2 positionNDC; // Normalized screen coordinates within the viewport    : [0, 1) (with the half-pixel offset)
    //     uint2  positionSS;  // Screen space pixel coordinates                       : [0, NumPixels)
    //     uint2  tileCoord;   // Screen tile coordinates                              : [0, NumTiles)
    //     float  deviceDepth; // Depth from the depth buffer                          : [0, 1] (typically reversed)
    //     float  linearDepth; // View space Z coordinate                              : [Near, Far]
    // };

    // To sample custom buffers, you have access to these functions:
    // But be careful, on most platforms you can't sample to the bound color buffer. It means that you
    // can't use the SampleCustomColor when the pass color buffer is set to custom (and same for camera the buffer).
    // float4 SampleCustomColor(float2 uv);
    // float4 LoadCustomColor(uint2 pixelCoords);
    // float LoadCustomDepth(uint2 pixelCoords);
    // float SampleCustomDepth(float2 uv);

    // There are also a lot of utility function you can use inside Common.hlsl and Color.hlsl,
    // you can check them out in the source code of the core SRP package.

    float4 _Tint;
    TEXTURE2D(_LinesTexture);
    SAMPLER(sampler_LinesTexture);
    float4x4 _EffectOrientation;
    TEXTURE2D_X(_HologramObjectBuffer);
    TEXTURE2D_X(_HologramObjectBufferDepth);
    TEXTURE2D_X(_HologramObjectTintBuffer);
    float4 _TextureLerp;
    float4 _HoloBufferBrightnessLerp;
    float3 _LineOffset;

    float Epsilon = 1e-10;

    float3 RGBtoHCV(in float3 RGB)
    {
        // Based on work by Sam Hocevar and Emil Persson
        float4 P = (RGB.g < RGB.b) ? float4(RGB.bg, -1.0, 2.0/3.0) : float4(RGB.gb, 0.0, -1.0/3.0);
        float4 Q = (RGB.r < P.x) ? float4(P.xyw, RGB.r) : float4(RGB.r, P.yzx);
        float C = Q.x - min(Q.w, Q.y);
        float H = abs((Q.w - Q.y) / (6 * C + Epsilon) + Q.z);
        return float3(H, C, Q.x);
    }
    float3 RGBtoHSV(in float3 RGB)
    {
        float3 HCV = RGBtoHCV(RGB);
        float S = HCV.y / (HCV.z + Epsilon);
        return float3(HCV.x, S, HCV.z);
    }

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float3 viewDirection = GetWorldSpaceNormalizeViewDir(posInput.positionWS);

        // When sampling RTHandle texture, always use _RTHandleScale.xy to scale your UVs first.
        float2 uv = posInput.positionNDC.xy * _RTHandleScale.xy;
        float4 holoObjColor = SAMPLE_TEXTURE2D_X_LOD(_HologramObjectBuffer, s_linear_clamp_sampler, uv, 0);
        float4 rawHoloObjColor = holoObjColor;
        float holoDepth = SAMPLE_TEXTURE2D_X_LOD(_HologramObjectBufferDepth, s_linear_clamp_sampler, uv, 0).r;

        float holoObjectBrigtness = clamp(RGBtoHSV(holoObjColor).z, 0, 1);

        // position information for the opaque fragment in the hologram buffer.
        PositionInputs holoBufferPosInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, holoDepth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

        // actual world space version
        float2 linesUV = (mul(_EffectOrientation, GetAbsolutePositionWS(holoBufferPosInput.positionWS+_LineOffset))).xy;
        // camera relative version
        //float2 linesUV = mul(_EffectOrientation, holoBufferPosInput.positionWS).xy;
        // screen coordinate version
        //float2 linesUV = mul(_EffectOrientation, holoBufferPosInput.positionSS * holoBufferPosInput.linearDepth).xy;

        float4 linesTexel = SAMPLE_TEXTURE2D(_LinesTexture, sampler_LinesTexture, linesUV);
        
        // alpha blending is used to determine if we use more of the custom pass buffer or more of the camera buffer for the fragment.
        // the alpha buffer is 1 where opaque was rendered and 0 where nothing was rendered
        float4 holoObjTint = SAMPLE_TEXTURE2D_X_LOD(_HologramObjectTintBuffer, s_linear_clamp_sampler, uv, 0);
        holoObjColor *= _Tint * holoObjTint;

        float lineAlphaBiased = lerp(_TextureLerp.x, _TextureLerp.y, linesTexel.a)
            * lerp(_HoloBufferBrightnessLerp.x, _HoloBufferBrightnessLerp.y, holoObjectBrigtness);
        holoObjColor.a = clamp(lineAlphaBiased, 0, 1);

        // Depth test so that objects in the camera target are not occluded by the hologram.
        if(holoDepth < depth)
        {
            holoObjColor = float4(0,0,0,0);
        }
        else
        {
            // debug dump stuff to red channel
            //holoObjColor = float4(linesTexel * _LinesScale.x, 0, 0, holoObjColor.a);
            //holoObjColor = float4(holoObjectBrigtness, 0, 0, holoObjColor.a);
            //holoObjColor = holoObjTint * RGBtoHSV(rawHoloObjColor).z * linesTexel.a ;
            holoObjColor = holoObjTint * rawHoloObjColor;
        }

        return holoObjColor;
    }

    ENDHLSL

    Properties
    {
        [HDR] _Tint("Color Multiplier", Color) = (.25, .5, .5, 1)
        _LinesTexture("Hologram Lines", 2D) =  "white" {}
        _TextureLerp("Texture alpha lerp", Vector) = (0, 1, 0, 0)
        _HoloBufferBrightnessLerp("Brightness lerp", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Pass
        {
            Name "Custom Pass 0"

            ZWrite Off
            ZTest Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
                #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
