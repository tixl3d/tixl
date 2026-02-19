#include "shared/hash-functions.hlsl"
// #include "shared/point.hlsl"
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

StructuredBuffer<PbrVertex> VerticesA : t0;        // input
StructuredBuffer<PbrVertex> VerticesB : t1;        // input
RWStructuredBuffer<PbrVertex> ResultVertices : u0; // output

[numthreads(64, 1, 1)] void main(uint3 i
                                 : SV_DispatchThreadID)
{
    uint resultCount, countA, countB, stride;
    ResultVertices.GetDimensions(resultCount, stride);
    VerticesA.GetDimensions(countA, stride);
    VerticesB.GetDimensions(countB, stride);

    if (i.x > resultCount)
        return;

    uint aIndex = i.x;
    uint bIndex = i.x;

    float t = i.x / (float)resultCount;

    if (PairingMode > 0.5 && countA != countB)
    {
        aIndex = (int)(countA * t);
        bIndex = (int)(countB * t);
    }

    PbrVertex A = VerticesA[aIndex];
    PbrVertex B = VerticesB[bIndex];

    float f = 0;

    if (BlendMode < 0.5)
    {
        f = BlendFactor;
    }
    else if (BlendMode < 1.5)
    {
        f = A.Selected;
    }
    else if (BlendMode < 2.5)
    {
        f = (1 - B.Selected);
    }

    // Ranged
    // see https://www.desmos.com/calculator/zxs1fy06uh
    else if (BlendMode < 3.5)
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

    ResultVertices[i.x].Position = lerp(A.Position, B.Position, f);
    ResultVertices[i.x].Normal = lerp(A.Normal, B.Normal, f);
    ResultVertices[i.x].Tangent = lerp(A.Tangent, B.Tangent, f);
    ResultVertices[i.x].Bitangent = lerp(A.Bitangent, B.Bitangent, f);
    ResultVertices[i.x].TexCoord = lerp(A.TexCoord, B.TexCoord, f);
    ResultVertices[i.x].Selected = lerp(A.Selected, B.Selected, f);
    ResultVertices[i.x].ColorRGB = lerp(A.ColorRGB, B.ColorRGB, f);
}
