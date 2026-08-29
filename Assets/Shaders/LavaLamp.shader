Shader "Custom/LavaLamp"
{
    Properties
    {
        _Speed ("Speed", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            float _Speed;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float blob(float2 uv, float2 center, float radius)
            {
                float d = length(uv - center);
                return smoothstep(radius, radius * 0.1, d);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 uv = i.uv;

                // Dark base
                float3 col = float3(0.02, 0.01, 0.04);

                // Drifting blob centers
                float2 c1 = float2(0.5 + 0.3 * sin(t * 0.7 + 1.0), 0.5 + 0.35 * cos(t * 0.5));
                float2 c2 = float2(0.5 + 0.35 * cos(t * 0.6 + 2.0), 0.5 + 0.3 * sin(t * 0.8 + 1.5));
                float2 c3 = float2(0.5 + 0.25 * sin(t * 0.9 + 3.0), 0.5 + 0.4 * cos(t * 0.4 + 0.5));
                float2 c4 = float2(0.5 + 0.4 * cos(t * 0.5 + 4.0), 0.5 + 0.25 * sin(t * 0.7 + 2.5));
                float2 c5 = float2(0.5 + 0.3 * sin(t * 0.35 + 5.0), 0.5 + 0.3 * cos(t * 0.55 + 3.5));

                // Pulsing radii
                float r1 = 0.28 + 0.08 * sin(t * 1.1);
                float r2 = 0.25 + 0.07 * cos(t * 0.9 + 1.0);
                float r3 = 0.22 + 0.06 * sin(t * 1.3 + 2.0);
                float r4 = 0.20 + 0.05 * cos(t * 1.0 + 3.0);
                float r5 = 0.26 + 0.07 * sin(t * 0.8 + 1.5);

                float b1 = blob(uv, c1, r1);
                float b2 = blob(uv, c2, r2);
                float b3 = blob(uv, c3, r3);
                float b4 = blob(uv, c4, r4);
                float b5 = blob(uv, c5, r5);

                // Colors that cycle over time
                float3 color1 = float3(0.5 + 0.5 * sin(t * 0.3), 0.2 + 0.3 * sin(t * 0.4 + 2.0), 0.8 + 0.2 * cos(t * 0.35 + 1.0));
                float3 color2 = float3(0.9 + 0.1 * sin(t * 0.25 + 1.0), 0.2 + 0.2 * cos(t * 0.3 + 3.0), 0.3 + 0.2 * sin(t * 0.4 + 2.0));
                float3 color3 = float3(0.1 + 0.2 * cos(t * 0.35 + 2.0), 0.7 + 0.3 * sin(t * 0.3 + 1.0), 0.4 + 0.3 * cos(t * 0.4));
                float3 color4 = float3(0.9 + 0.1 * sin(t * 0.2), 0.6 + 0.3 * cos(t * 0.35 + 1.5), 0.1 + 0.1 * sin(t * 0.3 + 3.0));
                float3 color5 = float3(0.6 + 0.3 * sin(t * 0.28 + 4.0), 0.1 + 0.2 * cos(t * 0.32 + 2.0), 0.8 + 0.2 * sin(t * 0.38 + 1.0));

                col += color1 * b1 * 0.7;
                col += color2 * b2 * 0.6;
                col += color3 * b3 * 0.5;
                col += color4 * b4 * 0.6;
                col += color5 * b5 * 0.5;

                // Subtle ambient glow
                float ambientWave = 0.03 + 0.02 * sin(t * 0.2 + uv.y * 3.0);
                col += float3(ambientWave * 0.5, ambientWave * 0.3, ambientWave * 0.8);

                return fixed4(saturate(col), 1.0);
            }
            ENDCG
        }
    }
}
