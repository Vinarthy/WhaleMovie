Shader "WhaleMovie/Seabed Caustics Overlay"
{
    Properties
    {
        _CausticsTex ("Caustics Mask", 2D) = "black" {}
        _FlowNoiseTex ("Local Flow Noise", 2D) = "gray" {}
        [HDR] _Tint ("Light Tint", Color) = (0.8, 0.95, 1, 1)
        _Intensity ("Brightness", Range(0, 2)) = 0.55
        _TileCount ("Repeats Across Terrain", Range(1, 30)) = 10
        _FlowSpeed ("Flow Speed", Range(0, 1)) = 0.16
        _WarpStrength ("Local Warp Strength", Range(0, 0.1)) = 0.02
        _NoiseTiling ("Flow Noise Repeats", Range(1, 12)) = 4
        _NoiseSpeed ("Flow Noise Speed", Range(0, 0.3)) = 0.04
        _SecondaryScale ("Second Layer Scale", Range(0.5, 2)) = 1.37
        _ModulationStrength ("Brightness Variation", Range(0, 1)) = 0.35
        _ChromaticOffset ("Rainbow Edge Width", Range(0, 0.02)) = 0.004
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }

        Pass
        {
            Name "SeabedCaustics"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CausticsTex);
            SAMPLER(sampler_CausticsTex);
            TEXTURE2D(_FlowNoiseTex);
            SAMPLER(sampler_FlowNoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _CausticsTex_ST;
                half4 _Tint;
                float _Intensity;
                float _TileCount;
                float _FlowSpeed;
                float _WarpStrength;
                float _NoiseTiling;
                float _NoiseSpeed;
                float _SecondaryScale;
                float _ModulationStrength;
                float _ChromaticOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 terrainUV : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _CausticsTex) * _TileCount;
                output.terrainUV = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float time = _Time.y;
                float2 flowUV = input.terrainUV * _NoiseTiling
                    + time * _NoiseSpeed * float2(0.31, -0.23);
                float2 flow = SAMPLE_TEXTURE2D(_FlowNoiseTex, sampler_FlowNoiseTex, flowUV).rg * 2.0 - 1.0;
                float2 warp = flow * _WarpStrength;
                float drift = time * _FlowSpeed;
                float2 primaryUV = input.uv + warp + drift * float2(0.04, 0.018);
                float2 secondaryUV = input.uv * _SecondaryScale + warp * 1.27
                    + float2(0.37, 0.19) + drift * float2(-0.031, 0.026);

                half secondLayer = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, secondaryUV).a;
                half brightnessVariation = 1.0 - _ModulationStrength * (1.0 - secondLayer);

                float2 fringe = float2(_ChromaticOffset, _ChromaticOffset * 0.5);
                half red = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, primaryUV + fringe).a;
                half green = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, primaryUV).a;
                half blue = SAMPLE_TEXTURE2D(_CausticsTex, sampler_CausticsTex, primaryUV - fringe).a;
                half3 color = (half3(red, green, blue) * brightnessVariation
                    + secondLayer * 0.12) * _Tint.rgb * _Intensity;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
