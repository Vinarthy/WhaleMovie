Shader "Custom/WaveWithReflection"
{
    Properties
    {
        _Smoothness("Light Smoothness", Range(8,256)) = 42
        _SpecularColor("Specular Color", Color) = (1,1,1,1)

        _WaveCount("Wave Count", int) = 30
        _RandomDirection("Random Direction", Range(0,1)) = 0.94
        _WavelengthMax("WaveLength Max", Range(0,20)) = 12
        _WavelengthMin("WaveLength Min", Range(0,5)) = 2.2
        _WavesteepnessMax("WaveSteepness Max", Range(0,10)) = 0.72
        _WavesteepnessMin("WaveSteepness Min", Range(0,1)) = 0.08
        _WaveSpeed("Wave Speed", Range(0,3)) = 0.48
        _Direction("Direction", Vector) = (1,1,0,0)

        _BaseColor("Base Color", Color) = (0.64465404,0.97253376,1,1)

        _RampTex("Depth Ramp", 2D) = "white" {}
        _SceneDepth("Depth Range", Float) = 5.57
        _RefractionIntensity("Refraction Intensity", Range(0,0.1)) = 0.0405 // 折射参数
        _RefractionGradientRange("Refraction Depth Range", Float) = 2

        _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Strength", Range(0,2)) = 1.15
        _NormalTiling("Normal Tiling", Float) = 0.55
        _NormalSpeed("Normal Speed", Float) = 0.06

        // ===== 新增：反射属性 =====
        _ReflectionIntensity("Reflection Intensity", Range(0,1)) = 0.38
        _ReflectionDistortion("Reflection Distortion", Range(0,1)) = 0.25
        _ReflectionRoughness("Reflection Roughness", Range(0,1)) = 0.1
        _PlanarReflectionStrength("Scene Reflection Strength", Range(0,1)) = 0.78
        _RefractionVisibility("Scene Refraction Visibility", Range(0,1)) = 0.535
        _WarmStreakStrength("Warm Streak Strength", Range(0,0.4)) = 0.4
    }

    SubShader
    {
        Tags
        { 
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            
            // 必须包含此库以使用反射探针/天空盒采样函数
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 projPos    : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
            float _Smoothness;
            half4 _SpecularColor;
            int _WaveCount;
            float _RandomDirection;
            float _WavelengthMax;
            float _WavelengthMin;
            float _WavesteepnessMax;
            float _WavesteepnessMin;
            float _WaveSpeed;
            float4 _Direction;
            half4 _BaseColor;
            float _SceneDepth;
            float _RefractionIntensity;
            float _RefractionGradientRange;
            float _NormalScale;
            float _NormalTiling;
            float _NormalSpeed;
            // 新增
            float _ReflectionIntensity;
            float _ReflectionDistortion;
            float _ReflectionRoughness;
            float _PlanarReflectionStrength;
            float _RefractionVisibility;
            float _WarmStreakStrength;
            CBUFFER_END

            TEXTURE2D(_RampTex); SAMPLER(sampler_RampTex);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_WaterReflectionLeft); SAMPLER(sampler_WaterReflectionLeft);
            TEXTURE2D(_WaterReflectionRight); SAMPLER(sampler_WaterReflectionRight);
            TEXTURE2D(_WaterRefractionLeft); SAMPLER(sampler_WaterRefractionLeft);
            TEXTURE2D(_WaterRefractionRight); SAMPLER(sampler_WaterRefractionRight);
            float4x4 _WaterReflectionVPLeft;
            float4x4 _WaterReflectionVPRight;
            float _WaterCaptureReady;

            float Random(int seed)
            {
                return frac(sin(dot(float2(seed,2), float2(12.9898, 78.233)))) * 2 - 1;
            }

            struct Gerstner
            {
                float3 positionWS;
                float3 binormal;
                float3 tangent;
            };

            Gerstner GerstnerWave(float4 direction,float3 positionWS,int waveCount,float wavelengthMax,float wavelengthMin,float steepnessMax,float steepnessMin,float randomdirection)
            {
                Gerstner gerstner;
                float3 P=0; float3 B=0; float3 T=0;
                for (int i = 0; i < waveCount; i++)
                {
                    float step = (float)i / (float)waveCount;
                    float2 d = float2(Random(13*i+1), Random(17*i+7));
                    d = normalize(lerp(normalize(direction.xy), d, randomdirection));
                    float wavelength = max(lerp(wavelengthMax, wavelengthMin, step) * (1.0 + 0.16 * Random(11*i+17)), 0.1);
                    float steepness = lerp(steepnessMax, steepnessMin, step)/waveCount;
                    float k = 2 * PI / wavelength;
                    float g = 9.81f;
                    float w = sqrt(g * k);
                    float a = steepness / k;
                    float2 wavevector = k * d;
                    float value = dot(wavevector, positionWS.xz + float2(i * 1.73, i * 0.97)) - w * _Time.y * _WaveSpeed;
                    P.x += d.x * a * cos(value);
                    P.z += d.y * a * cos(value);
                    P.y += a * sin(value);
                    T.x += d.x * d.x * k * a * -sin(value);
                    T.y += d.x * k * a * cos(value);
                    T.z += d.x * d.y * k * a * -sin(value);
                    B.x += d.x * d.y * k * a * -sin(value);
                    B.y += d.y * k * a * cos(value);
                    B.z += d.y * d.y * k * a * -sin(value);
                }
                gerstner.positionWS = positionWS + P;
                gerstner.tangent = float3(1 + T.x, T.y, T.z);
                gerstner.binormal = float3(B.x, B.y, 1 + B.z);
                return gerstner;
            }

            Varings vert (Attributes IN)
            {
                Varings OUT = (Varings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                Gerstner g = GerstnerWave(_Direction, positionWS, _WaveCount, _WavelengthMax, _WavelengthMin, _WavesteepnessMax, _WavesteepnessMin, _RandomDirection);
                positionWS = g.positionWS;
                OUT.normalWS = normalize(cross(g.binormal, g.tangent));
                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.projPos = ComputeScreenPos(OUT.positionCS);
                OUT.projPos.z = -TransformWorldToView(positionWS).z;
                return OUT;
            }

            half4 frag (Varings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                Light light = GetMainLight();
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                
                // ===== 1. 法线扰动 =====
                float2 uv = IN.positionWS.xz * _NormalTiling;
                float2 uv1 = uv + _Time.y * _NormalSpeed * float2(0.7, 0.3);
                float2 uv2 = float2(-uv.y, uv.x) * 0.82 + _Time.y * _NormalSpeed * float2(-0.4, 0.6);
                float3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1));
                float3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv2));
                float2 detailNormal = 0.5 * (n1.xy + float2(n2.y, -n2.x)) * _NormalScale;
                
                // 混合波浪法线和微波法线
                float3 normalWS = normalize(IN.normalWS + float3(detailNormal.x, 0, detailNormal.y));

                // ===== 2. 反射 (Skybox/Reflection Probe) =====
                // 使用扰动后的法线计算反射向量
                float3 reflectDir = reflect(-viewDirWS, normalWS);
                // 对反射向量添加一点基于法线的扭曲
                reflectDir.xz += detailNormal * _ReflectionDistortion;
                
                // 采样环境反射 (URP内置函数)
                half3 reflection = GlossyEnvironmentReflection(normalize(reflectDir), _ReflectionRoughness, 1.0);

                // ===== 3. 深度与折射 =====
                float2 screenUV = IN.projPos.xy / IN.projPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterZ = IN.projPos.z;
                float depthDiff = saturate((sceneZ - waterZ) / _SceneDepth);

                float2 offset = normalWS.xz * _RefractionIntensity;// 法线决定偏移方向，Intensity是扭曲程度
                offset *= saturate(depthDiff / abs(_RefractionGradientRange) + 0.001);
                float2 refractUV = screenUV + offset;// 改变取景位置
                
                float sceneZ2 = LinearEyeDepth(SampleSceneDepth(refractUV), _ZBufferParams);
                if (sceneZ2 - waterZ < 0) refractUV = screenUV;
                
                float3 refractColor = SampleSceneColor(refractUV);// 读取偏移位置的画面
                if (_WaterCaptureReady > 0.5)//相当于运行时开关
                {
                    #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    refractColor = unity_StereoEyeIndex == 0
                        ? SAMPLE_TEXTURE2D(_WaterRefractionLeft, sampler_WaterRefractionLeft, refractUV).rgb
                        : SAMPLE_TEXTURE2D(_WaterRefractionRight, sampler_WaterRefractionRight, refractUV).rgb;
                    #else
                    refractColor = SAMPLE_TEXTURE2D(_WaterRefractionLeft, sampler_WaterRefractionLeft, refractUV).rgb;
                    #endif
                }
                float3 depthColor = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(depthDiff, 0.5)).rgb;//深度染色条

                // ===== 4. 光照计算 =====
                half3 lambert = LightingLambert(light.color, light.direction, normalWS);
                half3 spec = LightingSpecular(light.color, light.direction, normalWS, viewDirWS, _SpecularColor, _Smoothness);

                // ===== 5. 最终融合 =====
                // 菲涅尔近似：水面侧看反射强，俯视折射强
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 3.0);
                
                float3 waterTint = depthColor * _BaseColor.rgb * (0.55 + 0.45 * lambert);
                float transmission = lerp(0.95, _RefractionVisibility, depthDiff);
                float3 waterBody = lerp(waterTint, refractColor, transmission);
                
                // 将反射叠加到水体上
                float3 finalColor = lerp(waterBody, reflection, fresnel * _ReflectionIntensity);
                if (_WaterCaptureReady > 0.5)
                {
                    float4 reflectedClip;
                    #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    reflectedClip = mul(unity_StereoEyeIndex == 0 ? _WaterReflectionVPLeft : _WaterReflectionVPRight, float4(IN.positionWS, 1));
                    #else
                    reflectedClip = mul(_WaterReflectionVPLeft, float4(IN.positionWS, 1));
                    #endif
                    float2 reflectedUV = reflectedClip.xy / max(reflectedClip.w, 0.0001) * 0.5 + 0.5;
                    reflectedUV += detailNormal * (0.015 * _ReflectionDistortion);
                    if (reflectedClip.w > 0 && all(reflectedUV >= 0) && all(reflectedUV <= 1))
                    {
                        float3 sceneReflection;
                        #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                        sceneReflection = unity_StereoEyeIndex == 0
                            ? SAMPLE_TEXTURE2D(_WaterReflectionLeft, sampler_WaterReflectionLeft, reflectedUV).rgb
                            : SAMPLE_TEXTURE2D(_WaterReflectionRight, sampler_WaterReflectionRight, reflectedUV).rgb;
                        #else
                        sceneReflection = SAMPLE_TEXTURE2D(_WaterReflectionLeft, sampler_WaterReflectionLeft, reflectedUV).rgb;
                        #endif
                        finalColor = lerp(finalColor, sceneReflection, _PlanarReflectionStrength * (0.18 + 0.82 * fresnel));
                    }
                }
                // 加上高光
                finalColor += spec * 0.4;
                float warmStreak = smoothstep(0.08, 0.38, n1.x + 0.45 * n1.y);
                finalColor = lerp(finalColor, float3(0.94, 0.69, 0.35), warmStreak * _WarmStreakStrength * (0.7 + 0.3 * fresnel));
                finalColor += warmStreak * warmStreak * 0.04;

                return float4(finalColor, _WaterCaptureReady > 0.5 ? 1.0 : depthDiff);
            }
            ENDHLSL
        }
    }
}
