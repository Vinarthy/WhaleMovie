Shader "Custom/Portal"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.45, 0.75, 1, 0.45)
        _PortalTint ("Portal Tint", Color) = (0.2, 0.65, 1, 1)
        [HDR]_SpiralColor ("White Spiral Color", Color) = (3, 3, 3, 1)
        _Alpha ("Base Alpha", Range(0, 1)) = 0.42
        _SpiralIntensity ("Spiral Intensity", Range(0, 5)) = 2.6
        _SpiralCount ("Spiral Count", Range(1, 12)) = 4
        _SpiralWidth ("Spiral Width", Range(0.01, 0.45)) = 0.12
        _SpiralSpeed ("Spiral Speed", Range(-5, 5)) = 1.15
        _SpiralTwist ("Spiral Twist", Range(-8, 8)) = 2.8
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _RimIntensity ("Rim Intensity", Range(0, 5)) = 1.7
        _VerticalFade ("Vertical Fade", Range(0, 0.45)) = 0.08
        _ShellOffset ("Shell Offset", Range(0, 0.2)) = 0.025
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "PortalSpiral"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _PortalTint;
                half4 _SpiralColor;
                half _Alpha;
                half _SpiralIntensity;
                half _SpiralCount;
                half _SpiralWidth;
                half _SpiralSpeed;
                half _SpiralTwist;
                half _RimPower;
                half _RimIntensity;
                half _VerticalFade;
                half _ShellOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 portalShellPositionOS = input.positionOS.xyz + input.normalOS * _ShellOffset;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(portalShellPositionOS);
                output.positionCS = positionInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                half verticalMask = 1.0;
                verticalMask *= smoothstep(0.0, max(_VerticalFade, 0.001), input.uv.y);
                verticalMask *= smoothstep(0.0, max(_VerticalFade, 0.001), 1.0 - input.uv.y);

                half spiralPhase = frac(input.uv.x * _SpiralCount + input.uv.y * _SpiralTwist - _Time.y * _SpiralSpeed);
                half spiralDistance = abs(spiralPhase - 0.5) * 2.0;
                half spiralLine = 1.0 - smoothstep(_SpiralWidth, _SpiralWidth + 0.055, spiralDistance);

                half ghostPhase = frac(input.uv.x * (_SpiralCount * 0.5) - input.uv.y * (_SpiralTwist * 0.65) - _Time.y * (_SpiralSpeed * 0.72));
                half ghostDistance = abs(ghostPhase - 0.5) * 2.0;
                half ghostLine = (1.0 - smoothstep(_SpiralWidth * 0.55, _SpiralWidth * 0.55 + 0.08, ghostDistance)) * 0.45;

                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(input.viewDirWS);
                half rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimIntensity;

                half spiral = saturate(spiralLine + ghostLine) * verticalMask;
                half3 portalBase = baseSample.rgb * _PortalTint.rgb;
                half3 color = portalBase + _SpiralColor.rgb * spiral * _SpiralIntensity + _SpiralColor.rgb * rim * 0.45;
                half alpha = saturate(baseSample.a * _Alpha + spiral * 0.65 + rim * 0.22) * verticalMask;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}



