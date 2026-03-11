#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer FloatParams : register(b0)
{
    float3 Offset;
    float A;

    float B;
    float C;
    float D;
    float Time;
    float2 GainAndBias;
}


cbuffer FloatParams : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer IntParams : register(b2)
{
    uint TotalCount;
    int2 TexSize;
}


StructuredBuffer<Point> SourcePoints : register(t0);
Texture2D<float4> Image : register(t1);
Texture2D<float4> Gradient : register(t2);

RWStructuredBuffer<Point> ResultPoints : register(u0);
sampler Sampler : register(s0);
sampler ClampedSampler : register(s1);

static const float3 Center=Offset;

//=== Additional Resources ==========================================
/*{RESOURCES(t3)}*/

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

inline float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}

float3 GetFieldNormal(float3 p, float d=0.001)
{
    return normalize(
        GetDistance(p + float3(d, -d, -d)) * float3(1, -1, -1) +
        GetDistance(p + float3(-d, d, -d)) * float3(-1, 1, -1) +
        GetDistance(p + float3(-d, -d, d)) * float3(-1, -1, 1) +
        GetDistance(p + float3(d, d, d)) * float3(1, 1, 1));
}


//===================================================================



//- DEFINES ------------------------------------
/*{defines}*/
//----------------------------------------------

float Biased(float f){return ApplyGainAndBias(f, GainAndBias);}
float4 SampleGradient(float f){return Gradient.SampleLevel(ClampedSampler, float2(f, 0.5), 0);}

[numthreads(64,1,1)]
void main(uint3 DTId : SV_DispatchThreadID)
{
    uint idx = DTId.x;
    uint i=idx;

    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if(idx >= TotalCount) {
        return;
    }

    float f = (float)idx / TotalCount;
    Point p = (Point)0;
    if(numStructs>0){
        p = SourcePoints[(idx)%numStructs];
    }else{
        p.Position=float3(0,0,0);
        p.FX1=1.0;
        p.Rotation=float4(0,0,0,1);
        p.Color=float4(1,1,1,1);
        p.Scale=float3(1,1,1);
        p.FX2=1.0;
   }

{
//- METHOD -------------------------------------
/*{method}*/
//----------------------------------------------
}
    ResultPoints[idx] = p;
}
