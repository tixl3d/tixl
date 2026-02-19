#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"

cbuffer ParamConstants : register(b0)
{
    //float Time;
    float SlicePosition;
    float Threshold;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}


cbuffer TimeConstants : register(b2)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct Pixel {
    float4 Color;
};

// struct Counter {
//     int Number;
// };

Texture2D<float4> FxTexture : register(t0);
sampler texSampler : register(s0);

RWStructuredBuffer<LegacyPoint> PointBuffer : register(u0); 
RWStructuredBuffer<uint> Counters : register(u1); 

// static const int2 DirectionsMoore[] = 
// {
//   int2( -1,  0),
//   int2( -1, +1),
//   int2(  0, +1),
//   int2( +1, +1),
//   int2( +1,  0),
//   int2( +1, -1),
//   int2(  0, -1),
//   int2( -1, -1),
// };

// static const int2 DirectionsNeumann[] = 
// {
//   int2( -1,  0),
//   int2( +1,  0),
//   int2(  0, -1),
//   int2(  0, +1),
// };

//

[numthreads(16,16,1)]
void main(uint3 DTid : SV_DispatchThreadID)
{         
    float hash = hash12(float2(DTid.xy));
    uint pointCount, stride;
    PointBuffer.GetDimensions(pointCount, stride);

    float4 col = FxTexture[DTid.xy];

    if(col.r > Threshold) 
    {
        uint counter;
        InterlockedAdd(Counters[0], 1,  counter);
        uint pointIndex = counter % pointCount;
        PointBuffer[pointIndex].position = float3(DTid.xy,SlicePosition);
        PointBuffer[pointIndex].w = col.r > Threshold 
                                    ? col.r
                                    : sqrt(-1); // NaN -> don't render
        PointBuffer[pointIndex].rotation =  float4(0,0,0,1);// float4(col.rgb,1);//float4(0,0,0,1);
    }
}
