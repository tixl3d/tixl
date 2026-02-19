#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    int startVertexIndex;
}

cbuffer Params : register(b1)
{
    float DebugValue;
}

StructuredBuffer<PbrVertex> Vertices : t0;            // input
RWStructuredBuffer<PbrVertex> ResultVertices : u0;    // output

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint size, stride;
    Vertices.GetDimensions(size, stride);

    if(i.x >= size)
        return;

    uint targetIndex = i.x + (int)startVertexIndex;
    ResultVertices[targetIndex] = Vertices[i.x];
    ResultVertices[targetIndex].Position.y += DebugValue;
}
