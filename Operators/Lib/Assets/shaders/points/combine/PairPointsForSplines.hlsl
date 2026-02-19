#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 TangentDirection;

    float InitWTo01;
    float SegmentCount;

    float TangentA;
    float TangentA_WFactor;
    float TangentB;
    float TangentB_WFactor;

    float Debug;
}

StructuredBuffer<Point> PointsA : t0;        // input
StructuredBuffer<Point> PointsB : t1;        // input
RWStructuredBuffer<Point> ResultPoints : u0; // output

inline float3 Interpolate(float t, float3 pA, float3 tA, float3 tB, float3 pB)
{
    float t2 = t * t;
    float t3 = t2 * t;

    return (2 * t3 - 3 * t2 + 1) * pA +
           (t3 - 2 * t2 + t) * tA +
           (-2 * t3 + 3 * t2) * pB +
           (t3 - t2) * -tB;
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint resultCount, countA, countB, stride;
    ResultPoints.GetDimensions(resultCount, stride);
    PointsA.GetDimensions(countA, stride);
    PointsB.GetDimensions(countB, stride);

    int segmentCount = (int)(SegmentCount + 0.5);

    int pointsPerSegment = segmentCount;

    uint pairIndex = i.x / pointsPerSegment;
    uint indexInLine = i.x % pointsPerSegment;
    float f = (float)indexInLine / (float)(pointsPerSegment - 2);

    if (indexInLine == pointsPerSegment - 1)
    {
        ResultPoints[i.x].Scale = NAN; // NaN for divider
        return;
    }

    uint indexA = pairIndex % countA;
    uint indexB = pairIndex % countB;

    float3 posA1 = PointsA[indexA].Position;
    float3 posB1 = PointsB[indexB].Position;
    float3 forward = TangentDirection;

    float paW = PointsA[indexA].FX1;
    float pbW = PointsB[indexB].FX1;

    float3 tA = qRotateVec3(forward, PointsA[indexA].Rotation) * (TangentA + paW * TangentA_WFactor);
    float3 tB = qRotateVec3(forward, PointsB[indexB].Rotation) * (TangentB + pbW * TangentB_WFactor);

    float3 pF = Interpolate(f, posA1, tA, tB, posB1);
    ResultPoints[i.x].Position = pF;
    ResultPoints[i.x].Color = lerp(PointsA[indexA].Color, PointsB[indexB].Color, f);
    ResultPoints[i.x].Scale = lerp(PointsA[indexA].Scale, PointsB[indexB].Scale, f);

    float3 pF2 = Interpolate(f + 0.001, posA1, tA, tB, posB1); // Adjusted increment for finite difference
    float3 forward2 = normalize(pF2 - pF);
    float3 up = float3(0, 0, 1);
    float fade = 1 - abs(dot(up, forward2));
    float3 refUp = lerp(tA, tB, f);
    ResultPoints[i.x].Rotation = qLookAt(forward2, refUp);

    // Ugly fix for the last point's orientation, but it works
    int fixPpoint = segmentCount - 2;
    ResultPoints[fixPpoint.x].Rotation *= float4(1, -1, -1, 1);

    // Handle NaN values correctly

    ResultPoints[i.x].FX1 = InitWTo01 > 0.5 ? f : lerp(PointsA[indexA].FX1, PointsB[indexB].FX2, f);
    ResultPoints[i.x].FX2 = 1;
}
