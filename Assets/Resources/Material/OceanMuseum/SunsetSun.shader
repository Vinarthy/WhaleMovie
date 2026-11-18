Shader "WhaleMovie/OceanMuseum/Sunset Sun"
{
    Properties
    {
        _RimColor("Rim Color", Color) = (1, 0.14, 0.015, 1)
        _MiddleColor("Middle Color", Color) = (1, 0.32, 0.03, 1)
        _CenterColor("Center Color", Color) = (1, 0.58, 0.08, 1)
        _Emission("Emission Strength", Float) = 1.8
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half4 _MiddleColor;
                half4 _CenterColor;
                half _Emission;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float facing = saturate(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                // Blender's emission ramp is driven by the geometry normal and view direction.
                half3 color = lerp(_RimColor.rgb, _MiddleColor.rgb, saturate(facing / 0.2));
                color = lerp(color, _CenterColor.rgb, saturate((facing - 0.2) / 0.45));
                return half4(color * _Emission, 1.0);
            }
            ENDHLSL
        }
    }
}
