Texture2DMS<float, 4> txMSDepth : t0;
RWTexture2D<float> outputTexture : register(u0);

[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    outputTexture[i.xy] = txMSDepth.Load(i.xy, 0);
}