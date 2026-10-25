Shader "Xena/RemasterLightShaft"
{
    // Remaster sun shaft replacing the original additive @sunbm polygon: an open frustum drawn
    // two-sided and additively; brightness peaks where the surface faces the viewer and fades
    // to the silhouette, along the length (uv.y) and at both ends, with slow light bands.
    Properties
    {
        _Color ("Colour", Color) = (1, 0.9, 0.68, 1)
        _Intensity ("Intensity", Float) = 0.2
        _BandSpeed ("Band speed", Float) = 0.06
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Blend One One
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "XenaSkyFog.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float _Intensity, _BandSpeed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 view = normalize(GetCameraPositionWS() - input.positionWS);
                float facing = abs(dot(normalize(input.normalWS), view));
                float core = pow(facing, 3.0);
                float along = input.uv.y;                       // 0 top (sun side) .. 1 bottom
                float ends = smoothstep(0.0, 0.18, along) * smoothstep(1.0, 0.7, along);
                float u = input.uv.x * 6.2831853;
                // Separate rays like the original beam: several sharp bands drifting slowly.
                float r1 = pow(saturate(0.5 + 0.5 * sin(u * 5.0 + _Time.y * _BandSpeed * 30.0)), 3.0);
                float r2 = pow(saturate(0.5 + 0.5 * sin(u * 11.0 - along * 2.0 - _Time.y * _BandSpeed * 17.0)), 4.0);
                float bands = 0.25 + 1.1 * r1 + 0.8 * r2;
                // Fade near the camera so the shaft never fills the screen when walking through it.
                float near = saturate((distance(GetCameraPositionWS(), input.positionWS) - 0.8) / 2.5);
                float3 light = _Color.rgb * (_Intensity * core * ends * bands * near);
                // Far fog dims it like the surfaces around it.
                float amount = pow(smoothstep(_XenaSkyFogParams.z, _XenaSkyFogParams.w, distance(GetCameraPositionWS(), input.positionWS)), 0.75);
                return half4(light * (1 - amount * _XenaSkyFogEnabled), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
