#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 SourceExtend;
    float Range;

    float3 SourcePivot;
    float Offset;
    float Scale;
    float Spin;
    float Twist;
}

cbuffer Params : register(b1)
{
    int SourceAlignmentAxis;
    int RepeatMode;
}

StructuredBuffer<LegacyPoint> SourcePoints : t0;
StructuredBuffer<LegacyPoint> Points : t1;
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;

static float Fraction;
static float3 PosA;
static float3 PosB;
static float4x4 OrientationA;
static float4x4 OrientationB;

static const float3 axisScaleFactors[] = {float3(0, 1, 1), float3(1, 0, 1), float3(1, 1, 0)};

inline float3 TransformVector(float3 v)
{
    float3 v2 = v * axisScaleFactors[SourceAlignmentAxis] * Scale + lerp(PosA, PosB, Fraction);
    v2 = lerp(mul(float4(v2 - PosA, 1), OrientationA).xyz + PosA,
              mul(float4(v2 - PosB, 1), OrientationB).xyz + PosB,
              Fraction);
    return v2;
}

inline float3 TransformDirection(float3 v)
{
    return lerp(mul(float4(v, 0), OrientationA).xyz,
                mul(float4(v, 0), OrientationA).xyz,
                Fraction);
}

inline float4 TransformOrientation(float4 r)
{
    return qSlerp(mul(r, OrientationA),
                  mul(r, OrientationB),
                  Fraction);
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint vertexIndex = i.x;

    uint vertexCount, stride;
    SourcePoints.GetDimensions(vertexCount, stride);
    if (vertexIndex > vertexCount)
    {
        return;
    }

    uint pointCount;
    Points.GetDimensions(pointCount, stride);

    float weight = 1;
    float3 offset;

    LegacyPoint p = SourcePoints[vertexIndex];
    float3 pos = (p.Position + SourcePivot) / SourceExtend;
    float f = pos[SourceAlignmentAxis];

    f = f * Range * Scale + Offset + 0.5;
    if (RepeatMode == 1)
    {
        f = mod(f, 1);
    }
    else if (RepeatMode == 2)
    {
        f = saturate(f);
    }

    float floatIndex = f * pointCount + 0.00001;

    uint aIndex = (int)clamp(floatIndex, 0, pointCount - 2);
    uint bIndex = aIndex + 1;
    Fraction = floatIndex - aIndex;

    float4 rotA = Points[aIndex].Rotation;
    float4 rotB = Points[bIndex].Rotation;

    if (SourceAlignmentAxis == 0)
    {
        float4 rotY = qFromAngleAxis(3.1415 / 2, float3(0, 1, 0));
        rotA = qMul(rotA, rotY);
        rotB = qMul(rotB, rotY);
    }
    else if (SourceAlignmentAxis == 1)
    {
        float4 rotY = qFromAngleAxis(3.1415 / 2, float3(1, 0, 0));
        rotA = qMul(rotA, rotY);
        rotB = qMul(rotB, rotY);
    }

    OrientationA = transpose(qToMatrix(rotA));
    OrientationB = transpose(qToMatrix(rotB));
    PosA = Points[aIndex].Position;
    PosB = Points[bIndex].Position;

    p.Position = TransformVector(pos);
    float4 r = p.Rotation;
    p.Rotation = qSlerp(qMul(rotA, r), qMul(rotB, r), Fraction);
    ResultPoints[vertexIndex] = p;
}
