Shader "Fart Fuel Racer/Turbo Radial Blur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                // Aim at the horizon, one third down from the top of the screen.
                // Account for the camera projection flip when rendering into a texture.
                #if UNITY_UV_STARTS_AT_TOP
                    float2 focus = float2(0.5, 0.5 - _ProjectionParams.x / 6.0);
                #else
                    float2 focus = float2(0.5, 2.0 / 3.0);
                #endif
                float2 direction = uv - focus;
                float edge = smoothstep(0.12, 0.55, length(direction));
                float2 streak = direction * (0.22 * _Intensity * edge);
                half4 color = 0;
                [unroll]
                for (int sampleIndex = 0; sampleIndex < 12; sampleIndex++)
                {
                    float2 sampleUV = saturate(uv - streak * (sampleIndex / 11.0));
                    color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV);
                }
                return color / 12.0;
            }
            ENDHLSL
        }
    }
}
