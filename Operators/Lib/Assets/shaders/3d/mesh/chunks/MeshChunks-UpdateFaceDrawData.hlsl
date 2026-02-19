#include "shared/point.hlsl"

#define THREADS_PER_GROUP 256

cbuffer Params : register(b0)
{
    uint FaceCount; // Computed from summing up chunk lenghts
    uint PointCount;
    uint ChunkIndexForPointsCounts;
};

struct ChunkDef
{
    int StartFaceIndex;
    int FaceCount;
    int StartVertexIndex;
    int VertexCount;
};

struct IndicesForDraw
{
    int PointIndex;
    int3 VertexIndices;
};

StructuredBuffer<uint> ChunkIndicesForPoints : register(t0);
StructuredBuffer<ChunkDef> ChunkDefs : register(t1);
StructuredBuffer<uint> ChunkSizes : register(t2);
StructuredBuffer<uint> ChunkEnds : register(t3);
StructuredBuffer<int3> FaceIndices : register(t4);

RWStructuredBuffer<IndicesForDraw> FaceDrawDatas : register(u0);

[numthreads(THREADS_PER_GROUP, 1, 1)]
void UpdateDrawData(uint DTid : SV_DispatchThreadID, uint _GI : SV_GroupIndex)
{
    uint faceIndex = DTid.x;
    if (faceIndex > FaceCount)
        return;

    uint left = 0;
    uint right = PointCount - 1;
    uint pointIndex = PointCount; // Initialize to an invalid index

    // Binary search for the correct pointIndex
    uint maxCount = 60;

    while (left <= right && maxCount-- > 0)
    {
        uint mid = (left + right) / 2;

        if (faceIndex < ChunkEnds[mid])
        {
            right = mid ;
        }
        else
        {
            left = mid + 1;
        }
    }

    pointIndex = left;

    // Check if a valid pointIndex was found
    
    if (pointIndex < PointCount)
    {
        uint chunkEndFaceIndex = ChunkEnds[pointIndex];
        uint chunkSize = ChunkSizes[pointIndex];
        uint chunkStartFaceIndex = chunkEndFaceIndex - chunkSize;    // FIXME: This point offset is weird
        uint faceIndexInPointChunk = faceIndex - chunkStartFaceIndex;

        uint chunkDefIndex = ChunkIndicesForPoints[pointIndex % ChunkIndexForPointsCounts];
        uint faceStartIndexForChunk = ChunkDefs[chunkDefIndex].StartFaceIndex;

        FaceDrawDatas[faceIndex].VertexIndices = FaceIndices[faceStartIndexForChunk + faceIndexInPointChunk];
        FaceDrawDatas[faceIndex].PointIndex = pointIndex;
    }
}

/*

ends [1,2] 
faceIndex = 1

left = 0
right = 1
mid = 0

midChunkEnd = ends[mid+1] = ends[1] = 

*/

// Write a method for a binary search in an inclusive prefix sum array

