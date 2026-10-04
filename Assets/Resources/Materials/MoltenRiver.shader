Shader "GemCatch/MoltenRiver"
{
    Properties
    {
        [MainTexture] _MainTex ("Cavern", 2D) = "black" {}
        _PulseStrength ("Lava glow strength", Range(0, 1)) = 0.35
        _EmberStrength ("Ember brightness", Range(0, 1)) = 0.7
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
            float4 _MainTex_TexelSize;
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _PulseStrength;
                float _EmberStrength;
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
                // Include the orange surface, not just its few white-hot highlights.
                // Red dominance excludes blue/purple smoke in both gamma and linear projects.
                half hot = smoothstep(0.18, 0.50, c.r);
                half orange = smoothstep(0.012, 0.09, c.g) * smoothstep(0.10, 0.30, c.r - c.b);
                half river = 1.0 - smoothstep(0.32, 0.42, uv.y);
                return hot * orange * river;
            }
            half3 Embers(float2 uv)
            {
                half3 light = 0;
                // Fixed seeds give each ember its own height, speed and sideways
                // drift. Fade at both ends so wrapping never creates a visible pop.
                [unroll]
                for (int i = 0; i < 16; i++)
                {
                    float seed = frac(sin((i + 1.0) * 127.1) * 43758.5453);
                    float seed2 = frac(sin((i + 1.0) * 311.7) * 22578.1459);
                    float phase = frac(seed + _Time.y * lerp(0.025, 0.055, seed2));
                    float2 center = float2(lerp(0.06, 0.94, seed2)
                        + 0.025 * sin(_Time.y * 0.55 + seed * 30.0),
                        lerp(0.04, 1.05, phase));
                    float2 delta = uv - center;
                    delta.x *= _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                    float radius = lerp(0.0012, 0.0022, seed);
                    float d2 = dot(delta, delta) / (radius * radius);
                    float core = exp2(-d2 * 1.5);
                    float halo = exp2(-d2 * 0.12) * 0.22;
                    float fade = smoothstep(0.0, 0.10, phase)
                        * (1.0 - smoothstep(0.78, 1.0, phase));
                    light += (half3(1.0, 0.50, 0.08) * core
                        + half3(1.0, 0.18, 0.015) * halo) * fade;
                }
                return light * _EmberStrength;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 still = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half mask = LavaMask(still.rgb, uv);
                // Six-second breathing glow only: no UV motion, flowing noise,
                // or displacement of the painted river.
                half pulse = 0.5 + 0.5 * sin(_Time.y * 1.04719755);
                half intensity = _PulseStrength * lerp(0.15, 1.0, pulse);
                half3 glow = (still.rgb * 0.45 + half3(0.35, 0.10, 0.005))
                    * mask * intensity;
                return half4(still.rgb + glow + Embers(uv), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
