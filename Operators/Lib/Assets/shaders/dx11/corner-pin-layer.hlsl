cbuffer Params : register(b0)
{
    row_major float4x4 Homography;
    float4 SourceTLTR; // source UV corners: TL.xy, TR.xy
    float4 SourceBRBL; // source UV corners: BR.xy, BL.xy
    float4 Color;
    float4 GridParams; // xy = metres spanned by the surface, z = line thickness (px), w = grid mode (>0.5)
    float4 GridColor;
    float4 GridOrigin; // xy = raster origin in source UV, z = minor lines per metre, w = minor line opacity
    float4 Mask;       // xy = centre (output px), z = radius (px), w = enabled (>0.5): only a disc of the layer shows
}

Texture2D<float4> InputTexture : register(t0);
sampler texSampler : register(s0);

// Unit quad corners: TL, TR, BR / TL, BR, BL
static const float2 Corners[] =
    {
        float2(0, 0),
        float2(1, 0),
        float2(1, 1),
        float2(0, 0),
        float2(1, 1),
        float2(0, 1),
};

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

vsOutput vsMain(uint vertexId : SV_VertexID)
{
    vsOutput output;
    float2 corner = Corners[vertexId];

    // The homography acts on (x, y, w); the rasterizer's perspective divide completes it.
    output.position = mul(float4(corner, 0, 1), Homography);

    // Sample the surface's source region: bilinear of the four source-quad UV corners by (u, v).
    // The rasterizer interpolates this perspective-correctly via position.w.
    float2 top = lerp(SourceTLTR.xy, SourceTLTR.zw, corner.x);
    float2 bottom = lerp(SourceBRBL.zw, SourceBRBL.xy, corner.x);
    output.texCoord = lerp(top, bottom, corner.y);
    return output;
}

// Anti-aliased line mask for a unit-periodic coordinate: 1 on the integer lines, 0 between them. Widths come
// from fwidth, so lines keep a constant screen thickness however the corner-pin warps the surface.
float gridLineMask(float2 coord, float thickness)
{
    float2 distToLine = abs(frac(coord) - 0.5);
    float2 pixelWidth = fwidth(coord);
    float2 lineAa = smoothstep(0.5, 0.5 - pixelWidth * max(thickness, 1), distToLine);
    return 1 - saturate(min(lineAa.x, lineAa.y));
}

float4 psMain(vsOutput input) : SV_TARGET
{
    // Grid mode: draw an analytic calibration raster over an opaque black canvas (unlit wall = black),
    // ignoring the input texture. Lines are anti-aliased in screen space via fwidth, so they stay a
    // constant thickness however the corner-pin warps the surface.
    if (GridParams.w > 0.5)
    {
        // Metres measured from the surface's anchor, so the raster's origin sits exactly where the anchor is
        // and its lines can be matched against real marks on the wall.
        float2 metres = (input.texCoord - GridOrigin.xy) * GridParams.xy;

        float major = gridLineMask(metres, GridParams.z * 2.7);

        // Subdivisions, dropped once they get too dense to resolve — otherwise they alias into moiré.
        float2 minorCoord = metres * max(GridOrigin.z, 1);
        float2 minorWidth = fwidth(minorCoord);
        float minorFade = saturate(1 - max(minorWidth.x, minorWidth.y) * 1);
        float minor = gridLineMask(minorCoord, GridParams.z *2) * GridOrigin.w * minorFade;

        // Alpha, not an opaque black backdrop: the raster is composited over the content so it stays readable
        // on top of it. Between the lines it's fully transparent, leaving content (or the cleared black) alone.
        return float4(GridColor.rgb, max(major, minor) * GridColor.a);
    }

    float4 color = InputTexture.Sample(texSampler, input.texCoord) * Color;

    // A calibration fragment: the layer fades out beyond a disc around one point, so only the photo right
    // around the feature reaches the wall.
    if (Mask.w > 0.5)
    {
        float d = distance(input.position.xy, Mask.xy);
        color.a *= 1 - smoothstep(Mask.z - 2, Mask.z, d);
    }

    return color;
}
