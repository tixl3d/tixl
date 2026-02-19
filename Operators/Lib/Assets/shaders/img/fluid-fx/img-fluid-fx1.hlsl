cbuffer ParamConstants : register(b0)
{
    float TestParam;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD; 
};

Texture2D<float4> InputA : register(t0);
Texture2D<float4> InputB : register(t1);
Texture2D<float4> InputFx : register(t2);
sampler samLinear : register(s0);


float4 psMain(vsOutput input) : SV_TARGET
{
    //return -0.5;
    uint width,height;
    InputA.GetDimensions(width,height);
    float2 size = float2(width,height);

    float2 uv = input.texCoord;
    //return InputB.Sample(samLinear, uv); 
    float4 a = InputB.Sample( samLinear, uv);
    //return a;
    //float4 a = ImageA(u);
    float2 velocity = +a.xy *1                       //fluid velocity
             //-float2(-2,1)             //gravity
             +float(uv.x<.05)*float2(1,0)  //wall
             +float(uv.y<.05)*float2(0,1)  //wall
             -float(uv.x>.95)*float2(1,0)  //wall
             -float(uv.y>.95)*float2(0,1); //wall


    float s = 0.;
    float z = 4.;//kernel convolution size
    for(float i=-z; i<=z; ++i)
    {
        for(float j=-z; j<=z; ++j)
        {
            float2 c = -velocity + float2(i,j);//translate the gaussian 2Dimage using the velocity
            s += exp(-dot(c,c));  //calculate the gaussian 2Dimage
        }
    }

    if(s==0.){
        s = 1.;
    }      //avoid division by zero
    
    s = 1./s;
    velocity+= (InputFx.Sample(samLinear, uv) - 1)  * float4(1,1,0,0) * TestParam  *1;
    return float4(velocity, s,1) ;   //velocity in .xy
                            //convolution normalization in .z

}

