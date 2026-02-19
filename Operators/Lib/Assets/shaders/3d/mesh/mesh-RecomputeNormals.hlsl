#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float Amount;
}

static const int MaxNeighbourFaceCount = 15;

struct FaceCount
{
    int Count;
    int FaceIndices[MaxNeighbourFaceCount];
};

StructuredBuffer<PbrVertex> SourceVertices : register(t0);
StructuredBuffer<int3> SourceFaces : register(t1);

RWStructuredBuffer<PbrVertex> ResultVertices : register(u0);
RWStructuredBuffer<FaceCount> VertexFaces : register(u1);

[numthreads(64, 1, 1)] void clear(uint3 i : SV_DispatchThreadID)
{
    uint gi = i.x;
    VertexFaces[gi].Count = 0;
    for (int index = 0; index < MaxNeighbourFaceCount; ++index)
    {
        VertexFaces[gi].FaceIndices[index] = 0;
    }
}

    [numthreads(64, 1, 1)] void registerFaceVertices(uint3 i : SV_DispatchThreadID)
{
    uint faceIndex = i.x;
    uint faceCount, _;
    SourceFaces.GetDimensions(faceCount, _);
    if (faceIndex >= faceCount)
        return;

    float tmp = SourceVertices[0].Normal;
    int3 verticeIndices = SourceFaces[i.x];

    for (int side = 0; side < 3; ++side)
    {
        int vIndex = verticeIndices[side];
        int orgValue = VertexFaces[vIndex].Count;
        InterlockedAdd(VertexFaces[vIndex].Count, 1, orgValue);
        if (orgValue >= MaxNeighbourFaceCount)
            return;

        VertexFaces[vIndex].FaceIndices[orgValue] = faceIndex;
    }
}

inline float ComputeTriangleArea(float3 P1, float3 P2, float3 P3)
{
    float3 U = P2 - P1;
    float3 V = P3 - P1;

    float3 CrossProduct = cross(U, V);

    return 0.5 * length(CrossProduct);
}

[numthreads(64, 1, 1)] void computeNormal(uint3 i : SV_DispatchThreadID)
{
    uint gi = i.x;
    uint count, _;
    ResultVertices.GetDimensions(count, _);
    if (gi >= count)
        return;

    int faceCount = VertexFaces[gi].Count;
    if (faceCount == 0)
        return;

    float3 normalSum = 0;
    float3 center = SourceVertices[gi].Position;

    float3 sidePositions[2];

    int usedNeighbourghFaceCount = 0;

    for (int face = 0; face < faceCount && face <= MaxNeighbourFaceCount; ++face)
    {
        uint faceIndex = VertexFaces[gi].FaceIndices[face];
        uint3 faceVertexIndices = SourceFaces[faceIndex];

        float3 P1 = SourceVertices[faceVertexIndices[0]].Position;
        float3 P2 = SourceVertices[faceVertexIndices[1]].Position;
        float3 P3 = SourceVertices[faceVertexIndices[2]].Position;

        float3 U = P2 - P1;
        float3 V = P3 - P1;

        float3 N = cross(U, V);

        float area = 0.5 * length(N);
        normalSum += N;
        usedNeighbourghFaceCount++;

        normalSum += N * area;
    }

    if (usedNeighbourghFaceCount == 0)
    {
        // Decide what to do...
        return;
    }

    // normalSum /= usedNeighbourghFaceCount;

    ResultVertices[gi] = SourceVertices[gi];

    float3 bitangent = SourceVertices[gi].Bitangent;
    float3 tangent = SourceVertices[gi].Tangent;
    float3 orgNormal = SourceVertices[gi].Normal;
    float3 newNormal = normalize(normalSum);

    float3 newTangent = cross(bitangent, newNormal);
    float3 newBitangent = cross(newNormal, newTangent);

    ResultVertices[gi].Selected = faceCount;
    ResultVertices[gi].Normal = newNormal;
    ResultVertices[gi].Tangent = newTangent;
    ResultVertices[gi].Bitangent = newBitangent;
}