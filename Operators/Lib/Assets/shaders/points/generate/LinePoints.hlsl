#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 Center;
    float LengthFactor;

    float3 Direction;
    float Pivot;

    // float W;
    // float WOffset;
    float OrientationAngle;
    float3 ManualOrientationAxis;

    float4 ColorA;
    float4 ColorB;

    float2 GainAndBias;
    float2 FX1;
    float2 FX2;

    float2 PointSize;
    float Twist;
}

cbuffer Params : register(b1)
{
    int AddSeparator;
    int OrientationMode;
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
    uint index = i.x;

    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);
    if (index >= pointCount)
        return;

    int seperatorOffset = AddSeparator ? 1 : 0;
    int steps = (pointCount - 1 - seperatorOffset);
    float f1 = ApplyGainAndBias(steps > 0 ? (float)(index) / steps : 0.5, GainAndBias);
    float f = f1 - Pivot;

    ResultPoints[index].Position = lerp(Center, Center + Direction * LengthFactor, f);

    // float f = (float)(index)/steps;
    // ResultPoints[index].W = W + WOffset * (float)(index)/steps;

    float4 rot2 = 0;
    if (OrientationMode < 0.5)
    {
        float4 rotate = qFromAngleAxis(3.141578 / 2 * 1, float3(0, 0, 1));

        rotate = qMul(rotate, qFromAngleAxis((OrientationAngle + Twist * f) / 180 * 3.141578, float3(0, 1, 0)));

        float3 upVector = float3(0, 0, 1);
        float t = abs(dot(normalize(Direction), normalize(upVector)));
        if (t > 0.999)
        {
            upVector = float3(0, 1, 0);
        }
        float4 lookAt = qLookAt(normalize(Direction), upVector);

        // rot2 = normalize(qMul(rotate, lookAt));
        rot2 = normalize(qMul(rotate, lookAt));
    }
    else
    {
        // FIXME: this rotation is hard to control and feels awkward.
        // I didn't come up with another method, though
        rot2 = normalize(qFromAngleAxis((OrientationAngle + Twist * f) / 180 * 3.141578, ManualOrientationAxis));
    }

    ResultPoints[index].Scale = (AddSeparator && index == pointCount - 1) ? sqrt(-1) : (PointSize.x + PointSize.y * f1);
    ResultPoints[index].Rotation = rot2;
    ResultPoints[index].Color = lerp(ColorA, ColorB, f1);
    ResultPoints[index].FX1 = FX1.x + FX1.y * f1;
    ResultPoints[index].FX2 = FX2.x + FX2.y * f1;
}
 