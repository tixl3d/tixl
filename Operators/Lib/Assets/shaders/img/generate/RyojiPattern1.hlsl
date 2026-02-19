cbuffer ParamConstants : register(b0)
{
    float4 Background;
    float4 Foreground;
    float4 Highlight;
    float2 Subdivisions;
    float2 SplitProbability;
    float2 ScrollSpeed;
    float2 ScrollProbability;
    float2 Padding;
    float Contrast;
    float Iterations;
    float Seed; 
    float ForegroundRatio;
    float HighlightProbability;
    float MixOriginal;
    float HighlightSeed;    
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

Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);

#define mod(x, y) (x - y * floor(x / y))


float sdBox( in float2 p, in float2 b )
{
    float2 d = abs(p)-b;
    return length(
        max(d,float2(0,0))) + min(max(d.x,d.y),
        0.0);
}


//----------------------------------------------------------------------------------------
//  1 out, 1 in...
float hash11(float p)
{
    p = frac(p * .1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

//----------------------------------------------------------------------------------------
//  1 out, 2 in...
float hash12(float2 p)
{
	float3 p3  = frac(float3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

//----------------------------------------------------------------------------------------
//  1 out, 3 in...
float hash13(float3 p3)
{
	p3  = frac(p3 * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

//----------------------------------------------------------------------------------------
//  2 out, 1 in...
float2 hash21(float p)
{
	float3 p3 = frac(float3(p,p,p) * float3(.1031, .1030, .0973));
	p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx+p3.yz)*p3.zy);

}

//----------------------------------------------------------------------------------------
///  2 out, 2 in...
float2 hash22(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx+33.33);
    return frac((p3.xx+p3.yz)*p3.zy);

}

//----------------------------------------------------------------------------------------
///  2 out, 3 in...
float2 hash23(float3 p3)
{
	p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx+33.33);
    return frac((p3.xx+p3.yz)*p3.zy);
}

//----------------------------------------------------------------------------------------
//  3 out, 1 in...
float3 hash31(float p)
{
   float3 p3 = frac(float3(p,p,p) * float3(.1031, .1030, .0973));
   p3 += dot(p3, p3.yzx+33.33);
   return frac((p3.xxy+p3.yzz)*p3.zyx); 
}


//----------------------------------------------------------------------------------------
///  3 out, 2 in...
float3 hash32(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz+33.33);
    return frac((p3.xxy+p3.yzz)*p3.zyx);
}

//----------------------------------------------------------------------------------------
///  3 out, 3 in...
float3 hash33(float3 p3)
{
	p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz+33.33);
    return frac((p3.xxy + p3.yxx)*p3.zyx);

}

//----------------------------------------------------------------------------------------
// 4 out, 1 in...
float4 hash41(float p)
{
	float4 p4 = frac(float4(p,p,p,p) * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy+33.33);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);
    
}

//----------------------------------------------------------------------------------------
// 4 out, 2 in...
float4 hash42(float2 p)
{
	float4 p4 = frac(float4(p.xyxy) * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy+33.33);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);

}

//----------------------------------------------------------------------------------------
// 4 out, 3 in...
float4 hash43(float3 p)
{
	float4 p4 = frac(float4(p.xyzx)  * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy+33.33);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);
}

//----------------------------------------------------------------------------------------
// 4 out, 4 in...
float4 hash44(float4 p4)
{
	p4 = frac(p4  * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy+33.33);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);
}


static float2 P;

float4 subDivideCel(float4 cel, float2 splitProbability) 
{
    float4 orgCel = cel;
    float2 hash = hash22(cel.xy + float2(Seed+0.1, cel.w) - cel.zw);

    float2 scrollFactor = hash > ScrollProbability ? 0: hash;
    float2 randomShift =(beatTime * ScrollSpeed  - orgCel.zw) * scrollFactor; //2
    P= frac(P);
    P -= randomShift;    


    if(hash.x > splitProbability.x && hash.y > splitProbability.y ) 
        return cel;

    // Subdivide
    cel.zw /= float2( 
        hash.x < splitProbability.x ? Subdivisions.x : 1,
        hash.y < splitProbability.y ? Subdivisions.y : 1);

    float2 positionInCel= P - cel.xy;
    float2 splitAlignedPosition = floor(positionInCel / cel.zw) * cel.zw;
    cel.xy += splitAlignedPosition;

    return cel;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{    
    P = psInput.texCoord;

    float4 cel = float4(0,0,1,1);
    int steps = min( Iterations, 10);
    cel = subDivideCel(cel, float2(1,1));
    for(int i=1; i < steps; i ++) {
        cel = subDivideCel(cel, SplitProbability);
    }
    
    float2 pp = P - cel.xy;
    float2 posInCel = mod(pp, cel.zw);
    //return float4(posInCel*1,0,1);
    if(posInCel.x < Padding.x * 0.1 || posInCel.y < Padding.y * 0.1){
        return Background;
    }
    
    float hashForCel = hash12(cel.xy + float2(Seed+0.1 , cel.w));
    
    float4 originalColor = ImageA.Sample(texSampler, P);
    float gray = lerp(
                    hashForCel,   
                    hashForCel > ForegroundRatio ? 0:1,
                    Contrast);
                    
    float4 color =  lerp(Background, lerp(Foreground, originalColor, MixOriginal), gray);
    float hashForCelHighlight =   hash11(hashForCel+ HighlightSeed);
    if(hashForCelHighlight < HighlightProbability) {
        color = Highlight;
    }
    return clamp(color,0,float4(100,100,100,1));
}