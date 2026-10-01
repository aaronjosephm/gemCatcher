Shader "GemCatch/CrystalTwinkle"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color; output.uv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 p = abs(input.uv * 2 - 1);
                float horizontal = pow(saturate(1 - p.x), 2) * pow(saturate(1 - p.y * 10), 2);
                float vertical = pow(saturate(1 - p.y), 2) * pow(saturate(1 - p.x * 10), 2);
                float halo = pow(saturate(1 - length(p) * 2), 3) * .3;
                return half4(input.color.rgb, input.color.a * saturate(horizontal + vertical + halo));
            }
            ENDHLSL
        }
    }
}
