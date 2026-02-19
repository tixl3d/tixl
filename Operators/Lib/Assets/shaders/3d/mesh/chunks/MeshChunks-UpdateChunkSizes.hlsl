#include "shared/point.hlsl"

#define THREADS_PER_GROUP 256

struct IndicesForDraw
{
    int PointIndex;
    int3 VertexIndices;
};

struct ChunkDef
{
    int StartFaceIndex;
    int FaceCount;
    int StartVertexIndex;
    int VertexCount;
};

cbuffer Params : register(b0)
{
    int PointCount; // we only needs this for out of bounds check
    int ChunkDefCount;
    int ChunkIndexForPointsCounts;
};

StructuredBuffer<int> ChunkIndicesForPoints : register(t0);
StructuredBuffer<ChunkDef> ChunkDefs : register(t1);

RWStructuredBuffer<uint> ChunkSizes : register(u0);

[numthreads(THREADS_PER_GROUP, 1, 1)] void UpdateChunkSizes(uint DTid : SV_DispatchThreadID, uint _GI : SV_GroupIndex)
{
    int pointIndex = DTid.x;
    if (pointIndex >= PointCount)
        return;

    int chunkIndex = ChunkIndicesForPoints[pointIndex % ChunkIndexForPointsCounts];

    ChunkSizes[pointIndex] = ChunkDefs[chunkIndex % ChunkDefCount].FaceCount;
}
