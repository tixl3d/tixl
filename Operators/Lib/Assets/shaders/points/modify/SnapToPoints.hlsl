#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"


cbuffer Params : register(b0)
{
    float BlendFactor;
    float Distance;
    float MaxAmount;
}


StructuredBuffer<LegacyPoint> Points1 : t0;         // input
StructuredBuffer<LegacyPoint> Points2 : t1;         // input
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;    // output

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    LegacyPoint A = Points1[i.x];
    LegacyPoint SnapPoint = Points2[i.x];
    float distance = length(A.Position - SnapPoint.Position);
    float blendFactor = smoothstep( BlendFactor + Distance, Distance  , distance ) * MaxAmount;

    ResultPoints[i.x].Position =  lerp(A.Position, SnapPoint.Position, blendFactor);
    ResultPoints[i.x].W = lerp(A.W, SnapPoint.W, BlendFactor);
}

