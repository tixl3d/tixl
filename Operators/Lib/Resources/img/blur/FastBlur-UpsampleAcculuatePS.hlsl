// Blur while upsampling, with artist-shaped (but always normalized) 9-tap tent-ish kernel.

cbuffer UpParams : register(b0)
{
    float2 InvLowSize; // 1/lowWidth, 1/lowHeight
    float OffsetPx;    // in LOW pixels
    float WCenter;     // normalized weights (sum to 1 together with others)
    float WCard;       // weight for each cardinal sample (4x)
    float WDiag;       // weight for each diagonal sample (4x)
    float2 _pad0;
};

Texture2D Low : register(t0);
SamplerState LinearSampler : register(s0);

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 psMain(PS_INPUT i) : SV_Target
{
    float2 d = InvLowSize * OffsetPx;

    float4 c = 0;
    c += Low.SampleLevel(LinearSampler, i.uv, 0) * WCenter;

    // cardinals
    c += Low.SampleLevel(LinearSampler, i.uv + float2(d.x, 0), 0) * WCard;
    c += Low.SampleLevel(LinearSampler, i.uv + float2(-d.x, 0), 0) * WCard;
    c += Low.SampleLevel(LinearSampler, i.uv + float2(0, d.y), 0) * WCard;
    c += Low.SampleLevel(LinearSampler, i.uv + float2(0, -d.y), 0) * WCard;

    // diagonals
    c += Low.SampleLevel(LinearSampler, i.uv + float2(d.x, d.y), 0) * WDiag;
    c += Low.SampleLevel(LinearSampler, i.uv + float2(-d.x, d.y), 0) * WDiag;
    c += Low.SampleLevel(LinearSampler, i.uv + float2(d.x, -d.y), 0) * WDiag;
    c += Low.SampleLevel(LinearSampler, i.uv + float2(-d.x, -d.y), 0) * WDiag;

    return c;
}