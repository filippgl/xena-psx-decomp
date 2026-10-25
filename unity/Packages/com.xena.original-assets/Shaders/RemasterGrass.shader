Shader "Xena/RemasterGrass"
{
    // Remaster grass tufts (PrototypeRemasterGrass): baked chunk meshes of crossed quads, uv.y = 0 at
    // the blade base and 1 at the tip. Vertex colour = the PS1 baked ground light under the tuft
    // (same encoding as level meshes: 0.5 = unlit) in rgb, blade height 0..1 in alpha. Wind bends
    // tips; tufts dissolve past _FadeStart.._FadeEnd metres (alpha-clip), then far fog applies.
    Properties
    {
        _MainTex ("Grass atlas", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.45
        _Wind ("Wind (m at the tip)", Float) = 0.08
        _FadeStart ("Fade start (m)", Float) = 22
        _FadeEnd ("Fade end (m)", Float) = 32
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "XenaSkyFog.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Color; float _Cutoff, _Wind, _FadeStart, _FadeEnd;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 light : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float tip = input.color.a; // 0 at the blade base, 1 at the tip
                float t = _Time.y * 1.7 + p.x * 0.45 + p.z * 0.33;
                p.xz += float2(sin(t) + 0.5 * sin(t * 2.3 + p.z), cos(t * 0.9) * 0.7) * _Wind * tip * tip;
                o.positionWS = p; o.positionCS = TransformWorldToHClip(p);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.light = clamp(round(input.color.rgb * 255.0), 0, 255) / 128.0;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                float d = distance(GetCameraPositionWS(), input.positionWS);
                clip(c.a - lerp(_Cutoff, 1.01, smoothstep(_FadeStart, _FadeEnd, d)));
                #if defined(UNITY_COLORSPACE_GAMMA)
                c.rgb = saturate(c.rgb * input.light);
                #else
                c.rgb = SRGBToLinear(saturate(LinearToSRGB(c.rgb) * input.light));
                #endif
                return half4(XenaSkyFog(c.rgb, input.positionWS), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
