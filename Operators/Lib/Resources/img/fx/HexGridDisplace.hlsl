cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float2 Offset;
    float Divisions;
    float LineThickness;    
    float MixOriginal;
    float Rotation;
}

cbuffer TimeConstants : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> ImageA : register(t0);
Texture2D<float> Effects : register(t1);
sampler texSampler : register(s0);

//#define mod(x, y) (x - y * floor(x / y))

float mod(float x, float y) {
    return (x - y * floor(x / y));
} 


float2 mod(float2 x, float2 y) {
    return (x - y * floor(x / y));
} 

// Based on "ShaderToy Tutorial - Hexagonal Tiling" 
// by Martijn Steinrucken aka BigWings/CountFrolic - 2019
// License Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.
//
// This shader is part of a tutorial on YouTube
// https://youtu.be/VmrIDyYiJBA

float HexDist(float2 p) {
	p = abs(p);
    
    float c = dot(p, normalize(float2(1,1.73)));
    c = max(c, p.x);
    
    return c;
}

float4 HexCoords(float2 uv) {
	float2 r = float2(1, 1.73);
    float2 h = r*.5;
    
    float2 a = mod(uv, r)-h;
    float2 b = mod(uv-h, r)-h;
    
    float2 gv = dot(a, a) < dot(b,b) ? a : b;
    
    float x = atan2(gv.x, gv.y);
    float y = .5-HexDist(gv);
    float2 id = uv-gv;
    return float4(x, y, id.x,id.y);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{   
    float2 uv = psInput.texCoord;
    float aspectRatio = TargetWidth/TargetHeight;
    float2 divisions = float2(Divisions * aspectRatio, Divisions);
    float2 p = psInput.texCoord;
    float2 cellOffset = Offset/ Divisions;
    
    
    p-= 0.5;

    p += cellOffset;
    

    // float sina = sin(angle);
    // float cosa = cos(angle);

    // p = float2(
    //     cosa * p.x - sina * p.y,
    //     cosa * p.y + sina * p.x 
    // );

    // Rotate
    float imageRotationRad = (-Rotation - 90) / 180 *3.141578;     

    float sina = sin(-imageRotationRad - 3.141578/2);
    float cosa = cos(-imageRotationRad - 3.141578/2);

    p.x *=aspectRatio;

    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x 
    );

    p.x /=aspectRatio;


    p *= divisions;

    float4 col = float4(0,0,0,0);
    float4 hc = HexCoords(p);    

    // float sinBackA = sin(-angle);
    // float cosBackA = cos(-angle);

    // float2 uv = (hc.zw /Divisions  + 0.5 - cellOffset); 
    // uv -= (0.5 - Offset / Divisions);
    // uv = float2(
    //     cosBackA * uv.x - sinBackA * uv.y,
    //     cosBackA * uv.y + sinBackA * uv.x 
    // );
    // uv += (0.5- Offset / Divisions);
    // uv *=  float2(aspectRatio,1);


    //return float4(hc.zw,0,1);
    // float2 p1 = hc.xy+Offset * float2(-1,1)/divisions;
    // float2 gridSize = float2( 1/divisions.x, 1/divisions.y);
    // float2 pShifted  = p1 -  gridSize/2;
    // float2 pInCell2 = mod(pShifted, gridSize);
    //return float4(pInCell2,0,1);

    //float2 pCel= p.xy-pInCell2 + gridSize/2;
    float2 pCel = (hc.zw + Offset) / divisions;
    float sina2 = sin(-(-imageRotationRad  - 3.141578/2));
    float cosa2 = cos(-(-imageRotationRad - 3.141578/2));

    pCel.x*= aspectRatio;
    pCel = float2(
        cosa2 * pCel.x - sina2 * pCel.y,
        cosa2 * pCel.y + sina2 * pCel.x 
    );
    pCel.x /= aspectRatio;
    pCel += 0.5;

    //return float4(pCel,0,1);

    float4 imgColorForCel = ImageA.Sample(texSampler, pCel);
    float value = length(imgColorForCel.rgb);
    //return imgColorForCel;

    float edgeEffect = Effects.Sample(texSampler, float2(value,0.75)) ;
    value = Effects.Sample(texSampler, float2(value,0)) ;

    float c = smoothstep(.001, LineThickness / 100 + edgeEffect, hc.y * value ) * (value);   
    c = clamp(c,0,1); 
    col = lerp(Background, Fill,c);

    float4 orgColorWithDisplacement = ImageA.Sample(texSampler, uv );
    col = lerp(col, orgColorWithDisplacement, MixOriginal);
    return float4(col);
}