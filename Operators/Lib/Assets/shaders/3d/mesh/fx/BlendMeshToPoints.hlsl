#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float BlendFactor;
    float BlendMode;
    float PairingMode;
    float Width;
    float Scatter;
}

StructuredBuffer<PbrVertex> Vertices : t0;
StructuredBuffer<Point> Points : t1;
RWStructuredBuffer<PbrVertex> ResultVertices : u0;

[numthreads(64, 1, 1)] void main(uint3 i
                                 : SV_DispatchThreadID)
{
    uint resultCount, countA, countB, stride;
    ResultVertices.GetDimensions(resultCount, stride);
    Vertices.GetDimensions(countA, stride);
    Points.GetDimensions(countB, stride);

    if (i.x > resultCount)
        return;

    uint vIndex = i.x;
    uint pIndex = i.x;

    float t = i.x / (float)resultCount;

    if (PairingMode > 0.5 && countA != countB)
    {
        vIndex = (int)(countA * t);
        pIndex = (int)(countB * t);
    }

    PbrVertex v = Vertices[vIndex];
    Point p = Points[pIndex];

    float f = 0;

    if (BlendMode < 0.5)
    {
        f = BlendFactor;
    }
    else if (BlendMode < 1.5)
    {
        f = v.Selected;
    }
    else if (BlendMode < 2.5)
    {
        f = (1 - p.FX1);
    }
    else if (BlendMode < 3.5)
    {
        f =  p.FX2;
    }

    // Ranged
    // see https://www.desmos.com/calculator/zxs1fy06uh
    else if (BlendMode < 4.5)
    {
        f = 1 - saturate((t - BlendFactor) / Width - BlendFactor + 1);
    }
    else
    {
        float b = BlendFactor % 2;
        if (b > 1)
        {
            b = 2 - b;
            t = 1 - t;
        }
        f = 1 - smoothstep(0, 1, saturate((t - b) / Width - b + 1));
    }

    float fallOffFromCenter = smoothstep(0, 1, 1 - abs(f - 0.5) * 2);
    f += (hash11(t) - 0.5) * Scatter * fallOffFromCenter;

    // Create a rotation from the mesh's local frame to world space
    float3x3 meshRotMatrix = float3x3(v.Tangent, v.Bitangent, v.Normal);
    float4 meshRot = qFromMatrix3Precise(transpose(meshRotMatrix));

    // Spherical interpolate between mesh rotation and point rotation
    float4 blendedRot = qSlerp(meshRot, p.Rotation, f);
    float4x4 blendedMatRaw = qToMatrix(blendedRot);

    // Transpose to get basis vectors as rows
    float3x3 blendedMatrix = transpose((float3x3)blendedMatRaw);
    
    ResultVertices[i.x].Position = lerp(v.Position, p.Position, f);
    // Blend the original and rotated vectors based on factor f
    ResultVertices[i.x].Tangent   = blendedMatrix[0];
    ResultVertices[i.x].Bitangent = blendedMatrix[1];
    ResultVertices[i.x].Normal    = blendedMatrix[2];
    
    ResultVertices[i.x].TexCoord = v.TexCoord;
    ResultVertices[i.x].Selected = v.Selected;
    ResultVertices[i.x].ColorRGB = v.ColorRGB;
}
