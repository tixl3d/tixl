#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float TriggerClear;
    float SourceSlice;
}

cbuffer Params : register(b1)
{
    int Direction;
}

Texture2D<float4> SourceImage : register(t0);
RWTexture2D<float4> ResultImage : register(u0);

[numthreads(1, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    int sourceWidth, sourceHeight, numLevels;
    SourceImage.GetDimensions(0, sourceWidth, sourceHeight, numLevels);

    int resultWidth, resultHeight;
    ResultImage.GetDimensions(resultWidth, resultHeight);

    int index = i.x;
    if (Direction == 0)
    {
        if (index > resultWidth)
            return;

        // Scrolling down
        for (int y = resultHeight - 1; y > 0; y--)
        {
            ResultImage[int2(index, y)] = ResultImage[int2(index, y - 1)];
        }
        ResultImage[int2(index, 0)] = SourceImage[int2(index, sourceHeight * SourceSlice)];
    }
    else if (Direction == 1)
    {
        if (index > resultHeight)
            return;
        // Scrolling left
        for (int x = 0; x < resultWidth; x++)
        {
            ResultImage[int2(x - 1, index)] = ResultImage[int2(x, index)];
        }
        ResultImage[int2(resultWidth - 1, index)] = SourceImage[int2(sourceWidth * SourceSlice, index)];
    }
    else
    {
        if (index > resultHeight)
            return;

        // Scrolling right
        for (int x = resultWidth - 1; x > 0; x--)
        {
            ResultImage[int2(x, index)] = ResultImage[int2(x - 1, index)];
        }
        ResultImage[int2(0, index)] = SourceImage[int2(sourceWidth * SourceSlice, index)];
    }
}
