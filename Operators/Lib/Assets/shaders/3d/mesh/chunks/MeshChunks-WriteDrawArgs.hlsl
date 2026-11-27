// One indexed indirect draw record per point: the index range of the chunk it places, read through the mesh
// index buffer. The draw reads the point index back from firstInstance.

struct ChunkDef
{
    int StartFaceIndex;
    int FaceCount;
    int StartVertexIndex;
    int VertexCount;
};

cbuffer Params : register(b0)
{
    int PointCount;
    int ChunkCount;
    int ChunkIndexCount; // 0 places chunk i at point i
};

StructuredBuffer<int> ChunkIndices : register(t0);
StructuredBuffer<ChunkDef> ChunkDefs : register(t1);

struct IndexedDrawArgs
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    int VertexOffset;
    uint FirstInstance;
};

RWStructuredBuffer<IndexedDrawArgs> DrawArgs : register(u0);

[numthreads(256, 1, 1)]
void main(uint3 threadId : SV_DispatchThreadID)
{
    uint pointIndex = threadId.x;
    if (pointIndex >= (uint)PointCount)
        return;

    int chunk = ChunkIndexCount > 0 ? ChunkIndices[pointIndex % (uint)ChunkIndexCount] : (int)pointIndex;
    chunk = ((chunk % ChunkCount) + ChunkCount) % ChunkCount;

    ChunkDef def = ChunkDefs[chunk];
    IndexedDrawArgs args;
    args.IndexCount = max(def.FaceCount, 0) * 3;
    args.InstanceCount = 1;
    args.FirstIndex = def.StartFaceIndex * 3;
    args.VertexOffset = 0; // mesh indices are absolute
    args.FirstInstance = pointIndex;
    DrawArgs[pointIndex] = args;
}
