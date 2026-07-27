// URP-compatible port of the original FX/Gem shader.
// Two-pass transparent gem: back-face refraction + front-face additive reflection.
// Uses the per-gem cubemap (_RefractTex) for both refraction and environment
// lighting, avoiding fragile reflection-probe sampling across pipeline versions.
Shader "FX/Gem"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _ReflectionStrength ("Reflection Strength", Range(0.0,2.0)) = 1.0
        _EnvironmentLight ("Environment Light", Range(0.0,2.0)) = 1.0
        _Emission ("Emission", Range(0.0,2.0)) = 0.0
        [NoScaleOffset] _RefractTex ("Refraction Texture", Cube) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half  _ReflectionStrength;
            half  _EnvironmentLight;
            half  _Emission;
        CBUFFER_END

        TEXTURECUBE(_RefractTex);
        SAMPLER(sampler_RefractTex);

        struct Attributes
        {
            float4 posOS    : POSITION;
            float3 normalOS : NORMAL;
        };
        ENDHLSL

        // ── Pass 0: Back-faces (inside of gem) ─────────────────────────
        Pass
        {
            Name "GemBack"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 posCS  : SV_POSITION;
                float3 cubeUV : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posCS = TransformObjectToHClip(v.posOS.xyz);

                float3 posWS   = TransformObjectToWorld(v.posOS.xyz);
                float3 viewDir = normalize(GetCameraPositionWS() - posWS);
                float3 normWS  = TransformObjectToWorldNormal(v.normalOS);
                o.cubeUV = reflect(-viewDir, -normWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 cubeSample = SAMPLE_TEXTURECUBE(_RefractTex, sampler_RefractTex, i.cubeUV).rgb;
                half3 refraction = cubeSample * _Color.rgb;
                half3 multiplier = cubeSample * _EnvironmentLight + _Emission;
                return half4(refraction * multiplier, 1.0);
            }
            ENDHLSL
        }

        // ── Pass 1: Front-faces (surface reflection, additive) ─────────
        Pass
        {
            Name "GemFront"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 posCS   : SV_POSITION;
                float3 cubeUV  : TEXCOORD0;
                half   fresnel : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posCS = TransformObjectToHClip(v.posOS.xyz);

                float3 posWS   = TransformObjectToWorld(v.posOS.xyz);
                float3 viewDir = normalize(GetCameraPositionWS() - posWS);
                float3 normWS  = TransformObjectToWorldNormal(v.normalOS);
                o.cubeUV  = reflect(-viewDir, normWS);
                o.fresnel = 1.0 - saturate(dot(normWS, viewDir));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 cubeSample = SAMPLE_TEXTURECUBE(_RefractTex, sampler_RefractTex, i.cubeUV).rgb;
                half3 refraction = cubeSample * _Color.rgb;
                half3 reflection = cubeSample * _ReflectionStrength * i.fresnel * _Color.rgb;
                half3 multiplier = cubeSample * _EnvironmentLight + _Emission;
                return half4(reflection + refraction * multiplier, 1.0);
            }
            ENDHLSL
        }

        // ── Depth-only pass (needed for transparent sorting) ───────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag

            struct DepthVaryings { float4 posCS : SV_POSITION; };

            DepthVaryings depthVert(Attributes v)
            {
                DepthVaryings o;
                o.posCS = TransformObjectToHClip(v.posOS.xyz);
                return o;
            }

            half4 depthFrag(DepthVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    // Built-in RP fallback (used only when URP is not the active pipeline).
    SubShader
    {
        Tags { "Queue" = "Transparent" }

        Pass
        {
            Cull Front
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; };

            v2f vert(float4 v : POSITION, float3 n : NORMAL)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                float3 viewDir = normalize(ObjSpaceViewDir(v));
                o.uv = -reflect(viewDir, n);
                o.uv = mul(unity_ObjectToWorld, float4(o.uv, 0));
                return o;
            }

            fixed4 _Color;
            samplerCUBE _RefractTex;
            half _EnvironmentLight;
            half _Emission;

            half4 frag(v2f i) : SV_Target
            {
                half3 refraction = texCUBE(_RefractTex, i.uv).rgb * _Color.rgb;
                half4 reflection = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, i.uv);
                reflection.rgb = DecodeHDR(reflection, unity_SpecCube0_HDR);
                half3 multiplier = reflection.rgb * _EnvironmentLight + _Emission;
                return half4(refraction.rgb * multiplier.rgb, 1.0f);
            }
            ENDCG
        }

        Pass
        {
            ZWrite On
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; half fresnel : TEXCOORD1; };

            v2f vert(float4 v : POSITION, float3 n : NORMAL)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                float3 viewDir = normalize(ObjSpaceViewDir(v));
                o.uv = -reflect(viewDir, n);
                o.uv = mul(unity_ObjectToWorld, float4(o.uv, 0));
                o.fresnel = 1.0 - saturate(dot(n, viewDir));
                return o;
            }

            fixed4 _Color;
            samplerCUBE _RefractTex;
            half _ReflectionStrength;
            half _EnvironmentLight;
            half _Emission;

            half4 frag(v2f i) : SV_Target
            {
                half3 refraction = texCUBE(_RefractTex, i.uv).rgb * _Color.rgb;
                half4 reflection = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, i.uv);
                reflection.rgb = DecodeHDR(reflection, unity_SpecCube0_HDR);
                half3 reflection2 = reflection * _ReflectionStrength * i.fresnel;
                half3 multiplier = reflection.rgb * _EnvironmentLight + _Emission;
                return fixed4(reflection2 + refraction.rgb * multiplier, 1.0f);
            }
            ENDCG
        }

        UsePass "VertexLit/SHADOWCASTER"
    }
}
