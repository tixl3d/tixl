#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

static const float4 FactorsForPositionAndW[] =
    {
        //     x  y  z  w
        float4(0, 0, 0, 0), // 0 nothing
        float4(1, 0, 0, 0), // 1 for x
        float4(0, 1, 0, 0), // 2 for y
        float4(0, 0, 1, 0), // 3 for z
        float4(0, 0, 0, 1), // 4 for w
        float4(0, 0, 0, 0), // avoid rotation effects
};

cbuffer Params : register(b0)
{
    float4x4 transformSampleSpace;

    float L;
    float LFactor;
    float LOffset;

    float R;
    float RFactor;
    float ROffset;

    float G;
    float GFactor;
    float GOffset;

    float B;
    float BFactor;
    float BOffset;

    float __padding;

    float3 Center;
    float Mode;
    float TranslationSpace;
    float RotationSpace;

    // float A;
    // float AFactor;
    // float AOffset;
   
}

StructuredBuffer<LegacyPoint> Points : t0;
RWStructuredBuffer<LegacyPoint> ResultPoints : u0; // output

Texture2D<float4> inputTexture : register(t1);
sampler texSampler : register(s0);

[numthreads(256, 4, 1)] void main(uint3 i
                                  : SV_DispatchThreadID)
{
    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);
    if(i.x >= pointCount) {
        return;
    }

    uint index = i.x;
    

    LegacyPoint p = Points[index];

    float3 pos = p.Position;
    pos -= Center;

    float3 posInObject = mul(float4(pos.xyz, 0), transformSampleSpace).xyz;
    float4 c = inputTexture.SampleLevel(texSampler, posInObject.xy * float2(1, -1) + float2(0.5, 0.5), 0.0);
    float gray = (c.r + c.g + c.b) / 3;

    // Rotation
    //ResultPoints[index].Rotation = p.Rotation;

    float4 rot = p.Rotation;
    float rotXFactor = (R == 5 ? (c.r * RFactor + ROffset) : 0) +
                       (G == 5 ? (c.g * GFactor + GOffset) : 0) +
                       (B == 5 ? (c.b * BFactor + BOffset) : 0) +
                       (L == 5 ? (gray * LFactor + LOffset) : 0);

    float rotYFactor = (R == 6 ? (c.r * RFactor + ROffset) : 0) +
                       (G == 6 ? (c.g * GFactor + GOffset) : 0) +
                       (B == 6 ? (c.b * BFactor + BOffset) : 0) +
                       (L == 6 ? (gray * LFactor + LOffset) : 0);

    float rotZFactor = (R == 7 ? (c.r * RFactor + ROffset) : 0) +
                       (G == 7 ? (c.g * GFactor + GOffset) : 0) +
                       (B == 7 ? (c.b * BFactor + BOffset) : 0) +
                       (L == 7 ? (gray * LFactor + LOffset) : 0);

    float tau = 3.141578 / 180;

    float4 rot2 = float4(0, 0, 0, 1);

    if (rotXFactor != 0)
    {
        rot2 = qMul(rot2, qFromAngleAxis(rotXFactor * tau, float3(1, 0, 0)));
    }
    if (rotYFactor != 0)
    {
        rot2 = qMul(rot2, qFromAngleAxis(rotYFactor * tau, float3(0, 1, 0)));
    }
    if (rotZFactor != 0)
    {
        rot2 = qMul(rot2, qFromAngleAxis(rotZFactor * tau, float3(0, 0, 1)));
    }

    rot2 = normalize(rot2);
    p.Rotation = qMul(rot, rot2);

    // Stretch
    float3 stretchFactor =float3( 
        (R == 8 ? (c.r * RFactor + ROffset) : 1) *
        (G == 8 ? (c.g * GFactor + GOffset) : 1) *
        (B == 8 ? (c.b * BFactor + BOffset) : 1) *
        (L == 8 ? (gray * LFactor + LOffset) : 1),

        (R == 9 ? (c.r * RFactor + ROffset) : 1) *
        (G == 9 ? (c.g * GFactor + GOffset) : 1) *
        (B == 9 ? (c.b * BFactor + BOffset) : 1) *
        (L == 9 ? (gray * LFactor + LOffset) : 1),

        (R == 10 ? (c.r * RFactor + ROffset) : 1) *
        (G == 10 ? (c.g * GFactor + GOffset) : 1) *
        (B == 10 ? (c.b * BFactor + BOffset) : 1) *
        (L == 10 ? (gray * LFactor + LOffset) : 1)
    );

    
    float3 stretchOffset = Mode < 0.5 ? stretchFactor
                               : float3(stretchFactor) * p.Stretch;

    p.Stretch *= stretchOffset;

    // Position
    float4 ff = FactorsForPositionAndW[(uint)clamp(L, 0, 5.1)] * (gray * LFactor + LOffset) +
                FactorsForPositionAndW[(uint)clamp(R, 0, 5.1)] * (c.r * RFactor + ROffset) +
                FactorsForPositionAndW[(uint)clamp(G, 0, 5.1)] * (c.g * GFactor + GOffset) +
                FactorsForPositionAndW[(uint)clamp(B, 0, 5.1)] * (c.b * BFactor + BOffset);

    float3 offset = Mode < 0.5 ? float3(ff.xyz)
                               : float3(ff.xyz) * p.Position;

    if (TranslationSpace > 0.5)
    {
        offset = qRotateVec3(offset, p.Rotation);
    }

    float3 newPos = p.Position + offset;

    if (RotationSpace < 0.5)
    {
        newPos = qRotateVec3(newPos, rot2);
    }
    p.Position = newPos;

    p.W = Mode < 0.5 ? (p.W + ff.w) : (p.W * (1 + ff.w));
                                    
    ResultPoints[index] = p;
}