cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float Radius;
    float Mode;
    float RadialBias;
    float RadialOffset;
    float Twist;
    float __padding;
    float2 Stretch;
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

#define mod(x,y) ((x)-(y*floor(x/y)))

float4 psMain(vsOutput input) : SV_TARGET
{    
    float width, height;
    Image.GetDimensions(width, height);

    float aspectRatio = TargetWidth/TargetHeight;
    float2 p = input.texCoord;
    
    float2 polar =0;

    if(Mode < 0.5) 
    {
        p-= 0.5;
        p.x *=aspectRatio;
        float l = 2*length(p) / Radius ;
        l= pow(l, RadialBias);

        polar = float2( atan2(p.x, p.y) / 3.141578 /2 + 0.5 , l  ) + Center;
        polar.y += RadialOffset;
        polar.x += polar.y * Twist;
        polar *= Stretch;
        //polar = mod(polar,1);

    }
    else {
        p.y += RadialOffset;
        float angle = p.x * 3.141578 *2;
        polar = float2( sin(angle ), cos(angle) ) * pow(p.y,RadialBias) /2 * Radius;
        polar.x /=aspectRatio;
        polar.x-= 0.5;
        polar.y-= 0.5;
        polar+= Center;
    }

    float4 orgColor = Image.Sample(texSampler, polar);
    return orgColor;

}