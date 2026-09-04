// Shared logic for envelope-binding positions to a skeleton.
// Including shaders declare their position source at t0 and call ComputeSkinWeights().
#ifndef __SKIN_BINDING__
#define __SKIN_BINDING__

struct SkinWeightDef
{
    int4 JointIndices;
    float4 Weights;
};

// One segment per joint in rest pose: from the joint to the average of its children (or itself for leaves)
struct BoneSegmentDef
{
    float4 Start;
    float4 End;
};

StructuredBuffer<BoneSegmentDef> BoneSegments : register(t1);
RWStructuredBuffer<SkinWeightDef> ResultWeights : register(u0);

cbuffer BindParams : register(b0)
{
    float Radius;
    float FalloffPower;
    float MaxInfluences;
    float _padding;
}

float DistanceToSegment(float3 p, float3 a, float3 b)
{
    float3 ab = b - a;
    float lengthSquared = dot(ab, ab);
    if (lengthSquared < 1e-12)
        return length(p - a);

    float t = saturate(dot(p - a, ab) / lengthSquared);
    return length(p - (a + ab * t));
}

SkinWeightDef ComputeSkinWeights(float3 position)
{
    uint jointCount, stride;
    BoneSegments.GetDimensions(jointCount, stride);

    int bestIndices[4] = { 0, 0, 0, 0 };
    float bestWeights[4] = { 0, 0, 0, 0 };

    uint nearestJoint = 0;
    float nearestDistance = 1e30;

    for (uint jointIndex = 0; jointIndex < jointCount; jointIndex++)
    {
        float d = DistanceToSegment(position, BoneSegments[jointIndex].Start.xyz, BoneSegments[jointIndex].End.xyz);
        if (d < nearestDistance)
        {
            nearestDistance = d;
            nearestJoint = jointIndex;
        }

        float w = pow(saturate(1 - d / Radius), FalloffPower);
        if (w <= bestWeights[3])
            continue;

        // Insert into the sorted top-4
        for (int slot = 0; slot < 4; slot++)
        {
            if (w <= bestWeights[slot])
                continue;

            for (int shift = 3; shift > slot; shift--)
            {
                bestWeights[shift] = bestWeights[shift - 1];
                bestIndices[shift] = bestIndices[shift - 1];
            }

            bestWeights[slot] = w;
            bestIndices[slot] = (int)jointIndex;
            break;
        }
    }

    int influenceLimit = clamp((int)MaxInfluences, 1, 4);
    for (int k = influenceLimit; k < 4; k++)
    {
        bestWeights[k] = 0;
    }

    SkinWeightDef result;
    float total = bestWeights[0] + bestWeights[1] + bestWeights[2] + bestWeights[3];
    if (total < 1e-6)
    {
        // Outside every envelope - snap to the nearest bone so no vertex is left unbound
        result.JointIndices = int4(nearestJoint, 0, 0, 0);
        result.Weights = float4(1, 0, 0, 0);
    }
    else
    {
        result.JointIndices = int4(bestIndices[0], bestIndices[1], bestIndices[2], bestIndices[3]);
        result.Weights = float4(bestWeights[0], bestWeights[1], bestWeights[2], bestWeights[3]) / total;
    }

    return result;
}
#endif
