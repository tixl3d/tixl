cbuffer ParamConstants : register(b0)
{
    float Falloff;
}

cbuffer ParamConstants : register(b1)
{
    int Mode;
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
sampler wrappedSampler : register(s0);

float4 ApplyBlending(float4 color, float2 uv, int direction)
{
    float2 offset = direction == 0 ? float2(0.5, 0) : float2(0, 0.5);

    float2 shiftedUV = uv + offset;

    float4 seamSample = Image.Sample(wrappedSampler, shiftedUV);

    float2 edge = abs(1.0 - ((direction == 0 ? uv.x : uv.y) * 2.0));
    float blendFactor = smoothstep(0.0, Falloff, edge);

    return lerp(color, seamSample, blendFactor);
}

float4 psMain(vsOutput input) : SV_TARGET
{
    float width, height;
    Image.GetDimensions(width, height);

    float2 uv = input.texCoord;
    float4 baseColor = Image.Sample(wrappedSampler, uv);

    // 0 - both
    // 1 - horitontal
    // 2 - vertical

    float4 color = 0;

    // Horizontal
    if (Mode != 1)
    {
        color = ApplyBlending(baseColor, uv, 0);
    }

    // Vertical
    // if (Mode != 2)
    {
        color = ApplyBlending(color, uv, 1);
    }

    return color;
    // float direction = uv.x;
    // float2 shiftedUV = float2(uv.x + 0.5, uv.y);

    // float direction = uv.x;
    // float2 shiftedUV = float2(uv.x + 0.5, uv.y);

    // // if (Direction < 0.5)
    // // {
    // //     shiftedUV = float2(uv.x, uv.y + 0.5);
    // //     direction = uv.y;
    // // }

    // float4 seamSample = Image.Sample(wrappedSampler, shiftedUV);

    // float edge = abs(1.0 - (direction * 2.0));
    // float blendFactor = smoothstep(0.0, Falloff, edge);
    // return float4(blendFactor, 0, 0, 1);

    // return lerp(Base, seamSample, blendFactor);
}