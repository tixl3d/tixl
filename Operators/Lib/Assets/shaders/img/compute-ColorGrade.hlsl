//RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 Gain;
    float4 Gamma;
    float4 Lift;
    float PreSaturate;
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

float4 psMain(vsOutput psInput) : SV_TARGET
{
    //uint width, height;
    //outputTexture.GetDimensions(width, height);

    //float2 uv = (float2)i.xy/ float2(width - 1, height - 1);
    float2 uv = psInput.texCoord;
    float4 c = inputTexture.SampleLevel(texSampler, uv, 0.0);

    float a = c.a;
    c.rgb = clamp( c.rgb, 0.000001,1000);

    // Saturation
    float gray = (c.r *0.22 + c.g*0.707 + c.b*0.071);
    c.rgb = lerp( float3(gray, gray,gray), c.rgb, PreSaturate); 
         
    // Grade        
    float3 liftScaled =   Lift * 2*Lift.a + (0.5-Lift.a);
    float3 gammaScaled =   Gamma * 2*Gamma.a + (0.5-Gamma.a);
    float3 gainScaled =   Gain * 2*Gain.a + (0.5-Gain.a);
    
    c.rgb=  pow( 
                   ( c.rgb+ (liftScaled * 2-1)*(1-c))*      // Lift
                   ( gainScaled * 2 )  // Gain
                   ,    
                   1/((gammaScaled*2)));        
    
   
    c.rgb = clamp(c.rgb, 0.000001,1000);

    // PostSaturate (Deprecated because confusing)
    //float gray2 = (c.r *0.22 + c.g*0.707 + c.b*0.071);
    //c.rgb = lerp( float3(gray2, gray2,gray2), c.rgb, PostSaturate); 
    //c.rgb = clamp(c.rgb, 0.000001,1000);
        

    c.a = a;
    
	//outputTexture[i.xy] = c;
    return c; 
}
