#include "shared/bias-functions.hlsl"
#include "shared/blend-functions.hlsl"
#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Black;
    float4 White;
    float4 GrayScaleWeights;

    float2 GainAndBias;
    float2 Stretch;

    float2 Offset;
    float Scale;
    float IterationFx;

    float IsTextureValid; // Added by _ImageFxShaderSetup
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

cbuffer Params : register(b2)
{
    int Method;
    int Iteration;
    int BlendMode;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);

#define mod(x, y) ((x) - (y) * floor((x) / (y)))

float4 psMain(vsOutput psInput) : SV_TARGET
{
    // float2 h = hash41(psInput.texCoord.x);
    // return float4(h.xy, 0, 1);
    float aspectRatio = TargetWidth / TargetHeight;
    float2 p = psInput.texCoord;
    p -= 0.5;

    int round = 1;
    int2 res = int2((int)TargetWidth / round, (int)TargetHeight / round) * round;

    // This will prevent repetitive artifacts in the pattern
    float epsilonScale = Scale - 0.0001f;

    float2 divisions = res / epsilonScale;
    float2 fixOffset = Offset * float2(-1, 1) / divisions;
    p += fixOffset;

    float2 p1 = p;
    float2 gridSize = float2(1 / divisions.x, 1 / divisions.y);
    float2 pInCell = mod(p1, gridSize);
    float2 cellIds = (p1 - pInCell + 0.5);

    float2 cellTiles = cellIds - fixOffset;
    pInCell *= divisions;

    float4 colorForCell = Image.Sample(texSampler, cellTiles) * GrayScaleWeights;

    float grayScale = (colorForCell.r + colorForCell.g + colorForCell.b) / 3;
    float biased = ApplyGainAndBias(grayScale, GainAndBias);

    float fxIterations = Iteration + IterationFx * biased;

    int F = (int)(fxIterations + 0.5);
    int X = (int)(cellIds.x * divisions.x / Stretch.x + 0.5); //  int(@P.x*float(chi("factorX")));
    int Y = (int)(cellIds.y * divisions.y / Stretch.y + 0.5); // int(@P.z*float(chi("factorY")));

    int method = abs(Method) % 5;

    int blackOrWhite = 0;
    if (method == 0)
    {
        blackOrWhite = !((X ^ Y) & F); // classic munching
    }
    else if (method == 1)
    {
        blackOrWhite = !(X & F ^ Y & F);
    }
    else if (method == 2)
    {
        blackOrWhite = !((X | Y) & F);
    }
    else if (method == 3)
    {
        blackOrWhite = !((X * Y) & F);
    }
    else if (method == 4)
    {
        blackOrWhite = !(((X ^ (Y << 1)) | (Y ^ (X << 1))) & F);
    }

    float4 c = lerp(Black, White, blackOrWhite);
    return (IsTextureValid < 0.5) ? c : BlendColors(Image.Sample(texSampler, psInput.texCoord), c, (int)BlendMode);
}