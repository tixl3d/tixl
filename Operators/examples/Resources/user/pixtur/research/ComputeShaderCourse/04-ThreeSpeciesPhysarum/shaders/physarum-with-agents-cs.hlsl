#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    //float AgentCount;
    float2 BlockCount;
    float AngleLockSteps;
    float AngleLockFactor;
}

cbuffer ResolutionBuffer : register(b1)
{
    float TargetWidth;
    float TargetHeight;
};

struct Breed
{
    float4 ComfortZones;
    float4 Emit;

    float SideAngle;
    float SideRadius;
    float FrontRadius;
    float BaseMovement;

    float BaseRotation;
    float MoveToComfort;
    float RotateToComfort;
    float _padding;
};

// struct Agent {
//     float2 Position;
//     float Breed;
//     float Rotation;
//     float SpriteOrientation;
// };



#define mod(x,y) ((x)-(y)*floor((x)/(y)))

sampler texSampler : register(s0);
Texture2D<float4> InputTexture : register(t0);

RWStructuredBuffer<Breed> Breeds : register(u0); 
RWStructuredBuffer<Point> Points : register(u1); 
RWTexture2D<float4> WriteOutput  : register(u2); 


static int2 block;
//static  int BlockCount =7;


int2 CellAddressFromPosition(float3 pos) 
{
    float aspectRatio = (TargetHeight/BlockCount.y)/(TargetWidth/BlockCount.x);
    float2 gridPos = (pos.xy * float2(aspectRatio,-1) +1)  * float2(TargetWidth, TargetHeight)/2;
    int2 celAddress = mod(int2(gridPos.x , gridPos.y ) + 0.5, float2(TargetWidth, TargetHeight));
    celAddress/=BlockCount;
    celAddress += float2(TargetWidth, TargetHeight)/ BlockCount * block;
    return celAddress;
}

// float2 GetUvFromPosition(float3 pos) 
// {
//     float aspectRatio = TargetHeight/TargetWidth;
//     float2 gridPos = (pos.xy * float2(aspectRatio,-1) +1)/2;
//     return  float2(gridPos.x, gridPos.y);
// }

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

#define CB Breeds[breedIndex]

float SoftLimit(float v, float limit) 
{
    return v < 0
     ? (1 + 1 / (-v-1)) * limit
     : -(1 + 1 / (v-1)) * limit;
}


// See https://www.desmos.com/calculator/dvknudqwxt
float ComputeComfortZone(float4 x, float4 cz) 
{
    //return x;
    float4 v=(max(abs(x-cz)-0, 0) * 1);
    v *= v;
    return (v.r + v.g + v.b)/2;
}


[numthreads(256,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{   
    uint agentCount, stride;
    Points.GetDimensions(agentCount, stride);

    if(i.x >= agentCount)
        return;

    block = int2(i.x % BlockCount.x,  i.x / BlockCount.x % BlockCount.y);

    int texWidth;
    int texHeight;
    WriteOutput.GetDimensions(texWidth, texHeight);

    float3 pos = Points[i.x].Position;
    float angle = Points[i.x].FX1;

    float hash =hash11(i.x * 123.1);

    int breedIndex = (i.x % 133 == 0) ? 1 : 0;

    // Sample environment
    float3 frontSamplePos = pos + float3(sin(angle),cos(angle),0) * CB.FrontRadius / TargetHeight;
    float4 frontSample = WriteOutput[CellAddressFromPosition(frontSamplePos)];
    float frontComfort= ComputeComfortZone(frontSample, CB.ComfortZones);

    float3 leftSamplePos = pos + float3(sin(angle - CB.SideAngle),cos(angle - CB.SideAngle),0) * CB.SideRadius / TargetHeight;
    float4 leftSample = WriteOutput[CellAddressFromPosition(leftSamplePos)];
    float leftComfort= ComputeComfortZone(leftSample, CB.ComfortZones);

    float3 rightSamplePos = pos + float3(sin(angle + CB.SideAngle),cos(angle + CB.SideAngle),0) * CB.SideRadius / TargetHeight;
    float4 rightSample = WriteOutput[CellAddressFromPosition(rightSamplePos)];
    float rightComfort= ComputeComfortZone(rightSample, CB.ComfortZones);

    // float dir = -SoftLimit(( min(leftComfort.r, frontComfort.r ) -  min(rightComfort.r, frontComfort.r)), 1);

    float _rotateToComfort = CB.RotateToComfort + (float)(block.x - (BlockCount-1)/2) * 0.1 ;

    float dir =   (frontComfort < min(leftComfort,  rightComfort))
                    ? 0
                    : leftComfort < rightComfort
                        ? -1
                        : 1;
    angle += dir * _rotateToComfort + CB.BaseRotation;
    angle = mod(angle, 2 * 3.141592);
    angle = RoundValue(angle / (2* 3.1416), AngleLockSteps, AngleLockFactor) * 2 * 3.141578;
    
    float _baseMove = CB.BaseMovement + ((float)block.y - (BlockCount-1)/2.) * 5;

    float move = clamp(((leftComfort + rightComfort)/2 - frontComfort),-1,1) * CB.MoveToComfort + _baseMove;
    pos += float3(sin(angle),cos(angle),0) * move / TargetHeight;
    Points[i.x].FX2 = angle;
    
    float3 aspectRatio = float3(TargetWidth / BlockCount.x /((float)TargetHeight / BlockCount.y),1,1);
    pos = (mod((pos  / aspectRatio + 1),2) - 1) * aspectRatio; 
    Points[i.x].Position = pos;
    Points[i.x].Rotation = qFromAngleAxis(-angle, float3(0,0,1));
    
    // Update map
    float2 gridPos = (pos.xy * float2(1,-1) +1)  * float2(texWidth, texHeight)/2;
    int2 celAddress = CellAddressFromPosition(pos);
    WriteOutput[celAddress] += CB.Emit;
}