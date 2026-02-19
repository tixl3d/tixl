#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"

cbuffer Params : register(b0)
{
    float3 Phase;   // Note that float3 vectors have to be aligned to 16 byte borders 
    float Amount;
}

StructuredBuffer<LegacyPoint> SourcePoints : t0;        
RWStructuredBuffer<LegacyPoint> ResultPoints : u0;   

[numthreads(64,1,1)]
void main(uint3 DTid : SV_DispatchThreadID)
{
    uint i= DTid.x;    // This is the index to the point

    // Do nothing if index is output of buffer.
    // (This can happen because this is called 64 times as defined in numthreads.)
    uint pointCount, _;
    SourcePoints.GetDimensions(pointCount, _);
    if(i >= pointCount) {
        return;
    }

    // Read to original point
    LegacyPoint p = SourcePoints[i];  

    // Here is a meaningless example that offsets the each points position 
    // depending on it's position and the input parameters
    p.Position += sin( p.Position * 10 + Phase) * Amount;

    // Write the point with modified position (all the original Orientation and W)
    ResultPoints[i] = p;
}