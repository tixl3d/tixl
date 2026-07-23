cbuffer Params : register(b0)
{
    float4 TargetSize; // xy = output canvas pixels
}

// A screen-aligned quad in output pixels — the handle/marker primitive for everything the projection has to
// show but the corner pin must not distort: annotation endpoints, calibration points, pickable handles.
// All members are float4 so the C# struct and the HLSL packing rules cannot disagree.
struct QuadInstance
{
    float4 Rect;   // xy = centre (px), zw = size (px)
    float4 Color;
    float4 Params; // x = rotation (radians), y = corner radius (px); z..w spare (a texture slot later)
};

StructuredBuffer<QuadInstance> Quads : register(t0);

struct vsOutput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 local : TEXCOORD0;  // offset from the centre, in pixels
    float2 extent : TEXCOORD1; // half size (px)
    float2 shape : TEXCOORD2;  // x = corner radius (px)
};

static const float2 Corners[6] =
    {
        float2(-1, -1),
        float2(1, -1),
        float2(1, 1),
        float2(-1, -1),
        float2(1, 1),
        float2(-1, 1),
};

vsOutput vsMain(uint vertexId : SV_VertexID)
{
    QuadInstance quad = Quads[vertexId / 6];
    float2 corner = Corners[vertexId % 6];

    // A pixel of slack for the antialiasing ramp, so the outer edge isn't cut by the geometry.
    float2 halfSize = quad.Rect.zw * 0.5 + 1;
    float2 local = corner * halfSize;

    float sinAngle, cosAngle;
    sincos(quad.Params.x, sinAngle, cosAngle);
    float2 rotated = float2(local.x * cosAngle - local.y * sinAngle,
                            local.x * sinAngle + local.y * cosAngle);

    float2 pixel = quad.Rect.xy + rotated;

    vsOutput output;
    output.position = float4(pixel.x / TargetSize.x * 2 - 1, 1 - pixel.y / TargetSize.y * 2, 0, 1);
    output.color = quad.Color;
    output.local = local;
    output.extent = halfSize;
    output.shape = float2(quad.Params.y, 0);
    return output;
}

float4 psMain(vsOutput input) : SV_TARGET
{
    // Rounded box in pixels: a radius of half the size gives a dot, zero gives a square, so one primitive
    // covers both markers and future textured handles.
    float radius = min(input.shape.x, min(input.extent.x, input.extent.y));
    float2 corner = abs(input.local) - (input.extent - radius);
    float dist = length(max(corner, 0)) + min(max(corner.x, corner.y), 0) - radius;

    float alpha = (1 - smoothstep(-0.5, 0.5, dist)) * input.color.a;
    if (alpha <= 0.001)
        discard;

    return float4(input.color.rgb, alpha);
}
