Shader "Xena/OriginalUnlit"
{
    Properties
    {
        _MainTex ("Original diffuse", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [Toggle] _UseVertexLighting ("Original vertex lighting", Float) = 0
        _LightBias ("Brightness", Range(-64,64)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "OriginalUnlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _Cull;
                float _UseVertexLighting;
                float _LightBias;
            CBUFFER_END
            // Original depth cueing (docs/xena-level-background.md, VERIFIED code): f = saturate(A - A*N/z),
            // z = view depth (m), A = F/(F-N); colour = lerp(colour, far, f) in encoded RGB. Set by PrototypeSky
            // (x enabled, y A, z A*N); per vertex like the level renderer.
            float4 _XenaOriginalFog; float4 _XenaOriginalFogColor;
            float XenaOriginalFogFactor(float3 positionOS)
            {
                if (_XenaOriginalFog.x < 0.5) return 0;
                float z = max(0.01, -TransformWorldToView(TransformObjectToWorld(positionOS)).z);
                return saturate(_XenaOriginalFog.y - _XenaOriginalFog.z / z);
            }
            half3 XenaOriginalFogApply(half3 color, float f)
            {
                if (f <= 0) return color;
                #if defined(UNITY_COLORSPACE_GAMMA)
                return lerp(color, _XenaOriginalFogColor.rgb, f);
                #else
                return SRGBToLinear(lerp(LinearToSRGB(color), _XenaOriginalFogColor.rgb, f));
                #endif
            }
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 lighting : TEXCOORD1; float fog : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.fog = XenaOriginalFogFactor(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.lighting = clamp(round(input.color.rgb * 255.0) + _LightBias, 0.0, 255.0) / 128.0;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                clip(color.a - _Cutoff);
                if (_UseVertexLighting > 0.5)
                {
                    // PS1 modulates encoded RGB. Unity's linear project has already
                    // decoded the sRGB texture; return to encoded RGB for this step.
                    #if defined(UNITY_COLORSPACE_GAMMA)
                    color.rgb = saturate(color.rgb * input.lighting);
                    #else
                    color.rgb = SRGBToLinear(saturate(LinearToSRGB(color.rgb) * input.lighting));
                    #endif
                }
                color.rgb = XenaOriginalFogApply(color.rgb, input.fog);
                return color;
            }
            ENDHLSL
        }
        // Remaster sun shadows (only a Remaster light casts them); alpha-tested
        // like the colour pass so leaves cast leaf-shaped shadows.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _Cull;
                float _UseVertexLighting;
                float _LightBias;
            CBUFFER_END
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct ShadowVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirection = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a * _Color.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
