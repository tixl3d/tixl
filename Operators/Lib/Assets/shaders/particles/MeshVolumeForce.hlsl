#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float Amount;
    float Attraction;
    float AttractionDecay;
    float Repulsion;

    float Bounciness;
    float RandomizeBounce;
    float RandomizeReflection;
    float InvertVolumeFactor;

    float SpeedFactor;
}

cbuffer IntParams : register(b1)
{
    int ParticleCount;
    int VertexCount;
    int FaceCount;
    int EnableBounce;

    int ApplyColorOnCollision;
    int SkipCount;
    int FrameCount;
}

RWStructuredBuffer<Particle> Particles : register(u0); 

StructuredBuffer<PbrVertex> Vertices: register(t0);
StructuredBuffer<int3> Indices: register(t1);




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

float DistanceToTriange(int faceIndex, float3 pos) 
{
    int3 f = Indices[faceIndex];
    float3 p0 = Vertices[f[0]].Position;
    float3 p1 = Vertices[f[1]].Position;
    float3 p2 = Vertices[f[2]].Position;


    float3 pointOnFace = closestPointOnTriangle(
        p0, p1,p2,
        pos
    );

    float3 dp = pointOnFace - pos;
    float3 n = Vertices[f[0]].Normal;

    float s = dot( normalize(n), normalize(dp));
    return length(dp) * (s>0.001 ? -1:1);
}


void findClosestPointAndDistance(
    in uint faceCount, 
    in float3 pos, 
    out uint closestFaceIndex, 
    out float3 closestSurfacePoint,
    out float3 closestNormal,
    out float closestSignedDistance) 
{
    closestFaceIndex = -1; 
    float closestDistance = 99999;

    for(uint faceIndex = 0; faceIndex < faceCount; faceIndex++) 
    {
        int3 f = Indices[faceIndex];

        float3 p0 = Vertices[f[0]].Position;
        float3 p1 = Vertices[f[1]].Position;
        float3 p2 = Vertices[f[2]].Position;
        float3 n = Vertices[f[0]].Normal;

        float3 dn = n * 0.0001;
        float3 pointOnFace = closestPointOnTriangle(
            p0 + dn, p1 + dn ,p2 + dn,
            pos 
        );
        
        float3 dp =    pointOnFace - pos;

        float distance2 = length(dp);

        if(distance2 < closestDistance) 
        {
            float s = dot( normalize(n), normalize(dp));

            closestDistance = distance2;
            closestSignedDistance = distance2* (s>0.001 ? -1:1);
            closestNormal = dp/ closestSignedDistance;
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
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);
    int gi = i.x;
    if (gi >= maxParticleCount)
        return;

    if( (((gi/64) +FrameCount) % SkipCount) > 0 )
        return;

    float3 pos = Particles[gi].Position;
    int closestFaceIndex;
    float3 closestSurfacePoint;
    float3 surfaceN;
    float distance;

    findClosestPointAndDistance(FaceCount, pos,  closestFaceIndex, closestSurfacePoint, surfaceN, distance);

    float4 rot = Particles[gi].Rotation;
    float3 velocity = Particles[gi].Velocity;
    float3 posNext = float3(pos + velocity * SpeedFactor * 0.01 * 2);
    float distanceNext = DistanceToTriange(closestFaceIndex, posNext);

    //Particles[gi].Color.g = distance < 0 ? 0.0 : 1;

    float3 force = 0;
    surfaceN *= InvertVolumeFactor;

    // Reflect if distance changes
    if (sign(distance * distanceNext) < 0 && distance * InvertVolumeFactor > 0)
    {
        float4 rand = hash41u(gi);
        float3 v = lerp(velocity,
                        reflect(velocity, surfaceN + (RandomizeReflection * (rand.xyz - 0.5))),
                        EnableBounce);

        velocity = lerp(velocity,
                        //
                        (v * Bounciness     //
                         * (RandomizeBounce //
                                * (rand.z - 0.5) +
                            1)), //
                        Amount);


        if (ApplyColorOnCollision)
        {
            // TODO:
            // Could sample text with mesh uv
            // float4 surfaceColor = GetField(float4(pos, 1));
            // Particles[gi].Color.rgb = surfaceColor.rgb;
        }
    }
    else
    {
        if (distance * InvertVolumeFactor < 0)
        {
            force = -surfaceN * Repulsion;
        }
        else
        {
            force = surfaceN * Attraction / (1 + distance * AttractionDecay);
        }
        velocity += force * Amount * SpeedFactor;
    }

    if (!isnan(velocity.x) && !isnan(velocity.y) && !isnan(velocity.z))
        Particles[gi].Velocity = velocity;
}
