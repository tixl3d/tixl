#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float Speed;
    float RandomizeSpeed;
    float Spin;
    float RandomSpin;

    float SurfaceDistance;
    float RandomSurfaceDistance;
    float Phase;
}

StructuredBuffer<PbrVertex> Vertices: t0;
StructuredBuffer<int3> Indices: t1;
//StructuredBuffer<LegacyPoint> SourcePoints : t2;         // input

RWStructuredBuffer<LegacyPoint> ResultPoints : u0;    // output


float3 closestPointOnTriangle( in float3 p0, in float3 p1, in float3 p2, in float3 sourcePosition )
{
    float3 edge0 = p1 - p0;
    float3 edge1 = p2 - p0;
    float3 v0 = p0 - sourcePosition;

    float a = dot(edge0, edge0 );
    float b = dot(edge0, edge1 );
    float c = dot(edge1, edge1 );
    float d = dot(edge0, v0 );
    float e = dot(edge1, v0 );

    float det = a*c - b*b;
    float s = b*e - c*d;
    float t = b*d - a*e;

    if ( s + t < det )
    {
        if ( s < 0.f )
        {
            if ( t < 0.f )
            {
                if ( d < 0.f )
                {
                    s = clamp( -d/a, 0.f, 1.f );
                    t = 0.f;
                }
                else
                {
                    s = 0.f;
                    t = clamp( -e/c, 0.f, 1.f );
                }
            }
            else
            {
                s = 0.f;
                t = clamp( -e/c, 0.f, 1.f );
            }
        }
        else if ( t < 0.f )
        {
            s = clamp( -d/a, 0.f, 1.f );
            t = 0.f;
        }
        else
        {
            float invDet = 1.f / det;
            s *= invDet;
            t *= invDet;
        }
    }
    else
    {
        if ( s < 0.f )
        {
            float tmp0 = b+d;
            float tmp1 = c+e;
            if ( tmp1 > tmp0 )
            {
                float numer = tmp1 - tmp0;
                float denom = a-2*b+c;
                s = clamp( numer/denom, 0.f, 1.f );
                t = 1-s;
            }
            else
            {
                t = clamp( -e/c, 0.f, 1.f );
                s = 0.f;
            }
        }
        else if ( t < 0.f )
        {
            if ( a+d > b+e )
            {
                float numer = c+e-b-d;
                float denom = a-2*b+c;
                s = clamp( numer/denom, 0.f, 1.f );
                t = 1-s;
            }
            else
            {
                s = clamp( -e/c, 0.f, 1.f );
                t = 0.f;
            }
        }
        else
        {
            float numer = c+e-b-d;
            float denom = a-2*b+c;
            s = clamp( numer/denom, 0.f, 1.f );
            t = 1.f - s;
        }
    }

    return p0 + s * edge0 + t * edge1;
}




void findClosestPointAndDistance(
    in uint faceCount, 
    in float3 pos, 
    out uint closestFaceIndex, 
    out float3 closestSurfacePoint) 
{
    closestFaceIndex = -1; 
    float closestDistance = 99999;

    for(uint faceIndex = 0; faceIndex < faceCount; faceIndex++) 
    {
        int3 f = Indices[faceIndex];
        float3 pointOnFace = closestPointOnTriangle(
            Vertices[f[0]].Position,
            Vertices[f[1]].Position,
            Vertices[f[2]].Position,
            pos
        );
        
        float distance2 = length(pointOnFace - pos);
        if(distance2 < closestDistance) {
            closestDistance = distance2;
            closestFaceIndex = faceIndex;
            closestSurfacePoint = pointOnFace;
        }
    }
}


float4 q_from_tangentAndNormal(float3 dx, float3 dz)
{
    dx = normalize(dx);
    dz = normalize(dz);
    float3 dy = -cross(dx, dz);
    
    float3x3 orientationDest= float3x3(
        dx, 
        dy,
        dz
        );
    
    return normalize( qFromMatrix3Precise( transpose( orientationDest)));
}


[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, pointStride;
    ResultPoints.GetDimensions(pointCount, pointStride);

    if(i.x >= pointCount) {
        //esultPoints[i.x].w = sqrt(-1);
        return;
    }

    uint vertexCount, vertexStride; 
    Vertices.GetDimensions(vertexCount, vertexStride);

    uint faceCount, faceStride; 
    Indices.GetDimensions(faceCount, faceStride);

    float signedPointHash = hash11(i.x % 123.567 * 123.1) * 2-1;
    LegacyPoint p = ResultPoints[i.x];

    float phase = ((Phase + (133.1123 * i.x) ) % 10000) * (1 + signedPointHash * 0.5);
    int phaseId = (int)phase;
    float1 normalizedNoise = lerp(hash31((i.x + phaseId) % 123121),
                                    hash31((i.x + phaseId) % 123121 + 1),
                                    smoothstep(0, 1,
                                               phase - phaseId));
    float3 signedNoise = normalizedNoise * 2 - 1;


    float3 pos = p.Position;
    float3 forward =  qRotateVec3( float3(1,0,0), p.Rotation);

    float usedSpeed = Speed + Speed * (1+signedPointHash) * RandomizeSpeed;

    float3 pos2 = pos + forward * usedSpeed;

    int closestFaceIndex;
    float3 closestSurfacePoint;
    findClosestPointAndDistance(faceCount, pos2,  closestFaceIndex, closestSurfacePoint);

    // Keep outside
    float3 distanceFromSurface= normalize(pos2 - closestSurfacePoint) * (SurfaceDistance + signedPointHash * RandomSurfaceDistance);
    distanceFromSurface *= dot(distanceFromSurface, Vertices[Indices[closestFaceIndex].x].Normal) > 0 
        ? 1 : -1;

    float3 targetPosWithDistance = closestSurfacePoint + distanceFromSurface;

    float3 movement = targetPosWithDistance - p.Position;
    float requiredSpeed= clamp(length(movement), 0.001,99999);
    float clampedSpeed = min(requiredSpeed, usedSpeed );
    float speedFactor = clampedSpeed / requiredSpeed;
    movement *= speedFactor;
    if(!isnan(movement.x) ) 
    {
        p.Position += movement;
        float4 orientation = normalize(q_from_tangentAndNormal(movement, distanceFromSurface));
        float4 mixedOrientation = qSlerp(orientation, p.Rotation, 0.96);

        float usedSpin = (Spin + RandomSpin) * signedNoise;
        if(abs(usedSpin) > 0.001) 
        {
            float randomAngle = signedPointHash  * usedSpin;
            mixedOrientation = normalize(qMul( mixedOrientation, qFromAngleAxis(randomAngle, distanceFromSurface )));
        }
            
        p.Rotation = mixedOrientation;
    }
    ResultPoints[i.x] = p;
    //ResultPoints[i.x].position += float3(0,0.01,0);
}

