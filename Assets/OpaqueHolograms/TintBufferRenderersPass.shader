Shader "Renderers/TintBufferRenderersPass"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1,1,1,1)
        _ColorMap("ColorMap", 2D) = "white" {}

        // Transparency
        _AlphaCutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [HideInInspector]_BlendMode("_BlendMode", Range(0.0, 1.0)) = 0.5
        [KeywordEnum(WorldSpace, ObjectSpace)] _TintSpace("Hologram effect space", Float) = 0
    }

    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    // #pragma enable_d3d11_debug_symbols

    //enable GPU instancing support
    #pragma multi_compile_instancing
    #pragma multi_compile _ DOTS_INSTANCING_ON

    #pragma multi_compile _TINTSPACE_WORLDSPACE _TINTSPACE_OBJECTSPACE

    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "FirstPass"
            Tags { "LightMode" = "FirstPass" }

            Blend Off
            ZWrite On
            ZTest LEqual

            Cull Back

            HLSLPROGRAM

            // Toggle the alpha test
            //#define _ALPHATEST_ON

            // Toggle transparency
            // have to set this true to write alpha to the target.
            #define _SURFACE_TYPE_TRANSPARENT

            // Toggle fog on transparent
            #define _ENABLE_FOG_ON_TRANSPARENT
            
            // List all the attributes needed in your shader (will be passed to the vertex shader)
            // you can see the complete list of these attributes in VaryingMesh.hlsl
            #define ATTRIBUTES_NEED_TEXCOORD0
            #define ATTRIBUTES_NEED_NORMAL
            #define ATTRIBUTES_NEED_TANGENT

            // List all the varyings needed in your fragment shader
            #define VARYINGS_NEED_TEXCOORD0
            #define VARYINGS_NEED_TANGENT_TO_WORLD
            #define VARYINGS_NEED_POSITION_WS

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            
            TEXTURE2D(_ColorMap);

            // Declare properties in the UnityPerMaterial cbuffer to make the shader compatible with SRP Batcher.
CBUFFER_START(UnityPerMaterial)
            float4 _ColorMap_ST;
            float4 _Color;

            float _AlphaCutoff;
            float _BlendMode;
CBUFFER_END

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassRenderersV2.hlsl"

            // If you need to modify the vertex datas, you can uncomment this code
            // Note: all the transformations here are done in object space
            #ifdef _TINTSPACE_OBJECTSPACE
            #define HAVE_MESH_MODIFICATION
            AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
            {
                //input.positionOS += input.normalOS * 0.0001; // inflate a bit the mesh to avoid z-fight
                input.uv0 = input.positionOS.xy / 1000;
                return input;
            }
            #endif

            // Put the code to render the objects in your custom pass in this function
            void GetSurfaceAndBuiltinData(FragInputs fragInputs, float3 viewDirection, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData)
            {
                #ifdef _TINTSPACE_OBJECTSPACE
                float2 linesUv = fragInputs.texCoord0;
                #else
                // default world space
                float2 linesUv = posInput.positionWS.xy;
                #endif

                float2 colorMapUv = TRANSFORM_TEX(linesUv, _ColorMap);
                float4 result = SAMPLE_TEXTURE2D(_ColorMap, s_trilinear_repeat_sampler, colorMapUv) * _Color;
                float opacity = result.a;
                float3 color = result.rgb;

                // Write back the data to the output structures
                ZERO_BUILTIN_INITIALIZE(builtinData); // No call to InitBuiltinData as we don't have any lighting
                ZERO_INITIALIZE(SurfaceData, surfaceData);
                builtinData.opacity = opacity;
                builtinData.emissiveColor = float3(0, 0, 0);
                surfaceData.color = color;
            }

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForwardUnlit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }
    }
}
