RWTexture2D<float> summedRows : register(u0);

[numthreads(32, 32, 1)] void ClearSumBuffer(uint3 threadID : SV_DispatchThreadID)
{
    summedRows[threadID.xy] = 0.0;
}
