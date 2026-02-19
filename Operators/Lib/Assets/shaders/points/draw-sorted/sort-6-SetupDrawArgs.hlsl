RWStructuredBuffer<uint> BucketCounter :register(u0);      
RWStructuredBuffer<uint> BucketPrefixSum :register(u1);    
RWStructuredBuffer<uint> DrawArgsBuffer :register(u2);   

cbuffer Params : register(b0)
{
    int BucketCount;
    int ParticleCount;
}

// 6. SetupDrawArgs.compute
// Total visible particles = last element of BucketPrefixSum + count
// Write DrawArgsBuffer as {6 * count, 1, 0, 0, 0}
[numthreads(1, 1, 1)]
void SetupDrawArgs(uint3 id : SV_DispatchThreadID)
{
    uint totalCount = BucketPrefixSum[BucketCount - 1] + BucketCounter[BucketCount - 1];
    DrawArgsBuffer[0] = totalCount * 6; // Vertex count (6 per quad)
    DrawArgsBuffer[1] = 1;              // Instance count
    DrawArgsBuffer[2] = 0;
    DrawArgsBuffer[3] = 0;
    DrawArgsBuffer[4] = 0;
}
