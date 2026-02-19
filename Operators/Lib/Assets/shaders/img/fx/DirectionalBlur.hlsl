cbuffer ParamConstants : register(b0)
{
    float Size;
    float NumberOfSamples;
    float Angle;
    float FxAngleFactor;
    float FxSizeFactor;
}

cbuffer Resolution : register(b2)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> InputTexture : register(t0);
Texture2D<float4> FxTexture : register(t1);
sampler samLinear : register(s0);


static const int WEIGHT_COUNT = 10;
static const float Gauss[WEIGHT_COUNT] = { 0.93, 0.86, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1 };


float4 psMain(vsOutput input) : SV_TARGET
{
	float2 uv = input.texCoord;

    float aspectRatio = TargetWidth/TargetHeight;
	
    float4 fx = FxTexture.Sample(samLinear, uv);
    float angle = (Angle +  FxAngleFactor * fx.r * 360)* 3.141578/180;

    float2 dir = float2(cos(angle), sin(angle) );
    float strength = Size * (1 + FxSizeFactor * fx.g);
    dir *= strength / NumberOfSamples;
    dir.y *= aspectRatio;

    float2 pos = dir;
    float4 c = InputTexture.Sample(samLinear, input.texCoord);
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
        c.rgb = c/totalWeight;

    c.a = clamp(c.a/totalWeight, 0,1);
    return clamp(c,0,1000);
}


