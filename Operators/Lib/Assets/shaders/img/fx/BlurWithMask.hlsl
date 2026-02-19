cbuffer ParamConstants : register(b0)
{
    float2 Direction;
    float Size;
    float NumberOfSamples;
    
    float4 Color;

    float Offset;
    float AddOriginal;
    float ApplyMaskToAlpha;
    float MaskContrast;
    float MaskOffset;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> InputTexture : register(t0);
Texture2D<float4> MaskTexture : register(t1);
sampler samLinear : register(s0);


static const int WEIGHT_COUNT = 10;
static const float Gauss[WEIGHT_COUNT] = { 0.93, 0.86, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1 };
static const float widthToHeight = 1;    // Aspect?

float4 psMain(vsOutput input) : SV_TARGET
{
    float4 mask = MaskTexture.Sample(samLinear, input.texCoord);
    float maskValue = saturate( ((mask.r+mask.g+mask.b)/3 -0.5) * MaskContrast + 0.5 + MaskOffset);

    float2 dir = Direction;
    dir *= 0.01*Size/NumberOfSamples * maskValue;    
    dir.y *= widthToHeight;

    float2 pos = dir;
    float4 orgColor = InputTexture.Sample(samLinear, input.texCoord);
    float4 c = orgColor;

    float totalWeight = 1;
    for (int i = 0; i < NumberOfSamples; ++i)
    {
        float index = (float)i*(WEIGHT_COUNT - 1)/NumberOfSamples;
        float weight = lerp(Gauss[(int)index], Gauss[(int)index + 1], frac(index));
        c += InputTexture.Sample(samLinear, input.texCoord + pos)*weight;
        c += InputTexture.Sample(samLinear, input.texCoord - pos)*weight;
        pos += dir;
        totalWeight += 2*weight;
    }

    //float4 factor = float4(Glow.xxx, 1) * Color;
    c =  Color * c / totalWeight + float4(Offset.xxx,0) + orgColor* AddOriginal * maskValue;
    c.a = clamp(c.a, 0,1) * lerp(1, (1-maskValue), ApplyMaskToAlpha);
    return clamp(c,0,1000);
}


