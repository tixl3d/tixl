// This shader is directly ported
// from lomateron's excellent "Simple detailed fluid":  https://www.shadertoy.com/view/sl3Szs

#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float2 MousePos;
    float MousePressed;
    float TriggerReset;

    float2 Gravity;  //4
    float BorderStrength;    
    float MassAttraction;

    float ApplyFxTexture;    // 8
    float FxRG_Acceleration; // 9
    float FxB_Mass;          // 10

    float SpeedFactor;       // 11
    float StabilizeFactor;   // 12
    float ResetFillFactor;

    float MouseClick_Force;
    float MouseClick_Fill;
}


sampler texSampler : register(s0);
Texture2D<float4> FxTexture : register(t0);

RWTexture2D<float4> BufferA  : register(u0); 
RWTexture2D<float4> BufferB  : register(u1); 
RWTexture2D<float4> BufferBRead  : register(u2); 
RWTexture2D<float4> ColorOutput  : register(u3); 

static const int MaxSteps = 2;

[numthreads(32,32,1)]
void main1(uint3 DTid : SV_DispatchThreadID)
{   
    uint width, height;
    BufferB.GetDimensions(width, height);
    if(DTid.x >= width || DTid.y >= height)
        return;

    float2 resolution = float2(width, height);
    float2 uv = DTid.xy / resolution;
    
    float4 a = BufferB[DTid.xy];

    float2 velocity = a.xy;                      //fluid velocity

    float border = BorderStrength;
    float borderWidth = 0.05;
    velocity +=Gravity             //gravity
             +float(uv.x < borderWidth)*float2(1,0)*border   //wall
             +float(uv.y < borderWidth)*float2(0,1)*border  //wall
             -float(uv.x > 1 - borderWidth)*float2(1,0)*border  //wall
             -float(uv.y > 1 - borderWidth)*float2(0,1)*border; //wall

    float s = 0;
    //float maxSteps = 3;                          // maxStepsernel convolution size
    for(float i=-MaxSteps; i<=MaxSteps; ++i)
    {
        for(float j=-MaxSteps; j<=MaxSteps; ++j)
        {
            float2 c = -velocity + float2(i,j) ; // translate the gaussian 2Dimage using the velocity
            s += exp(-dot(c,c));                 // calculate the gaussian 2Dimage
        }
    }

    if(s==0.){ s = 1.; }                         // avoid division by zero
    s = 1./s;

    BufferA[DTid.xy] = float4(velocity, s,1) ;   // velocity in .xy
                                                 // convolution normalization in .z
}
 

[numthreads(32,32,1)]
void main2(uint3 DTid : SV_DispatchThreadID)
{   
    uint width, height;
    BufferA.GetDimensions(width, height);
    if(DTid.x >= width || DTid.y >= height)
         return;

    float2 uv = DTid.xy / float2(width,height);

    float4 o = 0;
    //int steps = 3;           
    int2 d =0;                                      // kernel convolution size
    for(d.x=-MaxSteps; d.x<=MaxSteps; ++d.x)
    {
        for(d.y=-MaxSteps; d.y<=MaxSteps; ++d.y)
        {
            int2 p = DTid.xy + d;
            p=max(0, min(p, int2(width,height)-1));
            float4  a = BufferBRead[p];             // old velocity in a.xy, mass in a.z
            float4  b = BufferA[p];                 // new velocity in b.xy, normalization of convolution in .z
            float2  c = -b.xy - d;                  // translate the gaussian 2Dimage
            float s = a.z*exp(-dot(c,c))*b.z;       // calculate the normalized gaussian 2Dimage multiplied by mass
            float2  e = c*(a.z- MassAttraction - 0.2);    // fluid expands or attracts itself depending on mass
            o.xy += s*(b.xy+e);                     // sum all translated velocities
            o.z  += s;                              // sum all translated masses
        }
    }

    float tz = 1./o.z;
    if(o.z==0.){    tz = 0.;  }                     // avoid division by zero
    o.xy *= tz;                                     // calculate the average velocity

    // Clear Edge to avoid artifacts    
    uint edgeWidth = 1;

    if(DTid.x <= edgeWidth 
    || DTid.x >= width - edgeWidth 
    || DTid.y <= edgeWidth 
    || DTid.y >= height - edgeWidth ) 
    {
        o.rgb= 0;
    }
    
    // Apply FX Texture
    float4 fx = FxTexture.SampleLevel(texSampler, uv,0);

    o.b += fx.b * FxB_Mass * ApplyFxTexture * fx.a;
    o.xy += (fx.xy  -0.5) * FxRG_Acceleration * ApplyFxTexture * fx.a;
    o.xy *= SpeedFactor;
    
    // Stabilize at one
    float f = StabilizeFactor -1;
    o.z *= o.z < 1 ?  1 +f : 1-f;
    
    // Apply force effects
    if(MousePressed > 0.5)
    {
        float2 m = 3.*(uv-.5);
        float2 m2 = 8.*(uv- MousePos);
        m2.x *= (float)width/height;
        
        o += float4(m2,0,0)*.4*exp(-dot(m2,m2)) * MouseClick_Force;
        o.z += smoothstep(1,0, length(m2) ) * MouseClick_Fill;

        //o += float4(0, 0, length(m2),0) * MouseClick_Fill;


    }

    if(TriggerReset > 0.5) {
        ColorOutput[DTid.xy]= float4(1,1,0,1);    
        float2 m = 3.*(uv-.5);
        o = float4(0,1,1,1)*exp(-dot(m,m)) * ResetFillFactor; 
        o.xy = m*1;
        BufferB[DTid.xy] = o;        
    }
    //o.a = 1;

    BufferB[DTid.xy] = o;


    // Write color output
    float4 a = BufferB[DTid.xy];
    float4 c = a.z*(+sin(a.x*4.+float4(1,3,5,4))*.2
                     +sin(a.y*4.+float4(1,3,2,4))*.2+.6);
    c.a=1;
    ColorOutput[DTid.xy]= c;    


    ///-------  
    {   
        float2 p = 0.5;     
        float2 m2 = 8.*(uv- p);
        float2 pp = uv- MousePos;
        
        float k = smoothstep(1,0, 8 * length(pp) );
    }
    
}
