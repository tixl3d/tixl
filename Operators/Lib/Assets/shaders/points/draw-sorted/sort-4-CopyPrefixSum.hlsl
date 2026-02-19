RWStructuredBuffer<uint> BucketPrefixSum :register(u0);    
RWStructuredBuffer<uint> BucketOffsetSum :register(u1);   

cbuffer Params : register(b0)
{
    int BucketCount;
    int ParticleCount;
}

// 4. CopyPrefixSum.compute
// Copies BucketPrefixSum to BucketOffsetSum (read-only during write pass)
[numthreads(64, 1, 1)]
void CopyPrefixSum(uint3 id : SV_DispatchThreadID)
{
    if (id.x < BucketCount)
        BucketOffsetSum[id.x] = BucketPrefixSum[id.x];
}
