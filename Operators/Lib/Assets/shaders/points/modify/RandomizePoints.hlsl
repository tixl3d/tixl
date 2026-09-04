#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"
#include "shared/color-functions.hlsl"

cbuffer Params : register(b0)
{
    float Strength;
    float3 RandomizePosition;

    float3 RandomizeRotation;
    float RandomizeF1;

    float4 RandomizeColor;

    float3 Stretch;
    float RandomSeed;

    float2 GainAndBias;
    float Scale;
    float RandomizeF2;
}

cbuffer IntParams : register(b1)
{
    uint OffsetMode;
    uint UsePointSpace;
    uint Interpolation;
    int ClampColorsEtc;

    int Repeat;
    int StrengthFactor;
}

StructuredBuffer<Point> SourcePoints : register(t0);
RWStructuredBuffer<Point> ResultPoints : register(u0);


[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);
    Point p = SourcePoints[i.x];

    uint pointId = i.x;
    uint pointU = pointId * _PRIME0 % (Repeat == 0 ? 999999999 : Repeat);
    float particlePhaseOffset = hash11u(pointU);

    float phase = abs(particlePhaseOffset + RandomSeed);

    int phaseIndex = (uint)phase + pointU;

    float t = fmod(phase, 1);
    t = Interpolation == 0 ? 0 : (Interpolation == 1 ? t : smoothstep(0, 1, t));
    float4 biasedA = ApplyGainAndBias(lerp(hash41u(phaseIndex), hash41u(phaseIndex + 1), t), GainAndBias);
    float4 biasedB = ApplyGainAndBias(lerp(hash41u(phaseIndex + _PRIME0), hash41u(phaseIndex + _PRIME0 + 1), t), GainAndBias);

    float strength = Strength * (StrengthFactor == 0
                                     ? 1
                                 : (StrengthFactor == 1) ? p.FX1
                                                         : p.FX2);

    float4 rot = p.Rotation;

    if (OffsetMode == 1)
    {
        biasedA = (biasedA * 2) - 1;
        biasedB = (biasedB * 2) - 1;
        // biasedB -= OffsetMode * 0.5;
    }

    p.Position += strength * (UsePointSpace == 0
                                  ? qRotateVec3(biasedA.xyz * RandomizePosition, p.Rotation)
                                  : biasedA.xyz * RandomizePosition);

    float4 rgba = p.Color;
    if (length(RandomizeColor) > 0.001)
    {
        float4 HSBa = float4(rgb2hsb(p.Color.rgb), p.Color.a);
        HSBa += biasedB * RandomizeColor * strength;
        HSBa.x = fmod(HSBa.x, 1);
        rgba = float4(hsb2rgb(HSBa.xyz), HSBa.a);
    }

    p.Color = ClampColorsEtc
                  ? clamp(rgba, 0, float4(1000, 1000, 1000, 1))
                  : rgba;

    p.FX1 += biasedA.w * RandomizeF1 * strength;
    p.FX2 += biasedA.r * RandomizeF2 * strength;

    if (ClampColorsEtc && !IsSeparator(p))
    {
        p.FX1 = max(0, p.FX1);
        p.FX2 = max(0, p.FX2);
    }
    p.Scale += ((float3(biasedB.w, biasedA.w, biasedA.z) * Stretch) + biasedA.r * Scale) * strength; // Not ideal... distribution overlap

    // Rotation
    float3 randomRotate = (RandomizeRotation / 180 * PI) * strength * biasedA.xyz;
    rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.x, float3(1, 0, 0))));
    rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.y, float3(0, 1, 0))));
    rot = normalize(qMul(rot, qFromAngleAxis(randomRotate.z, float3(0, 0, 1))));
    p.Rotation = rot;

    ResultPoints[i.x] = p;
}
