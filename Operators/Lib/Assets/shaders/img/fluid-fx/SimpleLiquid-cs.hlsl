#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float2 MousePos;
    float MousePressed;
    float Clear;

    float2 Gravity;
    float BorderStrength;    
    float Damping;

    float MassAttraction;
    float Brightness;
    float StabilizeMass;
    float StabilizeMassTarget;

    float4 ApplyFxTexture;
}


sampler texSampler : register(s0);
Texture2D<float4> FxTexture : register(t0);

RWTexture2D<float4> BufferA  : register(u0); 
RWTexture2D<float4> BufferB  : register(u1); 
RWTexture2D<float4> ColorOutput  : register(u2); 
//RWTexture2D<float4> BufferBRead  : register(u2); 


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

    float border = BorderStrength;
    float2 velocity = a.xy * Damping                      //fluid velocity
             +Gravity             //gravity
             +float(uv.x<.05)*float2(1,0)*border   //wall
             +float(uv.y<.05)*float2(0,1)*border  //wall
             -float(uv.x>.95)*float2(1,0)*border  //wall
             -float(uv.y>.95)*float2(0,1)*border; //wall


    float s = 0;
    float maxSteps = 4;//maxStepsernel convolution size
    for(float i=-maxSteps; i<=maxSteps; ++i)
    {
        for(float j=-maxSteps; j<=maxSteps; ++j)
        {
            float2 c = -velocity + float2(i,j) ;//translate the gaussian 2Dimage using the velocity
            s += exp(-dot(c,c));  //calculate the gaussian 2Dimage
        }
    }

    if(s==0.){        s = 1.;    }      //avoid division by zero
    s = 1./s;

    BufferA[DTid.xy] = float4(velocity, s,1) ;   //velocity in .xy
                                                 //convolution normalization in .z
}
 

[numthreads(32,4,1)]
void main2(uint3 DTid : SV_DispatchThreadID)
{   
    uint width, height;
    BufferA.GetDimensions(width, height);
    if(DTid.x >= width || DTid.y >= height)
         return;

    float2 uv = DTid.xy / float2(width,height);

    float4 o = 0;
    int steps = 4;           
    int2 d =0;                    //kernel convolution size
    for(d.x=-steps; d.x<=steps; ++d.x)
    {
        for(d.y=-steps; d.y<=steps; ++d.y)
        {
            int2 p = DTid.xy + d;
            p=max(0, min(p, int2(width,height)-1));
            float4  a = BufferA[p];       //old velocity in a.xy, mass in a.z
            float4  b = BufferB[p];   //new velocity in b.xy, normalization of convolution in .z
            float2  c = -b.xy - d;  //translate the gaussian 2Dimage
            float s = a.z*exp(-dot(c,c))*b.z;       //calculate the normalized gaussian 2Dimage multiplied by mass
            float2  e = c*(a.z- MassAttraction);                 //fluid expands or attracts itself depending on mass
            o.xy += s*(b.xy+e);                     //sum all translated velocities
            o.z  += s;                              //sum all translated masses
        }
    }

    float tz = 1./o.z;
    if(o.z==0.){    tz = 0.;  }        //avoid division by zero
    o.xy *= tz;                        //calculate the average velocity

    o.b = lerp(o.b, StabilizeMassTarget, StabilizeMass);
    
    {
        int edgeWidth = 1;

        if(DTid.x <= edgeWidth 
        || DTid.x >= width - edgeWidth 
        || DTid.y <= edgeWidth 
        || DTid.y >= height - edgeWidth ) 
        {
            o.rgb= 0;
        }
    }


    if(MousePressed > 0.5)
    {
        float2 resolution = float2(width, height);
        float2 uv= DTid.xy / resolution;
        float2 m = 3.*(uv-.5);
        //o = float4(0,0,1,1)*exp(-dot(m,m));        

        float2 m2 = 8.*(uv- MousePos);
        o += float4(m2,0,0)*.4*exp(-dot(m2,m2)) * 0.6;

        // float2 m = 3.*(uv-.5);
        // o = float4(0,1,1,1)*exp(-dot(m,m)) * 0.1; 
        // o.xy = m*1;
        // BufferB[DTid.xy] = o;
    }
    o.a = 1;

    // Apply FX Texture
    float4 fx = FxTexture.SampleLevel(texSampler, uv,0);

    
    o.rgb = lerp(o.rgb, fx.rgb, ApplyFxTexture.rgb * ApplyFxTexture.a * fx.a);

    BufferB[DTid.xy] = o;



    
    // Write color output
    float4 a = BufferB[DTid.xy];
    float4 c = a.z*(+sin(a.x*4.+float4(1,3,5,4))*.2
                     +sin(a.y*4.+float4(1,3,2,4))*.2+.6);
    c.a=1;
    c.rgb*= Brightness;
    ColorOutput[DTid.xy]= c;    
}
