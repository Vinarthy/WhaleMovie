Shader "WhaleMovie/Cloud Sea Object"
{
    Properties
    {
        _NoiseTex("Shape Noise", 3D) = "white" {}
        _DetailTex("Detail Noise", 3D) = "white" {}
        _WeatherMap("Weather Map", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }

        Pass
        {
            Name "CloudSea"
            Blend One OneMinusSrcAlpha
            // Integrate each ray once, using the volume's exit face (also works inside it).
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE3D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            TEXTURE3D(_DetailTex);
            SAMPLER(sampler_DetailTex);
            TEXTURE2D(_WeatherMap);
            SAMPLER(sampler_WeatherMap);

            float _Density;
            float _Coverage;
            float _ShapeTiling;
            float _DetailTiling;
            float _ShapeSpeed;
            float _DetailSpeed;
            float _WarpStrength;
            float _DensityOffset;
            float _DetailErodeWeight;
            float _DetailExponent;
            float _EdgeFade;
            float _ViewAbsorption;
            float _LightAbsorption;
            float _Steps;
            float _LightSteps;
            float _MobileQuality;
            float _Brightness;
            float _AmbientFill;
            float4 _ShapeWeights;
            float4 _PhaseParams;
            float4 _LightColor;
            float4 _MidColor;
            float4 _ShadowColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float Remap(float value, float oldMin, float oldMax, float newMin, float newMax)
            {
                return newMin + (value - oldMin) / max(oldMax - oldMin, 0.0001) * (newMax - newMin);
            }

            float2 RayBox(float3 origin, float3 invDirection)
            {
                float3 t0 = (-0.5 - origin) * invDirection;
                float3 t1 = (0.5 - origin) * invDirection;
                float3 nearPlane = min(t0, t1);
                float3 farPlane = max(t0, t1);
                float entry = max(max(nearPlane.x, nearPlane.y), nearPlane.z);
                float exit = min(min(farPlane.x, farPlane.y), farPlane.z);
                float start = max(entry, 0.0);
                return float2(start, max(0.0, exit - start));
            }

            float HeightAndEdge(float3 positionOS, float weather)
            {
                float height = saturate(positionOS.y + 0.5);
                float bottomHeight = lerp(0.1, 0.5, weather);
                float bottom = saturate(Remap(height, 0.0, bottomHeight, 0.0, 1.0));
                float top = saturate(Remap(height, 0.0, max(weather, 0.08), 1.0, 0.0));
                float edgeDistance = min(0.5 - abs(positionOS.x), 0.5 - abs(positionOS.z));
                float edge = saturate(edgeDistance / max(_EdgeFade, 0.001));
                return bottom * top * edge;
            }

            float SampleDensity(float3 positionOS)
            {
                float shapeTime = _Time.y * _ShapeSpeed;
                float detailTime = _Time.y * _DetailSpeed;
                float2 weatherUV = positionOS.xz + 0.5 + float2(shapeTime * 0.4, 0.0);
                float weather = SAMPLE_TEXTURE2D_LOD(_WeatherMap, sampler_WeatherMap, weatherUV, 0).r;
                weather = saturate(weather + _Coverage - 0.5);

                float heightMask = HeightAndEdge(positionOS, weather);
                if (heightMask <= 0.0)
                    return 0.0;
                float3 worldPosition = TransformObjectToWorld(positionOS);
                float3 shapeUVW = worldPosition * _ShapeTiling + float3(shapeTime, shapeTime * 0.2, 0.0);
                if (_MobileQuality < 0.5)
                {
                    float warp = SAMPLE_TEXTURE2D_LOD(_WeatherMap, sampler_WeatherMap, weatherUV + 0.137, 0).g;
                    shapeUVW += (warp - 0.5) * _WarpStrength;
                }

                float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(_NoiseTex, sampler_NoiseTex, frac(shapeUVW), 0);
                float4 weights = max(_ShapeWeights, 0.0001);
                weights /= dot(weights, 1.0);
                float baseDensity = dot(shapeNoise, weights) * heightMask + _DensityOffset * 0.01;

                if (baseDensity <= 0.0)
                    return 0.0;

                float3 detailUVW = worldPosition * _DetailTiling + float3(detailTime, detailTime * 0.2, 0.0);
                // Reuse the shape texture's fine channel on mobile instead of a second 3D fetch.
                float detail;
                if (_MobileQuality > 0.5)
                    detail = shapeNoise.a;
                else
                {
                    detail = SAMPLE_TEXTURE3D_LOD(_DetailTex, sampler_DetailTex, frac(detailUVW), 0).r;
                    detail = pow(saturate(detail), max(_DetailExponent, 0.01));
                }
                float inverseShape = saturate(1.0 - baseDensity);
                float erosion = detail * inverseShape * inverseShape * inverseShape * _DetailErodeWeight;
                return saturate((baseDensity - erosion) * _Density);
            }

            float HG(float cosineAngle, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / max(4.0 * PI * pow(1.0 + g2 - 2.0 * g * cosineAngle, 1.5), 0.0001);
            }

            float Phase(float cosineAngle)
            {
                float phaseBlend = lerp(HG(cosineAngle, _PhaseParams.x), HG(cosineAngle, -_PhaseParams.y), 0.5);
                return _PhaseParams.z + phaseBlend * _PhaseParams.w;
            }

            float3 LightMarch(float3 positionOS, float3 lightDirectionOS, float viewLightCos)
            {
                if (_LightSteps < 0.5)
                {
                    // A single height-based scattering approximation avoids nested ray marching.
                    float height = saturate(positionOS.y + 0.5);
                    float3 ambient = lerp(_ShadowColor.rgb, _MidColor.rgb, height);
                    return lerp(ambient, _LightColor.rgb, saturate(height * 0.6 + 0.2));
                }
                float3 safeDirection = sign(lightDirectionOS) * max(abs(lightDirectionOS), 0.0001);
                float distanceToEdge = RayBox(positionOS, 1.0 / safeDirection).y;
                int count = clamp((int)_LightSteps, 1, 16);
                float stepLength = distanceToEdge / count;
                float stepLengthWorld = length(TransformObjectToWorldDir(lightDirectionOS, false)) * stepLength;
                float opticalDepth = 0.0;
                float3 samplePosition = positionOS;

                [loop]
                for (int index = 0; index < count; index++)
                {
                    samplePosition += lightDirectionOS * stepLength;
                    opticalDepth += SampleDensity(samplePosition) * stepLengthWorld;
                }

                float transmittance = exp(-opticalDepth * _LightAbsorption);
                float3 color = lerp(_MidColor.rgb, _LightColor.rgb, saturate(transmittance * 1.35));
                float lightingWeight = transmittance * 1.6;
                color = lerp(_ShadowColor.rgb, color, saturate(lightingWeight * lightingWeight * lightingWeight));
                float phaseLighting = 0.75 + saturate(Phase(viewLightCos)) * 0.75;
                return color * phaseLighting;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 cameraPositionWS = GetCameraPositionWS();
                float3 originOS = TransformWorldToObject(cameraPositionWS);
                float3 directionOS = normalize(input.positionOS - originOS);
                float3 viewDirectionWS = normalize(TransformObjectToWorld(input.positionOS) - cameraPositionWS);
                float3 safeDirection = sign(directionOS) * max(abs(directionOS), 0.0001);
                float2 hit = RayBox(originOS, 1.0 / safeDirection);

                if (hit.y <= 0.0)
                    discard;

                Light mainLight = GetMainLight();
                float3 lightDirectionOS = normalize(TransformWorldToObjectDir(mainLight.direction));
                float viewLightCos = dot(viewDirectionWS, mainLight.direction);
                int count = clamp((int)_Steps, 8, 128);
                float stepLength = hit.y / count;
                float stepLengthWorld = length(TransformObjectToWorldDir(directionOS, false)) * stepLength;
                float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);
                float travelled = hit.x + jitter * stepLength;
                float transmittance = 1.0;
                float3 radiance = 0.0;

                [loop]
                for (int index = 0; index < count; index++)
                {
                    if (travelled >= hit.x + hit.y || transmittance < 0.01)
                        break;

                    float3 positionOS = originOS + directionOS * travelled;
                    float density = SampleDensity(positionOS);

                    if (density > 0.001)
                    {
                        float extinction = density * stepLengthWorld * _ViewAbsorption;
                        float sampleAlpha = 1.0 - exp(-extinction);
                        float3 lighting = LightMarch(positionOS, lightDirectionOS, viewLightCos);
                        float3 sunTint = lerp(float3(1.0, 1.0, 1.0), mainLight.color, 0.35);
                        lighting = lerp(lighting, _LightColor.rgb, _AmbientFill);
                        radiance += transmittance * sampleAlpha * lighting * sunTint * _Brightness;
                        transmittance *= 1.0 - sampleAlpha;
                    }

                    travelled += stepLength;
                }

                float alpha = saturate(1.0 - transmittance);
                return half4(radiance, alpha);
            }
            ENDHLSL
        }
    }
}
