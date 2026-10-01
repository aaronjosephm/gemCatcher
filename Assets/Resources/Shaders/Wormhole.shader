Shader "GemCatch/Wormhole"
{
    Properties { _AnimationTime ("Animation time", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Keep the later skybox pass from overwriting the backdrop.
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _AnimationTime;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Silver(float phase)
            {
                return 0.65 + 0.25*cos(6.2831853*phase);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // Screen coordinates keep the tunnel round on every aspect and avoid mesh UV flips.
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 p = uv - float2(0.5,0.43);
                p.x *= _ScreenParams.x / _ScreenParams.y;
                float r = max(length(p),0.001);
                float angle = atan2(p.y,p.x);
                float t = _AnimationTime;
                float depth = log(r + 0.025);
                float twist = angle + depth*2.7 - t*0.34;
                // Integer angular frequencies meet seamlessly at the atan2 boundary.
                float ribbons = pow(saturate(0.5+0.5*sin(twist*5.0+sin(depth*5.0+t*0.5))),7.0);
                float fineRibbons = pow(saturate(0.5+0.5*sin(twist*9.0-depth*4.0+t*0.2)),18.0);
                float ringPhase = depth*24.0+t*2.4+sin(angle*3.0-t*0.25)*0.7;
                float rings = pow(saturate(0.5+0.5*sin(ringPhase)),14.0);
                float brightness = 0.0;
                float shade = Silver(angle/6.2831853+depth*0.22-t*0.035);
                brightness += shade*(ribbons*0.65+fineRibbons*0.25+rings*0.35);
                // A softly luminous accretion ring outlines the nearly black core.
                float rim = exp(-abs(r-0.062)*125.0);
                brightness += Silver(angle/6.2831853+t*0.04)*rim*0.65;
                brightness *= smoothstep(0.035,0.075,r);
                // Sparse background stars drifting slowly, without particle GameObjects.
                float2 starUV = p*95.0+float2(t*0.22,t*0.13);
                float2 cell = floor(starUV);
                float seed = Hash(cell);
                float2 offset = float2(Hash(cell+19.0),Hash(cell+43.0));
                float star = 1.0-smoothstep(0.025,0.10,length(frac(starUV)-offset));
                star *= step(0.987,seed)*(0.55+0.25*sin(t*1.5+seed*100.0));
                brightness += 0.75*star*smoothstep(0.10,0.22,r);
                // Keep foreground gems readable: no bloom dependency or full-screen flashes.
                brightness *= 0.85-0.25*smoothstep(0.25,0.85,length(p));
                return half4(brightness,brightness,brightness,1.0);
            }
            ENDHLSL
        }
    }
}
