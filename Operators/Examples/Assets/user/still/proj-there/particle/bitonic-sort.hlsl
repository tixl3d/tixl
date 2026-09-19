#include "shared/particle.hlsl"

cbuffer SortParameterConstBuffer : register(b0)
{
    unsigned int level;
    unsigned int levelMask;
    unsigned int width;
    unsigned int height;
};

RWStructuredBuffer<float2> Data : register(u0);
groupshared float2 SharedData[1024];

[numthreads(1024, 1, 1)]
void bitonicSort(uint3 Gid : SV_GroupID, uint3 DTid : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID, uint GI : SV_GroupIndex)
{
    SharedData[GI] = Data[DTid.x];
    GroupMemoryBarrierWithGroupSync();
    
    for (unsigned int j = level >> 1 ; j > 0 ; j >>= 1)
    {
        float2 result = ((SharedData[GI & ~j].y <= SharedData[GI | j].y) == (bool)(levelMask & DTid.x)) ? SharedData[GI ^ j] : SharedData[GI];
        GroupMemoryBarrierWithGroupSync();
        SharedData[GI] = result;
        GroupMemoryBarrierWithGroupSync();
    }
    
    Data[DTid.x] = SharedData[GI];
}

