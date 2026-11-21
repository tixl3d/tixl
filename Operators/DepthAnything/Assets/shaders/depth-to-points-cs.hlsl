// Unprojects a depth map into a camera-space point buffer that DrawPoints can render as
// billboard splats. One point per PixelStride-th pixel; invalid depths become zero-scale
// points that DrawPoints discards through its alpha cutoff.
//
// The Point layout matches shared/point.hlsl and T3.Core.DataTypes.Point (64 bytes).

struct Point
{
    float3 Position;
    float FX1;
    float4 Rotation;
    float4 Color;
    float3 Scale;
    float FX2;
};

Texture2D<float4> ColorTexture : register(t0);
Texture2D<float> DepthTexture : register(t1);
Texture2D<float> ConfidenceTexture : register(t2);
StructuredBuffer<Point> HistoryPoints : register(t3);

RWStructuredBuffer<Point> Points : register(u0);

cbuffer Params : register(b0)
{
    float TanHalfFovY;
    float DepthScale;
    float SplatSizePixels;
    float Opacity;

    uint DepthIsDisparity;
    uint HasColor;
    uint HasConfidence;
    uint PixelStride;

    uint GridWidth;
    uint GridHeight;
    float ConfidenceThreshold;
    float TemporalSmoothing;

    uint HasHistory;
    float _padding1;
    float _padding2;
    float _padding3;
}

static const float MinDisparity = 0.00001;

// Same validity test as depth-to-position-cs.hlsl: a bit test survives the compiler's
// default float optimizations, which may fold isnan() away.
bool IsValidDepth(float z)
{
    return (asuint(z) & 0x7fffffff) < 0x7f800000 && z > 0;
}

void WriteEmptyPoint(uint index)
{
    Point p;
    p.Position = float3(0, 0, 0);
    p.FX1 = 1;
    p.Rotation = float4(0, 0, 0, 1);
    p.Color = float4(0, 0, 0, 0);
    p.Scale = float3(0, 0, 0);
    p.FX2 = 1;
    Points[index] = p;
}

[numthreads(256, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    uint count = GridWidth * GridHeight;
    if (index >= count)
        return;

    uint gx = index % GridWidth;
    uint gy = index / GridWidth;

    uint depthWidth, depthHeight;
    DepthTexture.GetDimensions(depthWidth, depthHeight);
    if (depthWidth == 0 || depthHeight == 0)
    {
        WriteEmptyPoint(index);
        return;
    }

    int2 pixel = int2(gx * PixelStride, gy * PixelStride);
    pixel = min(pixel, int2(depthWidth - 1, depthHeight - 1));

    float rawDepth = DepthTexture.Load(int3(pixel, 0));
    float z = DepthIsDisparity != 0
                  ? DepthScale / max(rawDepth, MinDisparity)
                  : rawDepth * DepthScale;

    if (!IsValidDepth(z))
    {
        WriteEmptyPoint(index);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(depthWidth, depthHeight);

    if (HasConfidence != 0)
    {
        uint confidenceWidth, confidenceHeight;
        ConfidenceTexture.GetDimensions(confidenceWidth, confidenceHeight);
        if (confidenceWidth > 0 && confidenceHeight > 0)
        {
            int2 confidencePixel = int2(uv * float2(confidenceWidth, confidenceHeight));
            confidencePixel = min(confidencePixel, int2(confidenceWidth - 1, confidenceHeight - 1));
            if (ConfidenceTexture.Load(int3(confidencePixel, 0)) < ConfidenceThreshold)
            {
                WriteEmptyPoint(index);
                return;
            }
        }
    }

    float2 ndc = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    float aspect = (float)depthWidth / depthHeight;

    Point p;
    p.Position = float3(ndc.x * TanHalfFovY * aspect * z, ndc.y * TanHalfFovY * z, z);
    p.FX1 = 1;
    p.Rotation = float4(0, 0, 0, 1);

    float4 color = float4(1, 1, 1, 1);
    if (HasColor != 0)
    {
        uint colorWidth, colorHeight;
        ColorTexture.GetDimensions(colorWidth, colorHeight);
        if (colorWidth > 0 && colorHeight > 0)
        {
            // Nearest-neighbor lookup: the point grid is already subsampled, so a sampler
            // would only add filtering cost and another bound resource.
            int2 colorPixel = int2(uv * float2(colorWidth, colorHeight));
            colorPixel = min(colorPixel, int2(colorWidth - 1, colorHeight - 1));
            color = ColorTexture.Load(int3(colorPixel, 0));
        }
    }

    p.Color = float4(color.rgb, color.a * Opacity);

    // DrawPoints scales its billboard by 0.1 * PointSize * Scale in camera space, so the
    // Scale here is the world-space splat diameter for a PointSize of 5.
    float worldPerPixel = 2 * z * TanHalfFovY / depthHeight;
    float diameter = SplatSizePixels * worldPerPixel;
    p.Scale = float3(diameter, diameter, diameter);
    p.FX2 = 1;

    // A still camera still gets per-frame depth noise; blending with the previous point
    // buffer trades a little motion lag for a much calmer splat.
    if (HasHistory != 0 && TemporalSmoothing > 0)
    {
        Point previous = HistoryPoints[index];
        if (previous.Scale.x > 0)
        {
            float t = saturate(TemporalSmoothing);
            p.Position = lerp(p.Position, previous.Position, t);
            p.Scale = lerp(p.Scale, previous.Scale, t);
            p.Color = lerp(p.Color, previous.Color, t);
        }
    }

    Points[index] = p;
}
