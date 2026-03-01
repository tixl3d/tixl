#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    
    float Tolerance; 
    float Influence;
    float MaxBendAngle; // in radians, used if AngleConstraint is enabled         
}

cbuffer Params : register(b1)
{
    int MaxIterations;   // Usually 10-20 is enough
    int Reset;
    int PointsPerChain;
    int AngleConstraint; // 0 = off, 1 = on
}

// SourcePoints: the rest-pose / original chain (read-only, never changes)
StructuredBuffer<Point>   SourcePoints : t0;
StructuredBuffer<Point>   TargetPoints : t1;

// ResultPoints: the live stateful chain - persists across frames.
//   - chain root      is pinned to SourcePoints[chainStart] each solve
//   - chain end       is the target/effector (driven externally each frame)
//   - middle joints   are solved by FABRIK
RWStructuredBuffer<Point> ResultPoints : u0;

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint totalPoints, stride, targetPointCount;
    SourcePoints.GetDimensions(totalPoints, stride);
    TargetPoints.GetDimensions(targetPointCount, stride);

    uint pointsPerChain = (PointsPerChain > 0) ? (uint)PointsPerChain : totalPoints ;
    uint numChains      = totalPoints / pointsPerChain;

    if (pointsPerChain < 2 || numChains == 0)
        return;

    // --- Reset: copy source into result so all chains start in rest pose ---
    if (Reset == 1)
    {
        ResultPoints[i.x] = SourcePoints[i.x];
        return;
    }

    // --- Solve each chain independently ---
    for (uint c = 0; c < numChains; c++)
    {
        uint chainStart = c * pointsPerChain;
        uint chainEnd   = chainStart + pointsPerChain -1; // exclusive

        // Read live positions into local array
        float3 pos[256];
        for (uint j = 0; j < pointsPerChain; j++)
            pos[j] = ResultPoints[chainStart + j].Position;

        // Compute rest-pose segment lengths from SourcePoints
        float segLen[256];
        for (uint k = 0; k < pointsPerChain - 1; k++)
            segLen[k] = distance(SourcePoints[chainStart + k].Position,
                                 SourcePoints[chainStart + k + 1].Position);

        // Pin root to this chain's source rest position
        float3 rootPos = SourcePoints[chainStart].Position;

        // Target: use per-chain target if available, otherwise wrap around
        uint targetIdx = (c < targetPointCount) ? c : (c % targetPointCount);

        uint anchorIdx = (chainEnd < totalPoints) ? chainEnd : chainStart;
        float3 targetPos = lerp(ResultPoints[anchorIdx].Position, TargetPoints[targetIdx].Position, Influence);
        
       
        // Store original positions for blending
        float3 originalPos[256];
        for (uint o = 0; o < pointsPerChain; o++)
            originalPos[o] = pos[o];

        // --- FABRIK solve ---
        float error = 1e10f;

        for (int iter = 0; iter < MaxIterations && error > Tolerance; iter++)
        {
            // Forward pass: pull from target back toward root
            pos[pointsPerChain - 1] = targetPos;
            for (int f = (int)pointsPerChain - 2; f >= 0; f--)
            {
                float3 dir = normalize(pos[f] - pos[f + 1]);
                pos[f] = pos[f + 1] + dir * segLen[f];
            }

            // Backward pass: push from root out toward target
            pos[0] = rootPos;
            for (uint b = 1; b < pointsPerChain; b++)
            {
                float3 dir = normalize(pos[b] - pos[b - 1]);
                pos[b] = pos[b - 1] + dir * segLen[b - 1];
            }
            
            error = distance(pos[pointsPerChain - 1], targetPos);
        }
        if (AngleConstraint == 1){
            float maxBendAngle = radians(MaxBendAngle); // Example max bend angle in radians
                    // After computing the new position in the backward pass,
            // clamp the bend angle relative to the parent direction
            for (uint b = 1; b < pointsPerChain; b++)
            {
                float3 dir = normalize(pos[b] - pos[b - 1]);
                pos[b] = pos[b - 1] + dir * segLen[b - 1];

                // Cone constraint: clamp angle against parent segment
                if (b >= 2)
                {
                    float3 parentDir = normalize(pos[b - 1] - pos[b - 2]);
                    float3 currentDir = normalize(pos[b] - pos[b - 1]);

                    float cosAngle = dot(parentDir, currentDir);
                    float maxCosAngle = cos(MaxBendAngle); // MaxBendAngle in radians

                    if (cosAngle < maxCosAngle)
                    {
                        // Project currentDir onto the cone surface around parentDir
                        float3 perp = currentDir - parentDir * dot(currentDir, parentDir);
                        float perpLen = length(perp);
                        if (perpLen > 0.0001f)
                        {
                            float3 constrained = normalize(parentDir * cos(MaxBendAngle) 
                                            + normalize(perp) * sin(MaxBendAngle));
                            pos[b] = pos[b - 1] + constrained * segLen[b - 1];
                        }
                    }
                }
            }
        }
        // --- Write solved positions back with blending ---
        for (uint l = 0; l < pointsPerChain; l++)
        {
            uint globalIdx = chainStart + l;
            Point p = ResultPoints[globalIdx];

            float3 solvedPos = (l == 0) ? rootPos : pos[l];
            p.Position = solvedPos;

            // Update rotation to face the next joint
            if (l < pointsPerChain - 1)
            {
                float3 newDir = pos[l + 1] - pos[l];
                float3 orgDir = SourcePoints[globalIdx + 1].Position
                              - SourcePoints[globalIdx].Position;

                p.Scale.x = distance(pos[l], pos[l + 1]);
                if (length(newDir) > 0.0001f && length(orgDir) > 0.0001f)
                {
                    float4 alignRot = qFromVectors(normalize(orgDir), normalize(newDir));
                    p.Rotation = qMul(alignRot, SourcePoints[globalIdx].Rotation);
                }
            }
            if (l == pointsPerChain - 1)
            {
                // For the end effector, optionally copy rotation from target
                // or copy the last segment's rotation for a more natural look:
                p.Rotation = lerp(ResultPoints[globalIdx - 1].Rotation,TargetPoints[targetIdx].Rotation, Influence);
                p.Scale.x = distance(pos[l], pos[l - 1]); // Update scale for end segment as well
            }
            p.Scale.yz = SourcePoints[globalIdx].Scale.yz;
            ResultPoints[globalIdx].Position = p.Position;
            ResultPoints[globalIdx].Rotation = p.Rotation;
            ResultPoints[globalIdx].Scale    = p.Scale;
            ResultPoints[globalIdx].Color    = SourcePoints[globalIdx].Color;
            ResultPoints[globalIdx].FX1    = SourcePoints[globalIdx].FX1;
            ResultPoints[globalIdx].FX2   = p.FX2;
           
        }
         
    }
}
