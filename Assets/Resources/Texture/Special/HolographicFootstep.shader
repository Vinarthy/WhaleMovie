Shader "WhaleMovie/Holographic Footstep"
{
    Properties
    {
        [MainTexture] _PatternTex ("Scan Pattern", 2D) = "white" {}
        [HDR] _Tint ("Glow Color", Color) = (0.35, 1.35, 1.8, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.78
        _Brightness ("Brightness", Range(0, 4)) = 1.45
        _WorldTiling ("World Tiling", Range(0.1, 8)) = 1.5
        _ScanSpeed ("Scan Speed", Range(-5, 5)) = 0.55
        _PulseStrength ("Pulse Strength", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "HolographicFootstep"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PatternTex);
            SAMPLER(sampler_PatternTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PatternTex_ST;
                half4 _Tint;
                half _Opacity;
                half _Brightness;
                half _WorldTiling;
                half _ScanSpeed;
                half _PulseStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // World-space projection keeps the stripes consistent across imported footprint meshes.
                float2 uv = input.positionWS.xz * _WorldTiling;
                uv = uv * _PatternTex_ST.xy + _PatternTex_ST.zw;
                uv.y -= _Time.y * _ScanSpeed;
                half3 pattern = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, uv).rgb;
                half luminance = dot(pattern, half3(0.2126, 0.7152, 0.0722));
                half pulse = 1.0h + _PulseStrength * sin(_Time.y * 3.0h - input.positionWS.z * 9.0h);
                half alpha = saturate((luminance - 0.08h) * 1.45h) * _Opacity;
                return half4(pattern * _Tint.rgb * _Brightness * pulse, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
