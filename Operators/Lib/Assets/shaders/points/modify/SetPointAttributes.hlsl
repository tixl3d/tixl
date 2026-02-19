#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float SetPosition;
    float3 Position;

    float SetRotation;
    float3 RotationAxis;

    float RotationAngle;
    float2 __padding;
    float SetStretch;

    float3 Stretch;
    float __Padding2;

    float SetF1;
    float FX1;
    float SetF2;
    float FX2;

    float4 Color;

    float SetColor;
    float Amount;
}

cbuffer Params : register(b1)
{
    int AmountFactor;
}

StructuredBuffer<Point> SourcePoints : t0;

RWStructuredBuffer<Point> ResultPoints : u0;

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint index = (uint)i.x;
    uint pointCount, stride;

    SourcePoints.GetDimensions(pointCount, stride);
    if (index >= pointCount)
    {
        return;
    }

    Point p = SourcePoints[index];

    float strength = Amount * (AmountFactor == 0 ? 1 : (AmountFactor == 1 ? p.FX1 : p.FX2));

    if (SetColor > 0.5)
        p.Color = lerp(p.Color, Color, strength);

    if (SetPosition)
        p.Position = lerp(p.Position, Position, strength);

    if (SetStretch)
        p.Scale = lerp(p.Scale, Stretch, strength);

    if (SetF1)
        p.FX1 = lerp(p.FX1, FX1, strength);

    if (SetF2)
        p.FX2 = lerp(p.FX2, FX2, strength);

    if (SetRotation)
    {
        p.Rotation = qSlerp(p.Rotation, qFromAngleAxis(RotationAngle / 180 * PI, RotationAxis), strength);
    }

    ResultPoints[index] = p;
}
