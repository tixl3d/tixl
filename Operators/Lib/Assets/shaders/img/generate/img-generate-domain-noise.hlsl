// by CandyCat https://www.shadertoy.com/view/4sc3z2

//#define Use_Perlin
//#define Use_Value
#define Use_Simplex


cbuffer ParamConstants : register(b0)
{
    float4 ColorA;
    float4 ColorB;
    // float Scale;
    // float CenterX;
    // float CenterY;

    // float OffsetX;
    // float OffsetY;

    // float Angle;
    // float AngleOffset;
    // float Steps;
    // float Fade;
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





// Author @patriciogv - 2015
// http://patriciogonzalezvivo.com

#ifdef GL_ES
precision mediump float;
#endif

uniform float2 u_resolution;
uniform float2 u_mouse;
uniform float u_time;

float random (in float2 _st) {
    return frac(sin(dot(_st.xy,
                         float2(12.9898,78.233)))*
        43758.5453123);
}

// Based on Morgan McGuire @morgan3d
// https://www.shadertoy.com/view/4dS3Wd
float noise (in float2 _st) {
    float2 i = floor(_st);
    float2 f = frac(_st);

    // Four corners in 2D of a tile
    float a = random(i);
    float b = random(i + float2(1.0, 0.0));
    float c = random(i + float2(0.0, 1.0));
    float d = random(i + float2(1.0, 1.0));

    float2 u = f * f * (3.0 - 2.0 * f);

    return lerp(a, b, u.x) +
            (c - a)* u.y * (1.0 - u.x) +
            (d - b) * u.x * u.y;
}

#define NUM_OCTAVES 4

float fbm (  float2 _st) {
    float v = 0.0;
    float a = 0.5;
    float2 shift = float2(100.0, 100);
    // Rotate to reduce axial bias
    float2x2 rot = float2x2(cos(0.5), sin(0.5),
                    -sin(0.5), cos(0.50));

    //float n;
    for (int i = 0; i < NUM_OCTAVES; ++i) 
    {
        //n= a * abs(noise(_st)*2-1);
        //n= a * noise(_st);
        v += a * noise(_st);
        _st = mul(rot, _st) * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}

float fbmAbs (  float2 _st) {
    float v = 0.0;
    float a = 0.5;
    float2 shift = float2(100.0, 100);
    // Rotate to reduce axial bias
    float2x2 rot = float2x2(cos(0.5), sin(0.5),
                    -sin(0.5), cos(0.50));

    for (int i = 0; i < NUM_OCTAVES; ++i) 
    {
        v += a * abs(noise(_st)*2-1);
        _st = mul(rot, _st) * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}

float zebra(in float v, in float freq)    {
    return sin(v*freq*10.)/2.+0.5;
}

float3 domainNoise(float2 st) {
    float4 c = inputTexture.Sample(texSampler,st);
    st*= 5;

    //st.x += beatTime;
    //float ff = fbm(st);
    //return float3(ff,ff,ff);
    //float2 st = gl_FragCoord.xy/u_resolution.xy*3.;
    // st += st * abs(sin(u_time*0.1)*3.0);
    //float2 st; 
    //return float4()
    float3 color = float3(0,0,0);

    float2 q1 = float2(
        fbmAbs( st + float2(0,-1)*beatTime*0.2), 
        fbmAbs( st - float2(0,1)*beatTime*0.4)
    );

    float2 q2 = float2(
        fbmAbs( st + q1 + float2(0,-0.3)*beatTime*0.5), 
        fbmAbs( st + q1 - float2(0,-1)*beatTime*0.2)
    );

    float f = fbmAbs(st *0.5+ q2);

    //float f = fbmAbs(st+ q2*1);
    float3 col = lerp(
        ColorA.rgb,
        ColorB.rgb,
        atan2(q1.x, q1.y));

    return col*f;

/*
    float xx = (atan2(r1.x,r1.y))/3.14/2.;
    xx= zebra(xx,2.);

    //r1 = float2(1);
    //r1.x = fbm( st + 30.0720*q + float2(0.5*u_time,1) );



    color = lerp(float3(0.101961,0.619608,0.666667),
                float3(0.666667,0.666667,0.498039),
                clamp((f*f)*4.0,0.0,1.0));

//    color = lerp(color,
//                float3(0.955,0.927,0.867),
//                clamp(mod(length(q),0.1)*40.,1.036,3.0));

    color = lerp(color,
                float3(0.666667,1,1),
                clamp(length(r1.x),0.0,1.0));
    color.rgb *= pow(r1.x*q.x*q.y*10.,2.)*1.;
    color = lerp(color, float3(0.560,0.291,0.005),xx);
    //gl_FragColor = float4((f*f*f+.6*f*f+.5*f)*color,1.);
    //gl_FragColor = float4(xx,0,0,1.);
    return color;
    */
}







// Grab from https://www.shadertoy.com/view/4djSRW
#define MOD3 float3(.1031,.11369,.13787)
//#define MOD3 float3(443.8975,397.2973, 491.1871)
float hash31(float3 p3) 
{
	p3  = frac(p3 * MOD3);
    p3 += dot(p3, p3.yzx + 19.19);
    return -1.0 + 2.0 * frac((p3.x + p3.y) * p3.z);
}

float3 hash33(float3 p3)
{
	p3 = frac(p3 * MOD3);
    p3 += dot(p3, p3.yxz+19.19);
    return -1.0 + 2.0 * frac(float3((p3.x + p3.y)*p3.z, (p3.x+p3.z)*p3.y, (p3.y+p3.z)*p3.x));
}

float simplex_noise(float3 p)
{
    const float K1 = 0.333333333;
    const float K2 = 0.166666667;
    
    float3 i = floor(p + (p.x + p.y + p.z) * K1);
    float3 d0 = p - (i - (i.x + i.y + i.z) * K2);
    
    // thx nikita: https://www.shadertoy.com/view/XsX3zB
    float3 e = step(float3(0,0,0), d0 - d0.yzx);
	float3 i1 = e * (1.0 - e.zxy,1.0 - e.zxy,1.0 - e.zxy);
	float3 i2 = 1.0 - e.zxy * (1.0 - e);
    
    float3 d1 = d0 - (i1 - 1.0 * K2);
    float3 d2 = d0 - (i2 - 2.0 * K2);
    float3 d3 = d0 - (1.0 - 3.0 * K2);
    
    float4 h = max(0.6 - float4(dot(d0, d0), dot(d1, d1), dot(d2, d2), dot(d3, d3)), 0.0);
    float4 n = h * h * h * h * float4(dot(d0, hash33(i)), dot(d1, hash33(i + i1)), dot(d2, hash33(i + i2)), dot(d3, hash33(i + 1.0)));
    
    return dot(float4(31.316, 31.316, 31.316, 31.316), n);
}

float noise(float3 p) {
#ifdef Use_Perlin
    return perlin_noise(p * 2.0);
#elif defined Use_Value
    return value_noise(p * 2.0);
#elif defined Use_Simplex
    return simplex_noise(p*1);
#endif
    
    return 0.0;
}

// ========== Different function =============================================================================

float noise_itself(float3 p)
{
    return noise(p * 8.0);
}

float noise_sum(float3 p)
{
    float f = 0.0;
    p = p * 4.0;
    f += 1.0000 * noise(p); p = 2.0 * p;
    f += 0.5000 * noise(p); p = 2.0 * p;
	f += 0.2500 * noise(p); p = 2.0 * p;
	f += 0.1250 * noise(p); p = 2.0 * p;
	f += 0.0625 * noise(p); p = 2.0 * p;
    
    return f;
}

float noise_sum_abs(float3 p)
{
    float f = 0.0;
    p = p * 1.0;
    f += 1.0000 * abs(noise(p)); p = 2.0 * p;
    f += 0.5000 * abs(noise(p)); p = 2.0 * p;
	f += 0.2500 * abs(noise(p)); p = 2.0 * p;
	f += 0.1250 * abs(noise(p)); p = 2.0 * p;
	f += 0.0625 * abs(noise(p)); p = 2.0 * p;
    
    return f;
}


float noise_sum_abs_sin(float3 p)
{
    float f = noise_sum_abs(p);    
    return f ;
}


float4 psMain(vsOutput psInput) : SV_TARGET
{    

	float2 uv = psInput.texCoord; 

    /*
    float3 pos = float3(uv, beatTime * 0.1);
    float f = noise_sum_abs(pos);
    float f2 = noise_sum_abs(pos /2 + float3(1,1,0));
    f *= sin(f2)/2 + 0.5;
    //float f = noise_sum_abs(pos);
    //float f = noise_itself(pos);
    //float f = noise_sum(pos);
    //float3 col = getNoise(p);    
    return float4(f,f,f, 1.0);
    */

    float3 col = domainNoise(uv);
    return float4(col,1);
}