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


cbuffer FloatParams : register(b0)
{
    float LFactor;
    float LOffset;

    float RFactor;
    float ROffset;

    float GFactor;
    float GOffset;

    float BFactor;
    float BOffset;

    float Strength;
}

cbuffer IntParams : register(b1)
{
    int L;
    int R;
    int G;
    int B;
    int Mode;
    int TranslationSpace;
    int RotationSpace;
    int StrengthFactor;
}


StructuredBuffer<Point> Points : t0;
RWStructuredBuffer<Point> ResultPoints : u0; // output

Texture2D<float4> inputTexture : register(t1);
sampler texSampler : register(s0);

[numthreads(256, 4, 1)] void main(uint3 i
                                  : SV_DispatchThreadID)
{
    uint index = i.x;

    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);
    if (i.x >= pointCount)
        return;

    Point p = Points[index];
    
    float strength = Strength * (StrengthFactor == 0
                                     ? 1
                                 : (StrengthFactor == 1) ? p.FX1
                                                         : p.FX2);


    float divider = pointCount < 2 ? 1 : (pointCount - 0);
    float f = (float)i.x / divider;
    float2 uv = float2(f, 0.5);

    // float3 pos = P.position;
    // pos -= Center;

    // float3 posInObject = mul(float4(pos.xyz, 0), transformSampleSpace).xyz;

    float4 c = inputTexture.SampleLevel(texSampler, uv, 0);
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
    p.Rotation = qSlerp(p.Rotation,  qMul(rot, rot2), strength);

    // Stretch
    //float3 stretch = p.Stretch;
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
                               : float3(stretchFactor) * p.Scale;

    p.Scale *= lerp(1, stretchOffset, strength);

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
    p.Position = lerp(p.Position, newPos, strength);


    float fx1Factor =
        (R == 4 ? (c.r * RFactor + ROffset) : 0) +
        (G == 4 ? (c.g * GFactor + GOffset) : 0) +
        (B == 4 ? (c.b * BFactor + BOffset) : 0) +
        (L == 4 ? (gray * LFactor + LOffset) : 0);

    p.FX1 += fx1Factor;
    p.FX1 = lerp(p.FX1, Mode == 0 ? (p.FX1 + fx1Factor)
                                    : (p.FX1 * (1 + fx1Factor)), strength);

    float fx2Factor =
        (R == 11 ? (c.r * RFactor + ROffset) : 0) +
        (G == 11 ? (c.g * GFactor + GOffset) : 0) +
        (B == 11 ? (c.b * BFactor + BOffset) : 0) +
        (L == 11 ? (gray * LFactor + LOffset) : 0);

    //p.FX2 += fx2Factor;
    p.FX2 = lerp(p.FX2,  Mode == 0 ? (p.FX2 + fx2Factor)
                                    : (p.FX2 * (1 + fx2Factor)), strength);

    ResultPoints[index] = p;
}