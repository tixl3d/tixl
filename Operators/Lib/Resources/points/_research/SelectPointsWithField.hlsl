#include "shared/hash-functions.hlsl"
// #include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    float Strength;
    float2 GainAndBias;
    float Scatter;

    float2 FieldValueRange;
}

cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer Params : register(b2)
{
    int SelectMode;
    int ClampResult;
    int DiscardNonSelected;
    int StrengthFactor;

    int WriteTo;
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

    float s = GetDistance(pos);

    s = (s - FieldValueRange.x) / (FieldValueRange.y - FieldValueRange.x);

    float scatter = Scatter * (hash11u(i.x) - 0.5);

    s = ApplyGainAndBias(s, GainAndBias);

    float w = WriteTo == 0
                  ? 1
              : (WriteTo == 1) ? p.FX1
                               : p.FX2;

    float strength = Strength * (StrengthFactor == 0
                                     ? 1
                                 : (StrengthFactor == 1) ? p.FX1
                                                         : p.FX2);

    if (SelectMode == ModeOverride)
    {
        s *= strength;
    }
    else if (SelectMode == ModeAdd)
    {
        s += w * strength;
    }
    else if (SelectMode == ModeSub)
    {
        s = w - s * strength;
    }
    else if (SelectMode == ModeMultiply)
    {
        s = lerp(w, w * s, strength);
    }
    else if (SelectMode == ModeInvert)
    {
        s = s * (1 - w);
    }

    float result = (DiscardNonSelected && s <= 0)
                       ? NAN
                   : (ClampResult)
                       ? saturate(s)
                       : s;

    switch (WriteTo)
    {
    case 1:
        p.FX1 = result;
        break;
    case 2:
        p.FX2 = result;
        break;
    }
    // p.Selected = result;
    //  if (SetW)
    //  {
    //      p.W = result;
    //  }
    ResultPoints[i.x] = p;
}
