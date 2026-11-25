#include "shared/hash-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer FloatParams : register(b0)
{
    float Scatter;
    float StepSize;
}

cbuffer IntParams : register(b1)
{
    int SourceFaceCount;
    int ResultFaceCount;
    int StartIndex;
    int Seed;
}

StructuredBuffer<int3> SourceIndices : register(t0);
StructuredBuffer<PbrVertex> SourceVertices : register(t1);

RWStructuredBuffer<int3> ResultIndices : register(u0);
RWStructuredBuffer<PbrVertex> ResultVertices : register(u1);

inline int imod2(int val, int repeat)
{
    int x = val % repeat;
    return x + (x < 0) * repeat;
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    int faceIndex = i.x;
    if (faceIndex >= ResultFaceCount || SourceFaceCount <= 0)
        return;

    int scatterOffset = Scatter > 0.001
                            ? (float)SourceFaceCount * Scatter * hash11u(faceIndex + Seed * SourceFaceCount + StartIndex)
                            : 0;

    int sourceFaceIndex = imod2(StartIndex + (int)(faceIndex * StepSize) + scatterOffset, SourceFaceCount);
    int3 sourceIndices = SourceIndices[sourceFaceIndex];

    // Every picked face gets its own three vertices, so the vertex count is known up front without a compaction pass.
    int vertexIndex = faceIndex * 3;
    ResultVertices[vertexIndex + 0] = SourceVertices[sourceIndices.x];
    ResultVertices[vertexIndex + 1] = SourceVertices[sourceIndices.y];
    ResultVertices[vertexIndex + 2] = SourceVertices[sourceIndices.z];

    ResultIndices[faceIndex] = int3(vertexIndex, vertexIndex + 1, vertexIndex + 2);
}
