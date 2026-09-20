cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float2 Size;
    float2 Position;
    float Round;
    float Feather;
    float GradientBias;
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

Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);

#define mod(x, y) (x - y * floor(x / y))
float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}

// float2 max2(float2 p) {
//     return float2( 
//         abs(p.x),
//         abs(p.y)
//     );
// }


float sdBox( in float2 p, in float2 b )
{
    float2 d = abs(p)-b;
    return length(
        max(d,float2(0,0))) + min(max(d.x,d.y),
        0.0);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{    
    float2 p = psInput.texCoord;

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);

    p  = p *2.-1.;
    p-=Position;
    
    float d = sdBox(p, Size);
    
    float dBiased = GradientBias>= 0 
        ? pow( d, GradientBias+1)
        : 1-pow( clamp(1-d,0,10), -GradientBias+1);

    d = smoothstep(Round, Round+Feather, dBiased);
    float4 c= lerp(Fill, Background,  d);

    float a = clamp(orgColor.a + c.a - orgColor.a*c.a, 0,1);
    float3 rgb = (1.0 - c.a)*orgColor.rgb + c.a*c.rgb;   
    return float4(rgb,a);
}
