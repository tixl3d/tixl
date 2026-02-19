#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 GridSize;
    float Speed;

    float3 GridOffset;
    float SpeedVariation;

    float TriggerTurn;
    float Seed;
}

RWStructuredBuffer<LegacyPoint> ResultPoints : u0; 

static const float4 axisAngles[] = 
{
  float4( 0, 1, 0, 0),
  float4( 0, 1, 0, 1),
  float4( 0, 1, 0, 2),
  float4( 0, 1, 0, 3),
  float4( 1, 0, 0, -1),
  float4( 1, 0, 0, 1),
};


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    ResultPoints.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) {
        ResultPoints[i.x].W = 0 ;
        return;
    }

    LegacyPoint p = ResultPoints[i.x];
    float3 forward = float3(0,0,-1);
    float3 velocity = qRotateVec3(forward, normalize(p.Rotation)) * Speed;

    float3 localVelocity = velocity;

    float3 localPosition = mod(p.Position - GridOffset, GridSize);
    float3 newLocalPosition = localPosition + localVelocity;

    float hash = hash11((Seed + i.x + p.Position.x + p.Position.y + p.Position.z) * 421 % 1231);
    if( newLocalPosition.x <= 0 || newLocalPosition.x >= GridSize.x 
     || newLocalPosition.y <= 0 || newLocalPosition.y >= GridSize.y 
     || newLocalPosition.z <= 0 || newLocalPosition.z >= GridSize.z
     || TriggerTurn > 0.5
    ) {
        newLocalPosition = clamp(numStructs,0, GridSize);

        uint r = (uint)(hash * 10) % 4;
        float3 axis = r == 0 ? float3(-1,0,0) 
                                  : r == 1 ? float3(0,-1,0)
                                                : float3(0,0,-1);
        float4 axisAndAngle = axisAngles[(int)(hash * 6) % 6];
        ResultPoints[i.x].Rotation = qFromAngleAxis(axisAndAngle.w * PI/1.5, axisAndAngle.xyz);
    }
    ResultPoints[i.x].Position += velocity;
}

