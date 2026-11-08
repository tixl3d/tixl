// Recovery is driven by a per-point age rather than a per-frame lerp, so it is
// frame-rate independent and can be shaped with gain/bias.

#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

#define ATTR_POSITION 1
#define ATTR_ROTATION 2
#define ATTR_SCALE 4
#define ATTR_FX1 8
#define ATTR_FX2 16
#define ATTR_COLOR 32

cbuffer FloatParams : register(b0)
{
    float4 Color;

    float3 Position;
    float Strength;

    float3 RotationAxis;
    float RotationAngle;

    float3 Scale;
    float Fx1;

    float Fx2;
    float Duration;
    float2 GainAndBias;

    float DeltaTime;
    float Scatter;
    float Step;
    float __padding;
}

cbuffer IntParams : register(b1)
{
    int SetMask;
    int PositionMode;   // 0 = Offset, 1 = Replace
    int StrengthFactor; // 0 = None, 1 = F1, 2 = F2
    int DecayMode;      // 0 = Curve (Duration + GainAndBias), 1 = HalfLife

    int StartIndex;
    int Count;
    int Seed;
    int Reset;
}

struct FlashState
{
    float Age;
    uint Mask;
    float2 __padding;
};

StructuredBuffer<Point> SourcePoints : register(t0);

RWStructuredBuffer<Point> ResultPoints : register(u0);
RWStructuredBuffer<Point> FlashedPoints : register(u1);
RWStructuredBuffer<FlashState> States : register(u2);

static const float InactiveAge = 1e9;

inline int imod2(int val, int repeat)
{
    int x = val % repeat;
    return x + (x < 0) * repeat;
}

// Weight of the flashed attributes: 1 right after a flash, 0 when recovered.
float FlashWeight(float age)
{
    float d = max(Duration, 0.0001);

    if (DecayMode == 1)
    {
        float w = exp2(-age / d);
        return w < 0.001 ? 0 : w;
    }

    return 1 - ApplyGainAndBias(saturate(age / d), GainAndBias);
}

Point BlendBack(Point src, Point flashed, uint mask, float w)
{
    Point p = src;

    if (mask & ATTR_POSITION)
        p.Position = lerp(src.Position, flashed.Position, w);

    if (mask & ATTR_ROTATION)
        p.Rotation = qSlerp(src.Rotation, flashed.Rotation, w);

    if (mask & ATTR_SCALE)
        p.Scale = lerp(src.Scale, flashed.Scale, w);

    if (mask & ATTR_FX1)
        p.FX1 = lerp(src.FX1, flashed.FX1, w);

    if (mask & ATTR_FX2)
        p.FX2 = lerp(src.FX2, flashed.FX2, w);

    if (mask & ATTR_COLOR)
        p.Color = lerp(src.Color, flashed.Color, w);

    return p;
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);

    uint index = i.x;
    if (index >= pointCount)
        return;

    Point src = SourcePoints[index];

    FlashState s = States[index];
    if (Reset > 0 || isnan(s.Age))
    {
        s.Age = InactiveAge;
        s.Mask = 0;
        s.__padding = float2(0, 0);
        States[index] = s;
        FlashedPoints[index] = src;
        ResultPoints[index] = src;
        return;
    }

    if (s.Mask == 0 || IsSeparator(src))
    {
        ResultPoints[index] = src;
        return;
    }

    s.Age += DeltaTime;
    float w = FlashWeight(s.Age);

    if (w <= 0)
    {
        // Fully recovered -> stop blending
        s.Age = InactiveAge;
        s.Mask = 0;
        States[index] = s;
        ResultPoints[index] = src;
        return;
    }

    States[index] = s;
    ResultPoints[index] = BlendBack(src, FlashedPoints[index], s.Mask, w);
}

// Runs after main on trigger frames, so ResultPoints already holds this frame's state.
[numthreads(64, 1, 1)] void flash(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= (uint)Count)
        return;

    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);
    if (pointCount == 0)
        return;

    int scatterOffset = Scatter > 0.001
                            ? (int)((float)pointCount * Scatter * hash11u(i.x + Seed * pointCount + StartIndex))
                            : 0;

    uint index = (uint)imod2(StartIndex + (int)(i.x * Step) + scatterOffset, (int)pointCount);

    Point src = SourcePoints[index];
    if (IsSeparator(src))
        return;

    float strength = Strength * (StrengthFactor == 0 ? 1 : (StrengthFactor == 1 ? src.FX1 : src.FX2));

    // Start from the visible state so re-flashing a recovering point doesn't jump.
    Point current = ResultPoints[index];
    Point f = current;

    if (SetMask & ATTR_POSITION)
    {
        f.Position = PositionMode == 0
                         ? current.Position + Position * strength
                         : lerp(current.Position, Position, strength);
    }

    if (SetMask & ATTR_ROTATION)
    {
        float4 delta = qFromAngleAxis(RotationAngle / 180 * PI, RotationAxis);
        f.Rotation = qSlerp(current.Rotation, qMul(current.Rotation, delta), strength);
    }

    if (SetMask & ATTR_SCALE)
        f.Scale = lerp(current.Scale, current.Scale * Scale, strength);

    if (SetMask & ATTR_FX1)
        f.FX1 = lerp(current.FX1, Fx1, strength);

    if (SetMask & ATTR_FX2)
        f.FX2 = lerp(current.FX2, Fx2, strength);

    if (SetMask & ATTR_COLOR)
        f.Color = lerp(current.Color, Color, strength);

    // Attributes still recovering from an earlier flash restart with this one.
    FlashState s = States[index];
    s.Mask = (s.Mask | (uint)SetMask);
    s.Age = 0;

    States[index] = s;
    FlashedPoints[index] = f;
    ResultPoints[index] = f;
}
