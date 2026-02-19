Texture2D<float4> SourceImage : register(t0);
RWTexture2D<float4> Result : register(u0);

[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    int x = i.x;
    int y = i.y;
    Result[i.xy] = SourceImage[ int2(x,y)];
}

