// Blur while downsampling (Kawase-style 4-tap). DC gain = 1.0.

cbuffer DownParams : register(b0)
{
    float2 InvSrcSize; // 1/srcWidth, 1/srcHeight
    float OffsetPx;    // in SOURCE pixels
    float _pad0;
};

Texture2D Src : register(t0);
SamplerState LinearSampler : register(s0);

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 psMain(PS_INPUT i) : SV_Target
{
    float2 d = InvSrcSize * OffsetPx;

    // 4 taps on a square (bilinear fetches)
    float4 c =
        Src.SampleLevel(LinearSampler, i.uv + float2(-d.x, -d.y), 0) +
        Src.SampleLevel(LinearSampler, i.uv + float2(d.x, -d.y), 0) +
        Src.SampleLevel(LinearSampler, i.uv + float2(-d.x, d.y), 0) +
        Src.SampleLevel(LinearSampler, i.uv + float2(d.x, d.y), 0);

    return c * 0.25f; // weights sum to 1
}