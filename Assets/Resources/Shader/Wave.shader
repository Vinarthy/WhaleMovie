Shader "Custom/WaveWithReflection"
{
    Properties
    {
        _Smoothness("Light Smoothness", Range(8,256)) = 64
        _SpecularColor("Specular Color", Color) = (0.5,0.5,0.5,1)

        _WaveCount("Wave Count", int) = 16
        _RandomDirection("Random Direction", Range(0,1)) = 1
        _WavelengthMax("WaveLength Max", Range(0,5)) = 5
        _WavelengthMin("WaveLength Min", Range(0,5)) = 1
        _WavesteepnessMax("WaveSteepness Max", Range(0,10)) = 3
        _WavesteepnessMin("WaveSteepness Min", Range(0,1)) = 0
        _WaveSpeed("Wave Speed", Range(0,3)) = 1.0
        _Direction("Direction", Vector) = (1,1,0,0)

        _BaseColor("Base Color", Color) = (0.0,0.5,1.0,1)

        _RampTex("Depth Ramp", 2D) = "white" {}
        _SceneDepth("Depth Range", Float) = 5
        _RefractionIntensity("Refraction Intensity", Range(0,0.1)) = 0.02
        _RefractionGradientRange("Refraction Depth Range", Float) = 2

        _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalScale("Normal Strength", Range(0,2)) = 1
        _NormalTiling("Normal Tiling", Float) = 4
        _NormalSpeed("Normal Speed", Float) = 0.2

        // ===== 新增：反射属性 =====
        _ReflectionIntensity("Reflection Intensity", Range(0,1)) = 0.5
        _ReflectionDistortion("Reflection Distortion", Range(0,1)) = 0.1
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
            CBUFFER_END

            TEXTURE2D(_RampTex); SAMPLER(sampler_RampTex);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

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
                    float2 d = float2(Random(i), Random(2*i));
                    d = normalize(lerp(normalize(direction.xy), d, randomdirection));
                    float wavelength = lerp(wavelengthMax, wavelengthMin, step);
                    float steepness = lerp(steepnessMax, steepnessMin, step)/waveCount;
                    float k = 2 * PI / wavelength;
                    float g = 9.81f;
                    float w = sqrt(g * k);
                    float a = steepness / k;
                    float2 wavevector = k * d;
                    float value = dot(wavevector, positionWS.xz) - w * _Time.y * _WaveSpeed;
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
                float2 uv1 = uv + _Time.y * _NormalSpeed;
                float2 uv2 = uv - _Time.y * _NormalSpeed * 0.7;
                float3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1));
                float3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv2));
                float3 normalTS = normalize(n1 + n2);
                
                // 混合波浪法线和微波法线
                float3 normalWS = normalize(IN.normalWS + normalTS * _NormalScale);

                // ===== 2. 反射 (Skybox/Reflection Probe) =====
                // 使用扰动后的法线计算反射向量
                float3 reflectDir = reflect(-viewDirWS, normalWS);
                // 对反射向量添加一点基于法线的扭曲
                reflectDir.xz += normalTS.xz * _ReflectionDistortion;
                
                // 采样环境反射 (URP内置函数)
                half3 reflection = GlossyEnvironmentReflection(reflectDir, 0.0, 1.0); 

                // ===== 3. 深度与折射 =====
                float2 screenUV = IN.projPos.xy / IN.projPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterZ = IN.projPos.z;
                float depthDiff = saturate((sceneZ - waterZ) / _SceneDepth);

                float2 offset = normalWS.xz * _RefractionIntensity;
                offset *= saturate(depthDiff / abs(_RefractionGradientRange) + 0.001);
                float2 refractUV = screenUV + offset;
                
                float sceneZ2 = LinearEyeDepth(SampleSceneDepth(refractUV), _ZBufferParams);
                if (sceneZ2 - waterZ < 0) refractUV = screenUV;
                
                float3 refractColor = SampleSceneColor(refractUV);
                float3 depthColor = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(depthDiff, 0.5)).rgb;

                // ===== 4. 光照计算 =====
                half3 lambert = LightingLambert(light.color, light.direction, normalWS);
                half3 spec = LightingSpecular(light.color, light.direction, normalWS, viewDirWS, _SpecularColor, _Smoothness);

                // ===== 5. 最终融合 =====
                // 菲涅尔近似：水面侧看反射强，俯视折射强
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 3.0);
                
                float3 waterBody = lerp(refractColor, depthColor * (lambert * _BaseColor.rgb + spec), depthDiff);
                
                // 将反射叠加到水体上
                float3 finalColor = lerp(waterBody, reflection, fresnel * _ReflectionIntensity);
                // 加上高光
                finalColor += spec;

                return float4(finalColor, depthDiff);
            }
            ENDHLSL
        }
    }
}