cbuffer Params : register(b0)
{
    float4 TargetSize; // xy = output canvas pixels
}

// One overlay line, already carried through the corner pin on the CPU — so this shader never sees a
// homography, and the drawn width is a width in projector pixels rather than something the warp stretches.
// All members are float4 so the C# struct and the HLSL packing rules cannot disagree.
struct LineSegment
{
    float4 Points; // xy = A, zw = B, in output pixels
    float4 Color;
    float4 Params; // x = width (px); y..w spare
};

StructuredBuffer<LineSegment> Segments : register(t0);

struct vsOutput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 local : TEXCOORD0;  // x along the segment, y across it, both in output pixels
    float2 extent : TEXCOORD1; // x = segment length (px), y = half width (px)
};

// Two triangles per segment, as (along, across) in the segment's own frame.
static const float2 Corners[6] =
    {
        float2(0, -1),
        float2(1, -1),
        float2(1, 1),
        float2(0, -1),
        float2(1, 1),
        float2(0, 1),
};

vsOutput vsMain(uint vertexId : SV_VertexID)
{
    LineSegment segment = Segments[vertexId / 6];
    float2 corner = Corners[vertexId % 6];

    float2 a = segment.Points.xy;
    float2 delta = segment.Points.zw - a;
    float segmentLength = max(length(delta), 1e-5);
    float2 direction = delta / segmentLength;
    float2 normal = float2(-direction.y, direction.x);

    float halfWidth = max(segment.Params.x, 1) * 0.5;

    // The quad overhangs the segment by the half width plus a pixel for the antialiasing ramp, so the round
    // caps aren't clipped by their own geometry.
    float pad = halfWidth + 1;
    float along = lerp(-pad, segmentLength + pad, corner.x);
    float across = corner.y * pad;

    float2 pixel = a + direction * along + normal * across;

    vsOutput output;
    output.position = float4(pixel.x / TargetSize.x * 2 - 1, 1 - pixel.y / TargetSize.y * 2, 0, 1);
    output.color = segment.Color;
    output.local = float2(along, across);
    output.extent = float2(segmentLength, halfWidth);
    return output;
}

float4 psMain(vsOutput input) : SV_TARGET
{
    // Everything is in output pixels, so the antialiasing ramp is exactly one pixel wide — no fwidth needed.
    float alongClamped = clamp(input.local.x, 0, input.extent.x);
    float dist = length(float2(input.local.x - alongClamped, input.local.y));
    float alpha = (1 - smoothstep(input.extent.y - 0.5, input.extent.y + 0.5, dist)) * input.color.a;
    if (alpha <= 0.001)
        discard;

    return float4(input.color.rgb, alpha);
}
