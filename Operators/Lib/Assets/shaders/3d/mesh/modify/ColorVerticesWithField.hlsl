#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"
#include "shared/pbr.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    float Strength;
    float Range;
}

cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer Params : register(b2)
{
    int StrengthFactor;
}

StructuredBuffer<PbrVertex> SourceVertices : t0;
RWStructuredBuffer<PbrVertex> ResultVertices : u0;
sampler ClampedSampler : register(s0);

//=== Additional Resources ==========================================
/*{RESOURCES(t1)}*/

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

//===================================================================


[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourceVertices.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
        return;

    PbrVertex v = SourceVertices[i.x];

    float strength = Strength * v.Selected;

    float3 pos = v.Position;
    float4 field = GetField( float4(pos,1));
    v.ColorRGB = lerp(v.ColorRGB, field.rgb, strength);

    ResultVertices[i.x] = v;
}
