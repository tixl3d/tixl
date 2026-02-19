#include "shared/hash-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 GapColor;

    float SplitPosition;
    float SplitVariation;
    float SubdivisionThreshold;
    float RandomPhase;

    float Padding;
    float Feather;
    float2 ScrollOffset;

    float2 ScrollGainAndBias;
    float DirectionBias;

    float TextureFx;
    float IsTextureValid;
}

#define COLORMODE_DIVISIONS 0
#define COLORMODE_RANDOM 1

// cbuffer Time : register(b1)
// {
// }

cbuffer Resolution : register(b2)
{
    float TargetWidth;
    float TargetHeight;
}

cbuffer IntParams : register(b4)
{
    int UseApectForSplit;
    int UseRGSSMultiSampling;
    int MaxSubdivisions;
    int GradientMode;

    int ColorMode;
    int RandomSeed;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
Texture2D<float4> ImageB : register(t1);
sampler texSampler : register(s0);
sampler clampedSampler : register(s1);

#define fmod(x, y) ((x) - (y) * floor((x) / (y)))

static const float stepOffset = 0.25;

float PhaseHash(uint i)
{
    uint pointU = i * _PRIME0;
    float particlePhaseOffset = hash11u(pointU);
    float phase = abs(particlePhaseOffset + RandomPhase);

    int phaseIndex = (int)phase + pointU;

    float t = fmod(phase, 1);
    t = smoothstep(0, 1, t);

    return lerp(hash11u(phaseIndex),
                hash11u(phaseIndex + 1),
                t);
}

float4 ComputeSubdivision(float2 uv)
{
    int steps = (int)clamp(MaxSubdivisions, 1, 30);

    int mainSeed = RandomSeed;
    int step;

    float aspectRatio = TargetWidth / TargetHeight;

    float2 size = 1;
    float2 uvInCell = uv;
    // float hash2 = 0.5;
    float phaseHashForCell = (PhaseHash(mainSeed) - 0.5) * SplitVariation + SplitPosition;
    int seedInCell = RandomSeed;
    uint lastDirection = 0;

    [loop] for (step = 0; step < steps; ++step)
    {
        float aspect = UseApectForSplit == 1 ? size.x / size.y : 1;

        if (hash11u(seedInCell) * 2 + DirectionBias < aspect)
        {
            if (uvInCell.x < phaseHashForCell)
            {
                uvInCell.x /= phaseHashForCell;
                size.x *= phaseHashForCell;
                mainSeed += (int)(phaseHashForCell + 2123u);
                seedInCell *= 2;
            }
            else
            {
                uvInCell.x = (uvInCell.x - phaseHashForCell) / (1 - phaseHashForCell);
                size.x *= (1 - phaseHashForCell);
                mainSeed = (int)(mainSeed + 213u) % 1251u;
                seedInCell *= 3;
            }

            lastDirection = 0;
        }
        else
        {
            if (uvInCell.y < phaseHashForCell)
            {
                uvInCell.y /= phaseHashForCell;
                size.y *= phaseHashForCell;
                mainSeed = (int)(mainSeed + _PRIME2) % _PRIME1;
                seedInCell *= 5;
            }
            else
            {
                uvInCell.y = (uvInCell.y - phaseHashForCell) / (1 - phaseHashForCell);
                size.y *= (1 - phaseHashForCell);
                mainSeed = (int)(mainSeed + _PRIME1) % _PRIME2;
                seedInCell *= 7;
            }
            lastDirection = 1;
        }

        float hash = hash11u(seedInCell);
        uvInCell = fmod(uvInCell + ScrollOffset * float2(-1, 1) * ApplyGainAndBias(hash, ScrollGainAndBias), 1);

        phaseHashForCell = (PhaseHash(mainSeed) - 0.5) * SplitVariation + SplitPosition;

        float4 extra = Image.Sample(texSampler, uv - uvInCell * size + size / 2);
        float extraGray = (extra.r + extra.g + extra.b) / 3 * extra.a * TextureFx;

        if (hash <= SubdivisionThreshold - extraGray)
            break;
    }

    // float splitF = ColorMode == 0 ? hash11u(mainSeed) : step / (float)steps;
    float gradientF = GradientMode ? step / (float)steps : hash11u(mainSeed);
    float4 gradientColor = ImageB.SampleLevel(clampedSampler, float2(gradientF, 0.5), 0);

    float2 dd = (uvInCell - 0.5) * size;
    float2 d4 = (size - abs(dd * 2)) * float2(aspectRatio, 1);

    float d5 = min(d4.x, d4.y);
    float sGap = smoothstep(Padding - Feather, Padding + Feather, d5);

    float2 imageUv = uv - uvInCell * size + size / 2;

    float4 imageColor = lerp(Image.Sample(texSampler, imageUv), 1, ColorMode == 1) * gradientColor;

    return lerp(GapColor, imageColor, sGap);
}

float4 psMain(vsOutput input) : SV_TARGET
{
    // return float4(ColorMode, 0, 0, 1);
    //  float width, height;
    //  Image.GetDimensions(width, height);
    //  float imageAspect = width/height;

    float2 uv = input.texCoord;

    if (UseRGSSMultiSampling > 0.5)
    {
        // 4x rotated grid
        float4 offsets[2];
        offsets[0] = float4(-0.375, 0.125, 0.125, 0.375);
        offsets[1] = float4(0.375, -0.125, -0.125, -0.375);

        float2 sxy = float2(TargetWidth, TargetHeight);

        return (ComputeSubdivision(uv + offsets[0].xy / sxy) +
                ComputeSubdivision(uv + offsets[0].zw / sxy) +
                ComputeSubdivision(uv + offsets[1].xy / sxy) +
                ComputeSubdivision(uv + offsets[1].zw / sxy)) /
               4;
    }
    else
    {
        return ComputeSubdivision(uv);
    }
}