Shader "Xena/RemasterShell"
{
    // Closed Remaster shell (tools/build_remaster_shell.py). World-space layers instead of the PS1 atlas, so no
    // texture seam can follow a polygon: rock and moss triplanar, ground and path from above, height-blended.
    // Their pattern is relative to each texture's own mean, on the vertex colour that carries the blurred
    // original paint times the baked light (encoded RGB), so hue and brightness stay the original's.
    Properties
    {
        _RockColor ("Rock", 2D) = "gray" {}
        _RockNormal ("Rock normal", 2D) = "bump" {}
        _MossColor ("Moss", 2D) = "gray" {}
        _MossNormal ("Moss normal", 2D) = "bump" {}
        _GroundColor ("Ground", 2D) = "gray" {}
        _GroundNormal ("Ground normal", 2D) = "bump" {}
        _PathColor ("Path", 2D) = "gray" {}
        _PathNormal ("Path normal", 2D) = "bump" {}
        _Tile ("Metres per tile: rock, moss, ground, path", Vector) = (2.8, 2.2, 2.4, 2.2)
        _Pattern ("Pattern strength: rock, moss, ground, path", Vector) = (1.35, 1, 0.8, 0.8)
        _NormalStrength ("Normal strength", Float) = 1
        _MossTint ("Moss colour (encoded), a = its luminance", Vector) = (0.1, 0.2, 0.05, 0.15)
        _SunDirection ("Sun direction", Vector) = (0.35, 0.75, 0.25, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "RemasterShell"
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Not culled: a lens that still ends up inside the rock sees dark stone, never a hole through the world.
            Cull Off
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
            TEXTURE2D(_RockColor); TEXTURE2D(_RockNormal); TEXTURE2D(_MossColor); TEXTURE2D(_MossNormal);
            TEXTURE2D(_GroundColor); TEXTURE2D(_GroundNormal); TEXTURE2D(_PathColor); TEXTURE2D(_PathNormal);
            SAMPLER(sampler_RockColor);
            CBUFFER_START(UnityPerMaterial)
                float4 _Tile;
                float4 _Pattern;
                float _NormalStrength;
                float4 _MossTint;
                float4 _SunDirection;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float4 layers : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : TEXCOORD2; float4 layers : TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color; output.layers = input.layers;
                return output;
            }
            // Triplanar colour and whiteout-blended normal of one surface.
            void Triplanar(Texture2D color, Texture2D normal, float3 p, float3 n, float3 w, out float3 albedo, out float3 detail)
            {
                albedo = color.Sample(sampler_RockColor, p.zy).rgb * w.x + color.Sample(sampler_RockColor, p.xz).rgb * w.y + color.Sample(sampler_RockColor, p.xy).rgb * w.z;
                float3 tx = UnpackNormal(normal.Sample(sampler_RockColor, p.zy)), ty = UnpackNormal(normal.Sample(sampler_RockColor, p.xz)), tz = UnpackNormal(normal.Sample(sampler_RockColor, p.xy));
                tx = float3(tx.xy + n.zy, abs(tx.z) * n.x); ty = float3(ty.xy + n.xz, abs(ty.z) * n.y); tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
                detail = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
            }
            float Mean(Texture2D color) { return max(dot(color.SampleLevel(sampler_RockColor, float2(0.5, 0.5), 11).rgb, float3(0.3, 0.59, 0.11)), 0.02); }
            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                if (!front) return half4(0.006, 0.006, 0.005, 1);
                float3 n = normalize(input.normalWS), P = input.positionWS;
                float3 w = pow(abs(n), 5); w /= w.x + w.y + w.z;
                float3 albedoR, normalR, albedoM, normalM, albedoG, normalG, albedoT, normalT;
                float4 layers = input.layers;
                Triplanar(_RockColor, _RockNormal, P / _Tile.x, n, w, albedoR, normalR);
                // A large second scale breaks the repetition of the rock.
                float3 macro = _RockColor.Sample(sampler_RockColor, P.zy / 13.7).rgb * w.x + _RockColor.Sample(sampler_RockColor, P.xz / 13.7).rgb * w.y + _RockColor.Sample(sampler_RockColor, P.xy / 13.7).rgb * w.z;
                albedoR = lerp(albedoR, albedoR * macro / Mean(_RockColor), 0.6);
                Triplanar(_MossColor, _MossNormal, P / _Tile.y, n, w, albedoM, normalM);
                albedoG = _GroundColor.Sample(sampler_RockColor, P.xz / _Tile.z).rgb; normalG = UnpackNormal(_GroundNormal.Sample(sampler_RockColor, P.xz / _Tile.z)); normalG = normalize(float3(normalG.xy + n.xz, abs(normalG.z) * n.y)).xzy;
                albedoT = _PathColor.Sample(sampler_RockColor, P.xz / _Tile.w).rgb; normalT = UnpackNormal(_PathNormal.Sample(sampler_RockColor, P.xz / _Tile.w)); normalT = normalize(float3(normalT.xy + n.xz, abs(normalT.z) * n.y)).xzy;
                float meanR = Mean(_RockColor), meanM = Mean(_MossColor), meanG = Mean(_GroundColor), meanT = Mean(_PathColor);
                float lumR = dot(albedoR, float3(0.3, 0.59, 0.11)) / meanR, lumM = dot(albedoM, float3(0.3, 0.59, 0.11)) / meanM, lumG = dot(albedoG, float3(0.3, 0.59, 0.11)) / meanG, lumT = dot(albedoT, float3(0.3, 0.59, 0.11)) / meanT;
                // Height blend: the brighter, higher parts of a surface poke through its neighbour.
                float4 h = layers * float4(lumR, lumM, lumG, lumT) + layers; h = pow(h, 6) + 1e-5; h /= h.x + h.y + h.z + h.w;
                float3 relR = lerp(1, lerp(lumR.xxx, albedoR / meanR, 0.1), _Pattern.x), relM = lerp(1, lerp(lumM.xxx, albedoM / meanM, 0.3), _Pattern.y);
                float3 relG = lerp(1, lerp(lumG.xxx, albedoG / meanG, 0.3), _Pattern.z), relT = lerp(1, lerp(lumT.xxx, albedoT / meanT, 0.3), _Pattern.w);
                float3 detail = normalize(normalR * h.x + normalM * h.y + normalG * h.z + normalT * h.w);
                n = normalize(lerp(n, detail, _NormalStrength));
                float3 guide = SRGBToLinear(input.color.rgb);
                // Moss is the palette's green at the local baked light, not whatever paint lay under it.
                float encodedLum = dot(input.color.rgb, float3(0.3, 0.59, 0.11));
                float3 mossBase = lerp(guide, SRGBToLinear(saturate(_MossTint.rgb * min(encodedLum / max(_MossTint.a, 0.02), 1.05))), 0.7);
                float3 color = (h.x * guide * relR + h.y * mossBase * relM + h.z * guide * relG + h.w * guide * relT) * lerp(0.55, 1.0, input.color.a);
                float wrap = saturate((dot(n, normalize(_SunDirection.xyz)) + 0.4) / 1.4), sky = 0.5 + 0.5 * n.y;
                color *= lerp(1, 0.5 + 0.62 * wrap + 0.08 * sky, 0.75);
                Light sun = GetMainLight(TransformWorldToShadowCoord(P));
                color *= lerp(lerp(1.0, 0.72, saturate(dot(n, sun.direction) * 2.0)), 1.0, sun.shadowAttenuation);
                return half4(XenaSkyFog(color, P), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Tile;
                float4 _Pattern;
                float _NormalStrength;
                float4 _MossTint;
                float4 _SunDirection;
            CBUFFER_END
            float3 _LightDirection;
            float3 _LightPosition;
            float4 ShadowVert(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
