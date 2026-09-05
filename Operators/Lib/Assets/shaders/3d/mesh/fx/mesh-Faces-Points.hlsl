#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer FloatParams : register(b0)
{
    float3 Scale;
    float Fx1;

    float Fx2;
    float3 OffsetByTBN;

    float4 Color;

    float EnableScaleWithFaceArea;
    float OffsetScale;
}

cbuffer FloatParams : register(b1)
{
    int OrientationMode;
    int OutputMode;
    int FaceCount;
    int ResultCount;
}


static const  int CornerCountsForMode[]  = {1,3,5};

StructuredBuffer<int3> Faces : register(t0);
StructuredBuffer<PbrVertex> SourceVertices : register(t1);
Texture2D<float4> Texture : register(t2);
sampler LinearSampler : register(s0);

RWStructuredBuffer<Point> ResultPoints : register(u0);

float CalculateTriangleArea(float3 vertexA, float3 vertexB, float3 vertexC)
{
    float3 side1 = vertexB - vertexA;
    float3 side2 = vertexC - vertexA;

    float3 crossProduct = cross(side1, side2);

    return 0.5 * length(crossProduct);
}

float3 CalculateInscribedCircleCenter(float3 vertexA, float3 vertexB, float3 vertexC)
{
    float3 side1 = normalize(vertexB - vertexA);
    float3 side2 = normalize(vertexC - vertexA);
    float3 side3 = normalize(vertexC - vertexB);

    float cosA = dot(side2, side3);
    float cosB = dot(side1, side3);
    float cosC = dot(side1, side2);

    float sinA = sqrt(1.0 - cosA * cosA);
    float sinB = sqrt(1.0 - cosB * cosB);
    float sinC = sqrt(1.0 - cosC * cosC);

    float semiPerimeter = length(vertexA - vertexB) + length(vertexB - vertexC) + length(vertexC - vertexA);
    semiPerimeter *= 0.5;

    float area = sqrt(semiPerimeter * (semiPerimeter - length(vertexA - vertexB)) * (semiPerimeter - length(vertexB - vertexC)) * (semiPerimeter - length(vertexC - vertexA)));

    float3 center = (length(vertexB - vertexC) * vertexA + length(vertexC - vertexA) * vertexB + length(vertexA - vertexB) * vertexC) / (length(vertexB - vertexC) + length(vertexC - vertexA) + length(vertexA - vertexB));

    return center;
}

float CalculateInscribedCircleRadius(float3 vertexA, float3 vertexB, float3 vertexC)
{
    // Calculate the lengths of the sides
    float a = length(vertexB - vertexC);
    float b = length(vertexC - vertexA);
    float c = length(vertexA - vertexB);

    // Calculate semi-perimeter
    float s = (a + b + c) * 0.5;

    // Calculate area using Heron's formula
    float area = sqrt(s * (s - a) * (s - b) * (s - c));

    // Radius of inscribed circle
    return area / s;
}

void CalculateTangentBitangent(
    float3 vertexA, float3 vertexB, float3 vertexC,
    float2 uvA, float2 uvB, float2 uvC,
    out float3 tangent, out float3 bitangent)
{
    // Calculate the edges of the triangle
    float3 edge1 = vertexB - vertexA;
    float3 edge2 = vertexC - vertexA;

    // Calculate the difference in texture coordinates
    float2 deltaUV1 = uvB - uvA;
    float2 deltaUV2 = uvC - uvA;

    // Solve the system of equations to find tangent and bitangent
    float f = 1.0 / (deltaUV1.x * deltaUV2.y - deltaUV2.x * deltaUV1.y);
    tangent.x = f * (deltaUV2.y * edge1.x - deltaUV1.y * edge2.x);
    tangent.y = f * (deltaUV2.y * edge1.y - deltaUV1.y * edge2.y);
    tangent.z = f * (deltaUV2.y * edge1.z - deltaUV1.y * edge2.z);
    tangent = normalize(tangent);

    bitangent.x = f * (-deltaUV2.x * edge1.x + deltaUV1.x * edge2.x);
    bitangent.y = f * (-deltaUV2.x * edge1.y + deltaUV1.x * edge2.y);
    bitangent.z = f * (-deltaUV2.x * edge1.z + deltaUV1.x * edge2.z);
    bitangent = normalize(bitangent);

    // Ensure tangent, bitangent, and normal form a right-handed coordinate system
    float3 normal = normalize(cross(edge1, edge2));
    tangent = normalize(tangent - normal * dot(normal, tangent));
    bitangent = cross(normal, tangent);
}

#define OUTPUT_CENTERS 0
#define OUTPUT_CORNERS 1
#define OUTPUT_CORNERLOOPS 2

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numFaces, stride;
    Faces.GetDimensions(numFaces, stride);
    int idx = i.x;
    if (idx >= ResultCount)
        return;

    int cornerCount = CornerCountsForMode[OutputMode];

    int faceIndex = OutputMode == 0? idx : idx/ cornerCount; 

    int3 faceVertIndices = Faces[faceIndex];

    float2 uv0 = SourceVertices[faceVertIndices.x].TexCoord;
    float2 uv1 = SourceVertices[faceVertIndices.y].TexCoord; 
    float2 uv2 = SourceVertices[faceVertIndices.z].TexCoord;


    float3 p0 = SourceVertices[faceVertIndices.x].Position;
    float3 p1 = SourceVertices[faceVertIndices.y].Position;
    float3 p2 = SourceVertices[faceVertIndices.z].Position;
    float3 pCenter = CalculateInscribedCircleCenter(p0, p1, p2);

    //PbrVertex v = SourceVertices[idx];

    float3 normal = normalize(
        SourceVertices[faceVertIndices.x].Normal
      + SourceVertices[faceVertIndices.y].Normal
      + SourceVertices[faceVertIndices.z].Normal);

    // Calculate tangent and bitangent vectors
    float3 tangent, bitangent;
    CalculateTangentBitangent(p0, p1, p2, uv0, uv1, uv2, tangent, bitangent);
    pCenter += OffsetByTBN.x * tangent * OffsetScale + OffsetByTBN.y * bitangent * OffsetScale + OffsetByTBN.z * normal * OffsetScale;

    float3 upVector = float3(0, 1, 0);
    float4 faceOrientation = OrientationMode==0 
                        ? qLookAt(normal, upVector)
                        : qFromMatrix3(transpose( float3x3(tangent, bitangent,normal )));
    
    Point p;
    p.FX1 = Fx1;
    p.FX2 = Fx2;
    p.Scale = lerp(1,
                                    CalculateInscribedCircleRadius(p0, p1, p2),
                                    EnableScaleWithFaceArea) *
                                Scale;

    p.Rotation = normalize(faceOrientation);

    if(OutputMode == OUTPUT_CENTERS) 
    {
        p.Position = pCenter;

        float3 avgColor = (SourceVertices[faceVertIndices.x].ColorRGB + SourceVertices[faceVertIndices.y].ColorRGB + SourceVertices[faceVertIndices.z].ColorRGB)/3;

        float2 uvCenter = (uv0 + uv1 + uv2) / 3;
        p.Color = Color * Texture.SampleLevel(LinearSampler, uvCenter, 0) * float4(avgColor,1);
    }
    else if(OutputMode == OUTPUT_CORNERS) 
    {
        int cornerIndex = idx % cornerCount;
        p.Position = SourceVertices[faceVertIndices[cornerIndex]].Position;
        float2 uv = SourceVertices[faceVertIndices[cornerIndex]].TexCoord;
        float4 vertexColor = float4( SourceVertices[faceVertIndices[cornerIndex]].ColorRGB,1);
        p.Color = Color * Texture.SampleLevel(LinearSampler, uv, 0) * vertexColor;
    }
    else {
        int cornerIndex = idx % cornerCount;
        if(cornerIndex == 4) {
            p.Scale = NAN;
        } 
        else 
        {
            cornerIndex %=3;    // close loop

            float3 pos =SourceVertices[faceVertIndices[cornerIndex]].Position;
            
            p.Position = lerp(pCenter,  SourceVertices[faceVertIndices[cornerIndex]].Position, OffsetScale);
            
            float2 uv = SourceVertices[faceVertIndices[cornerIndex]].TexCoord;
            float4 vertexColor = float4( SourceVertices[faceVertIndices[cornerIndex]].ColorRGB,1);
            p.Color = Color * Texture.SampleLevel(LinearSampler, uv, 0) * vertexColor;
        }
    }


    ResultPoints[idx] = p; 
}