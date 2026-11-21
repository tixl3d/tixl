// Edge-aware bilateral smoothing of a depth map: neighbors only contribute when their depth is close to the center's.

Texture2D<float> Depth : register(t0);
RWTexture2D<float> Filtered : register(u0);

cbuffer Params : register(b0)
{
    float SpaceCoefficient;
    float ColorCoefficient;
    int Radius;
    float _padding;
}

bool IsValid(float value)
{
    // Bit test: the compiler may fold isnan() away under its default float optimizations
    return (asuint(value) & 0x7fffffff) < 0x7f800000;
}

[numthreads(16, 16, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Depth.GetDimensions(width, height);
    if (id.x >= width || id.y >= height)
        return;

    int2 center = int2(id.xy);
    float centerDepth = Depth.Load(int3(center, 0));
    if (!IsValid(centerDepth))
    {
        Filtered[id.xy] = centerDepth;
        return;
    }

    float sum = 0;
    float weightSum = 0;
    for (int dy = -Radius; dy <= Radius; dy++)
    {
        for (int dx = -Radius; dx <= Radius; dx++)
        {
            int2 neighbor = center + int2(dx, dy);
            if (neighbor.x < 0 || neighbor.y < 0 || neighbor.x >= (int)width || neighbor.y >= (int)height)
                continue;

            float neighborDepth = Depth.Load(int3(neighbor, 0));
            if (!IsValid(neighborDepth))
                continue;

            float difference = centerDepth - neighborDepth;
            float weight = exp((dx * dx + dy * dy) * SpaceCoefficient + difference * difference * ColorCoefficient);
            sum += neighborDepth * weight;
            weightSum += weight;
        }
    }

    Filtered[id.xy] = weightSum > 0 ? sum / weightSum : centerDepth;
}
