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

// A point with NaN Scale.x is a separator: it is never drawn, and line-style
// draws (lines, tubes, ribbons) break their strip at it. This is the only
// NaN convention for points — all other fields are plain values.
inline bool IsSeparator(Point p)
{
    return isnan(p.Scale.x);
}
#endif
