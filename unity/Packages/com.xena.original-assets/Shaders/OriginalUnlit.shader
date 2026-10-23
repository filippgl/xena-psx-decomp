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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 lighting : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
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
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
