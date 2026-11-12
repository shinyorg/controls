// SPIKE: CRT look as a Core Image Metal kernel — barrel curvature, RGB split, scanlines, vignette, flicker.
#include <CoreImage/CoreImage.h>
using namespace metal;

extern "C" { namespace coreimage {

float4 crt(sampler src, float time, float curvature, float scanline, float aberration, float lines, destination dest)
{
    float4 ext = src.extent();
    float2 uv = (dest.coord() - ext.xy) / ext.zw;

    // barrel distortion
    float2 cc = uv - 0.5;
    float2 duv = 0.5 + cc * (1.0 + curvature * dot(cc, cc));
    if (duv.x < 0.0 || duv.y < 0.0 || duv.x > 1.0 || duv.y > 1.0)
        return float4(0.0, 0.0, 0.0, 1.0);

    float2 p = ext.xy + duv * ext.zw;
    float2 off = float2(aberration, 0.0);
    float3 col = float3(
        src.sample(src.transform(p + off)).r,
        src.sample(src.transform(p)).g,
        src.sample(src.transform(p - off)).b);

    // scanlines (a fixed line count so it reads as a CRT at any resolution)
    float line = 0.5 + 0.5 * sin(duv.y * lines * 6.2831853);
    col *= mix(1.0, line, scanline);

    // vignette
    float v = 16.0 * duv.x * duv.y * (1.0 - duv.x) * (1.0 - duv.y);
    col *= pow(v, 0.3);

    // flicker + slight phosphor tint
    col *= 0.96 + 0.04 * sin(time * 110.0);
    col *= float3(0.95, 1.05, 0.95);
    return float4(col, 1.0);
}

}}
