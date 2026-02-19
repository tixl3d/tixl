#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Params : register(b0)
{
    float Radius;
    float RadiusOffset;
    float StartAngle;
    float Cycles;

    float CloseCircle;
    float3 Center;

    float3 CenterOffset;
    float __padding1;

    float3 Axis;
    float1 OrientationAngle;

    float3 OrientationAxis;
    float __padding2;

    float2 GainAndBias;
    float2 PointScaleRange;

    float2 FX1;
    float2 FX2;

    float4 Color;
}

RWStructuredBuffer<Point> ResultPoints : u0; // output

float3 RotatePointAroundAxis(float3 In, float3 Axis, float Rotation)
{
    float s = sin(Rotation);
    float c = cos(Rotation);
    float one_minus_c = 1.0 - c;

    Axis = normalize(Axis);
    float3x3 rot_mat =
        {one_minus_c * Axis.x * Axis.x + c, one_minus_c * Axis.x * Axis.y - Axis.z * s, one_minus_c * Axis.z * Axis.x + Axis.y * s,
         one_minus_c * Axis.x * Axis.y + Axis.z * s, one_minus_c * Axis.y * Axis.y + c, one_minus_c * Axis.y * Axis.z - Axis.x * s,
         one_minus_c * Axis.z * Axis.x - Axis.y * s, one_minus_c * Axis.y * Axis.z + Axis.x * s, one_minus_c * Axis.z * Axis.z + c};
    return mul(rot_mat, In);
}

[numthreads(256, 4, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);

    if (i.x >= pointCount)
        return;

    uint index = i.x;
    bool closeCircle = CloseCircle > 0.5;
    float angleStepCount = closeCircle ? (pointCount - 2) : pointCount;

    float ff = (float)(index) / angleStepCount;
    float f = ApplyGainAndBias(ff, GainAndBias);

    float l = Radius + RadiusOffset * f;
    float angle = (StartAngle * 3.141578 / 180 + Cycles * 2 * 3.141578 * f);
    float3 up = Axis.y > 0.7 ? float3(0, 0, 1) : float3(0, 1, 0);
    float3 direction = normalize(cross(Axis, up));

    float3 v2 = RotatePointAroundAxis(direction * l, Axis, angle);

    float3 c = Center + CenterOffset * f;
    float3 v = v2 + c;

    ResultPoints[index].Position = v;
    ResultPoints[index].Scale = ((closeCircle && index == pointCount - 1)
                                     ? NAN
                                     : PointScaleRange.x + PointScaleRange.y * f);

    float4 orientation = qFromAngleAxis(3.141578 / 2 * 1, normalize(OrientationAxis));

    orientation = qMul(orientation, qFromAngleAxis((OrientationAngle) / 180 * 3.141578, float3(1, 0, 0)));

    float4 lookat = qLookAt(Axis, up);

    float4 quat = qMul(orientation, qFromAngleAxis(angle, normalize(Axis)));

    float4 spin = qFromAngleAxis((OrientationAngle) / 180 * 3.141578, normalize(OrientationAxis));
    float4 spin2 = qFromAngleAxis(angle, float3(Axis));

    ResultPoints[index].Rotation = qMul(normalize(qMul(spin2, lookat)), spin);
    ResultPoints[index].Color = Color;

    ResultPoints[index].FX1 = FX1.x + FX1.y * f;
    ResultPoints[index].FX2 = FX2.x + FX2.y * f;
}
