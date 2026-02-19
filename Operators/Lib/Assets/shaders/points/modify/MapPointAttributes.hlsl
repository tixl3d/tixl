#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float Strength;
    float Range;
    float Phase;
}

cbuffer Params : register(b1)
{
    int InputMode;
    int MappingMode;
    int ApplyMode;
    int WriteTo;
    int WriteColor;
    int StrengthFactor;
}

StructuredBuffer<Point> SourcePoints : t0;
Texture2D<float4> CurveImage : register(t1);
Texture2D<float4> GradientImage : register(t2);

RWStructuredBuffer<Point> ResultPoints : u0;
sampler ClampedSampler : register(s0);

inline float3 fmod(float3 x, float3 y)
{
    return (x - y * floor(x / y));
}

#define INPUTMODE_BUFFERORDER 0
#define INPUTMODE_F1 1
#define INPUTMODE_F2 2
#define INPUTMODE_RANDOM 3

#define SPREADMODE_BUFFER 0
#define SPREADMODE_W 1
#define SPREADMODE_SELECTION 2

#define MAPPING_NORMAL 0
#define MAPPING_FORSTART 1
#define MAPPING_PINGPONG 2
#define MAPPING_REPEAT 3
#define MAPPING_USEORIGINALW 4

#define APPLYMODE_REPLACE 0
#define APPLYMODE_MULTIPLY 1
#define APPLYMODE_ADD 2

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    int index = i.x;
    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);
    if (index >= pointCount)
    {
        return;
    }

    Point p = SourcePoints[index];

    float strength = Strength * (StrengthFactor == 0
                                     ? 1
                                 : (StrengthFactor == 1) ? p.FX1
                                                         : p.FX2);

    float f0 = 0;
    switch (InputMode)
    {
    case INPUTMODE_BUFFERORDER:
        f0 = (float)index / (pointCount - 1);
        break;

    case INPUTMODE_F1:
        f0 = p.FX1;
        break;
    case INPUTMODE_F2:
        f0 = p.FX2;
        break;
    case INPUTMODE_RANDOM:
        f0 = hash11u(index);
        break;
    }

    if (isnan(f0))
    {
        ResultPoints[index] = p;
        return;
    }

    float f = 0;
    switch (MappingMode)
    {
    case MAPPING_NORMAL:
        f = (f0 - 0.5) / Range + 0.5 - Phase / Range;
        break;
    case MAPPING_FORSTART:
        f = f0 / Range - Phase;
        break;

    case MAPPING_PINGPONG:
        f = fmod((f0 * 2 - 1 - 2 * Phase * Range) / Range, 2);
        f += -1;
        f = abs(f);
        break;

    case MAPPING_REPEAT:
        f = f0 / Range - 0.5 - Phase;
        f = fmod(f, 1);
        break;
    }

    // if (MappingMode == MAPPING_NORMAL)
    // {
    // }
    // // What does this even mean?!
    // else if (MappingMode == MAPPING_FORSTART)
    // {
    //     f = f0 / Range - Phase;
    // }
    // else if (MappingMode == MAPPING_PINGPONG)
    // {
    //     f = fmod((f0 * 2 - 1 - 2 * Phase * Range) / Range, 2);
    //     f += -1;
    //     f = abs(f);
    // }
    // else if (MappingMode == MAPPING_REPEAT)
    // {
    //     f = f0 / Range - 0.5 - Phase;
    //     f = fmod(f, 1);
    // }
    // else
    // {
    //     f = p.W;
    // }

    if (WriteTo != 0)
    {
        float curveValue = CurveImage.SampleLevel(ClampedSampler, float2(f, 0.5), 0).r;

        float org = 1;
        switch (WriteTo)
        {
        case 1:
            org = p.FX1;
            break;
        case 2:
            org = p.FX1;
            break;
        }

        float newValue = 0;
        if (ApplyMode == APPLYMODE_REPLACE)
        {
            newValue = curveValue;
        }
        else if (ApplyMode == APPLYMODE_MULTIPLY)
        {
            newValue = org * curveValue;
        }
        else if (ApplyMode == APPLYMODE_ADD)
        {
            newValue = org + curveValue;
        }

        switch (WriteTo)
        {
        case 1:
            p.FX1 = lerp(org, newValue, strength);
            break;
        case 2:
            p.FX2 = lerp(org, newValue, strength);
            break;
        case 3:
            p.Scale = lerp(org, p.Scale * newValue, strength);
            break;
        }
    }

    float4 gradientColor = GradientImage.SampleLevel(ClampedSampler, float2(f, 0.5), 0);
    switch (WriteColor)
    {
    case 1:
        p.Color = lerp(p.Color, gradientColor, strength);
        break;
    case 2:
        p.Color = lerp(p.Color, p.Color * gradientColor, strength);
        break;
    }

    ResultPoints[index] = p;
}
