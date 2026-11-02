#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float UseVertexSelection;
    float Spherize;
    float Radius;
    float TaperAmount; // New parameter for tapering

    float TwistAmount; // New parameter for twisting
    float TaperAxis;
    float TwistAxis;
    float __padding;

    float3 Pivot;
    float __padding1;
    float3 TwistPivot;
    float __padding2;
    float2 Taper2;
}

StructuredBuffer<PbrVertex> SourceVerts : register(t0);
RWStructuredBuffer<PbrVertex> ResultVerts : register(u0);

inline float3 NormalizeToSphere(float3 position, float targetRadius)
{
    float3 posWithPivot = position - Pivot; // Not sure why this isn't working...
    float currentRadius = length(posWithPivot);
    return posWithPivot * (targetRadius / currentRadius);
}

// Tapering function based on selected axis
inline float2 TaperFunction(float y, float2 taperAmount)
{
    return float2(1.0 - taperAmount.x * y , 1.0 - taperAmount.y * y);
}


inline float3 TwistFunction(float3 position, float twistAmount)
{
    float angle;
    float cosA;
    float sinA;
    float3 twisted;
    float3 posWithPivot = position - TwistPivot; 
    switch ((int)TwistAxis)
    {
    case 0:
        angle = posWithPivot.x * twistAmount;
        cosA = cos(angle);
        sinA = sin(angle);
        twisted = float3(posWithPivot.x, posWithPivot.y * cosA - posWithPivot.z * sinA, posWithPivot.y * sinA + posWithPivot.z * cosA);
        break;
    case 1:
        angle = posWithPivot.y * twistAmount;
        cosA = cos(angle);
        sinA = sin(angle);
        twisted = float3(posWithPivot.x * cosA - posWithPivot.z * sinA, posWithPivot.y, posWithPivot.x * sinA + posWithPivot.z * cosA);
        break;
    case 2:
        angle = posWithPivot.z * twistAmount;
        cosA = cos(angle);
        sinA = sin(angle);
        twisted = float3(posWithPivot.x * cosA - posWithPivot.y * sinA, posWithPivot.x * sinA + posWithPivot.y * cosA, posWithPivot.z);
        break;
    }
    return (twisted + TwistPivot);
}

float3 DeformPosition(float3 pos, float s)
{
    float3 spherePos = NormalizeToSphere(pos, Radius);
    pos = lerp(pos, lerp(pos, spherePos, Spherize), s);

    float3 tapered = pos;
    switch ((int)TaperAxis)
    {
    case 0: tapered.yz *= TaperFunction(pos.x, Taper2 * TaperAmount); break;
    case 1: tapered.xz *= TaperFunction(pos.y, Taper2 * TaperAmount); break;
    case 2: tapered.xy *= TaperFunction(pos.z, Taper2 * TaperAmount); break;
    }
    pos = lerp(pos, tapered, s);

    float3 twisted = TwistFunction(pos, radians(TwistAmount));
    return lerp(pos, twisted, s);
}

[numthreads(64, 1, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourceVerts.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
        return;

    float s = UseVertexSelection > 0.5 ? SourceVerts[i.x].Selected : 1;

    float3 p  = SourceVerts[i.x].Position;
    float3 p0 = DeformPosition(p, s);

    // Columns of the Jacobian: how the deformed position changes along each source axis
    const float eps = 1e-3;
    float3 a = (DeformPosition(p + float3(eps, 0, 0), s) - p0) / eps;
    float3 b = (DeformPosition(p + float3(0, eps, 0), s) - p0) / eps;
    float3 c = (DeformPosition(p + float3(0, 0, eps), s) - p0) / eps;

    ResultVerts[i.x].Position = p0;

    // Normal: inverse transpose of J (via cofactors, sign-corrected for mirroring)
    float3 n = SourceVerts[i.x].Normal;
    float3 newN = n.x * cross(b, c) + n.y * cross(c, a) + n.z * cross(a, b);
    newN *= (dot(a, cross(b, c)) < 0) ? -1 : 1;
    float len = length(newN);
    ResultVerts[i.x].Normal = len > 1e-12 ? newN / len : n;

    // Tangent / bitangent: lie in the surface, so they transform with J itself
    float3 t = SourceVerts[i.x].Tangent;
    float3 tn = t.x * a + t.y * b + t.z * c;
    ResultVerts[i.x].Tangent = lerp(t, normalize(tn), s);

    float3 bt = SourceVerts[i.x].Bitangent;
    float3 bn = bt.x * a + bt.y * b + bt.z * c;
    ResultVerts[i.x].Bitangent = lerp(bt, normalize(bn), s);

    ResultVerts[i.x].TexCoord = SourceVerts[i.x].TexCoord;
    ResultVerts[i.x].TexCoord2 = SourceVerts[i.x].TexCoord2;
    ResultVerts[i.x].Selected = SourceVerts[i.x].Selected;
    ResultVerts[i.x].ColorRGB = SourceVerts[i.x].ColorRGB;
}
