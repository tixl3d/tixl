#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer FloatParams : register(b0)
{
    float3 Center;
    float Amount;
    float3 UpVector;
}

cbuffer IntParams : register(b1)
{
    int UseWAsWeight;
    int Flip;
    int AmountFactor;
    // float3 Center;
    // float Amount;
    // float3 UpVector;
    // float UseWAsWeight;
    // float Flip;
}

StructuredBuffer<Point> SourcePoints : t0;
RWStructuredBuffer<Point> ResultPoints : u0;

static const float PointSpace = 0;
static const float ObjectSpace = 1;
static const float WorldSpace = 2;

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
    {
        return;
    }

    Point p = SourcePoints[i.x];

    p.Position = p.Position;

    float strength = Amount * (AmountFactor == 0
                                   ? 1
                               : (AmountFactor == 1) ? p.FX1
                                                     : p.FX2);

    // float weight = UseWAsWeight > 0.5
    //                    ? p.FX1
    //                    : 1;

    // weight *= Amount;

    float sign = Flip > 0.5 ? -1 : 1;
    float4 newRot = qLookAt(normalize(Center - p.Position) * sign, normalize(UpVector));

    float3 forward = qRotateVec3(float3(0, 0, 1), newRot);
    float4 alignment = qFromAngleAxis(3.141578, forward);
    newRot = qMul(alignment, newRot);
    p.Rotation = normalize(qSlerp(normalize(p.Rotation), normalize(newRot), strength));
    ResultPoints[i.x] = p;
}
