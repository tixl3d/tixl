#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"

cbuffer ParamConstants : register(b0)
{
    float FXRedFor_Rotate;
    float FXGreenFor_FrontRadius;
    float FXBlueFor_SideAngle;
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
//Texture2D<float4> InputTexture : register(t0);
Texture2D<float4> FxTexture : register(t0);

RWStructuredBuffer<Breed> Breeds : register(u0); 
RWStructuredBuffer<LegacyPoint> Points : register(u1); 
RWTexture2D<float4> WriteOutput  : register(u2); 


static int2 block;
//static  int BlockCount =7;

static const float2 BlockCount = 1;

int2 CellAddressFromPosition(float3 pos) 
{
    float aspectRatio = (TargetHeight/BlockCount.y)/(TargetWidth/BlockCount.x);
    float2 gridPos = (pos.xy * float2(aspectRatio,-1) +1)  * float2(TargetWidth, TargetHeight)/2;
    int2 celAddress = mod(int2(gridPos.x , gridPos.y ) + 0.5, float2(TargetWidth, TargetHeight));
    celAddress/=BlockCount;
    celAddress += float2(TargetWidth, TargetHeight)/ BlockCount * block;
    return celAddress;
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
    uint AgentCount, stride;
    Points.GetDimensions(AgentCount, stride);

    if(i.x >= AgentCount)
        return;

    block = int2(i.x % BlockCount.x,  i.x / BlockCount.x % BlockCount.y);

    int texWidth;
    int texHeight;
    WriteOutput.GetDimensions(texWidth, texHeight);

    float3 pos = Points[i.x].Position;
    float angle = Points[i.x].Rotation.w;

    float hash =hash11(i.x * 123.1);

    int breedIndex = 0;//(i.x % 133 == 0) ? 1 : 0;

    float2 uv = CellAddressFromPosition(pos) / float2(texWidth, texHeight);
    float4 fxTexture = FxTexture.SampleLevel(texSampler, uv,0);
    float fxG = (fxTexture.g -0.5) * FXGreenFor_FrontRadius;
    //float fxA = (fxTexture.a -0.5) * FXGreenFor_FrontRadius;

    // Sample environment
    float3 frontSamplePos = pos + float3(sin(angle),cos(angle),0) * CB.FrontRadius / TargetHeight;//  + (fxTexture.g -0.5);// * FXGreenFor_FrontRadius;
    float4 frontSample = WriteOutput[CellAddressFromPosition(frontSamplePos)];
    float frontComfort= ComputeComfortZone(frontSample, CB.ComfortZones + fxG);

    float sideAngle = CB.SideAngle  - (fxTexture.b -0.5) * FXBlueFor_SideAngle;
    float3 leftSamplePos = pos + float3(sin(angle - sideAngle),cos(angle - sideAngle),0) * CB.SideRadius / TargetHeight;
    float4 leftSample = WriteOutput[CellAddressFromPosition(leftSamplePos)];
    float leftComfort= ComputeComfortZone(leftSample, CB.ComfortZones+fxG);

    float3 rightSamplePos = pos + float3(sin(angle + sideAngle),cos(angle + sideAngle),0) * CB.SideRadius / TargetHeight;
    float4 rightSample = WriteOutput[CellAddressFromPosition(rightSamplePos)];
    float rightComfort= ComputeComfortZone(rightSample, CB.ComfortZones+fxG);

    // float dir = -SoftLimit(( min(leftComfort.r, frontComfort.r ) -  min(rightComfort.r, frontComfort.r)), 1);

    float _rotateToComfort = CB.RotateToComfort + (float)(block.x - BlockCount/2) * 0.1 + (fxTexture.r -0.5) * FXRedFor_Rotate;

    float dir =   (frontComfort < min(leftComfort,  rightComfort))
                    ? 0
                    : leftComfort < rightComfort
                        ? -1
                        : 1;
    angle += dir * _rotateToComfort + CB.BaseRotation;
    angle = mod(angle, 2 * 3.141592);
    
    float _baseMove = CB.BaseMovement + ((float)block.y - BlockCount/2.) * 5;

    float move = clamp(((leftComfort + rightComfort)/2 - frontComfort),-1,1) * CB.MoveToComfort + _baseMove;
    pos += float3(sin(angle),cos(angle),0) * move / TargetHeight;
    Points[i.x].Rotation.w = angle;
    
    float3 aspectRatio = float3(TargetWidth / BlockCount.x /((float)TargetHeight / BlockCount.y),1,1);

    
    float3 newPos = (mod((pos  / aspectRatio + 1),2) - 1) * aspectRatio; 
    Points[i.x].W = length(newPos - pos) > 0.1 ? sqrt(-1) : 1;

    Points[i.x].Position = pos;
    //Points[i.x].rotation = rotate_angle_axis(-angle, float3(0,0,1));
    
    // Update map
    float2 gridPos = (pos.xy * float2(1,-1) +1)  * float2(texWidth, texHeight)/2;
    int2 celAddress = CellAddressFromPosition(pos);
    WriteOutput[celAddress] += CB.Emit;
}