#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"

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

static const float Factors[][4] =
    {
        //     x  y  z  w
        {0, 0, 0, 0}, // 0 nothing
        {1, 0, 0, 0}, // 1 for x
        {0, 1, 0, 0}, // 2 for y
        {0, 0, 1, 0}, // 3 for z
        {0, 0, 0, 1}, // 4 for w
        {0, 0, 0, 0}, // avoid rotation effects
};

cbuffer Params : register(b0)
{
    float4x4 transformSampleSpace;

    float LFactor;
    float LOffset;
    float RFactor;
    float ROffset;

    float GFactor;
    float GOffset;
    float BFactor;
    float BOffset;

    float3 Center;
    float Strength;
    float2 GainAndBias;
}

cbuffer Params : register(b1)
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

#define Attribute_NotUsed 0
#define Attribute_Position_X 1
#define Attribute_Position_Y 2
#define Attribute_Position_Z 3
#define Attribute_F1 4
#define Attribute_F2 5
#define Attribute_Rotate_X 6
#define Attribute_Rotate_Y 7
#define Attribute_Rotate_Z 8
#define Attribute_Scale_Uniform 9
#define Attribute_Scale_X 10
#define Attribute_Scale_Y 11
#define Attribute_Scale_Z 12
#define Attribute_CountMax 12
#define Attribute_Count 13

StructuredBuffer<Point> Points : register(t0);
Texture2D<float4> inputTexture : register(t1);
RWStructuredBuffer<Point> ResultPoints : register(u0); // output

sampler texSampler : register(s0);

[numthreads(256, 4, 1)] void main(uint3 i
                                  : SV_DispatchThreadID)
{
    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);
    if (i.x >= pointCount)
        return;

    uint index = i.x;

    Point p = Points[index];

    float3 pos = p.Position;
    pos -= Center;

    float3 posInObject = mul(float4(pos.xyz, 0), transformSampleSpace).xyz;
    float4 c = inputTexture.SampleLevel(texSampler, posInObject.xy * float2(0.5, -0.5) + float2(0.5, 0.5), 0.0);
    float gray = (c.r + c.g + c.b) / 3;

    float4 rgbl = ApplyGainAndBias(float4(c.rgb, gray), GainAndBias);

    float factors[Attribute_Count] = {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0};

    float strength = Strength * c.a * (StrengthFactor == 0 ? 1 : (StrengthFactor == 1) ? p.FX1
                                                                                       : p.FX2);

    factors[clamp(L, 0, Attribute_CountMax)] += (rgbl.w * LFactor + LOffset) * strength;
    factors[clamp(R, 0, Attribute_CountMax)] += (rgbl.r * RFactor + ROffset) * strength;
    factors[clamp(G, 0, Attribute_CountMax)] += (rgbl.g * GFactor + GOffset) * strength;
    factors[clamp(B, 0, Attribute_CountMax)] += (rgbl.b * BFactor + BOffset) * strength;

    float3 offset = float3(factors[Attribute_Position_X],
                           factors[Attribute_Position_Y],
                           factors[Attribute_Position_Z]) *
                    strength;

    if (TranslationSpace == 1)
    {
        offset = qRotateVec3(offset, p.Rotation);
    }
    p.Position += offset;

    p.Scale += (float3(factors[Attribute_Scale_X],
                       factors[Attribute_Scale_Y],
                       factors[Attribute_Scale_Z]) +
                factors[Attribute_Scale_Uniform]) *
               strength;

    p.FX1 += factors[Attribute_F1] * strength;
    p.FX2 += factors[Attribute_F2] * strength;

    float4 deltaRot = float4(0, 0, 0, 1);
    deltaRot = qMul(deltaRot, qFromAngleAxis(factors[Attribute_Rotate_X] * TAU, float3(1, 0, 0)));
    deltaRot = qMul(deltaRot, qFromAngleAxis(factors[Attribute_Rotate_X] * TAU, float3(0, 1, 0)));
    deltaRot = qMul(deltaRot, qFromAngleAxis(factors[Attribute_Rotate_X] * TAU, float3(0, 0, 1)));

    deltaRot = normalize(deltaRot);
    p.Rotation = qMul(deltaRot, deltaRot);

    // // Rotation
    // // ResultPoints[index].Rotation = p.Rotation;

    // float4 rot = p.Rotation;
    // float rotXFactor = (R == 5 ? (c.r * RFactor + ROffset) : 0) +
    //                    (G == 5 ? (c.g * GFactor + GOffset) : 0) +
    //                    (B == 5 ? (c.b * BFactor + BOffset) : 0) +
    //                    (L == 5 ? (gray * LFactor + LOffset) : 0);

    // float rotYFactor = (R == 6 ? (c.r * RFactor + ROffset) : 0) +
    //                    (G == 6 ? (c.g * GFactor + GOffset) : 0) +
    //                    (B == 6 ? (c.b * BFactor + BOffset) : 0) +
    //                    (L == 6 ? (gray * LFactor + LOffset) : 0);

    // float rotZFactor = (R == 7 ? (c.r * RFactor + ROffset) : 0) +
    //                    (G == 7 ? (c.g * GFactor + GOffset) : 0) +
    //                    (B == 7 ? (c.b * BFactor + BOffset) : 0) +
    //                    (L == 7 ? (gray * LFactor + LOffset) : 0);

    // // Stretch
    // float3 stretchFactor = float3(
    //     (R == 8 ? (c.r * RFactor + ROffset) : 1) *
    //         (G == 8 ? (c.g * GFactor + GOffset) : 1) *
    //         (B == 8 ? (c.b * BFactor + BOffset) : 1) *
    //         (L == 8 ? (gray * LFactor + LOffset) : 1),

    //     (R == 9 ? (c.r * RFactor + ROffset) : 1) *
    //         (G == 9 ? (c.g * GFactor + GOffset) : 1) *
    //         (B == 9 ? (c.b * BFactor + BOffset) : 1) *
    //         (L == 9 ? (gray * LFactor + LOffset) : 1),

    //     (R == 10 ? (c.r * RFactor + ROffset) : 1) *
    //         (G == 10 ? (c.g * GFactor + GOffset) : 1) *
    //         (B == 10 ? (c.b * BFactor + BOffset) : 1) *
    //         (L == 10 ? (gray * LFactor + LOffset) : 1));

    // float3 stretchOffset = Mode < 0.5 ? stretchFactor
    //                                   : float3(stretchFactor) * p.Scale;

    // p.Scale *= stretchOffset;

    // // Position
    // float4 ff = FactorsForPositionAndW[(uint)clamp(L, 0, 5.1)] * (gray * LFactor + LOffset) +
    //             FactorsForPositionAndW[(uint)clamp(R, 0, 5.1)] * (c.r * RFactor + ROffset) +
    //             FactorsForPositionAndW[(uint)clamp(G, 0, 5.1)] * (c.g * GFactor + GOffset) +
    //             FactorsForPositionAndW[(uint)clamp(B, 0, 5.1)] * (c.b * BFactor + BOffset);

    // float3 offset = Mode < 0.5 ? float3(ff.xyz)
    //                            : float3(ff.xyz) * p.Position;

    // if (TranslationSpace > 0.5)
    // {
    //     offset = qRotateVec3(offset, p.Rotation);
    // }

    // float3 newPos = p.Position + offset;

    // if (RotationSpace < 0.5)
    // {
    //     newPos = qRotateVec3(newPos, rot2);
    // }
    // p.Position = newPos;

    // p.FX1 = Mode < 0.5 ? (p.FX1 + ff.w) : (p.FX1 * (1 + ff.w));

    ResultPoints[index] = p;
}