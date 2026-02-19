//RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 Gain;
    float4 Gamma;
    float4 Lift;
    float4 VignetteColor;
    float2 VignetteCenter;
    float VignetteRadius;
    float VignetteBias;
    float PreSaturate;
}


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};


float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 c=inputTexture.Sample(texSampler, uv);
    c.rgb = clamp( c.rgb, 0.000001,1000);

    // Saturation
    float gray = (c.r *0.22 + c.g*0.707 + c.b*0.071);
    c.rgb = lerp( float3(gray, gray, gray), c.rgb, PreSaturate); 

    // Vignette
    float flipEdge =  VignetteRadius < 0 ? -1 : 1;

    float v = length( (uv - 0.5 - VignetteCenter * float2(1,-1)));
    v /= VignetteRadius * flipEdge/2;
    v -= 0.5;
    v = smoothstep(0,1,(v- 0.5) / (VignetteBias* flipEdge*2) + 0.5);

    // Grade        
    float3 liftScaled =   Lift.rgb * 2*Lift.a + (0.5-Lift.a);
    float3 gammaScaled =  Gamma.rgb * 2*Gamma.a + (0.5-Gamma.a);
    float3 gainScaled =   Gain.rgb * 2*Gain.a + (0.5-Gain.a);
    gainScaled += (VignetteColor.rgb-0.5) * v * (VignetteColor.a*2+1);
    
    c.rgb=  pow((c.rgb + (liftScaled.rgb * 2 - 1 ) * (1-c.rgb))  // Lift
                 * gainScaled * 2,                               // Gain                     
                   1/(gammaScaled * 2));
       
    c.rgb = clamp(c.rgb, 0.000001,1000);        
    c.a = clamp(c.a,0,1);
    return c;
}
