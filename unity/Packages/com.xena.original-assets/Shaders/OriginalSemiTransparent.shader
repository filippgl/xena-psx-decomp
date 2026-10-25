Shader "Xena/OriginalSemiTransparent"
{
    Properties
    {
        _MainTex ("Original diffuse", 2D) = "white" {}
        _StpMask ("Original per-texel STP bit", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Float) = 0.5
        _UseVertexLighting ("Original vertex lighting", Float) = 1
        _LightBias ("Brightness", Float) = 0
        _Cull ("Cull", Float) = 2
        _SrcBlend ("Source blend", Float) = 1
        _DstBlend ("Destination blend", Float) = 5
        _Ps1BlendMode ("Original blend mode", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_StpMask); SAMPLER(sampler_StpMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Color;
                float _Cutoff, _UseVertexLighting, _LightBias, _Cull, _SrcBlend, _DstBlend, _Ps1BlendMode;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 lighting : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.lighting = clamp(round(input.color.rgb * 255) + _LightBias, 0, 255) / 128;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                clip(color.a - _Cutoff);
                bool stp = SAMPLE_TEXTURE2D(_StpMask, sampler_StpMask, input.uv).r > 0.5;
                if (_UseVertexLighting > 0.5)
                {
                    #if defined(UNITY_COLORSPACE_GAMMA)
                    color.rgb = saturate(color.rgb * input.lighting);
                    #else
                    color.rgb = SRGBToLinear(saturate(LinearToSRGB(color.rgb) * input.lighting));
                    #endif
                }
                // Mode0: half foreground/background; mode1: add; mode3: add1/4.
                // With One/SrcAlpha, alpha0 replaces opaque texels and alpha1
                // preserves background for STP texels in the SAME draw call.
                color.a = _Ps1BlendMode < 0.5 ? (stp ? 0.5 : 1) : (stp ? 1 : 0);
                if (_Ps1BlendMode > 2.5 && stp) color.rgb *= 0.25;
                return color;
            }
            ENDHLSL
        }
    }
}
