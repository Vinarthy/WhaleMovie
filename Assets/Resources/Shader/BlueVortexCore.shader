Shader "WhaleMovie/Portal/Blue Vortex Core"
{
    Properties
    {
        _NoiseA ("Flow Noise A", 2D) = "gray" {}
        _NoiseB ("Flow Noise B", 2D) = "gray" {}
        [HDR] _DarkColor ("Dark", Color) = (0.006, 0.012, 0.045, 1)
        [HDR] _BlueColor ("Blue", Color) = (0.025, 0.35, 1.4, 1)
        [HDR] _WhiteColor ("White", Color) = (1.15, 1.25, 1.35, 1)
        _CoreBrightness ("Core Brightness", Range(0.2, 1.2)) = 0.85
        _SpinSpeed ("Spin Speed", Range(-2, 2)) = 0.16
        _Swirl ("Swirl", Range(0, 4)) = 1.7
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "PortalCore"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseA); SAMPLER(sampler_NoiseA);
            TEXTURE2D(_NoiseB); SAMPLER(sampler_NoiseB);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseA_ST;
                float4 _NoiseB_ST;
                half4 _DarkColor;
                half4 _BlueColor;
                half4 _WhiteColor;
                float _CoreBrightness;
                float _SpinSpeed;
                float _Swirl;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float angle = atan2(p.y, p.x) / 6.2831853;
                float t = _Time.y * _SpinSpeed;
                float spiral = angle + radius * _Swirl - t;
                float2 uvA = float2(spiral * 2.0, radius * 2.2 - t * 0.7) * _NoiseA_ST.xy + _NoiseA_ST.zw;
                float2 uvB = float2(angle * 3.0 - radius * (_Swirl + 0.8) + t * 0.7,
                                    radius * 3.0 + t * 0.38) * _NoiseB_ST.xy + _NoiseB_ST.zw;
                float a = SAMPLE_TEXTURE2D(_NoiseA, sampler_NoiseA, uvA).r;
                float b = SAMPLE_TEXTURE2D(_NoiseB, sampler_NoiseB, uvB).r;
                float wave = sin(angle * 31.4159 + radius * 19.0 - _Time.y * 1.7);
                float white = smoothstep(0.34, 0.53, b * 0.7 + a * 0.3 + wave * 0.08);
                float darkAccent = 1.0 - smoothstep(0.23, 0.34, a * 0.55 + b * 0.45 + wave * 0.07);
                half3 color = lerp(_BlueColor.rgb, _WhiteColor.rgb, white * 0.92);
                color = lerp(color, _DarkColor.rgb, darkAccent * 0.82);
                float innerRim = smoothstep(0.80, 0.98, radius);
                color += _BlueColor.rgb * innerRim * 0.14;
                return half4(color * _CoreBrightness, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
