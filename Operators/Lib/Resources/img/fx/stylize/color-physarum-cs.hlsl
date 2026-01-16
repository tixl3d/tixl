#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Emit;        // Base Emission
    float4 ColorPickUp; // From Image at position
    float4 ComfortZones;

    float BaseMovement;
    float BaseRotation;
    float SideAngle;
    float SideRadius;

    float FrontRadius;
    float MoveToComfort;
    float RotateToComfort;
    float AngleLockSteps;

    float AngleLockFactor;
}

cbuffer IntParams : register(b1)
{
    int TargetWidth;
    int TargetHeight;
};

#define mod(x, y) ((x) - (y) * floor((x) / (y)))

sampler texSampler : register(s0);
Texture2D<float4> InputTexture : register(t0);
Texture2D<float4> DiffisionRead : register(t1);

RWStructuredBuffer<Point> Points : register(u0);
RWTexture2D<float4> Diffision : register(u1);

// Rounds an input value i to steps values
// See: https://www.desmos.com/calculator/qpvxjwnsmu
float RoundValue(float i, float stepsPerUnit, float stepRatio)
{
    float u = 1 / stepsPerUnit;
    float v = stepRatio / (2 * stepsPerUnit);
    float m = i % u;
    float r = m - (m<v
                           ? 0
                           : m>(u - v)
                       ? u
                       : (m - v) / (1 - 2 * stepsPerUnit * v));
    float y = i - r;
    return y;
}

static const float ToRad = 3.141592 / 180;

#define CB Breeds[breedIndex]

float SoftLimit(float v, float limit)
{
    return v < 0
               ? (1 + 1 / (-v - 1)) * limit
               : -(1 + 1 / (v - 1)) * limit;
}

// See https://www.desmos.com/calculator/dvknudqwxt
float ComputeComfortZone(float4 x, float4 cz)
{
    // return x;
    float4 v = (max(abs(x - cz) - 0, 0) * 1);
    v *= v;
    return (v.r + v.g + v.b) / 2;
}

inline int2 AddressFromPos(float2 p)
{
    p.y *= -1;
    float aspect = TargetWidth / (float)TargetHeight;
    p += float2(aspect, 1);
    return p * 0.5 * TargetHeight;
}

[numthreads(256, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint agentCount, stride;
    Points.GetDimensions(agentCount, stride);

    if (i.x >= agentCount)
        return;

    // Points[i.x].Position.x += 0.001 + BaseMovement;
    // return;

    Point p = Points[i.x];

    float4 rot = p.Rotation;

    float2 pos = p.Position.xy;

    // float angle = p.FX1;
    float angle = 2 * -atan2(rot.z, rot.w);

    float hash = hash11u(i.x);
    // float hash = hash11(i.x * 123.1);
    // int breedIndex = (i.x % 133 == 0) ? 1 : 0;

    // Sample environment
    float2 frontSamplePos = pos + float2(sin(angle), cos(angle)) * FrontRadius / TargetHeight;
    float4 frontSample = Diffision[AddressFromPos(frontSamplePos)];

    float frontComfort = ComputeComfortZone(frontSample, ComfortZones);

    float2 leftSamplePos = pos + float2(sin(angle - SideAngle), cos(angle - SideAngle)) * SideRadius / TargetHeight;
    float4 leftSample = Diffision[AddressFromPos(leftSamplePos)];
    float leftComfort = ComputeComfortZone(leftSample, ComfortZones);

    float2 rightSamplePos = pos + float2(sin(angle + SideAngle), cos(angle + SideAngle)) * SideRadius / TargetHeight;
    float4 rightSample = Diffision[AddressFromPos(rightSamplePos)];
    float rightComfort = ComputeComfortZone(rightSample, ComfortZones);

    // float dir = -SoftLimit(( min(leftComfort.r, frontComfort.r ) -  min(rightComfort.r, frontComfort.r)), 1);

    // float _rotateToComfort = RotateToComfort + (float)(block.x - (BlockCount - 1) / 2) * 0.1;

    float dir = (frontComfort < min(leftComfort, rightComfort))
                    ? 0
                : leftComfort < rightComfort
                    ? -1
                    : 1;

    angle += dir * RotateToComfort + BaseRotation;

    // angle = mod(angle - 3.141592, 2 * 3.141592) - 3.141592;

    // angle = RoundValue(angle / (2 * 3.1416), AngleLockSteps, AngleLockFactor) * 2 * 3.141578;

    float move = clamp(((leftComfort + rightComfort) / 2 - frontComfort), -1, 1) * MoveToComfort + BaseMovement;

    pos += float2(sin(angle), cos(angle)) * move / TargetHeight;

    // Points[i.x].Color = DiffisionRead.SampleLevel(texSampler, pos, 0);

    // Points[.]

    float aspectRatio = TargetWidth / (float)TargetHeight;
    pos = (mod((pos / aspectRatio + 1), 2) - 1) * aspectRatio; // NOT SURE if this really works

    // pos += 0.01;

    Points[i.x].Position = float3(pos.xy, p.Position.z);
    Points[i.x].Rotation = qFromAngleAxis(-angle, float3(0, 0, 1)); // TODO: Check if this is flipped

    // Update map
    // float2 gridPos = (pos.xy * float2(1, -1) + 1) * float2(texWidth, texHeight) / 2;
    int2 celAddress = AddressFromPos(rightSamplePos);
    Diffision[celAddress] += Emit;
}