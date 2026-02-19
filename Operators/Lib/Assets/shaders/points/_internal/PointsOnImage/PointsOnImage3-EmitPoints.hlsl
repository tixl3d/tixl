#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/hash-functions.hlsl"

cbuffer Params : register(b0)
{
    int ImageWidth;
    int ImageHeight;
    int Seed;
    int ApplyColor;
}

cbuffer Params : register(b1)
{
    float2 GainAndBias;
    float ScatterWithinPixel;
}

Texture2D<float> CDF : register(t0);
Texture2D<float4> Image : register(t1);
RWStructuredBuffer<Point> ResultPoints : u0;

sampler texSampler : register(s0);

[numthreads(256, 1, 1)] void GeneratePoints(uint3 threadID : SV_DispatchThreadID)
{
    int RowSumIndex = ImageWidth;
    int ColumnSumIndex = ImageHeight;

    float2 probability = float2(
        hash11u(((threadID.x + 1) + Seed) * _PRIME0),
        hash11u(((threadID.x + 2) + _PRIME2 * Seed) * _PRIME1));

    // probability.y = 0.01;

    // use probability.x to find pos in cdf column
    uint index = 0;
    uint left = 0;
    uint right = ImageHeight;
    uint steps = log2(ImageHeight) + 1;
    for (uint j = 0; j < steps; ++j)
    {
        uint middle = left + (right - left) / 2;
        float cdfSegStart = CDF[uint2(RowSumIndex, middle)];
        float cdfSegEnd = CDF[uint2(RowSumIndex, middle + 1)];
        if (!((probability.y >= cdfSegStart) && (probability.y <= cdfSegEnd)))
        {
            if (probability.y < cdfSegStart)
            {
                right = middle;
            }
            else
            {
                left = middle;
            }
        }
        else
        {
            index = middle + 1;
        }
    }
    uint rowIndex = index;

    // Now search cdf row for x index
    left = 0;
    right = ImageWidth;
    steps = log2(ImageWidth) + 1;
    for (uint i = 0; i < steps; ++i)
    {
        uint middle = left + (right - left) / 2;
        index = middle;
        float cdfSegStart = CDF[uint2(middle, rowIndex)];
        float cdfSegEnd = CDF[uint2(middle + 1, rowIndex)];
        if (!((probability.x >= cdfSegStart) && (probability.x <= cdfSegEnd)))
        {
            if (probability.x < cdfSegStart)
            {
                right = middle;
            }
            else
            {
                left = middle + 1;
            }
        }
        else
        {
            index = middle + 1;
        }
    }
    uint columnIndex = index;

    float2 samplePosInUV = float2(float(columnIndex + 0.5) / (ImageWidth),
                                  (1 - float(rowIndex + 0.5) / (ImageHeight)));

    float aspectRatio = (float)ImageHeight / ImageWidth;
    ResultPoints[threadID.x].Color = ApplyColor ? Image.SampleLevel(texSampler, samplePosInUV * float2(1, -1) + float2(0, 1), 0) : 1;

    float2 scatter = float2(hash11u(threadID.x), hash11u((threadID.x * 13) * _PRIME0));
    float2 posXY = (samplePosInUV - 0.5) * 2 * float2(1 / aspectRatio, 1) + (scatter - 0.5) * ScatterWithinPixel * 2 / float2(ImageWidth * aspectRatio, ImageHeight);
    ResultPoints[threadID.x].Position = float3(posXY, 0);

    ResultPoints[threadID.x].FX1 = 1;
    ResultPoints[threadID.x].FX1 = 1;
    ResultPoints[threadID.x].Rotation = float4(0, 0, 0, 1);

    ResultPoints[threadID.x].Scale = 1;
}
