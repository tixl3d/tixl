#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Transforms : register(b0)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
}

cbuffer Params : register(b1)
{
    float4x4 TransformVolume;
    float FallOff;
    float Strength;
    float2 GainAndBias;
    float Phase;
    float Threshold;
    float Scatter;
}

cbuffer Params : register(b2)
{
    int VolumeShape;
    int SelectMode;
    int ClampResult;
    int DiscardNonSelected;

    int StrengthFactor;
    int WriteTo;
}

StructuredBuffer<Point> SourcePoints : t0;
RWStructuredBuffer<Point> ResultPoints : u0;

static const float NoisePhase = 0;

#define VolumeSphere 0
#define VolumeBox 1
#define VolumePlane 2
#define VolumeZebra 3
#define VolumeNoise 4

#define ModeOverride 0
#define ModeAdd 1
#define ModeSub 2
#define ModeMultiply 3
#define ModeInvert 4

float Bias2(float x, float bias)
{
    return bias < 0
               ? pow(x, clamp(bias + 1, 0.005, 1))
               : 1 - pow(1 - x, clamp(1 - bias, 0.005, 1));
}

inline float LinearStep(float min, float max, float t)
{
    return saturate((t - min) / (max - min));
}

[numthreads(64, 1, 1)] void main(uint3 i
                                 : SV_DispatchThreadID)
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

    float3 posInObject = p.Position;
    float3 posInVolume = mul(float4(posInObject, 1), TransformVolume).xyz;

    float s = 1;
    float scatter = Scatter * (hash11u(i.x) - 0.5);

    if (VolumeShape == VolumeSphere)
    {
        float distance = length(posInVolume) + scatter;
        s = LinearStep(1 + FallOff, 1, distance);
    }
    else if (VolumeShape == VolumeBox)
    {
        float3 t = abs(posInVolume);
        float distance = max(max(t.x, t.y), t.z) + Phase + scatter;
        s = LinearStep(1 + FallOff, 1, distance);
    }
    else if (VolumeShape == VolumePlane)
    {
        float distance = posInVolume.y + scatter;
        s = LinearStep(FallOff, 0, distance);
    }
    else if (VolumeShape == VolumeZebra)
    {
        float distance = 1 - abs(mod(posInVolume.y * 1 + Phase, 2) - 1) + scatter;
        s = LinearStep(Threshold + 0.5 + FallOff, Threshold + 0.5, distance);
    }
    else if (VolumeShape == VolumeNoise)
    {
        float3 noiseLookup = (posInVolume * 0.91 + Phase);
        float noise = snoise(noiseLookup);
        s = LinearStep(Threshold + FallOff, Threshold, noise + scatter);
    }

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
