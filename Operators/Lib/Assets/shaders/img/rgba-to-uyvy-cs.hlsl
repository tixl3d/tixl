// Packs an RGB texture into UYVY 4:2:2 for a video sender: every output texel holds two source pixels as
// (U, Y0, V, Y1), which in an R8G8B8A8 texture is exactly UYVY's byte order in memory. Converting here spares
// the sender's CPU encoder a colour conversion and halves what is read back from the GPU.
//
// BT.709 in video range (Y 16-235, chroma 16-240), which is what NDI expects for HD and larger frames. Chroma
// is the average of the pair, a box filter that is plenty for projection content.

Texture2D<float4> Source : register(t0);
RWTexture2D<unorm float4> Packed : register(u0);

static const float3 LumaWeights = float3(0.2126, 0.7152, 0.0722);

[numthreads(16, 16, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Source.GetDimensions(width, height);
    uint packedWidth = (width + 1) / 2;
    if (id.x >= packedWidth || id.y >= height)
        return;

    uint x0 = id.x * 2;
    uint x1 = min(x0 + 1, width - 1);
    float3 c0 = saturate(Source[uint2(x0, id.y)].rgb);
    float3 c1 = saturate(Source[uint2(x1, id.y)].rgb);

    float y0 = dot(c0, LumaWeights);
    float y1 = dot(c1, LumaWeights);

    float3 pair = (c0 + c1) * 0.5;
    float yPair = dot(pair, LumaWeights);
    float cb = (pair.b - yPair) / 1.8556;
    float cr = (pair.r - yPair) / 1.5748;

    const float lumaScale = 219.0 / 255.0;
    const float lumaOffset = 16.0 / 255.0;
    const float chromaScale = 224.0 / 255.0;
    const float chromaOffset = 128.0 / 255.0;
    Packed[id.xy] = float4(cb * chromaScale + chromaOffset,
                           y0 * lumaScale + lumaOffset,
                           cr * chromaScale + chromaOffset,
                           y1 * lumaScale + lumaOffset);
}
