Shader "Xena/OriginalHorizon"
{
    // Original level background (docs/xena-level-background.md, func_800A5D1C, VERIFIED on the native port):
    // in a virtual 320x240 screen, the horizon row is s5 = trunc(-pitch*45/64) (pitch in 4096 units, positive =
    // looking down); rows [0,s5) are the sky fill, [s5,s5+128) the HORIZ.STM strip (512x128, column
    // (x + heading/2 + 256) mod 512, drawn raw, never fogged), then the fog colour. Drawn first, behind all.
    Properties
    {
        _MainTex ("Horizon strip 512x128", 2D) = "black" {}
        _SkyColor ("Fill above the strip", Color) = (0.082, 0.380, 0.839, 1)
        _FogColor ("Fill below the strip", Color) = (0, 0.290, 0.380, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_point_repeat);
            float4 _SkyColor, _FogColor;
            // x heading (4096 units), y pitch (4096 units, + down)
            float4 _XenaHorizonView;
            struct Attributes { uint id : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float screenY : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings o; float2 uv = float2((input.id << 1) & 2, input.id & 2);
                o.positionCS = float4(uv * 2 - 1, UNITY_RAW_FAR_CLIP_VALUE, 1); o.screenY = _ProjectionParams.x < 0 ? uv.y : 1 - uv.y; return o; // screenY: 0 top .. 1 bottom; URP flips the projection (x = -1) when it renders upside down into a texture
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Row from the clip-space position itself (independent of render-target flips): clip y +1 = top.
                float2 px = float2(input.positionCS.x, input.screenY * _ScreenParams.y);
                float scale = 240.0 / _ScreenParams.y;
                float vy = floor(px.y * scale), vx = floor(160 + (px.x - _ScreenParams.x * 0.5) * scale);
                float s5 = trunc(-_XenaHorizonView.y * 45.0 / 64.0);
                // Fills: material colours (Unity converts them to linear like the textures).
                if (vy < s5) return half4(_SkyColor.rgb, 1);
                if (vy >= s5 + 128) return half4(_FogColor.rgb, 1);
                float column = fmod(fmod(vx + _XenaHorizonView.x * 0.5 + 256, 512) + 512, 512);
                float2 uv = float2((column + 0.5) / 512.0, 1 - (vy - s5 + 0.5) / 128.0);
                return half4(SAMPLE_TEXTURE2D(_MainTex, sampler_point_repeat, uv).rgb, 1);
            }
            ENDHLSL
        }
    }
}
