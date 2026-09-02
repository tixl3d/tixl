#include "shared/pbr.hlsl"
#include "shared/skin-binding.hlsl"

StructuredBuffer<PbrVertex> SourceVertices : register(t0);

[numthreads(64, 1, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint vertexCount, stride;
    SourceVertices.GetDimensions(vertexCount, stride);
    if (i.x >= vertexCount)
        return;

    ResultWeights[i.x] = ComputeSkinWeights(SourceVertices[i.x].Position);
}
