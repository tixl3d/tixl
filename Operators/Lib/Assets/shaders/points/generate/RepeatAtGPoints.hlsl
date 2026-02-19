#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float Scale;
}

cbuffer Params : register(b1)
{
    int ApplyTargetOrientation;
    int ApplyTargetScale;
    int ScaleFactorMode;
    int SetF1To;
    int SetF2To;

    int ConnectPointsMode;
    int AddSeperators;
}

#define Mode_None 1
#define Mode_Target_F1 2
#define Mode_Target_F2 3
#define Mode_Source_F1 4
#define Mode_Source_F2 5
#define Mode_Multiplied_F1 6
#define Mode_Multiplied_F2 7


StructuredBuffer<Point> SourcePoints : t0;   // input
StructuredBuffer<Point> TargetPoints : t1;   // input
RWStructuredBuffer<Point> ResultPoints : u0; // output

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint resultPointCount, sourcePointCount, targetPointCount, stride;

    SourcePoints.GetDimensions(sourcePointCount, stride);
    TargetPoints.GetDimensions(targetPointCount, stride);
    ResultPoints.GetDimensions(resultPointCount, stride);

    if (i.x >= resultPointCount)
    {
        return;
    }

    bool isSeperator = false;
    uint sourceIndex;
    uint targetIndex;

    if (ConnectPointsMode == 0)
    {        
        uint sourceLength = sourcePointCount + AddSeperators;
        sourceIndex = i.x % (sourceLength);
        targetIndex = (i.x / sourceLength) % targetPointCount;
        isSeperator = AddSeperators && sourceIndex == sourcePointCount;
    }
    else
    {
        uint loopLength = targetPointCount;
        sourceIndex = i.x / loopLength;
        targetIndex = i.x % loopLength;
        isSeperator = targetIndex == loopLength - 1;
    }

    if (isSeperator)
    {
        ResultPoints[i.x].Position = 0;
        ResultPoints[i.x].Scale = NAN;
        return;
    }

    Point sourceP = SourcePoints[sourceIndex];
    Point targetP = TargetPoints[targetIndex];
    float4 sourceRot = normalize(sourceP.Rotation);
    float4 targetRot = normalize(targetP.Rotation);

    float3 pLocal = ApplyTargetOrientation
                        ? qRotateVec3(sourceP.Position, targetRot)
                        : sourceP.Position;

    float factors[7] = {1,targetP.FX1, targetP.FX2,  sourceP.FX1, sourceP.FX2, sourceP.FX1 * targetP.FX1, sourceP.FX2 * targetP.FX2};
    float3 s = Scale * factors[ScaleFactorMode] * (ApplyTargetScale ? targetP.Scale : 1);

    ResultPoints[i.x].Position = pLocal * s + targetP.Position;
    ResultPoints[i.x].Rotation = ApplyTargetOrientation > 0.5 ? qMul(targetRot, sourceRot) : sourceRot;
    ResultPoints[i.x].Color = SourcePoints[sourceIndex].Color * TargetPoints[targetIndex].Color;
    ResultPoints[i.x].FX1 = factors[SetF1To];
    ResultPoints[i.x].FX2 = factors[SetF2To];
    ResultPoints[i.x].Scale = SourcePoints[sourceIndex].Scale * TargetPoints[targetIndex].Scale;    
}
