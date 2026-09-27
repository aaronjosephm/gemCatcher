Shader "GemCatch/MoltenRiver"
{
    Properties
    {
        [MainTexture] _MainTex ("Cavern", 2D) = "black" {}
        _FlowSpeed ("Downstream speed", Range(0, 0.05)) = 0.012
        _FlowStrength ("Surface contrast", Range(0, 1)) = 0.65
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
                float _FlowSpeed;
                float _FlowStrength;
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
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            float FlowNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + float2(1, 1)), f.x), f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 still = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half mask = LavaMask(still.rgb, uv);
                // Sample the artwork only at its original UV. Moving the painted
                // river and crossfading reset phases made it stretch and breathe.
                // Bend a fixed coordinate field along the river, then translate
                // surface detail along its length at a constant rate. Never multiply
                // a UV-dependent direction by time: that accumulates shear.
                float across = uv.x + 0.020 * cos(uv.y * 30.0 + 0.8);
                float downstream = uv.y + _Time.y * _FlowSpeed;
                float2 surfaceUV = float2(across * 65.0, downstream * 24.0);
                float crust = FlowNoise(surfaceUV);
                float detail = FlowNoise(surfaceUV * 2.0 + float2(13.7, 4.2));
                float surface = crust * 0.75 + detail * 0.25;
                // Dark rafts and hot seams drift together without a timed pulse,
                // texture reset, or movement of the silhouette and river banks.
                float rafts = smoothstep(0.48, 0.72, surface);
                float seams = smoothstep(0.30, 0.40, surface)
                    * (1.0 - smoothstep(0.43, 0.51, surface));
                half3 flowing = still.rgb * (1.0 - rafts * 0.65 * _FlowStrength);
                flowing += half3(0.28, 0.12, 0.012) * seams * _FlowStrength;
                return half4(lerp(still.rgb, flowing, mask), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
