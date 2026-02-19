#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float Range;
    float Offset;
    float Scale;
}

StructuredBuffer<PbrVertex> SourceVertices : t0;        
StructuredBuffer<LegacyPoint> Points : t1;        
RWStructuredBuffer<PbrVertex> ResultVertices : u0;   

static float f;
static float3 posA;
static float3 posB;
static float4x4 orientationA;
static float4x4 orientationB;

float3 TransformVector(float3 v) {
    float3 v2 = float3(0,v.yz) * Scale + lerp(posA, posB, f);
    v2 = lerp(mul( float4(v2 - posA, 1), orientationA).xyz + posA,
              mul( float4(v2 - posB, 1), orientationB).xyz + posB,
              f);
    return v2;
} 

float3 TransformDirection(float3 v) {
    // float3 v2 = float3(0,v.yz) + lerp(posA, posB, f);
    // v2 = lerp(mul( float4(v2 - posA, 0), orientationA).xyz,
    //           mul( float4(v2 - posB, 0), orientationB).xyz,
    //           f);

    return lerp( mul( float4(v, 0), orientationA).xyz, 
    mul( float4(v, 0), orientationA).xyz,
    f);

} 



[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint vertexIndex = i.x;

    uint vertexCount, stride;
    SourceVertices.GetDimensions(vertexCount, stride);
    if(vertexIndex > vertexCount) {
        return;
    }

    uint pointCount;
    Points.GetDimensions(pointCount, stride);

    float weight = 1;
    float3 offset;

    PbrVertex v = SourceVertices[vertexIndex];
    float3 posInWorld = v.Position;

    float floatIndex = posInWorld.x * (Range) * pointCount * Scale   + Offset * pointCount + 0.00001;

    uint aIndex = (int)clamp(floatIndex, 0, pointCount-2);
    uint bIndex = aIndex + 1;
    f = floatIndex - aIndex; 

    LegacyPoint pointA = Points[aIndex];
    LegacyPoint pointB = Points[bIndex];

    orientationA = transpose(qToMatrix(pointA.Rotation));
    orientationB = transpose(qToMatrix(pointB.Rotation));
    posA = pointA.Position;
    posB = pointB.Position;

    v.Position = TransformVector(v.Position);

    v.Normal = normalize(TransformDirection(v.Normal));
    v.Tangent = normalize(TransformDirection(v.Tangent));
    v.Bitangent = normalize(TransformDirection(v.Bitangent));

    ResultVertices[vertexIndex] = v;
}

