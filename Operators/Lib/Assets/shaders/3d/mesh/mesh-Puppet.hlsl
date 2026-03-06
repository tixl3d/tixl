#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformMatrix;
    float UseVertexSelection;
    float WeigthDebug;  // Enable/disable distance-based weighting
}

StructuredBuffer<PbrVertex> SourceVerts : t0;
StructuredBuffer<Point> AnchorPointsOriginal : t1;  // Original anchor positions (reference pose)
StructuredBuffer<Point> AnchorPointsCurrent : t2;   // Current anchor positions (deformed pose)
RWStructuredBuffer<PbrVertex> ResultVerts : u0;   

// Helper function to calculate influence weight between vertex and anchor
float CalculateInfluence(float3 vertexPos, float3 anchorPos, float falloffDist)
{
    float dist = distance(vertexPos, anchorPos);
    
    if (falloffDist <= 0)
    {
        // Inverse distance weighting
        return 1.0 / (dist + 0.001); // Add small epsilon to avoid division by zero
    }
    else
    {
        // Smooth falloff within radius
        float t =saturate(dist / falloffDist);
        return 1.0 - smoothstep(0.0, 1.0, t);
    }
}

// I need an alternative way to calculate weight in a linear way perpendicular to the line between two points.
float CalculateLinearInfluence(float3 vertexPos, float3 anchorPos, float3 anchorDir, float falloffDist)
{
    float3 toVertex = vertexPos - anchorPos;
    float projLength = dot(toVertex, anchorDir);
    float3 closestPoint = anchorPos + projLength * anchorDir;
    float dist = distance(vertexPos, closestPoint);
    
    if (falloffDist <= 0)
    {
        return 1.0 / (dist + 0.001);
    }
    else
    {
        float t = saturate(dist / falloffDist);
        return 1.0 - smoothstep(0.0, 1.0, t);
    }
}

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourceVerts.GetDimensions(numStructs, stride);
    if(i.x >= numStructs) {
        return;
    }
    
    uint numAnchors, anchorStride;
    AnchorPointsOriginal.GetDimensions(numAnchors, anchorStride);
    
    // Make sure both anchor buffers have the same number of elements
    uint numCurrentAnchors;
    AnchorPointsCurrent.GetDimensions(numCurrentAnchors, anchorStride);
    numAnchors = min(numAnchors, numCurrentAnchors);
    
    // Get vertex selection weight
    float s = UseVertexSelection > 0.5 ? SourceVerts[i.x].Selected : 1;
    
    // Get original vertex data
    float3 originalPos = SourceVerts[i.x].Position;
    float3 originalNormal = SourceVerts[i.x].Normal;
    float3 originalTangent = SourceVerts[i.x].Tangent;
    float3 originalBitangent = SourceVerts[i.x].Bitangent;
    
    // Start with original position
    float3 deformedPos = originalPos;
    float3 deformedNormal = originalNormal;
    float3 deformedTangent = originalTangent;
    float3 deformedBitangent = originalBitangent;
    
    if (numAnchors > 0)
    {
       
            // Multiple anchors: weighted influence from all anchors
            float3 totalPosition = 0;
            float3 totalNormal = 0;
            float3 totalTangent = 0;
            float3 totalBitangent = 0;
            float totalWeight = 0;
            
            for (uint j = 0; j < numAnchors; j++)
            {
                float3 anchorOriginalPos = AnchorPointsOriginal[j].Position;
                float3 anchorCurrentPos  = AnchorPointsCurrent[j].Position;
                float4 anchorOriginalRot = AnchorPointsOriginal[j].Rotation;
                float4 anchorCurrentRot  = AnchorPointsCurrent[j].Rotation;
                float3 anchorOriginalScale = AnchorPointsOriginal[j].Scale;
                float3 anchorCurrentScale  = AnchorPointsCurrent[j].Scale;      
                float3 anchorDir = anchorOriginalPos - AnchorPointsOriginal[j+1].Position;
                float weight = CalculateInfluence(originalPos, anchorOriginalPos, AnchorPointsOriginal[j].FX1);
                //float weight = CalculateLinearInfluence(originalPos, anchorOriginalPos,anchorDir, AnchorPointsOriginal[j].FX1);
                if (weight > 0.001)
                {
                    // 1. Rotation delta: "how much did this anchor rotate?"
                    float4 rotDelta = qMul(anchorCurrentRot, qInverse(anchorOriginalRot));
                    float3 scaleDelta = anchorCurrentScale / max(anchorOriginalScale, 0.0001);
                    // 2. Express vertex in the original anchor's local space
                    float3 localOffset = originalPos - anchorOriginalPos;

                    // 3. Rotate that local offset by the rotation delta
                    float3 rotatedOffset = qRotateVec3(localOffset, rotDelta);
                    float3 scaledRotatedOffset = rotatedOffset * scaleDelta;

                    // 4. Reconstruct world position using the current anchor
                    totalPosition += (anchorCurrentPos + scaledRotatedOffset) * weight;

                    // 5. Rotate normals/tangents the same way (no translation for directions)
                    totalNormal    += qRotateVec3(originalNormal,    rotDelta) * weight;
                    totalTangent   += qRotateVec3(originalTangent,   rotDelta) * weight;
                    totalBitangent += qRotateVec3(originalBitangent, rotDelta) * weight;

                    totalWeight += weight;
                }
            }
            
            if (totalWeight > 0)
            {
                deformedPos = totalPosition / totalWeight;
                deformedNormal = normalize(totalNormal / totalWeight);
                deformedTangent = normalize(totalTangent / totalWeight);
                deformedBitangent = normalize(totalBitangent / totalWeight);
            }
        
    }
    else
    {
        // Fall back to simple transform if no anchors
        deformedPos = mul(float4(originalPos, 1), TransformMatrix).xyz;
        deformedNormal = normalize(mul(float4(originalNormal, 0), TransformMatrix).xyz);
        deformedTangent = normalize(mul(float4(originalTangent, 0), TransformMatrix).xyz);
        deformedBitangent = normalize(mul(float4(originalBitangent, 0), TransformMatrix).xyz);
    }
    
    // Apply final position with selection blending
    ResultVerts[i.x].Position = lerp(originalPos, deformedPos, s);
    ResultVerts[i.x].Normal = lerp(originalNormal, deformedNormal, s);
    ResultVerts[i.x].Tangent = lerp(originalTangent, deformedTangent, s);
    ResultVerts[i.x].Bitangent = lerp(originalBitangent, deformedBitangent, s);
    
    // Pass through other attributes unchanged
    ResultVerts[i.x].TexCoord = SourceVerts[i.x].TexCoord;
    ResultVerts[i.x].TexCoord2 = SourceVerts[i.x].TexCoord2;
    ResultVerts[i.x].Selected = SourceVerts[i.x].Selected;

    // I need to debug influence weights, so I'm going to encode them in the color for now
     float influenceColorR = min(1, CalculateInfluence(originalPos, AnchorPointsOriginal[0].Position, AnchorPointsOriginal[0].FX1));
     float influenceColorG = min(1, CalculateInfluence(originalPos, AnchorPointsOriginal[1].Position, AnchorPointsOriginal[1].FX1));
     float influenceColorB = min(1, CalculateInfluence(originalPos, AnchorPointsOriginal[2].Position, AnchorPointsOriginal[2].FX1));
     float influenceColor4 = min(1, CalculateInfluence(originalPos, AnchorPointsOriginal[3].Position, AnchorPointsOriginal[3].FX1));
     ResultVerts[i.x].ColorRGB = lerp(SourceVerts[i.x].ColorRGB, float3(influenceColorR, influenceColorG, influenceColorB+influenceColor4), WeigthDebug);
   // ResultVerts[i.x].ColorRGB = SourceVerts[i.x].ColorRGB;
   
}