#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/bias-functions.hlsl"
#include "shared/SpriteDef.hlsl"

// Scalars only, so the tight packing of FloatsToBuffer never hits cbuffer alignment rules.
cbuffer Params : register(b0)
{
    float RangeStart;
    float RangeEnd;
    float Spread;
    float Randomize;

    float PositionX;
    float PositionY;
    float PositionZ;

    float RotationX;
    float RotationY;
    float RotationZ;

    float ScaleX;
    float ScaleY;

    float ColorR;
    float ColorG;
    float ColorB;
    float ColorA;

    float PivotX;
    float PivotY;

    float Gain;
    float Bias;

    float OutDirection;
}

cbuffer IntParams : register(b1)
{
    int Unit;
    int Seed;
}

StructuredBuffer<Point> SourcePoints : register(t0);
StructuredBuffer<SpriteDef> Sprites : register(t1);
Texture2D<float4> Gradient : register(t2);
RWStructuredBuffer<Point> ResultPoints : register(u0);

float4 SampleGradient(float position)
{
    uint width, height;
    Gradient.GetDimensions(width, height);
    float x = saturate(position) * (width - 1);
    uint left = (uint)x;
    uint right = min(left + 1, width - 1);
    return lerp(Gradient.Load(int3(left, 0, 0)), Gradient.Load(int3(right, 0, 0)), frac(x));
}

static const int UnitCharacters = 0;
static const int UnitGroups = 1;

float UnitHash(uint unitIndex)
{
    return hash11u(unitIndex + (uint)Seed * _PRIME0);
}

// Rank of the unit in a seeded shuffle, so a fully randomized order still reveals one unit at a time.
float GetShuffledOrder(uint unitIndex, uint unitCount)
{
    float hash = UnitHash(unitIndex);
    uint rank = 0;
    for (uint otherIndex = 0; otherIndex < unitCount; otherIndex++)
    {
        float otherHash = UnitHash(otherIndex);
        if (otherHash < hash || (otherHash == hash && otherIndex < unitIndex))
            rank++;
    }
    return rank;
}

[numthreads(64, 1, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    SourcePoints.GetDimensions(pointCount, stride);
    if (i.x >= pointCount)
        return;

    Point p = SourcePoints[i.x];

    uint spriteCount;
    Sprites.GetDimensions(spriteCount, stride);
    if (spriteCount == 0 || isnan(p.Scale.x))
    {
        ResultPoints[i.x] = p;
        return;
    }

    uint spriteIndex = i.x % spriteCount;
    SpriteDef sprite = Sprites[spriteIndex];

    bool useGroups = Unit == UnitGroups;
    uint unitIndex = useGroups ? sprite.GroupIndex : spriteIndex;

    // Group indices grow monotonically, so the last sprite holds the highest one.
    uint unitCount = useGroups ? Sprites[spriteCount - 1].GroupIndex + 1 : spriteCount;

    float order = unitIndex;
    if (Randomize > 0)
        order = lerp(order, GetShuffledOrder(unitIndex, unitCount), saturate(Randomize));

    float2 gainAndBias = float2(Gain, Bias);
    float spread = max(Spread, 0.0001);
    float builtIn = ApplyGainAndBias(saturate((RangeEnd - order) / spread), gainAndBias);
    // Mirrored so the build-out is the time-reverse of the build-in.
    float builtOut = 1 - ApplyGainAndBias(1 - saturate((RangeStart - order) / spread), gainAndBias);
    float inAmount = 1 - builtIn;
    float amount = saturate(inAmount + builtOut);

    // Position and rotation can leave in a different direction than they came in.
    float motionAmount = clamp(inAmount + builtOut * OutDirection, -1, 1);

    // 0 before the build-in, 0.5 while fully visible, 1 after the build-out
    float gradientPosition = 0.5 * builtIn + 0.5 * builtOut;
    p.Color *= SampleGradient(gradientPosition);

    if (amount <= 0)
    {
        ResultPoints[i.x] = p;
        return;
    }

    float2 boundsMin;
    float2 boundsMax;
    if (useGroups)
    {
        boundsMin = sprite.GroupBoundsMin;
        boundsMax = sprite.GroupBoundsMax;
    }
    else
    {
        float2 glyphCenter = -sprite.Pivot;
        boundsMin = glyphCenter - sprite.Size * 0.5;
        boundsMax = glyphCenter + sprite.Size * 0.5;
    }

    float2 pivotInGlyph = lerp(boundsMin, boundsMax, float2(PivotX, PivotY) * 0.5 + 0.5);
    float3 pivot = p.Position + qRotateVec3(float3(pivotInGlyph, 0), p.Rotation);

    float2 scale = lerp(1, float2(ScaleX, ScaleY), amount);

    float3 angles = radians(float3(RotationX, RotationY, RotationZ)) * motionAmount;
    float4 rotation = qFromAngleAxis(angles.x, float3(1, 0, 0));
    rotation = qMul(rotation, qFromAngleAxis(angles.y, float3(0, 1, 0)));
    rotation = qMul(rotation, qFromAngleAxis(angles.z, float3(0, 0, 1)));

    // Scale along the glyph's own axes, then rotate around the pivot in world space.
    float3 offsetInGlyph = qRotateVec3(p.Position - pivot, qConjugate(p.Rotation));
    offsetInGlyph.xy *= scale;
    float3 offsetFromPivot = qRotateVec3(offsetInGlyph, p.Rotation);

    p.Position = pivot + qRotateVec3(offsetFromPivot, rotation) + float3(PositionX, PositionY, PositionZ) * motionAmount;
    p.Rotation = normalize(qMul(rotation, p.Rotation));
    p.Scale.xy *= scale;
    p.Color *= lerp(1, float4(ColorR, ColorG, ColorB, ColorA), amount);

    ResultPoints[i.x] = p;
}
