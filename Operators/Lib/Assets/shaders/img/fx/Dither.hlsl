#include "shared/bias-functions.hlsl"
#include "shared/blend-functions.hlsl"
#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Black;
    float4 White;
    float4 GrayScaleWeights;

    float2 GainAndBias;
    float Scale;
    float Method;

    float2 Offset;
    float BlendMode;

    float IsTextureValid;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);

#define Bayer4(a) (Bayer2(.5 * (a)) * .25 + Bayer2(a))
#define Bayer8(a) (Bayer4(.5 * (a)) * .25 + Bayer2(a))
#define Bayer16(a) (Bayer8(.5 * (a)) * .25 + Bayer2(a))
#define Bayer32(a) (Bayer16(.5 * (a)) * .25 + Bayer2(a))
#define Bayer64(a) (Bayer32(.5 * (a)) * .25 + Bayer2(a))

inline float Bayer2(float2 a)
{
    a = floor(a);
    return frac(a.x / 2. + a.y * a.y * .75);
}

#define mod(x, y) ((x) - (y) * floor((x) / (y)))

float4 psMain(vsOutput psInput) : SV_TARGET
{
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

    float4 color = Image.Sample(texSampler, cellTiles);
    float grayScale = ApplyGainAndBias(saturate(color), GainAndBias);
    float2 fragCoord = cellIds * res;

    float n = Method < 0.5
                  ? Bayer64(fragCoord / epsilonScale)
                  : hash11u((int)(fragCoord.x) * 21 + (int)(fragCoord.y) * 12112);

    float dithering = (n * 2.0 - 1.0) * 0.5;
    float blackOrWhite = dithering + grayScale < 0.5 ? 0 : 1;

    float4 c = lerp(Black, White, blackOrWhite);
    return (IsTextureValid < 0.5) ? c : BlendColors(color, c, (int)BlendMode);
}