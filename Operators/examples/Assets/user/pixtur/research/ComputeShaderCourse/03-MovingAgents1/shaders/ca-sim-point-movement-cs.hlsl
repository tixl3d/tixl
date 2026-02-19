#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"

cbuffer ParamConstants : register(b0)
{
    float Spin;
    float TrailEnergy;

    float SampleDistance;
    float SampleAngle;

    float MoveDistance;
    float RotateAngle;

    float RerferenceEnergy;

    float AgentCount;
    float SnapToAnglesCounts;
    float SnapToAnglesRatio;

    float BRatio;
    float BTrail;
    float BMoveDistance;
    float BRotate;
}


cbuffer ResolutionBuffer : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

#define mod(x,y) ((x)-(y)*floor((x)/(y)))

sampler texSampler : register(s0);
Texture2D<float4> InputTexture : register(t0);

RWStructuredBuffer<LegacyPoint> Points : register(u0); 
//RWTexture2D<float4> WriteOutput  : register(u1); 

int2 CellAddressFromPosition(float3 pos) 
{
    float aspectRatio = TargetHeight/TargetWidth;
    float2 gridPos = (pos.xy * float2(aspectRatio,-1) +1)  * float2(TargetWidth, TargetHeight)/2;
    int2 celAddress = int2(gridPos.x, gridPos.y);
    return celAddress;
}

float2 GetUvFromPosition(float3 pos) 
{
    float aspectRatio = TargetHeight/TargetWidth;
    float2 gridPos = (pos.xy * float2(aspectRatio,-1) +1)/2;
    return  float2(gridPos.x, gridPos.y);
}

// Rounds an input value i to steps values
// See: https://www.desmos.com/calculator/qpvxjwnsmu
float RoundValue(float i, float stepsPerUnit, float stepRatio) 
{
    float u = 1 / stepsPerUnit;
    float v = stepRatio / (2 * stepsPerUnit);
    float m = i % u;
    float r = m - (m < v
                    ? 0
                    : m > (u - v)
                        ? u
                        : (m - v) / (1 - 2 * stepsPerUnit * v));
    float y = i - r;
    return y;
}

static const float ToRad = 3.141592/180;

[numthreads(256,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{   
    if(i.x >= AgentCount)
        return;

    //int texWidth;
    //int texHeight;
    //WriteOutput.GetDimensions(texWidth, texHeight);

    float3 pos = Points[i.x].position;
    float angle = Points[i.x].w;

    float hash =hash11(i.x * 123.1);

    float3 leftSamplePos = pos + float3(sin(angle - SampleAngle*ToRad),cos(angle - SampleAngle*ToRad),0) * SampleDistance / TargetHeight;
    //int2 leftSampleAddress = CellAddressFromPosition(leftSamplePos);
    //float4 leftSample = WriteOutput[leftSampleAddress];
    float2 leftSampleUv = GetUvFromPosition(leftSamplePos);
    float4 leftSample = InputTexture.SampleLevel(texSampler, leftSampleUv,0);

    float3 rightSamplePos = pos + float3(sin(angle + SampleAngle*ToRad),cos(angle + SampleAngle*ToRad),0) * SampleDistance / TargetHeight;
    //int2 rightSampleAddress = CellAddressFromPosition(rightSamplePos);
    //float4 rightSample = WriteOutput[rightSampleAddress] ;
    float2 rightSampleUv = GetUvFromPosition(rightSamplePos);
    float4 rightSample = InputTexture.SampleLevel(texSampler, rightSampleUv,0);

    float leftValue = pow(abs(leftSample.r - RerferenceEnergy),1);
    float rightValue = pow(abs(rightSample.r - RerferenceEnergy),1);
    float rotateDirection = leftValue  - rightSample.r;
    //float rotateDirection = leftValue  - rightValue;//+Spin;
    

    float rotate = hash >= BRatio ? RotateAngle : BRotate;
    //angle += rotateDirection * rotate*ToRad;
    angle +=rotateDirection * rotate*ToRad;
    angle = mod(angle, 2 * 3.141592);
    
    float roundedAngle = RoundValue(angle, SnapToAnglesCounts/(2*3.141592), SnapToAnglesRatio)+ Spin * ToRad;

    //float moveSpeedVariation = 1+(hash11(i.x * 123.1) - 0.5) * 0;

    float moveDistance = hash >= BRatio ? MoveDistance : BMoveDistance;
    pos += float3(sin(roundedAngle),cos(roundedAngle),0) * moveDistance / TargetHeight;
    Points[i.x].w = roundedAngle;
    
    float3 aspectRatio = float3(TargetWidth/(float)TargetHeight,1,1);
    pos = (mod((pos  / aspectRatio + 1),2) - 1) * aspectRatio; 
    Points[i.x].position = pos;
    Points[i.x].rotation = rotate_angle_axis(-roundedAngle, float3(0,0,1));
    
    // Update map
    //float2 gridPos = (pos.xy * float2(1,-1) +1)  * float2(texWidth, texHeight)/2;
    //int2 celAddress = int2(gridPos.x, gridPos.y);
    //int2 celAddress = CellAddressFromPosition(pos);
    //float trail = hash > BRatio ? TrailEnergy : BTrail;
    //WriteOutput[celAddress] += trail / pow(AgentCount,0.5);
}

// [numthreads(256,1,1)]
// void savePosition(uint3 i : SV_DispatchThreadID)
// {   
//     //float hash =hash11(i.x * 123.1);
//     //int2 celAddress = CellAddressFromPosition(Points[i.x].position);
//     //float trail = hash > BRatio ? TrailEnergy : BTrail;
//     //WriteOutput[celAddress] += trail / pow(AgentCount,0.5);
//     WriteOutput[i.xy] = float4(1,1,0,1);
// }