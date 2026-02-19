Texture2D<float4> InputTexture : register(t0);
// RWTexture2D<float> OutputTexture : register(u0);

RWStructuredBuffer<uint> ResultBuffer : register(u0);

cbuffer ParamConstants : register(b0)
{
    uint scaleFactor;
    uint textureWidth;
    uint textureHeight;
}

// Scaling factor for float to int conversion
// static const float scaleFactor = 255.0f;
//

groupshared uint localSum = 0;

[numthreads(1, 1, 1)] void clear(uint3 DTid : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID, uint GI : SV_GroupIndex)
{
    if (DTid.x == 0 && DTid.y == 0)
        ResultBuffer[0] = 0;
}

    // Thread group dimensions
    [numthreads(8, 8, 1)] void main(uint3 DTid : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID, uint GI : SV_GroupIndex)
{
    if (GI == 0)
        localSum = 0;

    GroupMemoryBarrierWithGroupSync();

    float4 color = InputTexture.Load(DTid);
    float luminance = (color.r + color.g + color.b) / 3 * color.a;

    // Convert luminance to an integer by scaling
    uint scaledLuminance = (uint)(luminance * scaleFactor);

    InterlockedAdd(localSum, scaledLuminance);

    // Synchronize to ensure all threads have completed accumulation
    GroupMemoryBarrierWithGroupSync();

    // After synchronization, the first thread in the group writes the accumulated sum to the global output
    if (GI == 0)
    {
        // Atomically add the local group sum to the global output
        InterlockedAdd(ResultBuffer[0], localSum);
    }
}