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
StructuredBuffer<LegacyPoint> SourcePoints : t1;
StructuredBuffer<LegacyPoint> TargetPoints : t2;

RWStructuredBuffer<PbrVertex> ResultVertices : u0;

float cross( in float2 a, in float2 b ) { return a.x*b.y - a.y*b.x; }

// From Inigo Quilez: https://iquilezles.org/articles/ibilinear/
float2 invBilinear( in float2 p, in float2 a, in float2 b, in float2 c, in float2 d )
{
    float2 res = float2(-1.0, -1.0);

    float2 e = b-a;
    float2 f = d-a;
    float2 g = a-b+c-d;
    float2 h = p-a;
        
    float k2 = cross( g, f );
    float k1 = cross( e, f ) + cross( h, g );
    float k0 = cross( h, e );
    
    // if edges are parallel, this is a linear equation
    if( abs(k2)<0.001 )
    {
        res = float2( (h.x*k1+f.x*k0)/(e.x*k1-g.x*k0), -k0/k1 );
    }
    // otherwise, it's a quadratic
    else
    {
        float w = k1*k1 - 4.0*k0*k2;
        if( w<0.0 ) return float2(-1.0, -1.0);
        w = sqrt( w );

        float ik2 = 0.5/k2;
        float v = (-k1 - w)*ik2;
        float u = (h.x - f.x*v)/(e.x + g.x*v);
        
        if( u<0.0 || u>1.0 || v<0.0 || v>1.0 )
        {
           v = (-k1 + w)*ik2;
           u = (h.x - f.x*v)/(e.x + g.x*v);
        }
        res = float2( u, v );
    }
    
    return res;
}


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint vertexIndex = i.x;

    uint vertexCount, sourcePointCount, targetPointCount, stride;
    SourceVertices.GetDimensions(vertexCount, stride);
    SourcePoints.GetDimensions(sourcePointCount, stride);
    TargetPoints.GetDimensions(targetPointCount, stride);

    if(vertexIndex >= vertexCount) {
        return;
    }

    PbrVertex v = SourceVertices[vertexIndex];

    float3 vp = v.Position;
    float2 p0= SourcePoints[0].Position.xy;
    float2 p1= SourcePoints[1].Position.xy;
    float2 p2= SourcePoints[2].Position.xy;
    float2 p3= SourcePoints[3].Position.xy;

    float2 uv = saturate(invBilinear(vp.xy, p0, p1, p2, p3));

    float3 pt0= TargetPoints[0].Position;
    float3 pt1= TargetPoints[1].Position;
    float3 pt2= TargetPoints[2].Position;
    float3 pt3= TargetPoints[3].Position;

    float2 targetPos = 
    lerp(
        lerp(pt0, pt1, uv.x),
        lerp(pt2, pt3, 1-uv.x),
        uv.y);

    
    v.Position = float3(targetPos.xy, vp.z);
    ResultVertices[vertexIndex] = v;
}

