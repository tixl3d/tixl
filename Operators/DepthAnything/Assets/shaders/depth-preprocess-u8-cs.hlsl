// Resamples any texture to the model's input size as 8-bit RGB pixels, for models that normalize inside their
// graph and take uint8 NHWC input. The same box-filtered resample as depth-preprocess-cs.hlsl; the CPU only
// drops the alpha channel of the small result.

Texture2D<float4> Source : register(t0);
RWTexture2D<unorm float4> Pixels : register(u0);

cbuffer Params : register(b0)
{
    uint ModelWidth;
    uint ModelHeight;
    uint LinearSource;
    uint _padding;
}

static const int MaxTapsPerAxis = 4;

float3 LoadBilinear(float2 position, int2 size)
{
    float2 shifted = position - 0.5;
    int2 lower = (int2)floor(shifted);
    float2 fraction = shifted - lower;
    int2 low = clamp(lower, int2(0, 0), size - 1);
    int2 high = clamp(lower + 1, int2(0, 0), size - 1);

    float3 top = lerp(Source.Load(int3(low.x, low.y, 0)).rgb, Source.Load(int3(high.x, low.y, 0)).rgb, fraction.x);
    float3 bottom = lerp(Source.Load(int3(low.x, high.y, 0)).rgb, Source.Load(int3(high.x, high.y, 0)).rgb, fraction.x);
    return lerp(top, bottom, fraction.y);
}

float3 EncodeSrgb(float3 linearColor)
{
    float3 low = linearColor * 12.92;
    float3 high = 1.055 * pow(linearColor, 1.0 / 2.4) - 0.055;
    return lerp(high, low, (float3)(linearColor <= 0.0031308));
}

[numthreads(16, 16, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ModelWidth || id.y >= ModelHeight)
        return;

    uint sourceWidth, sourceHeight;
    Source.GetDimensions(sourceWidth, sourceHeight);
    int2 sourceSize = int2(sourceWidth, sourceHeight);
    float2 scale = float2(sourceWidth, sourceHeight) / float2(ModelWidth, ModelHeight);

    int2 taps = clamp((int2)ceil(scale), int2(1, 1), int2(MaxTapsPerAxis, MaxTapsPerAxis));
    float3 sum = 0;
    for (int tapY = 0; tapY < taps.y; tapY++)
    {
        for (int tapX = 0; tapX < taps.x; tapX++)
        {
            float2 offset = (float2(tapX, tapY) + 0.5) / taps;
            sum += LoadBilinear((float2(id.xy) + offset) * scale, sourceSize);
        }
    }

    float3 color = saturate(sum / (taps.x * taps.y));
    if (LinearSource != 0)
    {
        color = EncodeSrgb(color);
    }

    Pixels[id.xy] = float4(color, 1);
}
