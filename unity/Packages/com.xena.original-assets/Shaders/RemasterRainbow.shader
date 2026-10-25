Shader "Xena/RemasterRainbow"
{
    // Remaster presentation only: a faint, soft additive rainbow over the
    // original rndbow strip. _UvRect normalizes the strip (x along the arc,
    // y across). Bands are pastel, Gaussian across the width, fade out towards
    // the arc ends and shimmer gently as if lit through drifting spray.
    Properties
    {
        _UvRect ("UV rect (min xy, size zw)", Vector) = (0, 0, 1, 0.25)
        _Intensity ("Intensity", Float) = 0.16
        _RedOutside ("Red on normalized V=1", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _UvRect;
                float _Intensity, _RedOutside;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 band : TEXCOORD0; };
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.band = saturate((input.uv - _UvRect.xy) / max(_UvRect.zw, 1e-4));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float across = _RedOutside > 0.5 ? 1 - input.band.y : input.band.y;
                float hue = lerp(0.0, 0.76, saturate((across - 0.15) / 0.7));
                float3 spectrum = saturate(abs(frac(hue + float3(0, 2.0 / 3, 1.0 / 3)) * 6 - 3) - 1);
                spectrum = lerp(spectrum, 1, 0.22);
                float centre = (input.band.y - 0.5) / 0.3;
                float envelope = exp(-centre * centre * 1.2);
                float ends = pow(saturate(sin(input.band.x * PI)), 1.6);
                float spray = 0.75 + 0.25 * Noise(float2(input.band.x * 18 - _Time.y * 0.15, input.band.y * 3 + _Time.y * 0.1));
                return half4(spectrum * envelope * ends * spray * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
