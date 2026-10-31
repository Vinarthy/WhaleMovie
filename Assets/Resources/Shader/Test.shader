Shader "Custom/Test"
{
    SubShader
    {
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct app { float4 pos : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };
            
            v2f vert(app IN) {
                v2f o;
                o.pos = TransformObjectToHClip(IN.pos.xyz);
                return o;
            }
            
            half4 frag(v2f IN) : SV_Target {
                return half4(1, 0, 1, 1); // 返回纯紫色，能看见就说明编译成功
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
