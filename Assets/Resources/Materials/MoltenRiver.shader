Shader "GemCatch/MoltenRiver"
{
    Properties
    {
        [MainTexture] _MainTex ("Cavern", 2D) = "black" {}
        _FlowTime ("Flow time", Float) = 0
        _FlowRate ("Cycles per second", Float) = 0.065
        _FlowDistance ("Flow distance", Range(0, 0.04)) = 0.016
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "LavaRiver"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _FlowTime;
                float _FlowRate;
                float _FlowDistance;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            half LavaMask(half3 c, float2 uv)
            {
                // Only bright orange/yellow river pixels, not the dark rock or red-lit walls.
                // Thresholds operate on linear texture samples.
                half hot = smoothstep(0.48, 0.82, c.r) * smoothstep(0.09, 0.3, c.g);
                half warm = 1.0 - smoothstep(0.12, 0.28, c.b);
                half river = 1.0 - smoothstep(0.31, 0.40, uv.y);
                return hot * warm * river;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 still = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half mask = LavaMask(still.rgb, uv);
                // Two offset flow phases crossfade so there is no visible loop reset.
                float phaseA = frac(_FlowTime * _FlowRate);
                float phaseB = frac(_FlowTime * _FlowRate + 0.5);
                float weightA = 1.0 - abs(phaseA * 2.0 - 1.0);
                // Downstream toward the foreground, bending with the winding river.
                float2 direction = normalize(float2(0.6 * sin(uv.y * 30.0 + 0.8), -1.0));
                float2 uvA = saturate(uv - direction * (phaseA - 0.5) * _FlowDistance);
                float2 uvB = saturate(uv - direction * (phaseB - 0.5) * _FlowDistance);
                half3 sampleA = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvA).rgb;
                half3 sampleB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvB).rgb;
                // Anchor both river banks; never pull dark rock pixels into moving lava.
                half3 a = lerp(still.rgb, sampleA, LavaMask(sampleA, uvA));
                half3 b = lerp(still.rgb, sampleB, LavaMask(sampleB, uvB));
                return half4(lerp(still.rgb, lerp(b, a, weightA), mask), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Unlit/Texture"
}
