Shader "Custom/Whale_URP_CartoonGlow"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)

        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,2)) = 1

        _EmissionColor ("Emission Color", Color) = (0.2,0.8,1,1)
        _EmissionStrength ("Emission Strength", Range(0,5)) = 1

        _RimColor ("Rim Color", Color) = (0.3,0.8,1,1)
        _RimPower ("Rim Width", Range(0.5,8)) = 2.5
        _RimStrength ("Rim Strength", Range(0,5)) = 2

        _SparkleTex ("Sparkle Noise", 2D) = "white" {}
        _SparkleColor ("Sparkle Color", Color) = (1,1,1,1)
        _SparkleStrength ("Sparkle Strength", Range(0,5)) = 1.5
        _SparkleThreshold ("Sparkle Threshold", Range(0,1)) = 0.85
        _SparkleScale ("Sparkle Scale", Range(1,50)) = 15
        _SparkleSpeed ("Sparkle Speed", Range(0,5)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            TEXTURE2D(_SparkleTex);
            SAMPLER(sampler_SparkleTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                float4 _SparkleTex_ST;

                half4 _BaseColor;
                half _BumpScale;

                half4 _EmissionColor;
                half _EmissionStrength;

                half4 _RimColor;
                half _RimPower;
                half _RimStrength;

                half4 _SparkleColor;
                half _SparkleStrength;
                half _SparkleThreshold;
                half _SparkleScale;
                half _SparkleSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float fogCoord : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nor = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nor.normalWS;
                OUT.tangentWS = nor.tangentWS;
                OUT.bitangentWS = nor.bitangentWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv),
                    _BumpScale
                );

                half3x3 TBN = half3x3(
                    normalize(IN.tangentWS),
                    normalize(IN.bitangentWS),
                    normalize(IN.normalWS)
                );

                half3 normalWS = normalize(mul(normalTS, TBN));
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light mainLight = GetMainLight();

                half NdotL = saturate(dot(normalWS, mainLight.direction));

                // 卡通分层光照
                half toonLight = NdotL > 0.5 ? 1.0 : 0.45;

                half3 color = baseTex.rgb * mainLight.color * toonLight;

                // 环境补光，避免暗部死黑
                color += baseTex.rgb * 0.25;

                // 边缘发光描边
                half rim = 1.0 - saturate(dot(viewDirWS, normalWS));
                rim = pow(rim, _RimPower);
                half3 emission = _RimColor.rgb * rim * _RimStrength;

                // 自发光
                emission += _EmissionColor.rgb * _EmissionStrength;

                // 闪点噪声
                float2 sparkleUV = IN.uv * _SparkleScale;
                sparkleUV += float2(_Time.y * _SparkleSpeed, _Time.y * _SparkleSpeed * 0.37);

                half noise = SAMPLE_TEXTURE2D(_SparkleTex, sampler_SparkleTex, sparkleUV).r;
                half sparkle = step(_SparkleThreshold, noise);

                half twinkle = abs(sin(_Time.y * 6.0 + IN.positionWS.x * 3.0 + IN.positionWS.y * 2.0));
                emission += _SparkleColor.rgb * sparkle * twinkle * _SparkleStrength;

                color += emission;
                color = MixFog(color, IN.fogCoord);

                return half4(color, baseTex.a);
            }
            ENDHLSL
        }
    }
}