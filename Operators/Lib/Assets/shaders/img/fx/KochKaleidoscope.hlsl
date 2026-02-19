// Koch Snowflake - by Martijn Steinrucken aka BigWings 2019
// Email:countfrolic@gmail.com Twitter:@The_ArtOfCode
// License Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.
//
// This effect is part of a tutorial on YouTube
// https://www.youtube.com/watch?v=il_Qg9AqQkE

cbuffer ParamConstants : register(b0)
{
    float Scale;
    float CenterX;
    float CenterY;

    float OffsetX;
    float OffsetY;

    float Angle;
    float Steps;
    float ShadeSteps;
    float ShadeFolds;
    float Rotate;
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

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);
static float2x2 rotation;
static float aspect;

float fmod(float x, float y)
{
    return x - y * floor(x / y);
}

float2 GetDirection(float angle)
{
    return float2(sin(angle), cos(angle));
}

float2x2 rotate2d(float _angle)
{
    return float2x2(cos(_angle), -sin(_angle),
                    sin(_angle), cos(_angle));
}

float4 KochKaleidoscope(float2 uv)
{
    uv = mul(rotation, uv);
    uv *= Scale;
    uv.x = abs(uv.x);

    float3 col = float3(0, 0, 0);
    float d;

    float angle = 0.;
    float2 n = GetDirection((5. / 6.) * 3.1415);

    uv.y += tan((5. / 6.) * 3.1415) * .5;
    d = dot(uv - float2(.5, 0), n);
    uv -= max(0., d) * n * 2.;

    float scale = 1.;
    float foldCount = 0;
    n = GetDirection(Angle * (2. / 3.) * 3.1415 / 90);
    uv.x += .5;

    for (int i = 1; i < Steps; i++)
    {
        uv *= 3.;
        scale *= 3.;
        uv.x -= 1.5;

        uv.x = abs(uv.x);
        uv.x -= .5;
        d = dot(uv, n);
        float foldSideShade = d < 0 ? 1 : 0;
        foldCount += foldSideShade * ShadeFolds;
        foldCount += d * ShadeSteps;
        float foldFactor = min(0., d);
        uv -= foldFactor * n * 2.;
    }

    d = length(uv - float2(clamp(uv.x, -1., 1.), 0));
    col += smoothstep(1. / 100, .0, d / scale);
    uv /= scale; // normalization

    float4 c = inputTexture.Sample(texSampler, uv + float2(OffsetX, OffsetY));
    c.rgb -= foldCount / Steps;
    return c;
}

cbuffer Resolution : register(b2)
{
    float TargetWidth;
    float TargetHeight;
}

float4 psMain(vsOutput input) : SV_TARGET
{
    uint width, height;
    inputTexture.GetDimensions(width, height);
    aspect = TargetWidth / TargetHeight;

    float2 uv = input.texCoord;
    rotation = rotate2d(Rotate / 180 * 3.141592);

    uv -= float2(CenterX, 1 - CenterY);
    uv.x *= aspect;

    float strength = .37;
    float2 offset = float2(strength / (float)width, strength / float(height));

    float4 c = (KochKaleidoscope(uv + offset * float2(1, 0)) + KochKaleidoscope(uv + offset * float2(-1, 0)) + KochKaleidoscope(uv + offset * float2(0, 1)) + KochKaleidoscope(uv + offset * float2(0, -1))) / 4;
    return clamp(c, float4(0, 0, 0, 0), float4(1000, 1000, 1000, 1));
}
