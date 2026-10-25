#ifndef XENA_SKY_FOG_INCLUDED
#define XENA_SKY_FOG_INCLUDED
// Remaster atmosphere. The HDRI panorama (same lookup as Skybox/Panoramic) is
// blended into a uniform horizon haze below ~37 deg elevation; distant surfaces
// fade into that hazed sky, so the level's outer edges and floating parts
// dissolve into the same haze the sky shows there, without cloud texture on rock.
// _XenaSkyFogParams: x rotation (deg), y exposure, z start (m), w end (m).
// _XenaHazeColor: horizon haze (linear). _XenaSkyFogEnabled is 0 in Original.
TEXTURE2D(_XenaSkyPanorama); SAMPLER(sampler_XenaSkyPanorama);
float4 _XenaSkyFogParams;
float4 _XenaHazeColor;
float _XenaSkyFogEnabled;

float3 XenaPanorama(float3 direction, float rotation, float exposure)
{
    float alpha = -rotation * PI / 180.0;
    float s, c; sincos(alpha, s, c);
    float3 d = normalize(float3(c * direction.x - s * direction.z, direction.y, s * direction.x + c * direction.z));
    float latitude = acos(clamp(d.y, -1, 1));
    float longitude = atan2(d.z, d.x);
    float2 uv = float2(0.5, 1.0) - float2(longitude * 0.5 / PI, latitude / PI);
    return SAMPLE_TEXTURE2D_LOD(_XenaSkyPanorama, sampler_XenaSkyPanorama, uv, 0).rgb * exposure;
}

// Hazed sky as drawn by Xena/RemasterSky.
float3 XenaHazedSky(float3 direction, float rotation, float exposure, float3 haze)
{
    float3 d = normalize(direction);
    float clear = smoothstep(0.05, 0.6, d.y);
    return lerp(haze, XenaPanorama(d, rotation, exposure), clear);
}

float3 XenaSkyColor(float3 direction)
{
    return XenaHazedSky(direction, _XenaSkyFogParams.x, _XenaSkyFogParams.y, _XenaHazeColor.rgb);
}

float3 XenaSkyFog(float3 color, float3 positionWS)
{
    if (_XenaSkyFogEnabled < 0.5) return color;
    float3 toPoint = positionWS - _WorldSpaceCameraPos;
    float distance = length(toPoint);
    // Thickens with distance: gentle near the start, opaque at the end.
    float amount = pow(smoothstep(_XenaSkyFogParams.z, _XenaSkyFogParams.w, distance), 0.75);
    return lerp(color, XenaSkyColor(toPoint / max(distance, 1e-4)), amount);
}
#endif
