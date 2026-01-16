Texture2D<float4> ImageA : register(t0);
Texture2D<float4> ImageB : register(t1);
RWByteAddressBuffer Counter : register(u0);

cbuffer ParamConstants : register(b0)
{
    float Scale;
}

[numthreads(1, 1, 1)] void reset(uint3 id : SV_DispatchThreadID)
{
    Counter.Store(0, 0);
}

    [numthreads(16, 16, 1)] void main(uint3 id : SV_DispatchThreadID)
{
    uint2 size;
    ImageA.GetDimensions(size.x, size.y);

    if (id.x >= size.x || id.y >= size.y)
        return;

    float4 a = ImageA.Load(int3(id.xy, 0));
    float4 b = ImageB.Load(int3(id.xy, 0));

    float diff = length(a - b);

    uint scaled = (uint)(diff * Scale);

    int dummy;
    Counter.InterlockedAdd(0, scaled, dummy);
}
