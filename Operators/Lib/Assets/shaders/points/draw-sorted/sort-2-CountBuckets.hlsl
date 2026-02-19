#include "shared/point.hlsl"

StructuredBuffer<Point> GPoints :register(t0); 

RWStructuredBuffer<uint> BucketIndices :register(u0);   
RWStructuredBuffer<uint> BucketCounter :register(u1);      

cbuffer Params : register(b0)
{
    int BucketCount;
    int ParticleCount;
}

cbuffer Params : register(b1)
{
    float InvBucketSize;
    float3 CameraPos;
    float3 ViewDir;
}


// 2. CountBuckets.compute
// Writes per-particle bucket index to BucketIndices
// Increments corresponding BucketCounter
[numthreads(64, 1, 1)]
void CountBuckets(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)ParticleCount) 
        return;

    float3 pos = GPoints[id.x].Position;

    if (
        //dot(pos - CameraPos, ViewDir) < 0 // hidden
    //|| 
    isnan(GPoints[id.x].Scale.x))  // dead
    {
        BucketIndices[id.x] = 0xFFFFFFFF;
        return;
    }
    

    float dist = dot(pos - CameraPos, ViewDir);
    uint bucket = clamp((uint)(dist * InvBucketSize), 0, BucketCount - 1);

    BucketIndices[id.x] = bucket;
    InterlockedAdd(BucketCounter[bucket], 1);
}
 