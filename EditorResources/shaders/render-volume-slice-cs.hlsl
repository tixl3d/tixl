Texture3D<float4> volumeTexture : t0;
RWTexture2D<float4> outputTexture : register(u0);

cbuffer Params : register(b0)
{
    int SliceZ;
}


[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    outputTexture[i.xy] = volumeTexture.Load(int4(i.xy, SliceZ, 0));
}

