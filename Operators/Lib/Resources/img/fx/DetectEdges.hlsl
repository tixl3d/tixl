cbuffer ParamConstants : register(b0)
{
    float4 Color;
    float SampleRadius;
    float Strength;
    float Contrast;
    float MixOriginal;
    float OutputAsTransparent;
}


cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

cbuffer ParamConstants : register(b2)
{
    int Invert;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);

float4 psMain(vsOutput input) : SV_TARGET
{
    float width, height;
    Image.GetDimensions(width, height);

    float sx = SampleRadius / width;
    float sy = SampleRadius / height;

    float x = input.texCoord.x;
    float y = input.texCoord.y;

    float4 y1 = Image.Sample(texSampler, float2(input.texCoord.x, input.texCoord.y + sy));
    float4 y2 = Image.Sample(texSampler, float2(input.texCoord.x, input.texCoord.y - sy));

    float4 x1 = Image.Sample(texSampler, float2(input.texCoord.x + sx, input.texCoord.y));
    float4 x2 = Image.Sample(texSampler, float2(input.texCoord.x - sx, input.texCoord.y));
    float4 m = Image.Sample(texSampler, float2(input.texCoord.x, input.texCoord.y));
    // return ((m-y1) + (m-y2) + (m-x1) + (m-x2)) * Strength;

    float average = (abs(x1.r - m.r) + abs(x2.r - m.r) + abs(y1.r - m.r) + abs(y2.r - m.r) +
                     abs(x1.g - m.g) + abs(x2.g - m.g) + abs(y1.g - m.g) + abs(y2.g - m.g) +
                     abs(x1.b - m.b) + abs(x2.b - m.b) + abs(y1.b - m.b) + abs(y2.b - m.b)) *
                        Strength +
                    Contrast;

    float4 edgeColor = OutputAsTransparent < 0.5
                           ? clamp(float4(average, average, average, 1), 0, 10000) * Color
                           : float4(Color.rgb, clamp(average, 0, 1));
    
    float4 color2= lerp(edgeColor, m, MixOriginal);


    return Invert ? float4(1-color2.rgb, color2.a) : color2;
}