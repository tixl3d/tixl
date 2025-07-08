//#include "shared/hash-functions.hlsl"
//#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
//#include "shared/pbr.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    float AmountInside;
    float AmountOutside;
    float Spin;
    float RandomSpin;

    float SurfaceDistance;
    float RandomSurfaceDistance; 
    float Phase;
    float NormalSamplingDistance;
}


cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer Params : register(b2)
{
    uint ParticleCount;
}

RWStructuredBuffer<Particle> Particles : u0;
//StructuredBuffer<int3> Indices : t1;
//=== Globals =======================================================
/*{GLOBALS}*/

//=== Resources =====================================================
/*{RESOURCES(t0)}*/

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

float4 q_from_tangentAndNormal(float3 dx, float3 dz)
{
    dx = normalize(dx);
    dz = normalize(dz);
    float3 dy = -cross(dx, dz);

    float3x3 orientationDest = float3x3(
        dx,
        dy,
        dz);

    return normalize(qFromMatrix3Precise(transpose(orientationDest)));
}

float3 GetNormal(float3 p)
{
    return normalize(
        GetDistance(p + float3(NormalSamplingDistance, -NormalSamplingDistance, -NormalSamplingDistance)) * float3(1, -1, -1) +
        GetDistance(p + float3(-NormalSamplingDistance, NormalSamplingDistance, -NormalSamplingDistance)) * float3(-1, 1, -1) +
        GetDistance(p + float3(-NormalSamplingDistance, -NormalSamplingDistance, NormalSamplingDistance)) * float3(-1, -1, 1) +
        GetDistance(p + float3(NormalSamplingDistance, NormalSamplingDistance, NormalSamplingDistance)) * float3(1, 1, 1));
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    if (i.x >= ParticleCount)
        return;

    Particle p = Particles[i.x];

    // float signedPointHash = hash11u(i.x) * 2-1;

    // float phase = ((Phase + (133.1123 * i.x) ) % 10000) * (1 + signedPointHash * 0.5);
    // int phaseId = (int)phase;
    // float1 normalizedNoise = lerp(hash31((i.x + phaseId) % 123121),
    //                                 hash31((i.x + phaseId) % 123121 + 1),
    //                                 smoothstep(0, 1,
    //                                            phase - phaseId));
    // float3 signedNoise = normalizedNoise * 2 - 1;

    // float3 forward = p.Velocity; // qRotateVec3( float3(1,0,0), p.Rotation);
    // float lForward = length(forward);
    // if (lForward < 0.0001)
    //     return;

    // float3 forwardDir = forward / lForward;
    // float usedSpeed = Amount * 0.01f; // * (1+signedPointHash * RandomizeSpeed);

    float3 pos = p.Position;
    // float e = 0.0001;

    // float3 n = float3(
    //     GetDistance(pos + float3(-e, 0, 0)) - GetDistance(pos + float3(e, 0, 0)),
    //     GetDistance(pos + float3(0, -e, 0)) - GetDistance(pos + float3(0, e, 0)),
    //     GetDistance(pos + float3(0, 0, -e)) - GetDistance(pos + float3(0, 0, e)));

    float3 n = GetNormal(pos);
    float d = GetDistance(pos);

    //float l = length(n);

    if ( isnan(d) || isnan(n.x))
        return;

    //n = normalize(n);

    // MODE - Velocity Rewrite
    // float3 side = cross(normalize(p.Velocity), n);
    // float4 rotateAroundSide = qFromAngleAxis(Spin, side);
    // float3 force = qRotateVec3(n, rotateAroundSide);
    // p.Velocity = lerp(forwardDir, force, Amount);

    if(sign(d) < 0) {
        p.Velocity += n * sign(d) * clamp(d,0,100) * AmountInside;
    }
    else 
    {
        p.Velocity -= n * sign(d) * clamp(d,0,100) * AmountOutside;
    }

    //p.Velocity += float3(0,1,0);
    //p.Position = float3(1,1,2);
    Particles[i.x] = p;


    // float3 pos2 = pos + forward * usedSpeed;

    // int closestFaceIndex;
    // float3 closestSurfacePoint;
    // findClosestPointAndDistance(FaceCount, pos2,  closestFaceIndex, closestSurfacePoint);

    // // Keep outside
    // float3 distanceFromSurface= normalize(pos2 - closestSurfacePoint) * (SurfaceDistance + signedPointHash * RandomSurfaceDistance);
    // distanceFromSurface *= dot(distanceFromSurface, Vertices[Indices[closestFaceIndex].x].Normal) > 0
    //     ? 1 : -1;

    // float3 targetPosWithDistance = closestSurfacePoint + distanceFromSurface;

    // float3 movement = targetPosWithDistance - p.Position;
    // float requiredSpeed= clamp(length(movement), 0.001,99999);
    // float clampedSpeed = min(requiredSpeed, usedSpeed );
    // float speedFactor = clampedSpeed / requiredSpeed;
    // movement *= speedFactor;

    // if(!isnan(movement.x) )
    // {
    //     p.Velocity += movement;
    //     float4 orientation = normalize(q_from_tangentAndNormal(movement, distanceFromSurface));
    //     float4 mixedOrientation = qSlerp(orientation, p.Rotation, 0.96);

    //     float usedSpin = (Spin + RandomSpin) * signedNoise;
    //     if(abs(usedSpin) > 0.001)
    //     {
    //         float randomAngle = signedPointHash  * usedSpin;
    //         mixedOrientation = normalize(qMul( mixedOrientation, qFromAngleAxis(randomAngle, distanceFromSurface )));
    //     }

    //     p.Rotation = mixedOrientation;
    // }
    // p.Velocity.z +=0.1f;
    // Particles[i.x] = p;
}
