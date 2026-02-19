cbuffer ParamConstants : register(b0)
{
    float4 ColorA;
    float4 ColorB;
    float2 Size;
    float UseAspectRatio;
    float Scale;
    float2 Offset;
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


#define mod(x, y) (x - y * floor(x / y))


float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth/TargetHeight;

    float2 p = psInput.texCoord;

    if(UseAspectRatio > 0.5) {
        p -= 0.5;
        p.x *= aspectRatio;
    }

    p /= Size * Scale ;
    p += Offset * float2(-1,1);

    float2 a = mod(p,1);

    float t= (a.x > 0.5 && a.y < 0.5) ||  (a.x < 0.5 && a.y > 0.5)
     ? 0 : 1;
    return lerp(ColorA, ColorB,  t);
}