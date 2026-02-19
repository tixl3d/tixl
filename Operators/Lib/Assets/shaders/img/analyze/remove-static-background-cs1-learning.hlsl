// Pass1_BackgroundAndMask.hlsl
// Corrected version: separates learning-space and detection-space z
// ZScale affects detection ONLY, never learning.

Texture2D<float4> InputFrame : register(t0);
RWTexture2D<float4> BgModel : register(u0); // rgb = mean, a = spread
RWTexture2D<float> MaskRaw : register(u1);

cbuffer Params : register(b0)
{
    float MeanRate;       // ~0.002 .. 0.02
    float SpreadUpRate;   // ~0.05
    float SpreadDownRate; // ~0.001
    float MinSpread;      // ~0.005 (in 0..1 RGB space)

    float ZScale;     // detection sensitivity only (>= 0)
    float BrightSupp; // 0..1, e.g. 0.35 (projector suppression)
    float BgGateLo;   // learning gate low  (zLearn)
    float BgGateHi;   // learning gate high (zLearn)

    float UseChroma;    // 0 or 1
    float ChromaWeight; // e.g. 0.5
    float IsTraining;   // 0 = live, 1 = training
    float Reset;
};

static const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

[numthreads(16, 16, 1)] void main(uint3 id : SV_DispatchThreadID)
{
    int2 p = int2(id.xy);

    float3 C = InputFrame.Load(int3(p, 0)).rgb;

    float4 state = BgModel[p];
    float3 M = state.rgb;
    float S = max(state.a, MinSpread);

    float3 d = C - M;

    // Signed luminance delta
    float lumDiff = dot(d, LUMA);
    float absLum = abs(lumDiff);

    // Optional chroma delta
    float chroma = 0.0;
    if (UseChroma > 0.5)
    {
        float3 Cn = normalize(max(C, 1e-5));
        float3 Mn = normalize(max(M, 1e-5));
        chroma = length(Cn - Mn);
    }

    // Base distance (semantic difference signal)
    float dist = absLum + ChromaWeight * chroma;

    // Brightening suppression (affects both learning + detection semantics)
    float brightBias = (lumDiff > 0.0) ? BrightSupp : 1.0;
    dist *= brightBias;

    float zLearn = dist / S;

    float bgWeight = 1.0 - smoothstep(BgGateLo, BgGateHi, zLearn);
    bgWeight = lerp(bgWeight, 1.0, IsTraining);

    // Update mean
    M += d * (MeanRate * bgWeight);

    // Update spread (asymmetric)
    float target = max(dist, MinSpread);
    float rate = (target > S) ? SpreadUpRate : SpreadDownRate;
    S += (target - S) * (rate * bgWeight);

    // DETECTION SPACE
    float zDetect = zLearn * max(ZScale, 0.0);

    // Smooth foreground confidence (Gaussian-like)
    float bgProb = 1.0 / (1.0 + zDetect * zDetect);
    float fg = saturate(1.0 - bgProb);

    // -------------------------------------------------

    if (Reset)
    {
        S = 0;
    }

    BgModel[p] = float4(M, S);
    MaskRaw[p] = fg;
}
