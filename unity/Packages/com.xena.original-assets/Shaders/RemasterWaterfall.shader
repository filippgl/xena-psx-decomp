Shader "Xena/RemasterWaterfall"
{
    // Remaster presentation only. Falling faces: accelerating, domain-warped
    // water sheet with aerated streaks, glassy lip, churning foam at the base,
    // sky Fresnel and sun glints. Near-horizontal faces of the same surface
    // (the river running over the edge) render as flowing water instead.
    // _UvRect normalizes the source texture-page sub-rect; _FlowSign is +1
    // when normalized V grows downward in the world.
    Properties
    {
        _MainTex ("Original waterfall texture", 2D) = "white" {}
        _UvRect ("UV rect (min xy, size zw)", Vector) = (0, 0, 1, 1)
        _FlowSign ("Flow sign", Float) = 1
        _DeepColor ("Body", Color) = (0.03, 0.14, 0.16, 1)
        _AeratedColor ("Aerated water", Color) = (0.95, 0.98, 1, 1)
        _SkyColor ("Sky", Color) = (0.55, 0.7, 0.85, 1)
        _SunDirection ("Sun direction", Vector) = (0.35, 0.75, 0.25, 0)
        _Speed ("Fall speed", Float) = 1.0
        _Opacity ("Body opacity", Range(0, 1)) = 0.5
        _HideFalling ("Hide falling faces (replaced by curtains)", Float) = 0
        _Across ("Across scale (m)", Float) = 6
        _BottomFade ("Dissolve into plunge foam", Float) = 0
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
                float4 _MainTex_ST, _UvRect, _DeepColor, _AeratedColor, _SkyColor, _SunDirection;
                float _FlowSign, _Speed, _Opacity, _HideFalling, _Across, _BottomFade;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 flow : TEXCOORD1; float3 positionWS : TEXCOORD2; float3 normalWS : TEXCOORD3; float fog : TEXCOORD4; };

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p)
            {
                float sum = 0, amplitude = 0.5;
                [unroll] for (int i = 0; i < 4; i++) { sum += Noise(p) * amplitude; p = p * 2.03 + 17.1; amplitude *= 0.5; }
                return sum;
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                float2 local = (input.uv - _UvRect.xy) / max(_UvRect.zw, 1e-4);
                output.flow = float2(local.x, _FlowSign > 0 ? local.y : 1 - local.y);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Curtains: travelling bulges so the sheet never looks rigid.
                float t = _Time.y * _Speed, x = output.flow.x * _Across, y = output.flow.y;
                float bulge = sin(x * 1.3 + y * 3.0 - t * 2.2) * 0.06 + sin(x * 3.7 - y * 7.0 - t * 3.4) * 0.03 + sin(x * 0.45 + t * 0.7) * 0.05;
                positionWS += normalize(output.normalWS) * bulge * (0.25 + y) * _BottomFade;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                float t = _Time.y * _Speed;
                float3 n = normalize(input.normalWS); if (!front) n = -n;
                float3 v = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 sun = normalize(_SunDirection.xyz);
                float falling = smoothstep(0.8, 0.55, abs(n.y));
                float2 f = input.flow;
                // Width coordinate in world units keeps streak size constant.
                float across = f.x * _Across;
                // 2: free-standing fall object (nothing to show behind it) -> drop faces.
                clip(_HideFalling > 1.5 && abs(n.y) < 0.75 ? -1 : 1);
                if (_HideFalling > 0.5 && abs(n.y) < 0.75)
                {
                    // Wet cliff behind the curtain (the original falling faces):
                    // dark streaked rock, a glossy running film and sun sheen.
                    float3 p = input.positionWS;
                    float stone = Fbm(p.xz * 1.7 + p.y * 0.9) * 0.6 + Fbm(float2(p.x + p.z, p.y) * 4.3) * 0.4;
                    float film = smoothstep(0.45, 0.8, Fbm(float2((p.x + p.z) * 5.0, p.y * 0.8 + t * 1.6)));
                    float3 rock = lerp(float3(0.07, 0.08, 0.07), float3(0.2, 0.22, 0.19), stone);
                    rock = lerp(rock, float3(0.1, 0.13, 0.12), 0.35) * (0.8 + 0.25 * film);
                    float3 rr = reflect(-v, normalize(n + float3(film - 0.5, 0, stone - 0.5) * 0.4));
                    float sheen = pow(saturate(dot(rr, sun)), 40) * (0.25 + film * 0.5);
                    return half4(XenaSkyFog(rock + sheen + film * 0.05, input.positionWS), 1);
                }

                // Falling sheet: velocity grows with drop (phase ~ sqrt(depth)).
                float phase = sqrt(saturate(f.y) + 0.02) * 6.0 - t * 1.9;
                float2 warp = float2(Fbm(float2(across * 0.7, phase * 0.5)), Fbm(float2(across * 0.7 + 5.2, phase * 0.5 + 1.3)));
                float body = Fbm(float2(across * 1.4 + warp.x * 1.6, phase * 0.9 + warp.y));
                float streaks = Fbm(float2((across + warp.x * 0.8) * 9.0, phase * 0.14));
                streaks = smoothstep(0.42, 0.72, streaks);
                float fine = smoothstep(0.5, 0.8, Fbm(float2((across + warp.y) * 22.0, phase * 0.3 - t * 0.6)));
                float clumps = smoothstep(0.55, 0.85, Fbm(float2(across * 2.6 + warp.y, phase * 1.8)));
                float aerated = saturate(0.32 - 0.22 * _BottomFade + streaks * (1 - 0.4 * _BottomFade) * 0.55 + fine * 0.25 + clumps * 0.4 + smoothstep(0.35, 1.0, f.y) * 0.25);
                float lip = smoothstep(0.1, 0.0, f.y);
                float foam = smoothstep(0.78, 1.0, f.y) * smoothstep(0.35, 0.75, Fbm(float2(across * 3.0, f.y * 4.0 - t * 3.5)) + 0.25);

                // Flowing river surface on near-horizontal faces.
                float glide = f.y * 3.0 - t * 0.8;
                float ripple = Fbm(float2(across * 1.6, glide));
                float surfaceFoam = smoothstep(0.62, 0.85, Fbm(float2(across * 4.0, glide * 2.0)));

                // Perturbed normal for glints and reflection.
                float2 slope = float2(Fbm(float2(across * 3.0 + 0.1, phase)) - Fbm(float2(across * 3.0 - 0.1, phase)),
                    Fbm(float2(across * 3.0, phase + 0.1)) - Fbm(float2(across * 3.0, phase - 0.1)));
                float3 tangent = normalize(cross(n, abs(n.y) > 0.9 ? float3(1, 0, 0) : float3(0, 1, 0)));
                float3 bitangent = cross(n, tangent);
                float3 shaded = normalize(n + (tangent * slope.x + bitangent * slope.y) * 1.2);
                float fresnel = 0.04 + 0.96 * pow(1 - saturate(dot(shaded, v)), 4);
                float3 r = reflect(-v, shaded);
                float glint = pow(saturate(dot(r, sun)), 240) * 0.5;

                // The original painted fall, scrolled down inside its page rect.
                float2 local = float2(f.x, frac(f.y - t * 0.45 * falling));
                float2 scrolledUv = _UvRect.xy + float2(local.x, _FlowSign > 0 ? local.y : 1 - local.y) * _UvRect.zw;
                half3 original = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(scrolledUv, _MainTex)).rgb;
                float3 water = lerp(_DeepColor.rgb, original * 0.6 + _DeepColor.rgb * 0.5, 0.35) * (0.75 + 0.5 * body);
                float3 fallColor = lerp(water, _AeratedColor.rgb, saturate(aerated + foam + lip * 0.35));
                fallColor = lerp(fallColor, original * 1.08, 0.12);
                float fallAlpha = saturate(_Opacity - 0.18 * _BottomFade + aerated * 0.45 + foam * 0.6 + lip * 0.2);
                float3 riverColor = lerp(water * 0.7, _AeratedColor.rgb, surfaceFoam * 0.45) * (0.85 + 0.3 * ripple);
                // Terrain pools have no bed below them: opaque deep water there.
                float riverAlpha = lerp(saturate(_Opacity * 0.9 + surfaceFoam * 0.3), 0.97, _HideFalling);

                float3 color = lerp(riverColor, fallColor, falling);
                float alpha = lerp(riverAlpha, fallAlpha, falling);
                float3 skyReflection = _XenaSkyFogEnabled > 0.5 ? XenaSkyColor(r) * 0.55 : _SkyColor.rgb;
                color = lerp(color, skyReflection, fresnel * lerp(0.5, 0.2, falling)) + glint;
                // Thin, soft side edges instead of slab borders.
                float edge = smoothstep(0.0, 0.06, f.x) * smoothstep(1.0, 0.94, f.x);
                alpha = saturate(alpha + fresnel * 0.2 + glint) * lerp(1, edge, falling);
                // Curtains dissolve into churning spray instead of a cut edge.
                float dissolve = smoothstep(1.0, 0.86, f.y + (Fbm(float2(across * 2.0, t * 2.0)) - 0.5) * 0.12);
                // Curtains break into separate ribbons that merge as they spread.
                float ribbon = saturate(smoothstep(0.38, 0.6, Fbm(float2(across * 0.85, f.y * 0.5 - t * 0.04))) + f.y * 0.35);
                // Narrow falls stay a continuous stream; wide ones may split.
                float gaps = lerp(0.75, 0.05, saturate((_Across - 2.0) / 6.0));
                alpha *= lerp(1, dissolve * lerp(gaps, 1, ribbon), _BottomFade);
                return half4(XenaSkyFog(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
