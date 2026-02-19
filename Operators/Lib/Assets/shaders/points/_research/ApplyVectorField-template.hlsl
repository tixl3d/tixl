#include "shared/hash-functions.hlsl"
// #include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer Params : register(b1)
{
    float Strength;
    float3 UpVector;

    float ClampLength;
    float ScaleLength;
}

cbuffer Params : register(b2)
{
    int Normalize;
    int StrengthFactor;
    int SetFx1To;
    int SetFx2To;
}

StructuredBuffer<Point> SourcePoints : t0;
RWStructuredBuffer<Point> ResultPoints : u0;
sampler ClampedSampler : register(s0);

//=== Additional Resources ==========================================
/*{RESOURCES(t1)}*/

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

inline float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}

//===================================================================

static const float NoisePhase = 0;

#define ModeOverride 0
#define ModeAdd 1
#define ModeSub 2
#define ModeMultiply 3
#define ModeInvert 4

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
        return;

    Point p = SourcePoints[i.x];

    if (isnan(p.Scale.x))
    {
        ResultPoints[i.x] = p;
        return;
    }

    float3 pos = p.Position;
    float4 f = GetField(float4(pos, 0));

    float3 d = f.xyz;

    float l = length(d) + 0.00001;
    d /= l;

    if (Normalize > 0)
    {
        l = 1;
    }

    if (ClampLength >= 0)
    {
        l = min(l, ClampLength);
    }
    l *= ScaleLength;

    float strength = Strength * (StrengthFactor == 0
                                     ? 1
                                 : (StrengthFactor == 1) ? p.FX1
                                                         : p.FX2);

    float4 r = qLookAt(d, UpVector);
    p.Rotation = qSlerp(p.Rotation, r, strength);

    float results[4];
    results[2] = f.w * ScaleLength;
    results[1] = l;

    results[0] = p.FX1;
    p.FX1 = lerp(p.FX1, results[SetFx1To], strength);

    results[0] = p.FX2;
    p.FX2 = lerp(p.FX1, results[SetFx2To], strength);

    ResultPoints[i.x] = p;
}
