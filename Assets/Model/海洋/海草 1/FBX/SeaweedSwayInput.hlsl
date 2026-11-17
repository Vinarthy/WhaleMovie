// URP 14 lit surface and shared deformation for color, shadow and depth passes.
#ifndef WHALE_SEAWEED_INPUT_INCLUDED
#define WHALE_SEAWEED_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half _Cutoff;
half _BumpScale;
half _Smoothness;
half _Metallic;
half _Surface;
float timeScale;
float _SwayStrength;
float _SwaySpeed;
float _SwayHeight;
float4 _SwayDirection;
CBUFFER_END

void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
{
    surface = (SurfaceData)0;
    half4 color = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    surface.alpha = Alpha(color.a, _BaseColor, _Cutoff);
    surface.albedo = color.rgb * _BaseColor.rgb;
    surface.metallic = _Metallic;
    surface.specular = half3(0.04h, 0.04h, 0.04h);
    surface.smoothness = _Smoothness;
    surface.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    surface.occlusion = 1.0h;
}

// World-space height also works with FBX axis conversion and different mesh scales.
// timeScale = 0 uses the undeformed pose, compatible with shared LOD pause materials.
void SeaweedDeform(inout float4 positionOS, inout float3 normalOS, out float2 slope)
{
    slope = 0;
    UNITY_BRANCH
    if (abs(timeScale) < 0.00001 || _SwayStrength == 0) return;
    float3 origin = TransformObjectToWorld(float3(0, 0, 0));
    float3 position = TransformObjectToWorld(positionOS.xyz);
    float invHeight = rcp(max(_SwayHeight, 0.001));
    float h = max(position.y - origin.y, 0.0) * invHeight;
    float weight = saturate(h);
    float phase = dot(origin.xz, float2(0.73, 1.17));
    float t = _Time.y * timeScale * _SwaySpeed;
    float wave = sin(t + h * 1.4 + phase) + 0.25 * sin(t * 1.73 + h * 2.1 + phase);
    float derivative = cos(t + h * 1.4 + phase) * 1.4
                     + 0.525 * cos(t * 1.73 + h * 2.1 + phase);
    float2 direction = _SwayDirection.xz / max(length(_SwayDirection.xz), 0.001);
    position.xz += direction * (_SwayStrength * weight * weight * wave);
    float weightDerivative = (h > 0.0 && h < 1.0) ? 2.0 * weight : 0.0;
    slope = direction * (_SwayStrength * invHeight * (weightDerivative * wave + weight * weight * derivative));
    float3 normal = TransformObjectToWorldNormal(normalOS);
    normal.y -= dot(slope, normal.xz);
    normalOS = TransformWorldToObjectNormal(normal);
    positionOS.xyz = TransformWorldToObject(position);
}

void SeaweedDeformTangent(inout float4 tangentOS, float2 slope)
{
    float3 tangent = TransformObjectToWorldDir(tangentOS.xyz, false);
    tangent.xz += slope * tangent.y;
    tangentOS.xyz = TransformWorldToObjectDir(normalize(tangent));
}
#endif
