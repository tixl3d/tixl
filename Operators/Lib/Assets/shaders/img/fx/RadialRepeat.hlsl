cbuffer ParamConstants : register(b0)
{
    float2 Offset;
    float Slices;
    float RotateSlice;

    float Zoom;
    float RotateImage;
    float2 CenterPosition;
}

cbuffer ResolutionConstants : register(b1)
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

float fmod(float x, float y)
{
    return x - y * floor(x / y);
}

static const float PI = 3.14159265359f;
static const float TAU = 2.0f * PI;

float2 RadialRepeat(float2 p)
{

    float angle = atan2(p.y, p.x);
    float rotateSliceFrac = RotateSlice / 360.0f;
    float rotateImageRad = RotateImage / 180 * PI;
    float radius = length(p)*Zoom;

    float sector = TAU / Slices;

    angle = fmod(angle - rotateImageRad, TAU);
    if (angle < 0.0) angle += TAU;

    angle = fmod(angle, sector);

    angle -= sector * (rotateSliceFrac * Slices);

    float2 q = float2(cos(angle), sin(angle)) * radius;

    return q;
}


float4 psMain(vsOutput psInput) : SV_TARGET
{
    float width, height;
    ImageA.GetDimensions(width, height);
    float aspectRatio = width / height;

    float2 p = psInput.texCoord;
    p -= 0.5;
    p.x *= aspectRatio;
    float2 centerPos = float2(CenterPosition.x, -CenterPosition.y);
    p = RadialRepeat(p-centerPos);
    p += Offset;
    p.x /= aspectRatio;

    p += 0.5;
    float4 c = ImageA.SampleLevel(texSampler, p, 0);

    return c;
}
