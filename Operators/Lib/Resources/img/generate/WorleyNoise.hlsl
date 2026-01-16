#include "shared/bias-functions.hlsl"
#include "shared/blend-functions.hlsl"

// This shader is based on a ShaderToy Project by jamelouis https://www.shadertoy.com/view/3dXyRl 
// Ported to TiXL by Newemka (so you know who to blame)

cbuffer ParamConstants : register(b0)
{
    float4 ColorA;
    float4 ColorB;

    float2 Offset;
    float2 Stretch;

    float Scale;
    float Phase;
    float2 Clamping;

    float2 GainAndBias;
    float Method;
    float Randomness; //12.6
    float FxTextureBlend;
    //float BlendMode;
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

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);


// from https://www.shadertoy.com/view/3dXyRl

#define UIF (1.0 / float(0xffffffffU))



/* float2 hash22(float2 p)//Dave Hoskins https://www.shadertoy.com/view/4djSRW
{
    //return frac(cos(mul(p,float2x2(-64.2,71.3,81.4,-29.8)) * 8321.3)); 
    return frac(cos(mul(p,float2x2(-64.2,71.3,81.4,-29.8)) * (Phase + 8321.3)));
} */

// The original hash was not looking good enough imo

// This one is gorgeous!! Hash22 from Hash without Sine 2 https://www.shadertoy.com/view/XdGfRR 
float2 hash22(float2 p)
{
	uint2 q = uint2(int2(p))*uint2(1597334673U, 3812015801U);
	q = (q.x ^ q.y) * uint2(1597334673U, 3812015801U);
	return float2(q) * UIF + Phase; // It's amazing + Phase is doing a perfect job
}



float fmod(float x, float y)
{
    return (x - y * floor(x / y));
}

 

float4 psMain(vsOutput psInput) : SV_TARGET
{
    // Get the dimensions of the image texture
    float width, height;
    inputTexture.GetDimensions(width, height);
    float2 resolution = float2(width, height);

    
    float2 offset = Offset * float2(1,-1) *Scale *Stretch ; 
    
    float aspectRatio = TargetWidth / TargetHeight;
    float2 uv = psInput.texCoord ;
    float2 uvI = psInput.texCoord ;
    uv += 0.5;
    uv *=Stretch * Scale ;
    uv.x *= aspectRatio;


    // Worley code begins
    int wt = 0;
    int f2t = 0;

    //worley F1
    if (Method < 1){
        
        wt = 0;
        f2t = 0;
    }
    //manhattan worley F1
    else if (Method < 2){
        wt = 1;
        f2t = 0;
    }
    //chebyshev worley F1 
    else if (Method < 3){
        wt = 2;
        f2t = 0;
    }
    //worley F2-F1
    else if (Method < 4){
        wt = 0;
        f2t = 1;
    }
    //manhattan worley F2-F1
    else if (Method < 5){
        wt = 1;
        f2t = 1;
    }
    //chebyshev worley F2-F1
    else if (Method < 6){
        wt = 2;
        f2t = 1;
    }
    float2 q = uv - offset;
   // q = (q/Scale) ;
    float f1 = 9e9;
    float f2 = f1;
    float2 cellCenter;
    for(int i = -1; i < 2; i++){
        for(int j = -1; j < 2; j++){
            float2 p = floor(q) + float2(i, j);
            
            float2 h = hash22(p);
            
            //float2 h = inputTexture.SampleLevel(texSampler, psInput.texCoord,6).xx * hash22(p) ; // Just in case you want to use the texture to influence the noise
            float2 g = p + 0.5 + 0.5 * sin(h*Randomness);
            
            float d = f1;
            
            if(wt == 0) {
                d = distance(g,q);
               
            }else if(wt == 2) {
                float xx = abs(q.x-g.x);
                float yy = abs(q.y-g.y);
                d = max(xx, yy);
            } else{
                float xx = abs(q.x-g.x);
                float yy = abs(q.y-g.y);
                d = xx + yy;
            }
            if(d < f2){f2 =d;}

            if(d < f1){
                f2 = f1;
                f1 = d;
                cellCenter = g;
                
            }
        }
    }
    float worleyValue = (f2t == 0) ? f1 : f2 - f1;
    
    // Sample the texture at the cell center
    //float2 sampleUV = cellCenter - Offset;
    // sampleUV = (-1*sampleUV) *(-1*Scale*Stretch);
        //sampleUV.x /= aspectRatio;
        //sampleUV +=.5 ;
        //sampleUV *= float2(1,-1);
        //float2 offsetInv = Offset ;
    float2 sampleUV = cellCenter + offset ;
    sampleUV /= Stretch * Scale; 
    sampleUV.x /= aspectRatio;
    sampleUV -=.5 ;
    
    
	
    //float4 textureValue = inputTexture.SampleLevel(texSampler, sampleUV, 0);
    float4 textureValue = inputTexture.SampleLevel(texSampler, sampleUV, 0);
    textureValue.rgb = textureValue.rgb*FxTextureBlend;

    worleyValue = ApplyGainAndBias(worleyValue, GainAndBias);
    
    float4 worleyNoise = lerp(ColorB, ColorA, clamp(worleyValue, Clamping.x, Clamping.y));
 
    float3 blended = worleyNoise.rgb * textureValue.rgb;// *FxTextureBlend;

    return (IsTextureValid < 0.5) ? lerp(ColorB, ColorA, clamp(worleyValue, Clamping.x, Clamping.y)) : float4(blended,worleyNoise.a);
}