Shader "Xena/RemasterWater"
{
    // Remaster presentation only: procedural wave normals, Fresnel sky
    // reflection and sun glint over the original stream texture.
    Properties
    {
        _MainTex ("Original stream texture", 2D) = "white" {}
        _DeepColor ("Deep", Color) = (0.01, 0.06, 0.12, 1)
        _ShallowColor ("Shallow", Color) = (0.04, 0.22, 0.33, 1)
        _SkyColor ("Sky", Color) = (0.42, 0.58, 0.72, 1)
        _HorizonColor ("Horizon", Color) = (0.72, 0.8, 0.82, 1)
        _SunDirection ("Sun direction", Vector) = (0.35, 0.75, 0.25, 0)
        _SunColor ("Sun", Color) = (1, 0.93, 0.8, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.93
        _Detail ("Original texture detail", Range(0, 1)) = 0.55
        _FlowDirection ("Flow direction XZ", Vector) = (0.3, 1, 0, 0)
        _FlowSpeed ("Flow speed", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _DeepColor, _ShallowColor, _SkyColor, _HorizonColor, _SunDirection, _SunColor, _FlowDirection;
                float _Opacity, _Detail, _FlowSpeed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float fog : TEXCOORD2; };

            static const float2 WaveDirections[4] = { float2(1, 0.2), float2(-0.4, 1), float2(0.7, -0.7), float2(-1, -0.3) };
            static const float WaveFrequency[4] = { 1.7, 2.9, 4.3, 6.1 };
            static const float WaveAmplitude[4] = { 0.022, 0.016, 0.012, 0.009 };
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            // Value-noise gradient (analytic derivative) for small ripples.
            float2 NoiseGradient(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f), du = 6 * f * (1 - f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return du * float2(lerp(b - a, d - c, u.y), lerp(c - a, d - b, u.x));
            }
            float2 WaveGradient(float2 p, float t)
            {
                float2 flow = normalize(_FlowDirection.xy) * _FlowSpeed * t;
                float2 g = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float2 d = normalize(WaveDirections[k]);
                    g += d * cos(dot(d, p) * WaveFrequency[k] + t * (1.1 + 0.35 * k)) * WaveFrequency[k] * WaveAmplitude[k];
                }
                g += NoiseGradient(p * 2.3 - flow * 2.3) * 0.05;
                g += NoiseGradient(p * 5.7 + flow.yx * 4.0) * 0.03;
                g += NoiseGradient(p * 13.1 - flow * 7.0) * 0.012;
                return g;
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += 0.015 * sin(positionWS.x * 1.3 + _Time.y * 1.4) * cos(positionWS.z * 1.1 + _Time.y * 1.1);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 grad = WaveGradient(input.positionWS.xz, _Time.y);
                float3 n = normalize(float3(-grad.x, 1, -grad.y));
                float3 v = normalize(_WorldSpaceCameraPos - input.positionWS);
                float facing = saturate(dot(n, v));
                float fresnel = 0.02 + 0.98 * pow(1 - facing, 5);
                float3 r = reflect(-v, n);
                float3 sky = lerp(_HorizonColor.rgb, _SkyColor.rgb, saturate(r.y * 1.4));
                // Remaster: reflect the actual HDRI panorama.
                if (_XenaSkyFogEnabled > 0.5) sky = XenaSkyColor(r) * 0.55;
                float3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, facing);
                half3 original = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + grad * 0.02).rgb;
                body = lerp(body, body * 0.6 + original * 0.55, _Detail);
                float3 sun = normalize(_SunDirection.xyz);
                float glint = pow(saturate(dot(r, sun)), 220) * 4 + pow(saturate(dot(r, sun)), 24) * 0.12;
                float3 color = lerp(body, sky, fresnel) + glint * _SunColor.rgb;
                float alpha = saturate(lerp(_Opacity, 1, fresnel) + glint);
                return half4(XenaSkyFog(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
