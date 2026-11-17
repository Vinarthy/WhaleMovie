Shader "WhaleMovie/SeaweedSway"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,2)) = 0.8
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        _Smoothness("Smoothness", Range(0,1)) = 0.15
        _Metallic("Metallic", Range(0,1)) = 0
        timeScale("Animation Time Scale (0 = Stop)", Float) = 1
        _SwayStrength("Sway Distance (Meters)", Range(0,0.2)) = 0.035
        _SwaySpeed("Sway Speed", Range(0,5)) = 0.8
        _SwayHeight("Plant Height (Meters)", Float) = 1.4
        _SwayDirection("Current Direction (XZ)", Vector) = (1,0,0.35,0)
        [HideInInspector] _Surface("Surface", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SeaweedVertex
            #pragma fragment LitPassFragment
            #pragma multi_compile_instancing
            #define _ALPHATEST_ON 1
            #define _NORMALMAP 1
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile _ _FORWARD_PLUS
            #include "SeaweedSwayInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            Varyings SeaweedVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 slope;
                SeaweedDeform(input.positionOS, input.normalOS, slope);
                SeaweedDeformTangent(input.tangentOS, slope);
                return LitPassVertex(input);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SeaweedVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #define _ALPHATEST_ON 1
            #define _NORMALMAP 1
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "SeaweedSwayInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            Varyings SeaweedVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 slope;
                SeaweedDeform(input.positionOS, input.normalOS, slope);
                return ShadowPassVertex(input);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SeaweedVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #define _ALPHATEST_ON 1
            #define _NORMALMAP 1
            #include "SeaweedSwayInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            Varyings SeaweedVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 slope;
                float3 unusedNormal = float3(0, 1, 0);
                SeaweedDeform(input.position, unusedNormal, slope);
                return DepthOnlyVertex(input);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SeaweedVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #define _ALPHATEST_ON 1
            #define _NORMALMAP 1
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "SeaweedSwayInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            Varyings SeaweedVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 slope;
                SeaweedDeform(input.positionOS, input.normal, slope);
                SeaweedDeformTangent(input.tangentOS, slope);
                return DepthNormalsVertex(input);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

