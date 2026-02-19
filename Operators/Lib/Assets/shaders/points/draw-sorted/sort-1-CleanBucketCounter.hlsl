RWStructuredBuffer<uint> BucketCounter :register(u0);      

cbuffer Params : register(b0)
{
    int BucketCount;
    int ParticleCount;
}


 // 1. ClearBucketCounter.compute
// Sets all bucket counts to 0
[numthreads(64, 1, 1)]
void ClearBucketCounter(uint3 DTid : SV_DispatchThreadID)
{
    if (DTid.x < BucketCount)
        BucketCounter[DTid.x] = 0;
}
