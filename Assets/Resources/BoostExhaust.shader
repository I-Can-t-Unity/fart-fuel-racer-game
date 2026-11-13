Shader "Fart Fuel Racer/Boost Exhaust"
{
    Properties
    {
        _Pulse ("Pulse", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Blend One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Pulse;
            CBUFFER_END

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(float3 position : POSITION, float2 uv : TEXCOORD0)
            {
                Varyings output;
                output.position = TransformObjectToHClip(position);
                output.uv = uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float distanceFromOutlet = input.uv.x;
                float across = abs(input.uv.y * 2.0 - 1.0);
                float envelope = 1.0 - smoothstep(0.72, 1.0, across);
                float fade = 1.0 - smoothstep(0.12, 1.0, distanceFromOutlet);
                float streaks = 0.85 + 0.15 * cos(across * 33.0 + distanceFromOutlet * 7.0);
                float nozzle = (1.0 - smoothstep(0.0, 0.17, distanceFromOutlet))
                    * (1.0 - smoothstep(0.1, 0.85, across));
                float3 cone = float3(0.50, 0.085, 1.0) * envelope * fade * streaks
                    * lerp(0.12, 0.92, _Pulse);
                float3 core = float3(0.92, 0.60, 1.0) * nozzle * lerp(0.65, 2.1, _Pulse);
                return half4(cone + core, 0.0);
            }
            ENDHLSL
        }
    }
}
