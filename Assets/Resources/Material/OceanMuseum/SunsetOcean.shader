Shader "WhaleMovie/OceanMuseum/Sunset Ocean"
{
    Properties
    {
        _WaterColor("Deep Water", Color) = (0.003, 0.012, 0.022, 1)
        _SkyTint("Sky Reflection Tint", Color) = (0.22, 0.32, 0.46, 1)
        [HDR] _SunColor("Sun Reflection Color", Color) = (1.8, 0.58, 0.15, 1)
        _SunDirection("Sun Direction (World XYZ)", Vector) = (0, 0.028, 1, 0)
        _WaterIOR("Water IOR", Range(1, 1.8)) = 1.333
        _FineWaves("Fine Scale XY / Strength / Distance", Vector) = (0.35, 2.5, 0.95, 1.1)
        _BroadWaves("Broad Scale XY / Strength / Distance", Vector) = (0.08, 0.35, 0.85, 2.2)
        _FineFlow("Fine Wave Flow (World XZ)", Vector) = (0.045, 0.12, 0, 0)
        _BroadFlow("Broad Wave Flow (World XZ)", Vector) = (-0.03, 0.18, 0, 0)
        _RoughnessRange("Noise Min/Max / Roughness Min/Max", Vector) = (0.2, 0.8, 0.03, 0.5)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _WaterColor;
            float4 _SkyTint;
            float4 _SunColor;
            float4 _SunDirection;
            float _WaterIOR;
            float4 _FineWaves;
            float4 _BroadWaves;
            float4 _FineFlow;
            float4 _BroadFlow;
            float4 _RoughnessRange;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float fogFactor : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.fogFactor = ComputeFogFactor(position.positionCS.z);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float2 Gradient(float2 cell)
            {
                float3 hash = frac(float3(cell.xyx) * float3(0.1031, 0.1030, 0.0973));
                hash += dot(hash, hash.yzx + 33.33);
                return frac((hash.xx + hash.yz) * hash.zy) * 2.0 - 1.0;
            }

            float GradientNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 blend = f * f * f * (f * (6.0 * f - 15.0) + 10.0);
                float a = dot(Gradient(cell), f);
                float b = dot(Gradient(cell + float2(1, 0)), f - float2(1, 0));
                float c = dot(Gradient(cell + float2(0, 1)), f - float2(0, 1));
                float d = dot(Gradient(cell + 1.0), f - 1.0);
                return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
            }

            float WaveNoise(float2 p, float persistence)
            {
                float footprint = max(length(ddx(p)), length(ddy(p)));
                float amplitude = 1.0;
                float sum = 0.0;
                float weight = 0.0;
                // Blender Noise Detail 3 adds three octaves to the base noise.
                [unroll] for (int octave = 0; octave < 4; octave++)
                {
                    float visible = 1.0 - smoothstep(0.25, 0.75, footprint);
                    // Keep the normalization constant across the filtered octaves.
                    [branch] if (visible > 0.0)
                        sum += GradientNoise(p) * amplitude * visible;
                    weight += amplitude;
                    amplitude *= persistence;
                    p *= 2.0;
                    footprint *= 2.0;
                }
                return saturate(0.5 + 0.8 * sum / weight);
            }

            float3 BumpNormal(float3 normal, float3 dx, float3 dy, float height)
            {
                float3 r1 = cross(dy, normal);
                float3 r2 = cross(normal, dx);
                float determinant = dot(dx, r1);
                float3 surfaceGradient = ddx(height) * r1 + ddy(height) * r2;
                return SafeNormalize(normal - surfaceGradient * sign(determinant)
                    / max(abs(determinant), 1e-8));
            }

            float Fresnel(float reflectance, float cosTheta)
            {
                float grazing = 1.0 - saturate(cosTheta);
                return reflectance + (1.0 - reflectance) * Pow4(grazing) * grazing;
            }

            float SunSpecular(float3 normal, float3 view, float3 light, float roughness, float reflectance)
            {
                float3 halfway = SafeNormalize(view + light);
                float nl = saturate(dot(normal, light));
                float nv = saturate(dot(normal, view));
                float nh = saturate(dot(normal, halfway));
                // A minimum sun lobe width approximates the source's finite sun disc.
                float alpha = max(roughness * roughness, 0.045);
                float alphaSquared = alpha * alpha;
                float denominator = (1.0 - nh * nh) + nh * nh * alphaSquared;
                float distribution = alphaSquared / (PI * denominator * denominator);
                float k = 0.5 * alpha;
                float visibility = 0.25 / max((nv * (1.0 - k) + k) * (nl * (1.0 - k) + k), 0.001);
                return min(3.0, distribution * visibility * Fresnel(reflectance, dot(view, halfway)) * nl)
                    * smoothstep(0.0, 0.06, nv);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Geometry Position in Blender XY becomes world XZ on the Unity plane.
                float2 worldXZ = input.positionWS.xz;
                float fine = WaveNoise((worldXZ - _Time.y * _FineFlow.xy) * _FineWaves.xy, 0.68);
                float broad = WaveNoise((worldXZ - _Time.y * _BroadFlow.xy) * _BroadWaves.xy, 0.5);
                float3 dx = ddx(input.positionWS);
                float3 dy = ddy(input.positionWS);
                float3 normal = BumpNormal(float3(0.0, 1.0, 0.0), dx, dy, fine * _FineWaves.z * _FineWaves.w);
                normal = BumpNormal(normal, dx, dy, broad * _BroadWaves.z * _BroadWaves.w);

                float roughnessFactor = saturate((broad - _RoughnessRange.x)
                    / max(_RoughnessRange.y - _RoughnessRange.x, 0.001));
                float roughness = lerp(_RoughnessRange.z, _RoughnessRange.w, roughnessFactor);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float iorRatio = (_WaterIOR - 1.0) / (_WaterIOR + 1.0);
                float reflectance = iorRatio * iorRatio;
                float fresnel = Fresnel(reflectance, dot(normal, view));
                float3 reflection = reflect(-view, normal);
                reflection.y = abs(reflection.y);
                half3 sky = GlossyEnvironmentReflection(reflection, roughness, 1.0);
                half3 color = lerp(_WaterColor.rgb, sky * _SkyTint.rgb, fresnel);
                color += _SunColor.rgb * SunSpecular(normal, view, SafeNormalize(_SunDirection.xyz),
                    roughness, reflectance);
                return half4(MixFog(color, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            float DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
