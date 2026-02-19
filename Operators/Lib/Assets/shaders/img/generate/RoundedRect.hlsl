cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 OutlineColor;
    float4 Background;

    float2 Stretch;
    float2 Center;

    float Scale;
    float Round;
    float Stroke;
    float Feather;
    float GradientBias;
    float Rotate;

    float IsTextureValid;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);

float sdBox(in float2 p, in float2 b)
{
    float2 d = abs(p) - b;
    return length(
               max(d, float2(0, 0))) +
           min(max(d.x, d.y),
               0.0);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth / TargetHeight;

    float2 p = psInput.texCoord;
    p -= 0.5;
    p.x *= aspectRatio;

    // Rotate
    float imageRotationRad = (-Rotate - 90) / 180 * 3.141578;

    float sina = sin(-imageRotationRad - 3.141578 / 2);
    float cosa = cos(-imageRotationRad - 3.141578 / 2);

    p -= Center * float2(1, -1);
    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);

    float2 size = Stretch * Scale;
    float minSize = min(size.x, size.y);
    float roundOffset = minSize * Round;
    float2 rsize = size - roundOffset;

    float d = sdBox(p, rsize / 2);
    d = GradientBias >= 0
            ? pow(d, GradientBias + 1)
            : 1 - pow(clamp(1 - d, 0, 10), -GradientBias + 1);

    float feather = Scale * Feather / 2;
    float dInside = smoothstep(-feather, feather, d - roundOffset / 2);

    float stroke = max(Stroke * minSize, 0);
    float dStroke = smoothstep(-feather, feather, d - roundOffset / 2 - stroke);

    // Prevent spill into background if stroke size is 0
    float showStroke = saturate(abs(stroke) * 100);
    float4 outlineColor = lerp(Fill, OutlineColor, showStroke);

    float4 cInside = lerp(Fill, outlineColor, dInside);

    float4 cStroke = lerp(Background, outlineColor, 1 - dStroke);
    float4 c = lerp(cInside, cStroke, dStroke);

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    return (IsTextureValid < 0.5) ? c
                                  : float4((1.0 - c.a) * orgColor.rgb + c.a * c.rgb,
                                           orgColor.a + c.a - orgColor.a * c.a);

    // float a = clamp(orgColor.a + c.a - orgColor.a * c.a, 0, 1);

    // float3 rgb = (1.0 - c.a) * orgColor.rgb + c.a * c.rgb;
    // return float4(rgb, a);
}