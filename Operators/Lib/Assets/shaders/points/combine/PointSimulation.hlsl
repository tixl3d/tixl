#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

StructuredBuffer<LegacyPoint> SourcePoints : t0;         // input
RWStructuredBuffer<LegacyPoint> ResultPoints : u0; 



cbuffer Params : register(b0)
{
    float MixOriginal;
    float Reset;
};


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    if(Reset > 0.5) {
        ResultPoints[i.x] = SourcePoints[i.x];
        return;
    }

    uint sourcePointcount, stride;
    SourcePoints.GetDimensions(sourcePointcount, stride);
    
    if(i.x >= sourcePointcount) 
        return;

    float currentW = ResultPoints[i.x].W;
    float orgW = SourcePoints[i.x].W;

    if(isnan(orgW) || isnan(currentW) || isnan(ResultPoints[i.x].Position.x)) 
    {
        ResultPoints[i.x] = SourcePoints[i.x];
        return;
    }

    ResultPoints[i.x].W = lerp( currentW, orgW, MixOriginal );

    ResultPoints[i.x].Position = lerp(ResultPoints[i.x].Position,  SourcePoints[i.x].Position, MixOriginal);
    ResultPoints[i.x].Color = lerp(ResultPoints[i.x].Color,  SourcePoints[i.x].Color, MixOriginal);
    ResultPoints[i.x].Stretch = lerp(ResultPoints[i.x].Stretch,  SourcePoints[i.x].Stretch, MixOriginal);
    ResultPoints[i.x].Selected = lerp(ResultPoints[i.x].Selected,  SourcePoints[i.x].Selected, MixOriginal);
    ResultPoints[i.x].Rotation = qSlerp(ResultPoints[i.x].Rotation,  SourcePoints[i.x].Rotation, MixOriginal);

}