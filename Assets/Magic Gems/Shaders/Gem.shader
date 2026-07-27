// URP-compatible port of the original FX/Gem shader.
// Two-pass transparent gem: back-face refraction + front-face additive reflection.
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

        // Shared HLSL included by both passes via HLSLINCLUDE so the
        // CBUFFER is identical (SRP batcher requirement).
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half  _ReflectionStrength;
            half  _EnvironmentLight;
            half  _Emission;
        CBUFFER_END

        TEXTURECUBE(_RefractTex);   SAMPLER(sampler_RefractTex);

        // Sample the default reflection probe. URP populates unity_SpecCube0
        // automatically for every rendered object.
        half3 SampleReflectionProbe(float3 dir)
        {
            half4 raw = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, dir, 0);
            return DecodeHDREnvironment(raw, unity_SpecCube0_HDR);
        }
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

            struct Attributes
            {
                float4 posOS    : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 posCS  : SV_POSITION;
                float3 cubeUV : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posCS = TransformObjectToHClip(v.posOS.xyz);

                float3 camWS  = GetCameraPositionWS();
                float3 viewOS = normalize(TransformWorldToObject(camWS) - v.posOS.xyz);
                float3 reflOS = -reflect(viewOS, v.normalOS);
                o.cubeUV = TransformObjectToWorldDir(reflOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 refraction = SAMPLE_TEXTURECUBE(_RefractTex, sampler_RefractTex, i.cubeUV).rgb * _Color.rgb;
                half3 probe      = SampleReflectionProbe(i.cubeUV);
                half3 multiplier = probe * _EnvironmentLight + _Emission;
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

            struct Attributes
            {
                float4 posOS    : POSITION;
                float3 normalOS : NORMAL;
            };

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

                float3 camWS  = GetCameraPositionWS();
                float3 viewOS = normalize(TransformWorldToObject(camWS) - v.posOS.xyz);
                float3 reflOS = -reflect(viewOS, v.normalOS);
                o.cubeUV  = TransformObjectToWorldDir(reflOS);
                o.fresnel = 1.0 - saturate(dot(v.normalOS, viewOS));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 refraction = SAMPLE_TEXTURECUBE(_RefractTex, sampler_RefractTex, i.cubeUV).rgb * _Color.rgb;
                half3 probe      = SampleReflectionProbe(i.cubeUV);
                half3 reflection = probe * _ReflectionStrength * i.fresnel;
                half3 multiplier = probe * _EnvironmentLight + _Emission;
                return half4(reflection + refraction * multiplier, 1.0);
            }
            ENDHLSL
        }

        // ── Shadow caster ──────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 posOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings  { float4 posCS : SV_POSITION; };

            Varyings ShadowVert(Attributes v)
            {
                Varyings o;
                float3 posWS    = TransformObjectToWorld(v.posOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                posWS   = ApplyShadowBias(posWS, normalWS, lightDir);
                o.posCS = TransformWorldToHClip(posWS);

            #if UNITY_REVERSED_Z
                o.posCS.z = min(o.posCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                o.posCS.z = max(o.posCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    // Built-in RP fallback (used when URP is not the active pipeline).
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
