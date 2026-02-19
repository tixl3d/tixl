cbuffer ParamConstants : register(b0)
{
    float2 MousePos;
    float MousePressed;
    float TestParam;
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

Texture2D<float4> InputA : register(t0);
Texture2D<float4> InputB : register(t1);
Texture2D<float4> InputC : register(t2);
sampler samLinear : register(s0);

float4 psMain2(vsOutput input) : SV_TARGET
{
    uint width,height;
    InputB.GetDimensions(width,height);
    float2 size = float2(width,height);

    float2 uv= input.texCoord;
    float2 p = uv * size;

    float4 o = 0;
    float steps = 4;                               //kernel convolution size
    for(float dx=-steps; dx<=steps; ++dx)
    {
        for(float dy=-steps; dy<=steps; ++dy)
        {
            float4  b = InputB.Sample(samLinear, uv + float2(dx, dy) / size);        //old velocity in a.xy, mass in a.z
            float4  a = InputA.Sample(samLinear, uv + float2(dx, dy) / size);        //new velocity in b.xy, normalization of convolution in .z
            float2  c = -b.xy-float2(dx , dy) ;       //translate the gaussian 2Dimage
            float s = a.z*exp(-dot(c,c))*b.z;    //calculate the normalized gaussian 2Dimage multiplied by mass
            float2  e = c*(a.z-.8);              //fluid expands or atracts itself depending on mass
            o.xy += s*(b.xy+e);                  //sum all translated velocities
            o.z  += s;                           //sum all translated masses
        }
    }

    float tz = 1./o.z;
    if(o.z==0.){
        tz = 0.;
    }              //avoid division by zero
    o.xy *= tz;                        //calculate the average velocity

    //o.rbga = InputC.Sample(samLinear,uv);
    //o.a = 1;
    //o.b=0.1;

    // if(MousePressed>0.)                    //mouse click adds velocity
    // {
    //     float2 m = 8.*(uv- MousePos);
    //     o += float4(m,0,0)*.4*exp(-dot(m,m)) * 10;
    // }
    if(MousePressed>0 > 0.5)
    {
        float2 m = 3.*(uv-.5);
        o = float4(0,0,1,1)*exp(-dot(m,m));
    }
    //fragColor = o;
    //}    
    return o;
}
