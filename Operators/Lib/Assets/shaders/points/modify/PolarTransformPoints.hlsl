#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformMatrix;
    float Mode;
}


StructuredBuffer<LegacyPoint> SourcePoints : t0;        
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;   

static const float PointSpace = 0;
static const float ObjectSpace = 1;
static const float WorldSpace = 2;

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) {
        return;
    }

    LegacyPoint p = SourcePoints[i.x];

    float3 pos = p.Position;
    float4 rot = p.Rotation;

    pos = mul(float4(pos,1), TransformMatrix).xyz;

    float4 rotYAxis = qFromAngleAxis(pos.x, float3(0,1,0));
    float4 rotXAxis = qFromAngleAxis(-pos.y, float3(1,0,0));

    if(Mode > 0.5) 
    {
        pos = float3(
            pos.x,
            pos.z * sin(pos.y),
            pos.z * cos(pos.y)
        );
    }

    pos = float3(
        pos.z * sin(pos.x),
        pos.y,
        pos.z * cos(pos.x)
    );


    if(Mode > 0.5) 
    {
        rot = normalize(qMul(rotXAxis, rot));
    }
    rot = normalize(qMul(rotYAxis, rot));

    p.Position = pos;
    p.Rotation = rot;
    ResultPoints[i.x] = p;
}

