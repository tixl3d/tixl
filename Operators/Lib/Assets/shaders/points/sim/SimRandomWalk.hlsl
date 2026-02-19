#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float TriggerStep;
    float StepWidth;
    float TurnAngle;
    float StepRatio;

    float TurnRatio;
    float RandomStepWidth;
    float RandomRotateAngle;
    float Seed;

    float2 AreaEdgeRange;
    float2 AreaEdgeCenter;
    float Time; // For Random Seed

}


RWStructuredBuffer<LegacyPoint> ResultPoints : u0; 

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    if(TriggerStep < 0.5)
        return;

    float2 hash2 = hash22(float2(i.x, Time));
    if(hash2.x > StepRatio) 
        return;

    if(hash2.y < TurnRatio) {
        float turnDirection = hash2.y * 100 % 1 > 0.5 ? -1 : 1;

        float4 q = ResultPoints[i.x].Rotation;
        float randomAngle = (TurnAngle + hash2.x * RandomRotateAngle) * 3.141578 / 180 * turnDirection;
        
        ResultPoints[i.x].Rotation = qMul(q, qFromAngleAxis(randomAngle, float3(0,0,1)));
    }

    float3 forward = float3(StepWidth + RandomStepWidth * hash2.y, 0,0);
    float3 step = qRotateVec3(forward, ResultPoints[i.x].Rotation );

    ResultPoints[i.x].Position += step;
    ResultPoints[i.x].W += 0;
}

