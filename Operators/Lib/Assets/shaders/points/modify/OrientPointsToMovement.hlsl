#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 UpVector;
    float AlignToUpVector;

    float Smoothing;
    float MinMovement;
    float JumpThreshold;
    float Flip;

    float Reset;
}

StructuredBuffer<Point> SourcePoints : register(t0);

// Still holds last frame's result when the shader runs, which serves as per-point state.
RWStructuredBuffer<Point> ResultPoints : register(u0);

float3 ProjectOnPlane(float3 v, float3 planeNormal)
{
    return v - planeNormal * dot(v, planeNormal);
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
        return;

    Point p = SourcePoints[i.x];
    if (IsSeparator(p))
    {
        ResultPoints[i.x] = p;
        return;
    }

    Point prev = ResultPoints[i.x];

    // A freshly allocated buffer carries no unit rotation, so it is treated like a reset.
    bool hasState = Reset < 0.5
                    && abs(length(prev.Rotation) - 1) < 0.01
                    && !any(isnan(prev.Position))
                    && !IsSeparator(prev);

    if (!hasState)
    {
        float inputRotationLength = length(p.Rotation);
        p.Rotation = inputRotationLength > 0.0001 ? p.Rotation / inputRotationLength : QUATERNION_IDENTITY;
        ResultPoints[i.x] = p;
        return;
    }

    float4 prevRotation = normalize(prev.Rotation);
    float3 movement = p.Position - prev.Position;
    float distance = length(movement);

    bool isTeleport = JumpThreshold > 0 && distance > JumpThreshold;
    if (distance <= MinMovement || isTeleport)
    {
        p.Rotation = prevRotation;
        ResultPoints[i.x] = p;
        return;
    }

    float3 forward = movement / distance * (Flip > 0.5 ? -1 : 1);

    // Carrying the previous up over to the new forward avoids the flips of a fixed up vector.
    float3 prevUp = qRotateVec3(float3(0, 1, 0), prevRotation);
    float3 targetUp = length(UpVector) > 0.0001 ? normalize(UpVector) : float3(0, 1, 0);
    float3 up = ProjectOnPlane(lerp(prevUp, targetUp, AlignToUpVector), forward);

    if (length(up) < 0.0001)
        up = ProjectOnPlane(prevUp, forward);

    if (length(up) < 0.0001)
        up = ProjectOnPlane(abs(forward.x) < 0.9 ? float3(1, 0, 0) : float3(0, 1, 0), forward);

    float4 orientation = qLookAt(forward, -normalize(up));
    p.Rotation = normalize(qSlerp(prevRotation, orientation, 1 - saturate(Smoothing)));
    ResultPoints[i.x] = p;
}
