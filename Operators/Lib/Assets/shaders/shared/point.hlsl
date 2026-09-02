// Points and particles share the same structure and stride,
// but some attributes change their meaning:
//   FX1 -> Radius
//   Scale -> Velocity
//   FX2 -> BirthTime
#ifndef __POINT__
#define __POINT__

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
