Shader "Custom/URP/Hologram"
{
    Properties
    {
        [MainTexture] _BaseMap("人物原始贴图", 2D) = "white" {}
        [MainColor] _BaseColor("全息颜色", Color) = (0.30, 0.75, 0.90, 1)

        [Header(Body)]
        _Opacity("主体透明度", Range(0, 1)) = 0.72
        _TextureDetail("保留贴图明暗", Range(0, 1)) = 0.85

        [Header(Edge Glow)]
        [HDR] _EdgeColor("边缘光颜色", Color) = (0.25, 0.85, 1.0, 1)
        _EdgePower("边缘光宽度", Range(1, 8)) = 3
        _EdgeIntensity("边缘光强度", Range(0, 2)) = 0.45
        _EdgeOpacity("边缘额外透明度", Range(0, 0.5)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Soft Hologram"
            Tags { "LightMode" = "UniversalForward" }

            // 只画模型正面，避免背面叠加后显得过亮、过乱
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS    : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EdgeColor;
                half _Opacity;
                half _TextureDetail;
                half _EdgePower;
                half _EdgeIntensity;
                half _EdgeOpacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInput =
                    GetVertexPositionInputs(input.positionOS.xyz);

                VertexNormalInputs normalInput =
                    GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInput.positionCS;
                output.positionWS = positionInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(
                    _BaseMap, sampler_BaseMap, input.uv);

                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS =
                    normalize(GetWorldSpaceViewDir(input.positionWS));

                // 把原贴图转为明暗信息，让衣服、面部细节仍然可见
                half luminance = dot(
                    tex.rgb, half3(0.2126, 0.7152, 0.0722));

                half textureShade = lerp(
                    1.0h,
                    0.28h + luminance * 0.95h,
                    _TextureDetail);

                // 用主光源提供轻微的体积明暗，不让模型变成一块纯色
                Light mainLight = GetMainLight();
                half diffuse = saturate(
                    dot(normalWS, mainLight.direction));

                half lighting = 0.60h + diffuse * 0.40h;

                // 只在视角边缘增加柔和青光
                half rim = pow(
                    1.0h - saturate(dot(normalWS, viewDirWS)),
                    _EdgePower);

                half3 bodyColor =
                    _BaseColor.rgb * textureShade * lighting;

                half3 edgeColor =
                    _EdgeColor.rgb * rim * _EdgeIntensity;

                half alpha = saturate(
                    (_Opacity + rim * _EdgeOpacity)
                    * tex.a
                    * _BaseColor.a);

                return half4(bodyColor + edgeColor, alpha);
            }
            ENDHLSL
        }
    }
}