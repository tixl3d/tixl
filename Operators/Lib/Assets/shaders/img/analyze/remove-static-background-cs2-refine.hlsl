Texture2D<float> MaskRaw : register(t0);
Texture2D<float> MaskRawPrev : register(t1);
RWTexture2D<float> MaskRefined : register(u0);

cbuffer Params2 : register(b0)
{
    float VoteThreshold; // e.g. 0.4 (how "fg" a pixel must be to vote strongly)
    float DensityLo;     // e.g. 0.20
    float DensityHi;     // e.g. 0.45
    float KeepOriginal;  // e.g. 0.5  (blend refined with raw)

    float TemporalStability; // 0 = off, 1 = very stable
};

float SoftVote(float v, float t)
{
    // smooth vote around threshold
    return smoothstep(t - 0.15, t + 0.15, v);
}

[numthreads(16, 16, 1)] void main(uint3 id : SV_DispatchThreadID)
{
    int2 p = int2(id.xy);

    // ------------------------------------------------------------
    // 1) TEMPORAL VALIDATION (per pixel, no neighborhood)
    // ------------------------------------------------------------

    float rawNow = MaskRaw.Load(int3(p, 0));
    float rawPrev = MaskRawPrev.Load(int3(p, 0));

    // Hard temporal AND (no ghosting, no accumulation)
    float rawHold = min(rawNow, rawPrev);

    // Artist-controlled strength
    float rawTemporal = lerp(rawNow, rawHold, TemporalStability);

    // ------------------------------------------------------------
    // 2) SPATIAL DENSITY (purely spatial, uses temporally validated value)
    // ------------------------------------------------------------

    float sum = 0.0;
    float wsum = 0.0;

    // 5×5 neighborhood
    [unroll] for (int y = -2; y <= 2; y++)
    {
        [unroll] for (int x = -2; x <= 2; x++)
        {
            // Each neighbor has already temporally validated itself
            float v = MaskRaw.Load(int3(p + int2(x, y), 0));

            // Apply the same temporal rule locally
            float vPrev = MaskRawPrev.Load(int3(p + int2(x, y), 0));
            v = lerp(v, min(v, vPrev), TemporalStability);

            float w = 1.0;
            sum += SoftVote(v, VoteThreshold) * w;
            wsum += w;
        }
    }

    float density = sum / max(wsum, 1e-5);

    // ------------------------------------------------------------
    // 3) DENSITY → REFINED MASK
    // ------------------------------------------------------------

    float refined = smoothstep(DensityLo, DensityHi, density);

    // ------------------------------------------------------------
    // 4) FINAL BLEND
    // ------------------------------------------------------------

    // KeepOriginal:
    // 0 → rawTemporal only
    // 1 → fully refined
    float outMask = lerp(rawTemporal, max(rawTemporal, refined), 1 - KeepOriginal);

    MaskRefined[p] = outMask;
}
