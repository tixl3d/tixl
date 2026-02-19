#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformVolume;
    float FallOff;
    float VolumeShape;
    float SelectMode;
    float ClampResult;
    float Strength;
    float Phase;
    float Threshold;
    float UseVertexSelection;
}

StructuredBuffer<PbrVertex> SourceVertices : t0;
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

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourceVertices.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
    {
        return;
    }

    ResultVertices[i.x] = SourceVertices[i.x];
    float3 posInObject = SourceVertices[i.x].Position;

    float3 posInVolume = mul(float4(posInObject, 1), TransformVolume).xyz;

    float s = 1;

    if (VolumeShape < VolumeSphere)
    {
        float distance = length(posInVolume);
        s = smoothstep(1 + FallOff, 1, distance);
    }
    else if (VolumeShape < VolumeBox)
    {
        float3 t = abs(posInVolume);
        float distance = max(max(t.x, t.y), t.z) + Phase;
        s = smoothstep(1 + FallOff, 1, distance);
    }
    else if (VolumeShape < VolumePlane)
    {
        float distance = posInVolume.y;
        s = smoothstep(FallOff, 0, distance);
    }
    else if (VolumeShape < VolumeZebra)
    {
        float distance = 1 - abs(mod(posInVolume.y * 1 + Phase, 2) - 1);
        s = smoothstep(Threshold + 0.5 + FallOff, Threshold + 0.5, distance);
    }
    else if (VolumeShape < VolumeNoise)
    {
        float3 noiseLookup = (posInVolume * 0.91 + Phase);
        float noise = snoise(noiseLookup);
        s = smoothstep(Threshold + FallOff, Threshold, noise);
    }

    if (SelectMode < ModeOverride)
    {
        s *= Strength;
    }
    else if (SelectMode < ModeAdd)
    {
        s += SourceVertices[i.x].Selected * Strength;
    }
    else if (SelectMode < ModeSub)
    {
        s = SourceVertices[i.x].Selected - s * Strength;
    }
    else if (SelectMode < ModeMultiply)
    {
        s = lerp(SourceVertices[i.x].Selected, SourceVertices[i.x].Selected * s, Strength);
    }
    else if (SelectMode < ModeInvert)
    {
        s = s * (1 - SourceVertices[i.x].Selected);
    }

    ResultVertices[i.x].Selected = ClampResult < 0.5 ? s : saturate(s);
}
