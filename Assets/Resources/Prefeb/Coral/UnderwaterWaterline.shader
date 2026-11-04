Shader "Custom/UnderwaterWaterline"
{
    Properties
    {
        _CenterTint("Center Color (Alpha = Opacity)", Color) = (0.133796, 0.7391683, 0.7735849, 0.14)
        _EdgeTint("Edge Color (Alpha = Opacity)", Color) = (0.025, 0.19, 0.35, 0.38)
        _GradientStart("Gradient Start (Screen Radius)", Range(0, 1)) = 0.05
        _GradientEnd("Gradient End (Screen Radius)", Range(0, 2)) = 0.9
        _VisibilityStart("Visibility Fade Start (m)", Float) = 2
        _VisibilityEnd("Visibility Fade End (m)", Float) = 12
        _FogColor("Distant Water Color", Color) = (0.07, 0.28, 0.36, 1)
        _FogStrength("Far Distance Fog Opacity", Range(0, 1)) = 0.96
        _WaterLevel("Water Level", Float) = 0
        _BlendWidth("Waterline Softness (m)", Range(0.001, 0.25)) = 0.05
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CenterTint;
                half4 _EdgeTint;
                half4 _FogColor;
                float _GradientStart;
                float _GradientEnd;
                float _VisibilityStart;
                float _VisibilityEnd;
                float _FogStrength;
                float _WaterLevel;
                float _BlendWidth;
            CBUFFER_END
            float _WaterCapturePass;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float worldY : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.worldY = positionWS.y;
                output.positionWS = positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // The water capture cameras must not tint their own source textures.
                clip(0.5 - _WaterCapturePass);
                half coverage = smoothstep(-_BlendWidth, _BlendWidth, _WaterLevel - input.worldY);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float radius = length(screenUV * 2.0 - 1.0);
                half edgeBlend = smoothstep(_GradientStart, max(_GradientEnd, _GradientStart + 0.001), radius);
                half4 tint = lerp(_CenterTint, _EdgeTint, edgeBlend);

                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float3 cameraWS = GetCameraPositionWS();
                if (cameraWS.y < _WaterLevel && input.positionWS.y > cameraWS.y + 0.0001)
                {
                    // Transparent water is absent from the depth texture. Limit fog to
                    // the distance of the water surface for upward-looking rays.
                    float surfaceRatio = (_WaterLevel - cameraWS.y) / (input.positionWS.y - cameraWS.y);
                    float overlayEyeDepth = -TransformWorldToView(input.positionWS).z;
                    sceneDepth = min(sceneDepth, overlayEyeDepth * surfaceRatio);
                }
                half distantFog = smoothstep(_VisibilityStart, max(_VisibilityEnd, _VisibilityStart + 0.001), sceneDepth);
                half fogAlpha = distantFog * saturate(_FogStrength);
                half tintAlpha = saturate(tint.a);
                half opacity = fogAlpha + tintAlpha * (1.0 - fogAlpha);
                half3 overlayColor = (_FogColor.rgb * fogAlpha * (1.0 - tintAlpha)
                    + tint.rgb * tintAlpha) / max(opacity, 0.0001);
                return half4(overlayColor, opacity * coverage);
            }
            ENDHLSL
        }
    }
}
