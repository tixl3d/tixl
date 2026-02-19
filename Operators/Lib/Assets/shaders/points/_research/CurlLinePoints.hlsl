#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float A;
    float B;
    float C;
    float MagnitudeA;
    float FreqA;
    float LineLength;
}


StructuredBuffer<LegacyPoint> SourcePoints : t0;        
Texture2D<float4> FxTexture : register(t1);
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;   
sampler texSampler : register(s0);

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    // uint numStructs, stride;
    // SourcePoints.GetDimensions(numStructs, stride);
    // if(i.x >= numStructs) {
    //     return;
    // }

    int lineLength = (int)LineLength;
    int lineIndex = i.x;
    int lineStartPointIndex = lineIndex * lineLength;

    float3 pos = SourcePoints[lineStartPointIndex].Position;

    for(int pointOnLineIndex = 0; pointOnLineIndex < lineLength; pointOnLineIndex++) 
    {
        int pointIndex = pointOnLineIndex + lineStartPointIndex;
        float normalizedPositionOnLine = pointOnLineIndex / LineLength;
        float4 fx = FxTexture.SampleLevel(texSampler, float2(normalizedPositionOnLine,0.5), 0 );

        float phase = lineIndex * B;
        float3 sinOffset = float3( 
            sin( pointOnLineIndex * C + phase) * fx.r, 
            cos (pointOnLineIndex * C + phase) * fx.g,
            0 ) * MagnitudeA;

        ResultPoints[pointIndex].Position = pos + sinOffset + float3(0.1,0,0);
        pos += float3(0,A/LineLength,0);

        ResultPoints[pointIndex].Rotation = SourcePoints[pointIndex].Rotation;
        ResultPoints[pointIndex].W = SourcePoints[pointIndex].W;
    }
}

