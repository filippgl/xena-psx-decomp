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
// Painted 360-degree landscape (Codex segments, tools/stitch_panorama.py), cylindrical:
// u = azimuth, v from the top = horizon - tan(elevation) * aspect / 2pi.
// _XenaLandscapeParams: x rotation (deg), y horizon row (0..1 from the top), z width/height, w enabled.
TEXTURE2D(_XenaLandscape); SAMPLER(sampler_XenaLandscape);
float4 _XenaLandscapeParams;

float3 XenaWithLandscape(float3 direction, float3 sky)
{
    if (_XenaLandscapeParams.w < 0.5) return sky;
    float3 d = normalize(direction);
    float alpha = -_XenaLandscapeParams.x * PI / 180.0; float s, c; sincos(alpha, s, c);
    float3 r = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
    float elevation = clamp(r.y / max(length(r.xz), 1e-4), -40, 40);
    float vTop = _XenaLandscapeParams.y - elevation * _XenaLandscapeParams.z / (2 * PI);
    float2 uv = float2(0.5 - atan2(r.z, r.x) * 0.5 / PI, 1 - saturate(vTop));
    float3 land = SAMPLE_TEXTURE2D_LOD(_XenaLandscape, sampler_XenaLandscape, uv, 0).rgb;
    // The painted sky's top dissolves into the HDRI sky above it.
    return lerp(sky, land, smoothstep(0.05, 0.18, vTop));
}

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
    return XenaWithLandscape(d, lerp(haze, XenaPanorama(d, rotation, exposure), clear));
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
    // Partly fogged surfaces fade into the flat horizon haze; only (almost) fully fogged ones take
    // the detailed sky/landscape behind them. Mixing a wall with the painted crags at 30 m made it
    // look see-through.
    float3 far = lerp(_XenaHazeColor.rgb, XenaSkyColor(toPoint / max(distance, 1e-4)), smoothstep(0.8, 1.0, amount));
    return lerp(color, far, amount);
}
#endif
