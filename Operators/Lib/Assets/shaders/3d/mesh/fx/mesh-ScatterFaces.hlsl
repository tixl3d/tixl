#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float Direction;
    float Amount;
    float Rotate;
    float Push;

    float Shrink;
    float Scatter;
    float Distort;
    float NoiseAmount;

    float NoiseFrequency;
    float NoisePhase; // 8
    float NoiseVariation;
    float UseVertexSelection;

    float3 AmountDistribution; // 12
    float __padding2;

    float3 Center;
}

StructuredBuffer<int3> Faces : t0;
StructuredBuffer<PbrVertex> SourceVertices : t1;

RWStructuredBuffer<PbrVertex> ResultVertices : u0;
static float3 variationOffset;
static float4x4 transform;
static float OffsetDirection = 0.5;

const static float DirectionSurface = 0.5;
const static float DirectionNoise = 1.5;
const static float DirectionCenter = 2.5;

float3 GetNoise(float3 pos, float3 variation)
{
    float3 noiseLookup = (pos * 0.91 + variation) * NoiseFrequency + NoisePhase;
    float3 noise = snoiseVec3(noiseLookup);
    return (noise + OffsetDirection) * AmountDistribution;
}

void tranformVertex(int vertexIndex, float influence, float3 pCenter, float3 normalizedDirection)
{
    float3 pos = ResultVertices[vertexIndex].Position;
    float vertexRandom = hash11(vertexIndex);
    if (Shrink != 0)
    {
        float shrinkScale = influence * Shrink * Amount;
        pos.xyz += (pCenter - pos) * shrinkScale;
    }

    float pushStrength = Distort * hash11(vertexIndex) * Amount;
    pos.xyz += normalizedDirection * influence * pushStrength;
    pos = mul(transform, float4(pos, 1)).xyz;
    pos.xyz += normalizedDirection * influence * pushStrength;

    ResultVertices[vertexIndex].Position = pos;
    ResultVertices[vertexIndex].Normal = normalize(mul(float4(ResultVertices[vertexIndex].Normal.xyz, 0), transform)).xyz;
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numFaces, stride;
    Faces.GetDimensions(numFaces, stride);
    if (i.x >= numFaces)
    {
        return;
    }

    int faceIndex = i.x;
    float3 random = hash41u(faceIndex).xyz;
    int3 faceVertices = Faces[faceIndex];

    float3 pos0 = SourceVertices[faceVertices.x].Position;
    float3 pos1 = SourceVertices[faceVertices.y].Position;
    float3 pos2 = SourceVertices[faceVertices.z].Position;

    float2 uv0 = SourceVertices[faceVertices.x].TexCoord;
    float2 uv1 = SourceVertices[faceVertices.y].TexCoord;
    float2 uv2 = SourceVertices[faceVertices.z].TexCoord;
    float2 avgUv = (uv0 + uv1 + uv2) / 3;

    float3 pCenter = (pos0 + pos1 + pos2) / 3;
    float3 posInWorld = pCenter;

    float3 variationOffset = hash41u((float)(faceIndex % 1234) / 0.123).xyz * NoiseVariation;
    float3 noiseOffset = GetNoise(posInWorld, variationOffset);
    float3 direction = 0;

    float3 avgNormal = (SourceVertices[faceVertices.x].Normal + SourceVertices[faceVertices.y].Normal + SourceVertices[faceVertices.z].Normal) / 3;

    if (Direction < DirectionSurface)
    {
        direction = avgNormal + noiseOffset * NoiseAmount;
    }
    else if (Direction < DirectionNoise)
    {
        direction = noiseOffset * NoiseAmount;
    }
    else
    {
        direction = (posInWorld - Center) * (1 + noiseOffset.x * NoiseAmount);
    }

    float avgSelection = (SourceVertices[faceVertices.x].Selected + SourceVertices[faceVertices.y].Selected + SourceVertices[faceVertices.z].Selected) / 3;

    float weight = UseVertexSelection > 0.5 ? avgSelection : 1;

    // DANGER! This will cause multiple access problems with shared vertices!

        PbrVertex v1 = SourceVertices[faceVertices.x];
    PbrVertex v2 = SourceVertices[faceVertices.y];
    PbrVertex v3 = SourceVertices[faceVertices.z];

    ResultVertices[faceVertices.x] = v1;
    ResultVertices[faceVertices.y] = v2;
    ResultVertices[faceVertices.z] = v3;

    int seed = faceIndex;

    // float3 direction = (pCenter - Center);
    // float3 direction = offset;

    float distance = length(direction);

    float3 normalizedDirection = abs(distance) > 0.001 ? direction / distance : 0;

    float influence = clamp(weight, 0.0, 1);

    float angleX = influence * 3.141578 * Rotate / 360 * random.x * Amount;
    float cax = cos(angleX);
    float sax = sin(angleX);
    float4x4 rotx = {1, 0, 0, 0,
                     0, cax, -sax, 0,
                     0, sax, cax, 0,
                     0, 0, 0, 1};
    float angleY = influence * 3.141578 * Rotate / 360 * random.y * Amount;
    float cay = cos(angleY);
    float say = sin(angleY);
    float4x4 roty = {cay, 0, say, 0,
                     0, 1, 0, 0,
                     -say, 0, cay, 0,
                     0, 0, 0, 1};

    float4x4 translateToOrigin = {1, 0, 0, -pCenter.x,
                                  0, 1, 0, -pCenter.y,
                                  0, 0, 1, -pCenter.z,
                                  0, 0, 0, 1};

    float4x4 translateBack = {1, 0, 0, pCenter.x,
                              0, 1, 0, pCenter.y,
                              0, 0, 1, pCenter.z,
                              0, 0, 0, 1};

    float3 offset2 = direction * influence * (Push + random.z * Scatter) * Amount;
    float4x4 translateOffset = {1, 0, 0, offset2.x,
                                0, 1, 0, offset2.y,
                                0, 0, 1, offset2.z,
                                0, 0, 0, 1};

    float4x4 rotation = mul(rotx, roty);

    transform = translateToOrigin;
    transform = mul(rotation, transform);
    transform = mul(translateBack, transform);
    transform = mul(translateOffset, transform);

    tranformVertex(faceVertices.x, influence, pCenter, direction);
    tranformVertex(faceVertices.y, influence, pCenter, direction);
    tranformVertex(faceVertices.z, influence, pCenter, direction);

    pos0 = ResultVertices[faceVertices.x].Position;
    pos1 = ResultVertices[faceVertices.y].Position;
    pos2 = ResultVertices[faceVertices.z].Position;

    float3 n = cross(pos1 - pos0, pos2 - pos0);

    float i2 = saturate(influence * 10);
    ResultVertices[faceVertices.x].Normal = lerp(ResultVertices[faceVertices.x].Normal, n, i2);
    ResultVertices[faceVertices.y].Normal = lerp(ResultVertices[faceVertices.y].Normal, n, i2);
    ResultVertices[faceVertices.z].Normal = lerp(ResultVertices[faceVertices.z].Normal, n, i2);
}
