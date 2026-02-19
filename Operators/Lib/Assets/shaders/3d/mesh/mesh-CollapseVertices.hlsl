#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformVolume;
    float Amount;
    float FallOff;
    float Strength;
    float Phase;

    float3 GridOffset;
    float NoiseThreshold;

    float BlendStep;
}

cbuffer Params : register(b1)
{
    uint Count;
    int VolumeShape;
    int StepCount;
}

#ifndef fmod
#define fmod(x, y) ((x) - (y) * floor((x) / (y)))
#endif

#define linearstep(a, b, f) saturate(((f) - (a)) / ((b) - (a)))

StructuredBuffer<PbrVertex> SourceVertices : t0;
StructuredBuffer<uint3> FaceIndices : t1;
RWStructuredBuffer<PbrVertex> ResultVertices : u0;

static const float NoisePhase = 0;

static const float VolumeSphere = 0.5;
static const float VolumeBox = 1.5;
static const float VolumePlane = 2.5;
static const float VolumeZebra = 3.5;
static const float VolumeNoise = 4.5;

static const float ModeOverride = 0.5;
static const float ModeAdd = 1.5;
static const float ModeSub = 2.5;
static const float ModeMultiply = 3.5;
static const float ModeInvert = 4.5;

float GetFieldAtPosition(float3 posInObject)
{
    float3 posInVolume = mul(float4(posInObject, 1), TransformVolume).xyz;
    float s = 1;

    switch (VolumeShape)
    {
    case VolumeSphere:
        float distance = length(posInVolume);
        s = linearstep(1 + FallOff, 1, distance);
        break;

    case VolumeBox:
    {

        float3 t = abs(posInVolume);
        float distance = max(max(t.x, t.y), t.z) + Phase;
        s = linearstep(1 + FallOff, 1, distance);
        break;
    }
    case VolumePlane:
    {

        float distance = posInVolume.y;
        s = linearstep(FallOff, 0, distance);
        break;
    }
    case VolumeZebra:
    {

        float distance = 1 - abs(mod(posInVolume.y * 1 + Phase, 2) - 1);
        s = linearstep(NoiseThreshold + 0.5 + FallOff, NoiseThreshold + 0.5, distance);
        break;
    }

    case VolumeNoise:
    {

        float3 noiseLookup = (posInVolume * 0.91 + Phase);
        float noise = snoise(noiseLookup);
        s = linearstep(NoiseThreshold + FallOff, NoiseThreshold, noise);
        break;
    }
    }
    return s;
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= Count)
        return;

    // Vertex mode
    {

        PbrVertex v = SourceVertices[i.x];
        float3 pos = SourceVertices[i.x].Position;

        float s = GetFieldAtPosition(pos);

        float xx = s * StepCount;
        uint step = uint(xx);
        float ff = xx - step;

        if (s > 0.1 / StepCount)
        {
            float maxS = 1 << StepCount;
            float ss1 = (1 << step) / maxS * Strength;
            float3 snap1 = floor((pos - GridOffset) / ss1 + 0.5) * ss1;

            float ss2 = (1 << (step + 1)) / maxS * Strength;
            float3 snap2 = floor((pos - GridOffset) / ss2 + 0.5) * ss2;

            v.Position = lerp(v.Position, lerp(snap1, snap2, smoothstep(0, 1, ff * BlendStep)) + GridOffset, Amount);
        }

        ResultVertices[i.x] = v;
    }
}
