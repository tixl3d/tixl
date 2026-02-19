cbuffer ParamConstants : register(b0)
{
    float2 Offset;
    float2 Stretch;

    float Scale;    // 4
    float Rotation; // 5

    //float RepeatMode; // 6
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

/* float mod(float x, float y)
{
    return (x - y * floor(x / y));
}

float2 mod(float2 x, float2 y)
{
    return (x - y * floor(x / y));
} */

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float height, width;
    ImageA.GetDimensions(height, width);
    float2 aspect2 = width / height;

    float2 uv = psInput.texCoord;

    float sourceAspectRatio = TargetWidth / TargetHeight;

    float2 divisions = float2(sourceAspectRatio / Stretch.x, 1 / Stretch.y) / Scale;
    float2 p = psInput.texCoord;
    p += Offset;
    p -= 0.5;

    // Rotate
    float imageRotationRad = (-Rotation - 90) / 180 * 3.141578;

    float sina = sin(-imageRotationRad - 3.141578 / 2);
    float cosa = cos(-imageRotationRad - 3.141578 / 2);

    p.x *= sourceAspectRatio;

    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);

    p.x *= aspect2 / sourceAspectRatio;
    p *= divisions;

   // float2 samplePos = (RepeatMode > 0.5) ? abs(mod(p - 0.5, 2) - 1)
   //                                       : p + 0.5;
    float2 samplePos = p + 0.5;

    float4 imgColorForCel = ImageA.Sample(texSampler, samplePos);
    return imgColorForCel;
}