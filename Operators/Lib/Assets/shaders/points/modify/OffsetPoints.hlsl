#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer TimeConstants : register(b0)
{
    float GlobalTime;
    float Time;
    float RunTime;
    float BeatTime;
    float LastFrameDuration;
}; 
 

cbuffer Params : register(b1)
{
    float3 Direction;
    float Distance;
}

// struct Point {
//     float3 Position;
//     float W;
// };

StructuredBuffer<Point> Points1 : register(t0);         // input
RWStructuredBuffer<Point> ResultPoints : register(u0);    // output

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    Points1.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) {
        ResultPoints[i.x].FX1 = 0 ;
        return;
    }

    ResultPoints[i.x].Position = Points1[i.x].Position +  qRotateVec3(Direction * Distance, Points1[i.x].Rotation);
    ResultPoints[i.x].Rotation = Points1[i.x].Rotation;
    ResultPoints[i.x].Color = Points1[i.x].Color;
    ResultPoints[i.x].FX2 = Points1[i.x].FX2;
    ResultPoints[i.x].FX1 = Points1[i.x].FX1;
}

