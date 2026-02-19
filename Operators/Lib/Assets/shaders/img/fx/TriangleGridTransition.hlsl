cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float2 Offset;
    float Divisions;
    float LineThickness;    
    float MixOriginal;
    float Rotation;
    float EffectWidth;
    float Scatter;
    float Gap;
    float2 EffectCenter;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

cbuffer TimeConstants : register(b2)
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
Texture2D<float4> ImageB : register(t1);
sampler texSampler : register(s0);

#define mod(x, y) ((x) - (y) * floor((x) / (y)))



#define PI 3.1415926535897

#define rot(a) float2x2(cos(a),-sin(a),sin(a),cos(a))



float2 hash22(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx+33.33);
    return frac((p3.xx+p3.yz)*p3.zy);
}


float4 psMain(vsOutput psInput) : SV_TARGET
{   
    
    float aspectRatio = TargetWidth/TargetHeight;
    float2 p = psInput.texCoord;
    float2 cellOffset = Offset/ Divisions;
    
    p-= 0.5;

    p += cellOffset;
    p.x *=aspectRatio;

    // Effect ramp
    float radians = Rotation / 180 *3.141578;
    float2 angle =  float2(sin(radians),cos(radians));
    float fx=  dot(p-EffectCenter, angle) / EffectWidth / 2 + 0.5;


    // Triange grid
    float2 step =  float2( 1 , sin(PI/3))/ Divisions;
    float2 halfStep = step/2;
    float2 oddRowOffsetOffset = -float2( mod(floor(p/step).y,2.) * halfStep.x , 0.);

    float2 posInCell = mod(p+oddRowOffsetOffset, step);

    float2 bottomCenter = (posInCell -float2(halfStep.x, 0)) / step;
    float2 topCenter = (posInCell -float2(0, step.y)) / step;

    float2 line1 = mul(bottomCenter, rot(1.107));
    float2 line2 = mul(bottomCenter, rot(-1.107));
    float isPointingDown = line1.y >= 0 && line2.y >= 0 ? 0 :1;
    float isRightSide = bottomCenter.x > 0 ? 1 :0;
    float2 line3 = lerp(topCenter, bottomCenter, isPointingDown);

    float2 posInTriange = posInCell + isPointingDown * 
             (float2(isPointingDown * (isRightSide ? -1 : 1) * halfStep.x  ,0)
             );

    float2 cellId = p-posInTriange;

    float distanceFromEdge = min( abs(line1.y), min(abs(line2.y), abs(line3.y)));

    float2 hash = hash22(cellId+2000);

    float blendFx=  dot(p-EffectCenter - posInTriange, angle) / EffectWidth   + Scatter * (hash.y - 0.5);

    if(abs(blendFx) < Gap) {
        return Background;
    }

    float4 orgColor = blendFx < 0 
        ? ImageA.Sample(texSampler, psInput.texCoord)
        : ImageB.Sample(texSampler, psInput.texCoord);

    float2 uv = psInput.texCoord;
    float cellFx = 1-saturate( abs(blendFx)-Gap);

    float celShade = sin((beatTime + hash * 999) ) +1;
    float lineThickness = -0.02+LineThickness* cellFx;
    float edge = smoothstep(lineThickness+0.01, lineThickness + 0.02, distanceFromEdge);
    lineThickness += 0.02;
    float highlightEdge= smoothstep(lineThickness+0.01, lineThickness + 0.02, distanceFromEdge) * cellFx;
    //return float4(highlightEdge -edge,0,0,1);
    orgColor = lerp(orgColor, Fill, highlightEdge); 

    float fxShade = lerp(1, saturate(celShade-cellFx), saturate(cellFx));
    return lerp(Background, orgColor,(1-saturate(cellFx)) *edge);
}