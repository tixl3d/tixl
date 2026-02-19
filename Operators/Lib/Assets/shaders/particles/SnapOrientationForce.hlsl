#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float Amount;
    float SnapAngle;
    float PhaseAngle;
    float Variation;
    float VariationRatio;

    float KeepPlanar;
    float SpaceAndPlane;  // 0 = Camera Space, 1 = World XY, 2 = World XZ, 3 = World YZ
}

cbuffer Params : register(b1)
{
    int RandomSeed;
}

cbuffer Transforms : register(b2)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

RWStructuredBuffer<Particle> Particles : u0; 

// Helper function to get plane coordinates based on space and plane selection
void GetPlaneCoordinates(float3 v, float spaceAndPlane, out float2 planeCoords, out float remainingAxis)
{
    if(spaceAndPlane < 0.5) {
        // Camera Space (uses XY plane in camera space)
        planeCoords = v.xy;
        remainingAxis = v.z;
    }
    else if(spaceAndPlane < 1.5) {
        // World XY plane
        planeCoords = v.xy;
        remainingAxis = v.z;
    }
    else if(spaceAndPlane < 2.5) {
        // World XZ plane
        planeCoords = v.xz;
        remainingAxis = v.y;
    }
    else {
        // World YZ plane
        planeCoords = v.yz;
        remainingAxis = v.x;
    }
}

// Helper function to set plane coordinates back to 3D vector
float3 SetPlaneCoordinates(float2 planeCoords, float remainingAxis, float spaceAndPlane, float3 originalV)
{
    if(spaceAndPlane < 0.5) {
        // Camera Space (XY plane)
        return float3(planeCoords, remainingAxis);
    }
    else if(spaceAndPlane < 1.5) {
        // World XY plane
        return float3(planeCoords, remainingAxis);
    }
    else if(spaceAndPlane < 2.5) {
        // World XZ plane
        return float3(planeCoords.x, remainingAxis, planeCoords.y);
    }
    else {
        // World YZ plane
        return float3(remainingAxis, planeCoords);
    }
}

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint maxParticleCount, _;
    Particles.GetDimensions(maxParticleCount, _);

    int id = i.x;

    if(i.x >= maxParticleCount) {
        return;
    }

    float3 vInObject = Particles[i.x].Velocity;

    // Choose whether to work in camera space or world space
    float3 v;
    if(SpaceAndPlane < 0.5) {
        // Camera Space: transform to camera space
        float4 vInCamera = mul(float4(vInObject, 0), WorldToCamera);
        v = vInCamera.xyz;
    }
    else {
        // World Space: work directly in object/world space
        v = vInObject;
    }

    // Get coordinates for the selected plane
    float2 planeCoords;
    float remainingAxis;
    GetPlaneCoordinates(v, SpaceAndPlane, planeCoords, remainingAxis);

    float lengthXY = length(planeCoords);
    if(lengthXY < 0.00001)
        return;

    float2 normalizedV = normalize(planeCoords);

    float a = atan2(normalizedV.x, normalizedV.y);

    float aNormalized = ((a + PI) / (PI*2)) %1;
    float subdivisions = 360 / SnapAngle;

    float4 hash = hash41u(id + RandomSeed * _PRIME0);
    if(hash.x < VariationRatio) {
        aNormalized += (hash.y - 0.5) * Variation ;
    }
    float t = aNormalized * subdivisions;

    float tRounded = ((int)(t + 0.5)) / subdivisions;
    
    float newAngle = lerp(aNormalized, tRounded, Amount);

    float alignedRotation = (newAngle - 0.5) * 2 * PI + (PhaseAngle/360);

    float2 newPlaneCoords = float2(sin(alignedRotation), cos(alignedRotation)) * lengthXY;
    
    // Apply KeepPlanar to the remaining axis
    remainingAxis *= (1-KeepPlanar);

    // Convert back to 3D vector
    float3 newV = SetPlaneCoordinates(newPlaneCoords, remainingAxis, SpaceAndPlane, v);

    float3 newVelocity;
    if(SpaceAndPlane < 0.5) {
        // Transform back from camera space to object space
        newVelocity = mul(float4(newV, 0), CameraToWorld).xyz;
    }
    else {
        // Use the modified velocity directly in object space
        newVelocity = newV;
    }

    Particles[i.x].Velocity = lerp(vInObject, newVelocity, 1);
}