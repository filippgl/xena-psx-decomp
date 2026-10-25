Shader "Xena/HitEffect"
{
    // Original hit effects (docs/xena-hit-effects.md): untextured gouraud polygons/lines in
    // screen space, drawn over the frame. HitAdditive = PS1 abr 1 (B + F), HitOpaque = blood lines.
    Properties
    {
        _SrcBlend ("Source blend", Float) = 1
        _DstBlend ("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Overlay" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return half4(input.color.rgb, 1); }
            ENDHLSL
        }
    }
    Fallback Off
}
