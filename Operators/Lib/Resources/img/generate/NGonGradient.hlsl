#include "shared/blend-functions.hlsl"
#include "shared/bias-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float2 Position;
    float Sides;
    float Radius;
    float Curvature;
    float Blades;
    float Roundness;

    float Rotate;

    float Width;
    float Offset;
    float PingPong;
    float Repeat;

    float BlendMode;
    float2 GainAndBias;

    float IsTextureValid; // Automatically added by _FxShaderSetup
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
Texture2D<float4> Gradient : register(t1);
sampler texSampler : register(s0);
sampler clammpedSampler : register(s1); //sadly this is not solving the issue

static float PI = 3.141592653;
static float TAU = (3.1415926535 * 2);

float fmod(float x, float y)
{
    return (x - y * floor(x / y));
}
/* ------- Previous version ---------- */
// based on https://www.shadertoy.com/view/Wll3R2
/*
float sdNgon(in float2 p, in float r, in float n)
{
    // Can precompute these
    float inv_n = 1.0 / n;

    // Perform radial repeat
    float2 rp = float2(atan2(p.y, p.x), length(p)); // into polar coords
    rp.x /= TAU;
    rp.x = fmod(rp.x + inv_n * 0.5, inv_n) - 0.5 * inv_n;
    rp.x *= rp.x > 0 ? (1 - saturate(Blades)) : 1;
    rp.y = saturate(lerp(rp.y, r, Curvature));
    rp.x *= TAU;

    p = float2(cos(rp.x), sin(rp.x)) * rp.y; // back to cartesian
    float2 b = float2(r, r);
    b.y = b.x * tan(TAU * inv_n * 0.5);
    float2 d = abs(p) - b;

    float sd = length(max(d, float2(0, 0))) + min(d.x, 0.0);
    return sd;
}*/

/* ------- New version ---------- */
// What if we could use this instead? https://www.shadertoy.com/view/7tSXzt
float sdRegularPolygon(in float2 p, in float r, in float n) 
{   // these lines can be precomputed for a given shape
    float an = 3.141593/float(n);
    float2 acs = float2(cos(an),sin(an));
    
    // Store original length for curvature calculation
    float originalLen = length(p);
    
    // reduce to first sector
    float bn = fmod(atan2(p.x,p.y),2.0*an) - an;
    bn *= bn > 0 ? (1 - saturate(Blades)) : 1; //Blades parameter is working
    
    p = length(p)*float2(cos(bn),abs(sin(bn)));
    
    // line sdf
    p -= r*acs;
    
    p.y += clamp(-p.y, 0.0, r*acs.y);
    p.y *= p.y > 0 ? (saturate(Roundness)) : 1;  // we can control the roundness
    
    // Apply curvature effect to the distance field
    float dist = length(p)*sign(p.x);
    
    // Adjust distance based on curvature (flower effect)
    // This pulls points toward or away from the boundary
    float flowerEffect = (r - originalLen) * Curvature; // Curvature is working again ^_^
    dist += flowerEffect;
    
    return dist;
}

// Function to rotate a point around the origin
inline float2 rotatePoint(float2 p, float angle)
{
    angle = radians(angle); // Convert angle to radians
    float cosAngle = cos(angle);
    float sinAngle = sin(angle);
    return float2(
        p.x * cosAngle + p.y * sinAngle,
        p.x * sinAngle - p.y * cosAngle);
}

float PingPongRepeat(float x, float pingPong, float repeat)
{
    float baseValue = x;
    float repeatValue = frac(baseValue);
    float pingPongValue = 1.0 - abs(frac(x * 0.5) * 2.0 - 1.0);
    float singlePingPong = abs(x);

    // Select pingpong type: single or repeating
    float pingPongOutput = lerp(singlePingPong, pingPongValue, step(0.5, repeat));

    // Select between base, repeat, or pingpong
    float value = lerp(baseValue, repeatValue, step(0.5, repeat)); // If repeat, use repeatValue
    value = lerp(value, pingPongOutput, step(0.5, pingPong));      // If pingpong, override with pingpong

    // Clamp final result if not repeating
    value = lerp(saturate(value), value, step(0.5, repeat)); // If NOT repeating, clamp to [0..1]

    return value;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth / TargetHeight;
    float2 p = psInput.texCoord;
    p -= 0.5;
    p.x *= aspectRatio;

    // Rotate
    p = rotatePoint(p, Rotate);

    p += Position.yx;
    //float c = sdNgon(p, Radius, Sides) * 2 - Offset * Width;
    float c = sdRegularPolygon(p, Radius, Sides) * 2 - Offset * Width ;

    float4 orgColor = ImageA.Sample(texSampler, psInput.texCoord);
    c = PingPongRepeat(c / Width, PingPong, Repeat);

    float dBiased = ApplyGainAndBias(c, GainAndBias);
    dBiased = clamp(dBiased, 0.001, 0.999);
    float4 gradient = Gradient.Sample(clammpedSampler, float2(dBiased, 0));

    return (IsTextureValid < 0.5) ? gradient : BlendColors(orgColor, gradient, (int)BlendMode);
}