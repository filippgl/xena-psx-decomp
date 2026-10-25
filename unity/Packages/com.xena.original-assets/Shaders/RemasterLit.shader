Shader "Xena/RemasterLit"
{
    // Remaster presentation only: the original texture and baked vertex
    // lighting, shaped by a soft sun term over smoothed subdivision normals.
    Properties
    {
        _MainTex ("Original diffuse", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [Toggle] _UseVertexLighting ("Original vertex lighting", Float) = 0
        _LightBias ("Brightness", Range(-64,64)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _SunDirection ("Sun direction", Vector) = (0.35, 0.75, 0.25, 0)
        _Shape ("Shape lighting strength", Range(0, 1)) = 0.75
        _RockDetail ("CC0 rock detail on steep grey surfaces", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "RemasterLit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.xena.original-assets/Shaders/XenaSkyFog.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            // CC0 ambientCG Rock051 (global, Remaster only): x tiles per metre,
            // y albedo strength, z normal strength, w AO strength.
            TEXTURE2D(_XenaRockAlbedo); SAMPLER(sampler_XenaRockAlbedo);
            TEXTURE2D(_XenaRockNormal); SAMPLER(sampler_XenaRockNormal);
            TEXTURE2D(_XenaRockAO); SAMPLER(sampler_XenaRockAO);
            float4 _XenaRockParams;
            float _XenaRockEnabled;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _Cutoff;
                float _Cull;
                float _UseVertexLighting;
                float _LightBias;
                float4 _SunDirection;
                float _Shape;
                float _RockDetail;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 seam0 : TEXCOORD1; float2 seam1 : TEXCOORD2; float2 seam2 : TEXCOORD3; float3 seamWeight : TEXCOORD4; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 lighting : TEXCOORD1; float3 normalWS : TEXCOORD2; float4 seamA : TEXCOORD3; float2 seamB : TEXCOORD4; float3 seamWeight : TEXCOORD5; float3 positionWS : TEXCOORD6; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.seamA = float4(TRANSFORM_TEX(input.seam0, _MainTex), TRANSFORM_TEX(input.seam1, _MainTex)); output.seamB = TRANSFORM_TEX(input.seam2, _MainTex); output.seamWeight = input.seamWeight;
                output.lighting = clamp(round(input.color.rgb * 255.0) + _LightBias, 0.0, 255.0) / 128.0;
                return output;
            }
            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 own = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                // Crossfade into neighbours' texture continuation across seams.
                float3 sw = saturate(input.seamWeight);
                half4 blended = own * (1 - min(1, sw.x + sw.y + sw.z))
                    + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.seamA.xy) * sw.x
                    + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.seamA.zw) * sw.y
                    + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.seamB) * sw.z;
                blended.a = own.a;
                half4 color = blended * _Color;
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
                float3 n = normalize(input.normalWS);
                // Two-sided faces: light the visible side.
                if (!front) n = -n;
                // Rock relief: steep, grey (non-grass) original texels only, so
                // the palette stays the original's and walkable ground is untouched.
                float grey = 1 - saturate((max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b))) * 4.0);
                float rock = _RockDetail * _XenaRockEnabled * smoothstep(0.8, 0.5, abs(n.y)) * grey;
                if (rock > 0.001)
                {
                    float3 p = input.positionWS * _XenaRockParams.x;
                    float3 w = pow(abs(n), 4); w /= (w.x + w.y + w.z);
                    float2 uvX = p.zy, uvY = p.xz, uvZ = p.xy;
                    float3 albedo = SAMPLE_TEXTURE2D(_XenaRockAlbedo, sampler_XenaRockAlbedo, uvX).rgb * w.x
                        + SAMPLE_TEXTURE2D(_XenaRockAlbedo, sampler_XenaRockAlbedo, uvY).rgb * w.y
                        + SAMPLE_TEXTURE2D(_XenaRockAlbedo, sampler_XenaRockAlbedo, uvZ).rgb * w.z;
                    float ao = SAMPLE_TEXTURE2D(_XenaRockAO, sampler_XenaRockAO, uvX).r * w.x
                        + SAMPLE_TEXTURE2D(_XenaRockAO, sampler_XenaRockAO, uvY).r * w.y
                        + SAMPLE_TEXTURE2D(_XenaRockAO, sampler_XenaRockAO, uvZ).r * w.z;
                    float3 tX = UnpackNormal(SAMPLE_TEXTURE2D(_XenaRockNormal, sampler_XenaRockNormal, uvX));
                    float3 tY = UnpackNormal(SAMPLE_TEXTURE2D(_XenaRockNormal, sampler_XenaRockNormal, uvY));
                    float3 tZ = UnpackNormal(SAMPLE_TEXTURE2D(_XenaRockNormal, sampler_XenaRockNormal, uvZ));
                    tX = float3(tX.xy + n.zy, abs(tX.z) * n.x); tY = float3(tY.xy + n.xz, abs(tY.z) * n.y); tZ = float3(tZ.xy + n.xy, abs(tZ.z) * n.z);
                    float3 detailNormal = normalize(tX.zyx * w.x + tY.xzy * w.y + tZ.xyz * w.z);
                    float luminance = dot(albedo, float3(0.3, 0.59, 0.11));
                    // Detail relative to mid-grey: keeps the original hue/brightness,
                    // carries the rock's full pattern and a hint of its moss tint.
                    float3 relief = lerp(luminance.xxx, albedo, 0.35) / 0.55;
                    color.rgb *= lerp(1, relief * lerp(1, ao, _XenaRockParams.w), _XenaRockParams.y * rock);
                    n = normalize(lerp(n, detailNormal, _XenaRockParams.z * rock));
                }
                float wrap = saturate((dot(n, normalize(_SunDirection.xyz)) + 0.4) / 1.4);
                float sky = 0.5 + 0.5 * n.y;
                float shape = 0.7 + 0.34 * wrap + 0.1 * sky;
                shape = lerp(shape, 0.5 + 0.62 * wrap + 0.08 * sky, saturate(rock * 1.5));
                color.rgb *= lerp(1, shape, _Shape);
                // Sun shadows on top of the baked original lighting.
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                // Only sun-facing surfaces darken: back faces are already dim from shape light.
                float shade = lerp(1.0, 0.72, saturate(dot(n, sun.direction) * 2.0));
                color.rgb *= lerp(shade, 1.0, sun.shadowAttenuation);
                color.rgb = XenaSkyFog(color.rgb, input.positionWS);
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
                float4 _SunDirection;
                float _Shape;
                float _RockDetail;
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
