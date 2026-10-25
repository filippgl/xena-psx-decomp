Shader "Xena/RemasterFoam"
{
    // Remaster presentation only: churning whitewater where a curtain meets
    // the pool. uv.x runs along the plunge line (metres), uv.y across it
    // (-1 behind .. +1 in front); foam boils at the line, expanding rings and
    // broken patches drift outward.
    Properties
    {
        _Color ("Foam", Color) = (0.93, 0.97, 1, 1)
        _Speed ("Speed", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.xena.original-assets/Shaders/XenaSkyFog.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Speed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p)
            {
                float sum = 0, amplitude = 0.5;
                [unroll] for (int i = 0; i < 5; i++) { sum += Noise(p) * amplitude; p = p * 2.07 + 11.3; amplitude *= 0.5; }
                return sum;
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += sin(input.uv.x * 3.1 + _Time.y * 4.0) * 0.015 * (1 - abs(input.uv.y));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _Speed;
                float along = input.uv.x, across = input.uv.y, d = abs(across);
                // Outward drift: patterns move away from the plunge line.
                float drift = d * 2.2 - t * 0.55;
                float boil = Fbm(float2(along * 2.3, drift * 2.0 + t * 0.4));
                float patches = smoothstep(0.42, 0.7, Fbm(float2(along * 1.4 + 3.0, drift)));
                float rings = pow(0.5 + 0.5 * sin(d * 22.0 - t * 5.0 + Fbm(float2(along * 0.8, t * 0.2)) * 6.0), 6) * smoothstep(1.0, 0.25, d);
                float core = smoothstep(0.75, 0.0, d) * (0.35 + 0.5 * boil) * (0.6 + 0.4 * patches);
                float foam = saturate(core + patches * smoothstep(1.0, 0.2, d) * 0.7 + rings * 0.35);
                float alpha = foam * smoothstep(1.0, 0.75, d);
                return half4(XenaSkyFog(_Color.rgb * (0.78 + 0.22 * boil), input.positionWS), saturate(alpha * 0.85));
            }
            ENDHLSL
        }
    }
}
