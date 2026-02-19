cbuffer ParamConstants : register(b0)
{
    float RotateMirror;
    float RotateImage;
    float Width;
    float Offset;

    float2 OffsetImage;
    float __dummy__;
    float ShadeAmount;

    float4 ShadeColor;
    float OffsetEdge;
}

// cbuffer TimeConstants : register(b1)
// {
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
// }

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

float mod(float x, float y)
{
    return (x - y * floor(x / y));
}

float4 psMain(vsOutput psInput) : SV_TARGET
{

    float rotateScreenRad = (-RotateMirror + RotateImage - 90) / 180 * 3.141578;

    uint imageWidth, imageHeight;
    ImageA.GetDimensions(imageWidth, imageHeight);

    float imageAspect = (float)imageWidth / imageHeight;

    float aspectRatio = TargetWidth / TargetHeight;
    float2 p = psInput.texCoord;
    p -= 0.5;
    p.x *= aspectRatio;
    // p.x *= aspectRatio;

    // p-= float2(0.5 * aspectRatio, 0.5);

    float sina = sin(-rotateScreenRad - 3.141578 / 2);
    float cosa = cos(-rotateScreenRad - 3.141578 / 2);

    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);

    // Show Center
    // if( length(p - Center) < 0.01) {
    //     return float4(1,1,0,1);
    // }

    float mirrorRotationRad = (+RotateImage - 90) / 180 * 3.141578;
    float2 angle = float2(sin(mirrorRotationRad), cos(mirrorRotationRad));

    float dist = dot(p, angle);
    float offset = Offset % 2;
    dist += offset;

    float shade = 0;

    float d = 0;
    float mDist = dist % (2 * Width);
    if (dist > Width)
    {

        if (mDist > Width)
        {
            shade = 1;
            d = -2 * (mDist - Width);
        }
    }
    else if (dist < 0)
    {
        mDist *= -1;
        if (mDist < Width)
        {
            shade = 1;
        }
        else
        {
            d = -2 * (mDist - Width);
        }
    }
    d -= dist - mDist;
    d += offset;
    d += OffsetEdge;
    p += d * angle;
    p.x /= aspectRatio;

    p *= float2(aspectRatio / imageAspect, 1);
    p += float2(0.5, 0.5);
    p += OffsetImage * float2(1 / imageAspect, 1);

    float4 texColor = ImageA.Sample(texSampler, p);

    float4 color = lerp(texColor, ShadeColor, shade * ShadeAmount);

    color = clamp(color, float4(0, 0, 0, 0), float4(100, 100, 100, 1));
    return color;
}