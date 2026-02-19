#include "shared/pbr.hlsl"
#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"


cbuffer EmitParameter : register(b0)
{
    float Seed;
    float UseVertexSelection;
};

uint wang_hash(in out uint seed)
{
    seed = (seed ^ 61) ^ (seed >> 16);
    seed *= 9;
    seed = seed ^ (seed >> 4);
    seed *= 0x27d4eb2d;
    seed = seed ^ (seed >> 15);
    return seed;
}

struct FaceProperties {
    float normalizedFaceArea;
    float cdf;
};

StructuredBuffer<PbrVertex> Vertices : t0;
StructuredBuffer<int3> FaceIndices : t1;
StructuredBuffer<FaceProperties> CDFs : t2;
Texture2D<float4> ColorMap : t3;

sampler texSampler : register(s0);

RWStructuredBuffer<LegacyPoint> ResultPoints : u0;
RWStructuredBuffer<float4> ResultColors : u1;

[numthreads(160,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, faceCount, stride; 

    ResultPoints.GetDimensions(pointCount, stride);
    FaceIndices.GetDimensions(faceCount, stride);

    if (i.x >= pointCount)
        return; 


    uint rng_state = (i.x * (uint)(Seed * 10317));
    float xi = (float(wang_hash(rng_state)) * (1.0 / 4294967296.0));

    uint left = 0;
    uint width = faceCount -2;
    uint right = width;
    uint steps = log2(width) + 1;
    uint cdfIndex;
    for (uint j = 0; j < steps; ++j)
    {
        uint middle = (right + left) / 2 ; 
        float cdfSegStart = CDFs[middle].cdf;
        float cdfSegEnd = CDFs[middle + 1].cdf;
        if (right == left || (cdfSegStart <= xi  && cdfSegEnd > xi))
        {
            cdfIndex = middle +1;
        }
        else {
            if (xi < cdfSegStart)
            {
                right = middle;
            }
            else
            {
                left = middle +1;
            }    
        }
    }


    uint faceIndex = cdfIndex;
    if (faceIndex >= (uint)faceCount)
        return;

    float xi1 = (float(wang_hash(rng_state)) * (1.0 / 4294967296.0));
    float xi2 = float(wang_hash(rng_state)) * (1.0 / 4294967296.0);

    uint3 fIndices = FaceIndices[faceIndex];

    // Compute barycentric coordinates
    LegacyPoint p;

    p.Selected = 1;
    p.Stretch = 1;
    float xi1Sqrt = sqrt(xi1);
    float u = 1.0 - xi1Sqrt;
    float v = xi2 * xi1Sqrt; 
    float w = 1.0 - u - v;

    p.Position = Vertices[fIndices[0]].Position * u
               + Vertices[fIndices[1]].Position * v
               + Vertices[fIndices[2]].Position * w; 

    float3 normal = normalize(Vertices[fIndices[0]].Normal * u 
                  + Vertices[fIndices[1]].Normal * v
                  + Vertices[fIndices[2]].Normal * w);

    float3 binormal = normalize(Vertices[fIndices[0]].Bitangent * u 
                    + Vertices[fIndices[1]].Bitangent * v
                    + Vertices[fIndices[2]].Bitangent * w);

    float3 tangent = normalize(Vertices[fIndices[0]].Tangent * u 
                   + Vertices[fIndices[1]].Tangent * v
                   + Vertices[fIndices[2]].Tangent * w);
    
    float3x3 orientationDest= float3x3( tangent,binormal, normal );

    p.Rotation = normalize(qFromMatrix3Precise(transpose(orientationDest)));
    p.W = 1;



    float2 uv = Vertices[fIndices[0]].TexCoord * u 
            + Vertices[fIndices[1]].TexCoord * v
            + Vertices[fIndices[2]].TexCoord * w;

    float4 color = ColorMap.SampleLevel(texSampler, uv* float2(1, -1), 0);
    ResultColors[i.x] = color;
    p.Color = color;
    ResultPoints[i.x] = p;
}

