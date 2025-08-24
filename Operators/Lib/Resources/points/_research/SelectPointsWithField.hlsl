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

    float Center;
    float Range;
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
    int MappingMode;
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

#define SPREADMODE_BUFFER 0
#define SPREADMODE_W 1
#define SPREADMODE_SELECTION 2

#define MAPPING_CENTERED 0
#define MAPPING_FORSTART 1
#define MAPPING_PINGPONG 2
#define MAPPING_REPEAT 3
#define MAPPING_USEORIGINALW 4

inline float fmod(float x, float y)
{
    return (x - y * floor(x / y));
}

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
    float3 d = mod(pos, 1);

    float scatter = Scatter * (hash11u(i.x) - 0.5);

    float f0 = GetDistance(pos) + scatter * Range;

    float f = 0;
    switch (MappingMode)
    {
    case MAPPING_CENTERED:
        f = (f0 + Range / 2) / Range - Center / (Range * 0.5f);
        break;
    case MAPPING_FORSTART:
        f = f0 / Range - Center;
        break;

    case MAPPING_PINGPONG:
        f = fmod((2 * f0 - 2 * Center * Range - 1) / Range, 2);
        f += -1;
        f = abs(f);
        break;

    case MAPPING_REPEAT:
        f = f0 / Range - 0.5 - Center;
        f = fmod(f, 1);
        break;
    }

    // s = (s - FieldValueRange.x) / (FieldValueRange.y - FieldValueRange.x);

    f = 1 - ApplyGainAndBias(f, GainAndBias);

    float org = WriteTo == 0
                    ? 1
                : (WriteTo == 1) ? p.FX1
                                 : p.FX2;

    if (SelectMode == ModeOverride)
    {
        // s *= strength;
    }
    else if (SelectMode == ModeAdd)
    {
        f += org; // * strength;
    }
    else if (SelectMode == ModeSub)
    {
        f = org - f;
    }
    else if (SelectMode == ModeMultiply)
    {
        f *= org;
    }
    else if (SelectMode == ModeInvert)
    {
        f = 1 - f * org;
    }

    // float result = (DiscardNonSelected && s <= 0)
    //                    ? NAN
    //                : (ClampResult)
    //                    ? saturate(s)
    //                    : s;

    float strength = Strength * ((StrengthFactor == 0
                                      ? 1
                                  : (StrengthFactor == 1) ? p.FX1
                                                          : p.FX2));

    float result = lerp(org, f, strength);

    result = lerp(result, saturate(result), ClampResult);

    switch (WriteTo)
    {
    case 1:
        p.FX1 = result;
        break;
    case 2:
        p.FX2 = result;
        break;
    }

    ResultPoints[i.x] = p;
}
