#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float DebugParam;
}

cbuffer Params : register(b1)
{
    int SourceCount;
    int ResultCount;
}

struct MinMax
{
    uint3 Min;
    uint3 Max;
};

StructuredBuffer<LegacyPoint> SourcePoints : register(t0);
RWStructuredBuffer<LegacyPoint> ResultPoints : register(u0);
RWStructuredBuffer<MinMax> Bounds : register(u1);

groupshared uint3 g_MinValue = 0xffffffffu;
groupshared uint3 g_MaxValue = 0;

inline uint FloatToOInt(float value)
{
    // For negative values, the mask becomes 0xffffffff.
    // For positive values, the mask becomes 0x80000000.
    uint uvalue = asuint(value);
    uint mask = -int(uvalue >> 31) | 0x80000000;
    return uvalue ^ mask;
}

inline float OIntToFloat(uint value)
{
    // If the msb is set, the mask becomes 0x80000000.
    // If the msb is unset, the mask becomes 0xffffffff.
    uint mask = ((value >> 31) - 1) | 0x80000000;
    return asfloat(value ^ mask);
}

[numthreads(1, 1, 1)] void clear2(uint3 DTid : SV_DispatchThreadID)
{
    Bounds[0].Min = 0xffffffffu;
    Bounds[0].Max = 0;
}

    [numthreads(512, 1, 1)] void main(uint3 DTid : SV_DispatchThreadID, uint Gi : SV_GroupIndex)
{
    uint stride, sourcePointCount;
    SourcePoints.GetDimensions(sourcePointCount, stride);

    LegacyPoint sourcePoint = SourcePoints[DTid.x];
    float3 position = sourcePoint.Position;

    // float isValid = !(DTid.x >= sourcePointCount || isnan(position.x) || isnan(position.y) || isnan(position.z));

    float isValid = !(isnan(position.x) || isnan(position.y) || isnan(position.z));
    uint3 intPos = uint3(
        FloatToOInt(position.x),
        FloatToOInt(position.y),
        FloatToOInt(position.z));

    // This using thread group shared memory to fist compute the group
    // bounds. This might be slightly faster but the current implementation
    // is glitchy.

    // if (isValid)
    // {
    //     InterlockedMin(g_MinValue.x, intPos.x);
    //     InterlockedMin(g_MinValue.y, intPos.y);
    //     InterlockedMin(g_MinValue.z, intPos.z);

    //     InterlockedMax(g_MaxValue.x, intPos.x);
    //     InterlockedMax(g_MaxValue.y, intPos.y);
    //     InterlockedMax(g_MaxValue.z, intPos.z);
    // }

    // //--------------------------------------------
    // GroupMemoryBarrierWithGroupSync();

    // if (Gi.x == 0)
    // {
    //     InterlockedMin(Bounds[0].Min.x, g_MinValue.x);
    //     InterlockedMin(Bounds[0].Min.y, g_MinValue.y);
    //     InterlockedMin(Bounds[0].Min.z, g_MinValue.z);

    //     InterlockedMax(Bounds[0].Max.x, g_MaxValue.x);
    //     InterlockedMax(Bounds[0].Max.y, g_MaxValue.y);
    //     InterlockedMax(Bounds[0].Max.z, g_MaxValue.z);
    // }

    if (isValid)
    {
        InterlockedMin(Bounds[0].Min.x, intPos.x);
        InterlockedMin(Bounds[0].Min.y, intPos.y);
        InterlockedMin(Bounds[0].Min.z, intPos.z);

        InterlockedMax(Bounds[0].Max.x, intPos.x);
        InterlockedMax(Bounds[0].Max.y, intPos.y);
        InterlockedMax(Bounds[0].Max.z, intPos.z);
    }

    AllMemoryBarrierWithGroupSync();

    float3 minPos = float3(
        OIntToFloat(Bounds[0].Min.x),
        OIntToFloat(Bounds[0].Min.y),
        OIntToFloat(Bounds[0].Min.z));
    float3 maxPos = float3(
        OIntToFloat(Bounds[0].Max.x),
        OIntToFloat(Bounds[0].Max.y),
        OIntToFloat(Bounds[0].Max.z));

    float3 centerPos = (minPos + maxPos) * 0.5;

    if (Gi.x == 0)
    {
        ResultPoints[0].Position = centerPos;
        ResultPoints[0].Stretch = maxPos - minPos;
        ResultPoints[0].Selected = 1;
        ResultPoints[0].Color = 1;
        ResultPoints[0].W = 1;
        ResultPoints[0].Rotation = float4(0, 0, 0, 1);
    }
}
