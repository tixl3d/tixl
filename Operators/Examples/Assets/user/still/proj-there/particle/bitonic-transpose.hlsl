#include "shared/particle.hlsl"

cbuffer SortParameterConstBuffer : register(b0)
{
    unsigned int level;
    unsigned int levelMask;
    unsigned int width;
    unsigned int height;
};


StructuredBuffer<float2> Input : register(t0);
RWStructuredBuffer<float2> Data : register(u0);

#define TRANSPOSE_BLOCK_SIZE 32
groupshared float2 TransposeSharedData[TRANSPOSE_BLOCK_SIZE * TRANSPOSE_BLOCK_SIZE];

[numthreads(TRANSPOSE_BLOCK_SIZE, TRANSPOSE_BLOCK_SIZE, 1)]
void transpose(uint3 Gid : SV_GroupID, uint3 DTid : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID, uint GI : SV_GroupIndex)
{
    TransposeSharedData[GI] = Input[DTid.y * width + DTid.x];
    GroupMemoryBarrierWithGroupSync();
    uint2 XY = DTid.yx - GTid.yx + GTid.xy;
    Data[XY.y * height + XY.x] = TransposeSharedData[GTid.x * TRANSPOSE_BLOCK_SIZE + GTid.y];
}

