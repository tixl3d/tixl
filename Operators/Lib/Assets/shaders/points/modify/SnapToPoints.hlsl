#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"


cbuffer Params : register(b0)
{
    float BlendFactor;
    float Distance;
    float MaxAmount;
}


StructuredBuffer<Point> Points1 : register(t0);         // input
StructuredBuffer<Point> Points2 : register(t1);         // input
RWStructuredBuffer<Point> ResultPoints : register(u0);    // output

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    Point A = Points1[i.x];
    Point SnapPoint = Points2[i.x];
    float distance = length(A.Position - SnapPoint.Position);
    float blendFactor = smoothstep( BlendFactor + Distance, Distance  , distance ) * MaxAmount;

    ResultPoints[i.x].Position =  lerp(A.Position, SnapPoint.Position, blendFactor);
    ResultPoints[i.x].FX1 = lerp(A.FX1, SnapPoint.FX1, BlendFactor);
}

