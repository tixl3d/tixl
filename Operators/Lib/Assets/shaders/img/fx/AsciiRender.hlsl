#include "shared/hash-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer Resolution : register(b0)
{
    float TargetWidth;
    float TargetHeight;
}

cbuffer ParamConstants : register(b1)
{
    float4 Fill;
    float4 Background;
    float2 Offset;
    float2 FontCharSize;
    float ScaleFactor;
    float MaxInColors;
    float2 GainAndBias;

    float Scatter;
}

// cbuffer TimeConstants : register(b1)
// {
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
// }

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
Texture2D<float4> ImageB : register(t1);
Texture2D<float> FontSortingOrder : register(t2);

sampler texSampler : register(s0);

sampler texSamplerPoint : register(s1);

#define mod(x, y) ((x) - (y) * floor((x) / (y)))

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth / TargetHeight;
    float2 p = psInput.texCoord;
    p -= 0.5;
    float2 fontCharWidth = FontCharSize;
    float2 divisions = float2(TargetWidth / fontCharWidth.x, TargetHeight / fontCharWidth.y) / ScaleFactor;
    float2 fixOffset = Offset * float2(-1, 1) / divisions;
    p += fixOffset;

    float2 p1 = p;
    float2 gridSize = float2(1 / divisions.x, 1 / divisions.y);
    float2 pInCell = mod(p1, gridSize);
    float2 cellIds = (p1 - pInCell + 0.5);
    float2 cellTiles = cellIds - fixOffset;

    pInCell *= divisions;

    float4 colFromImageA = ImageA.Sample(texSampler, cellTiles);
    float grayScale = (colFromImageA.r + colFromImageA.g + colFromImageA.b) / 3;

    float dBiased = ApplyGainAndBias(grayScale, GainAndBias);

    // float cellId =
    float randomOffset = hash11u((uint)(cellIds.x * TargetWidth + 0.5) + (uint)(cellIds.y * TargetHeight + 0.5) * 73939133);
    // return float4(cellIds,randomOffset,1);
    dBiased += randomOffset * Scatter;
    dBiased = clamp(dBiased, 0.0001, 0.999); // Prevent spilling from white to black

    float4 letter = FontSortingOrder.SampleLevel(texSamplerPoint, float2(dBiased, 0.4), 0);
    // return float4(letter.x * 1, 0,0,1);

    float letterIndex = letter * 256;
    float rowIndex = floor(letterIndex / 16);
    float columnIndex = floor(letterIndex % 16);

    float2 letterPos = float2(columnIndex, rowIndex) / 16;
    float2 uv = pInCell / 16 + letterPos;

    float4 colorFromFont = ImageB.SampleLevel(texSamplerPoint, uv, 0);
    // colorFromFont.rgb *= 0.6;

    if (Background.a < 1)
    {
        float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
        return lerp(lerp(orgColor, Background, Background.a), Fill, colorFromFont.r);
    }

    return lerp(Background,
                lerp(1, colFromImageA, MaxInColors) * Fill,
                colorFromFont.r);
}