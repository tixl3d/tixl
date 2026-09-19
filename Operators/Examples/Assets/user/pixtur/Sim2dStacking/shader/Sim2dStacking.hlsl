#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"

cbuffer Params : register(b0)
{
    float3 Phase;   // Note that float3 vectors have to be aligned to 16 byte borders 
    float Amount;
}

//StructuredBuffer<Point> SourcePoints : t0;        
RWStructuredBuffer<Point> Points : u0;   

[numthreads(64,1,1)]
void main(uint3 DTid : SV_DispatchThreadID)
{
    uint i= DTid.x;    // This is the index to the point

    uint pointCount, _;
    Points.GetDimensions(pointCount, _);
    if(i >= pointCount) {
        return;
    }

    Point p = Points[i];  

    p.position.y += 0.1 * Amount;

    Points[i] = p;
}