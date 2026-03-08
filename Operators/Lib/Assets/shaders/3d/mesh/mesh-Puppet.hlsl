#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float4x4 TransformMatrix;
    float UseVertexSelection;
    
    float WeightCtrl;
}

cbuffer IntParams : register(b1)
{
    int InfluenceMode;
    int WeigthDebug;
}

StructuredBuffer<PbrVertex> SourceVerts : t0;
StructuredBuffer<Point> AnchorPointsOriginal : t1;  // Original anchor positions (reference pose)
StructuredBuffer<Point> AnchorPointsCurrent : t2;   // Current anchor positions (deformed pose)
RWStructuredBuffer<PbrVertex> ResultVerts : u0;   

// Helper function to calculate influence weight between vertex and anchor
float CalculateInfluence(float3 vertexPos, float3 anchorPos, float falloffDist)
{
    float dist = distance(vertexPos, anchorPos);
    // let's use switch case to allow easy switching between different falloff functions
    switch (InfluenceMode) {
        case 0: // Smooth mode
            return exp2(-dist*dist*2/abs(falloffDist)) / (dist + 0.001);            
        case 1: // Linear falloff mode
            float t =saturate(dist / falloffDist);
            return 1.0 - smoothstep(0.0, 1.0, t);      
        case 2: // Inverse square falloff
            return 1.0 / (dist*dist /abs(falloffDist) + 0.001);
        case 3: // Gaussian falloff
            return exp2(-dist*dist*2/abs(falloffDist));  
        case 4: // Inverse Cubic falloff
            return pow(1.0 / (dist/falloffDist + 0.001),3.0);
        default:
    return 1.0; // No falloff, full influence
    }
}

float3 hsv2rgb(float3 hsv){return lerp(1,saturate(abs(frac((hsv.x-float3(0,2,4)/6))-.5)*6-1),hsv.y)*hsv.z;}

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
    float totalWeight = 0;
    float3 weightColor = 0;
    if (numAnchors > 0)
    {
       
            // Multiple anchors: weighted influence from all anchors
            float3 totalPosition = 0;
            float3 totalNormal = 0;
            float3 totalTangent = 0;
            float3 totalBitangent = 0;
            
            
            for (uint j = 0; j < numAnchors; j++)
            {
                float3 anchorOriginalPos = AnchorPointsOriginal[j].Position;
                float3 anchorCurrentPos  = AnchorPointsCurrent[j].Position;
                float4 anchorOriginalRot = AnchorPointsOriginal[j].Rotation;
                float4 anchorCurrentRot  = AnchorPointsCurrent[j].Rotation;
                float3 anchorOriginalScale = AnchorPointsOriginal[j].Scale;
                float3 anchorCurrentScale  = AnchorPointsCurrent[j].Scale;      
                //float3 anchorDir = anchorOriginalPos - AnchorPointsOriginal[j+1].Position;
                float weight = CalculateInfluence(originalPos, anchorOriginalPos, AnchorPointsOriginal[j].FX1*WeightCtrl);
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

                    weightColor+=weight*hsv2rgb(float3(float(j)/numAnchors,1,1));
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
    ResultVerts[i.x].ColorRGB = SourceVerts[i.x].ColorRGB;

    if (WeigthDebug == 1)
    {
        ResultVerts[i.x].ColorRGB = weightColor / max(totalWeight, 0.001);
    }
   
}