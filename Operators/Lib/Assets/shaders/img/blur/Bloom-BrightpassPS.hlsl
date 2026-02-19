// BrightPassPS.hlsl
// Outputs pixel color only if its brightness exceeds a threshold.
cbuffer ThresholdParams : register(b0)
{
    float3 ColorWeights;
    float Threshold;
};

Texture2D SourceTexture : register(t0);
SamplerState LinearSampler : register(s0);

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 psMain(PS_INPUT input) : SV_Target
{
    float4 color = SourceTexture.Sample(LinearSampler, input.uv);
    color = clamp(color,0, float4(1000,1000,1000,1));

    // Calculate brightness (luminance)
    float brightness = dot(color.rgb, ColorWeights);

    // Subtract threshold and saturate (keeps positive values, zeros out negative)
    // This creates a smoother falloff than a hard step function.
    // Adjust the subtraction or use smoothstep for different falloffs.
    float contribution = saturate(brightness - Threshold);
    if(isnan(contribution)) {
        contribution=0;
        color = 0;
    }

    // Output the original color multiplied by its contribution factor
    // Pixels below threshold will have contribution=0 -> output black
    return float4(color.rgb * contribution, color.a); // Preserve original alpha? Or set to contribution?
}
