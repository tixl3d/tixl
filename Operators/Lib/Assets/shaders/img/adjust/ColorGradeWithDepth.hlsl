Texture2D<float4> InputTexture : register(t0);
Texture2D<float> DepthBuffer : register(t1);
Texture2D<float4> Gradient : register(t2);

sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 Gain;
    float4 Gamma;               // 4
    float4 Lift;                // 8
    float4 VignetteColor;       // 12
    float2 VignetteCenter;      // 16
    float VignetteRadius;       // 18
    float VignetteBias;         // 19
    float2 GradientDepthRange;  // 20
    float NearClip;             // 22
    float FarClip;              // 23
    float PreSaturate;          // 24
}


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

float DepthToSceneZ(float depth) 
{
    float n = NearClip;
    float f = FarClip;
    return (2.0 * n) / (f + n - depth * (f - n)) * (FarClip-NearClip) + NearClip;    
}


float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 c=InputTexture.Sample(texSampler, uv);
    c.rgb = clamp( c.rgb, 0.000001,1000);

    float depth = DepthBuffer.SampleLevel(texSampler, uv,0 ).r;
    float z = DepthToSceneZ(depth);
    float normalizedZ = saturate((z - GradientDepthRange.x) / (GradientDepthRange.y - GradientDepthRange.x));

    //return float4(normalizedZ.xxx ,1);

    float4 gradientColor = Gradient.SampleLevel(texSampler, float2(normalizedZ, 0.5),0);


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
    gainScaled += (VignetteColor.rgb-0.5) * v * (VignetteColor.a*2+1)
               + (gradientColor.rgb - 0.5) * (gradientColor.a*2+1);
    
    
    c.rgb=  pow((c.rgb + (liftScaled.rgb * 2 - 1 ) * (1-c.rgb))  // Lift
                 * gainScaled * 2,                               // Gain                     
                   1/(gammaScaled * 2));
       
    c.rgb = clamp(c.rgb, 0.000001,1000);        
    c.a = clamp(c.a,0,1);


    return c;
}
