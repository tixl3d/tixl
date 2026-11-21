// 2.5D parallax: shifts an image by its depth as if the camera moved sideways. A gather march along the
// offset direction finds, for each output pixel, the nearest surface that lands on it, so no holes open up.

Texture2D<float4> Image : register(t0);
Texture2D<float> Depth : register(t1);
RWTexture2D<float4> Result : register(u0);

cbuffer Params : register(b0)
{
    float2 Offset;
    float MinDepth;
    float MaxDepth;
    float FocalPlane;
    int Steps;
    uint DepthIsDisparity;
    float _padding;
}

static const float MinRange = 0.0001;

// 0 at the focal plane, positive for surfaces nearer to the camera
float NearFactor(float depthValue)
{
    float range = max(MaxDepth - MinDepth, MinRange);
    float normalized = saturate((depthValue - MinDepth) / range);
    float near = DepthIsDisparity != 0 ? normalized : 1 - normalized;
    return near - FocalPlane;
}

float3 SampleImage(float2 uv, uint width, uint height)
{
    float2 position = clamp(uv * float2(width, height) - 0.5, 0, float2(width - 1, height - 1));
    int2 low = (int2)floor(position);
    int2 high = min(low + 1, int2(width - 1, height - 1));
    float2 fraction = position - low;
    float3 top = lerp(Image.Load(int3(low.x, low.y, 0)).rgb, Image.Load(int3(high.x, low.y, 0)).rgb, fraction.x);
    float3 bottom = lerp(Image.Load(int3(low.x, high.y, 0)).rgb, Image.Load(int3(high.x, high.y, 0)).rgb, fraction.x);
    return lerp(top, bottom, fraction.y);
}

float NearFactorAt(float2 uv, uint depthWidth, uint depthHeight)
{
    int2 coordinate = clamp((int2)(uv * float2(depthWidth, depthHeight)), int2(0, 0), int2(depthWidth - 1, depthHeight - 1));
    return NearFactor(Depth.Load(int3(coordinate, 0)));
}

[numthreads(16, 16, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Image.GetDimensions(width, height);
    if (id.x >= width || id.y >= height)
        return;

    uint depthWidth, depthHeight;
    Depth.GetDimensions(depthWidth, depthHeight);

    float2 uv = (float2(id.xy) + 0.5) / float2(width, height);
    int steps = max(Steps, 1);

    // A surface with near factor f lands at source + Offset * f, so the source pixel for this output pixel is
    // uv - Offset * t with the surface's own f equal to t. Marching t from the largest factor downward, the first
    // crossing is the nearest surface, because nearer surfaces shift further.
    float tStart = 1 - FocalPlane;
    float tEnd = -FocalPlane;
    float2 sampleUv = uv - Offset * tEnd;
    float previousGap = 0;
    float previousT = tStart;
    for (int step = 0; step < steps; step++)
    {
        float t = lerp(tStart, tEnd, (step + 1.0) / steps);
        float gap = NearFactorAt(uv - Offset * t, depthWidth, depthHeight) - t;
        if (gap >= 0)
        {
            float blend = gap == previousGap ? 1 : saturate(-previousGap / (gap - previousGap));
            sampleUv = uv - Offset * lerp(previousT, t, blend);
            break;
        }

        previousGap = gap;
        previousT = t;
    }

    Result[id.xy] = float4(SampleImage(sampleUv, width, height), 1);
}
