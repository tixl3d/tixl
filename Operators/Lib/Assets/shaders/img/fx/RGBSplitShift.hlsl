cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float2 R;
    float2 G;
    float2 B;
    float Amount;
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

Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);

Texture2D<float4> ShiftControl : register(t1);
sampler shiftSampler : register(s1);

float4 psMain(vsOutput input) : SV_TARGET
{
    float2 centerproof = Center * float2(-1.0, 1.0);
    float width, height;
    Image.GetDimensions(width, height);
    float aspectRatio = width / height;

    float2 flipY = float2(1,-1);
    float2 Ra = R * Amount * flipY;
    float2 Ga = G * Amount * flipY;
    float2 Ba = B * Amount * flipY;

    float2 uv = input.texCoord;
    uv += centerproof;

    // Sample the shift control texture to get the shift amounts for each channel
    float4 shiftValues = ShiftControl.SampleLevel(shiftSampler, uv,0);

    // Combine the original shift parameters with the shift values from the texture
    float2 shiftR = uv - (shiftValues.x * Ra);
    float2 shiftG = uv - (shiftValues.y * Ga);
    float2 shiftB = uv - (shiftValues.z * Ba);

    // Sample the image with the shifted coordinates
    //float4 orgColor = Image.Sample(texSampler, uv);
    float4 c;

    c.r = Image.Sample(texSampler, shiftR).r;
    c.g = Image.Sample(texSampler, shiftG).g;
    c.b = Image.Sample(texSampler, shiftB).b;
    c.a = Image.Sample(texSampler, uv).a;

    return c;
}
