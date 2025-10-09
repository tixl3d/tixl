//#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float Impact;
    float SampleRadius;
    float Twist;
    float Mode;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

cbuffer Resolution : register(b2)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};


Texture2D<float4> DisplaceMap : register(t0);
sampler texSampler : register(s0);

const static float Gray_ToRGB=0.5;
const static float Gray_ToRGBNeg=1.5;
const static float Gray_ToAngleAndMagnitude=2.5;
const static float Red_ToRG_KeepBA=3.5;

float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}

#define mod(x, y) ((x) - (y) * floor((x) / (y)))

float4 psMain(vsOutput psInput) : SV_TARGET
{       
    float displaceMapWidth, displaceMapHeight;
    DisplaceMap.GetDimensions(displaceMapWidth, displaceMapHeight);

    float2 uv = psInput.texCoord;

    float sx = SampleRadius / (float)displaceMapWidth;
    float sy = SampleRadius / (float)displaceMapHeight;
    
    float4 cx1= DisplaceMap.Sample(texSampler,  float2(uv.x + sx, uv.y));
    float4 cx2= DisplaceMap.Sample(texSampler,  float2(uv.x - sx, uv.y)); 
    float4 cy1= DisplaceMap.Sample(texSampler, float2(uv.x,       uv.y + sy));
    float4 cy2= DisplaceMap.Sample(texSampler, float2(uv.x,       uv.y - sy));    

    float grayX1= (cx1.r + cx1.g + cx1.b) / 3;
    float grayX2= (cx2.r + cx2.g + cx2.b) / 3;
    float grayY1= (cy1.r + cy1.g + cy1.b) / 3;
    float grayY2= (cy2.r + cy2.g + cy2.b) / 3;

    float2 d = Mode > Red_ToRG_KeepBA
            ? float2( (grayX1-grayX2) , (grayY1-grayY2)) 
            : float2( (cx1.r-cx2.r) , (cy1.r-cy2.r)) ;

    float4 uvImage = DisplaceMap.Sample(texSampler, uv);
    float angle = (d.x == 0 && d.y==0) ? 0 :  atan2(d.x, d.y) + Twist / 180 * 3.141592;
    float len = length(d);
    float2 direction = float2( sin(angle), cos(angle));

    if(Mode < Gray_ToRGB) {
        float3 normal = normalize(float3(len * direction * Impact, 1));
        normal.y = -normal.y; // Flip Y channel
        return float4(normal / 2 + 0.5, 1);
    }

    if(Mode < Gray_ToRGBNeg) {
        float3 normal = normalize(float3(len * direction * Impact, 1));
        normal.y = -normal.y; // Flip Y channel
        return float4(normal, 1);
    }

    if(Mode < Gray_ToAngleAndMagnitude)
    {
        return float4(mod(-angle, 2*3.141592), len * Impact, 0,1);
    }

    return float4( float2(len * direction * Impact) +0.5, uvImage.ba);
}