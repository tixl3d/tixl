#include "shared/hash-functions.hlsl"
#include "shared/blend-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float4 Highlight;
    float2 Radius;
    float2 Position;

    float RingCount;
    float Feather;
    float Rotate;
    float Offset;

    float2 _Segments;
    float2 _Twist;
    float2 _Thickness;
    float2 _Ratio;

    float _FillRatio;
    float _HighlightRatio;
    float HighlightSeed;

    float Distort;
    float Contrast;
    float Seed;

    float BlendMode;

    float IsTextureValid; // Automatically added by _FxShaderSetup
}

// cbuffer TimeConstants : register(b1)
// {
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
// }

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

#define mod(x, y) (x - y * floor(x / y))

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth / TargetHeight;
    float ringRadius = (Radius.y - Radius.x) / RingCount;
    float scaledFeather = Feather / ringRadius / 10;
    float2 p = psInput.texCoord;

    p -= 0.5;
    p.x *= aspectRatio;
    p -= Position * float2(1, -1);

    float d2 = length(p);

    float normalizedDistance = (d2 - Radius.x) / (Radius.y - Radius.x);

    float isInsideRadius = normalizedDistance < 0 ? 0 : 1;

    //return float4((isnan(normalizedDistance) ? 1 : 0.5).xxx ,1);
    normalizedDistance =  pow(max(normalizedDistance,0), Distort);

    float c = smoothstep(0 - 0.01, 0, normalizedDistance);
    c *= smoothstep(1 + 0.01, 1, normalizedDistance);

    float rings = normalizedDistance * RingCount + Offset;
    float ringV = mod(rings, 1);
    float ringIndex = floor(rings);

    float2 ringHash = hash22(float2((ringIndex + 1) * 124.34 + 1.12, (Seed + 0.5) % 312.113));

    float segments = _Segments.x + (ringHash.x - 0.5) * _Segments.y;
    float ringCenter = abs(ringV - 0.5);

    float angle = (atan2(p.x, p.y) / 2 / 3.141578 + 0.5) + Rotate / 180 / 3.141578;
    float2 ringRotate = _Twist / (180 * 3.141578);
    float ringAngle = angle + 0.5 + (ringHash.x - 0.5) * ringRotate.y;

    float ringIndexFromCenter = (ringIndex - Offset) % RingCount;
    float ringAngle2 = mod((ringAngle + ringRotate.x * ringIndexFromCenter / RingCount), 1) * segments;



    // return float4(ringAngle2, 0,0,1);
    float segmentV = ringAngle2 % 1;
    float segmentIndex = floor(ringAngle2 - segmentV + 0.01);
    float segmentAngle = mod(ringAngle2, 1);

    float seed = (segmentIndex * 1.123 + ringIndex % 12.31 + Seed % 712.1);
    float4 segmentHash = hash42(float2(seed * 9234.131, (Seed + 0.5) * 13.791));

    float segmentThickness = saturate(_Thickness.x / 2 + (segmentHash.y - 0.5) * _Thickness.y);

    // Rings
    c *= smoothstep(segmentThickness + scaledFeather, segmentThickness - scaledFeather, ringCenter);

    float f = scaledFeather / d2 * 0.1;
    float segmentRatio = (_Ratio.x + (segmentHash.x - 0.5) * _Ratio.y) / 2;
    float brightness = lerp(segmentHash.w, 1, Contrast);

    // Segment
    c *= smoothstep(segmentRatio + f, segmentRatio - f, abs(segmentAngle - 0.5));
    c *= segmentHash.x > _FillRatio ? 0 : 1;

    float4 color = lerp(Background, Fill, c * brightness * isInsideRadius);

    float highlightHash = hash11(seed + HighlightSeed);
    float4 colorOut = highlightHash >= _HighlightRatio ? color : Highlight * c * isInsideRadius;

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    return (IsTextureValid < 0.5) ? colorOut : BlendColors(orgColor, colorOut, (int)BlendMode);
}