#include "shared/point.hlsl"
#include "shared/skin-binding.hlsl"

StructuredBuffer<Point> SourcePoints : register(t0);

[numthreads(64, 1, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);
    if (i.x >= pointCount)
        return;

    ResultWeights[i.x] = ComputeSkinWeights(SourcePoints[i.x].Position);
}
