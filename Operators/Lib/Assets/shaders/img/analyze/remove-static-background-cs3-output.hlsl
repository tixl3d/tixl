
// Final outputs
#define OUTPUT_FOREGROUND 0 // camera RGB, alpha = refined foreground mask
#define OUTPUT_MASK 1       // grayscale mask in RGB, alpha = 1

// Debug outputs
#define DEBUG_MEAN 2
#define DEBUG_SPREAD 3
#define DEBUG_Z 4
#define DEBUG_RAW_MASK 5
#define DEBUG_REFINED 6
#define DEBUG_BRIGHT_DARK 7
#define DEBUG_RANGE 8
Texture2D<float4> InputFrame : register(t0); // current camera frame (linear RGB)
Texture2D<float4> BgModel : register(t1);    // rgb = mean, a = spread
Texture2D<float> MaskRaw : register(t2);
Texture2D<float> MaskRefined : register(t3);

RWTexture2D<float4> Output : register(u0);

cbuffer Params : register(b0)
{
    float SpreadScale; // visualization scale (e.g. 1..3)
    float ZVisScale;   // e.g. 0.25 (z≈4 -> white)
};

cbuffer IntParams : register(b1)
{
    int ViewMode; // one of the defines above
}

static const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

[numthreads(16, 16, 1)] void main(uint3 id : SV_DispatchThreadID)
{
    int2 p = int2(id.xy);

    float3 C = InputFrame.Load(int3(p, 0)).rgb;
    float4 bg = BgModel.Load(int3(p, 0));

    float3 mean = bg.rgb;
    float spread = max(bg.a, 1e-5) * SpreadScale;

    float currentBrightness = dot(C, LUMA);
    float meanBrightness = dot(mean, LUMA);

    float lumDiff = currentBrightness - meanBrightness;
    float z = abs(lumDiff) / spread;

    float rawMask = MaskRaw.Load(int3(p, 0));
    float refinedMask = MaskRefined.Load(int3(p, 0));

    float4 outColor = float4(0, 0, 0, 1);

    switch (ViewMode)
    {
        // -------------------------------------------------
        // Final outputs
        // -------------------------------------------------

    case OUTPUT_FOREGROUND:
        outColor.rgb = C;
        outColor.a = refinedMask;
        break;

    case OUTPUT_MASK:
        outColor.rgb = refinedMask.xxx;
        outColor.a = 1.0;
        break;

        // -------------------------------------------------
        // Debug views
        // -------------------------------------------------

    case DEBUG_MEAN:
        outColor = float4(mean, 1.0);
        break;

    case DEBUG_SPREAD:
    {
        float s = saturate(spread);
        outColor = float4(s, s, s, 1.0);
        break;
    }

    case DEBUG_Z:
    {
        float zv = saturate(z * ZVisScale);
        outColor = float4(zv, zv, zv, 1.0);
        break;
    }

    case DEBUG_RAW_MASK:
        outColor = float4(rawMask.xxx, 1.0);
        break;

    case DEBUG_REFINED:
        outColor = float4(refinedMask.xxx, 1.0);
        break;

    case DEBUG_BRIGHT_DARK:
        // Red = brighter than background
        // Blue = darker than background
        outColor = (lumDiff > 0.0)
                       ? float4(1, 0, 0, 1)
                       : float4(0, 0, 1, 1);
        break;

    case DEBUG_RANGE:
    {
        float minB = saturate(meanBrightness - spread);
        float maxB = saturate(meanBrightness + spread);

        outColor = float4(
            saturate(currentBrightness), // R
            minB,                        // G
            maxB,                        // B
            1.0);
        break;
    }

    default:
        outColor = float4(1, 0, 1, 1); // invalid mode
        break;
    }

    Output[p] = outColor;
}