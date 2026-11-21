// Resamples any texture to the model's input size and writes it as planar, ImageNet-normalized RGB: the output is
// ModelWidth x (3 * ModelHeight), the red plane first, then green, then blue. Read back row by row, that is the
// NCHW float tensor the model takes, so the CPU does no per-pixel work and only the small model-sized image
// crosses the GPU/CPU boundary, whatever the resolution of the source.

Texture2D<float4> Source : register(t0);
RWTexture2D<float> Planes : register(u0);

cbuffer Params : register(b0)
{
    uint ModelWidth;
    uint ModelHeight;
    uint LinearSource;
    uint PadRows;
}

static const float3 Mean = float3(0.485, 0.456, 0.406);
static const float3 Std = float3(0.229, 0.224, 0.225);
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

    // Letterbox: the image keeps its aspect in the rows between the black bands, so a fixed square input does not squeeze it
    if (id.y < PadRows || id.y >= ModelHeight - PadRows)
    {
        float3 black = (0.0 - Mean) / Std;
        Planes[uint2(id.x, id.y)] = black.r;
        Planes[uint2(id.x, id.y + ModelHeight)] = black.g;
        Planes[uint2(id.x, id.y + ModelHeight * 2)] = black.b;
        return;
    }

    uint contentHeight = ModelHeight - 2 * PadRows;
    uint row = id.y - PadRows;

    uint sourceWidth, sourceHeight;
    Source.GetDimensions(sourceWidth, sourceHeight);
    int2 sourceSize = int2(sourceWidth, sourceHeight);
    float2 scale = float2(sourceWidth, sourceHeight) / float2(ModelWidth, contentHeight);

    // Box filter over the source footprint of this texel; plain bilinear would alias when downscaling 1080p
    int2 taps = clamp((int2)ceil(scale), int2(1, 1), int2(MaxTapsPerAxis, MaxTapsPerAxis));
    float3 sum = 0;
    for (int tapY = 0; tapY < taps.y; tapY++)
    {
        for (int tapX = 0; tapX < taps.x; tapX++)
        {
            float2 offset = (float2(tapX, tapY) + 0.5) / taps;
            sum += LoadBilinear((float2(id.x, row) + offset) * scale, sourceSize);
        }
    }

    float3 color = saturate(sum / (taps.x * taps.y));
    if (LinearSource != 0)
    {
        color = EncodeSrgb(color);
    }

    float3 normalized = (color - Mean) / Std;
    Planes[uint2(id.x, id.y)] = normalized.r;
    Planes[uint2(id.x, id.y + ModelHeight)] = normalized.g;
    Planes[uint2(id.x, id.y + ModelHeight * 2)] = normalized.b;
}
