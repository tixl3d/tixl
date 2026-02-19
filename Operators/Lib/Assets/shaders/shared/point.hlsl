// Points are particles share the same structure and stride,
// But some attributes change their meaning:
//   W -> Radius
//   Stretch -> Velocity
//   Selected -> BirthTime
#ifndef __POINT__
#define __POINT__

struct LegacyPoint
{
    float3 Position;
    float W;
    float4 Rotation;
    float4 Color;
    float3 Stretch;
    float Selected;
};

struct Point
{
    float3 Position;
    float FX1;
    float4 Rotation;
    float4 Color;
    float3 Scale;
    float FX2;
};

struct Particle
{
    float3 Position;
    float Radius;
    float4 Rotation;
    float4 Color;
    float3 Velocity;
    float BirthTime;
};
#endif
