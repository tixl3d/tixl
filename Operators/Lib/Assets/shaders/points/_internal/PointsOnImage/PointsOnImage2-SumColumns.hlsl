cbuffer Params : register(b0)
{
    int ImageWidth;
    int ImageHeight;
}

RWTexture2D<float> Sums : register(u0);

groupshared float MidSums[4096];

[numthreads(1, 1, 1)] void SumColumns(uint3 threadID : SV_DispatchThreadID)
{
    float sum = 0;
    int RowSumIndex = ImageWidth;
    int ColumnSumIndex = ImageHeight;

    for (uint y = 0; y < ImageHeight; ++y)
    {
        MidSums[y] = Sums[uint2(RowSumIndex, y)];
    }

    // Not needed with single thread group
    // AllMemoryBarrierWithGroupSync();

    for (y = 0; y < ImageHeight; ++y)
    {
        sum += MidSums[y];
    }

    float sumReciproc = (sum == 0.0f) ? 0 : 1.0f / sum;
    float normalizedSum = 0;

    // Now sum up, scale by overall sum and store
    for (y = 0; y < ImageHeight; ++y)
    {
        normalizedSum += MidSums[y] * sumReciproc;
        Sums[int2(RowSumIndex, y)].r = normalizedSum;
    }
}
