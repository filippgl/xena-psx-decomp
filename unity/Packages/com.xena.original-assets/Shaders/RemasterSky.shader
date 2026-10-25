Shader "Xena/RemasterSky"
{
    // Remaster sky dome: CC0 HDRI panorama hazed towards the horizon colour
    // (XenaSkyFog.hlsl), matching the aerial perspective on level surfaces, with the
    // painted 360-degree landscape (_XenaLandscape) over its lower part.
    Properties
    {
        _MainTex ("Panorama (HDR)", 2D) = "grey" {}
        _Rotation ("Rotation", Range(0, 360)) = 0
        _Exposure ("Exposure", Range(0, 8)) = 1
        _HazeColor ("Horizon haze", Color) = (0.64, 0.71, 0.78, 1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "XenaSkyFog.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _HazeColor;
                float _Rotation, _Exposure;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float alpha = -_Rotation * PI / 180.0; float s, c; sincos(alpha, s, c);
                float3 r = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
                float2 uv = float2(0.5, 1.0) - float2(atan2(r.z, r.x) * 0.5 / PI, acos(clamp(r.y, -1, 1)) / PI);
                float3 sky = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb * _Exposure;
                return half4(XenaWithLandscape(d, lerp(_HazeColor.rgb, sky, smoothstep(0.05, 0.6, d.y))), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
