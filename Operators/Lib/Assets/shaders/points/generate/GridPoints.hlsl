#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 Size;
    float __padding1;

    float3 Center;
    float __padding2;

    float3 OrientationAxis;
    float OrientationAngle;

    float3 Pivot;
    float __padding3;

    float4 Color;

    float PointScale;
    float FX1;
    float FX2;
}

cbuffer Params : register(b1)
{
    int3 Count;
    int SizeMode;
    int Tiling;
}

RWStructuredBuffer<Point> ResultPoints : u0; // output

static const float2 HexOffsetsAndAngles[] =
    {
        float2(-1, 90), float2(0, 30),    // 0
        float2(0, 150), float2(-1, -30),  // 1
        float2(-1, -150), float2(0, -90), // 2
        float2(0, 30), float2(-1, 90),    // 3
        float2(-1, -30), float2(0, 150),  // 4
        float2(0, -90), float2(-1, -150), // 5
};

static const float ToRad = 3.141578 / 180;

[numthreads(256, 1, 1)] void main(uint3 i
                                  : SV_DispatchThreadID)
{
    // Note: We assume that 0 count have been clamped earlier
    uint3 c = (uint3)Count;

    uint index = i.x;

    uint3 cell = int3(
        index % c.x,
        index / c.x % c.y,
        index / (c.x * c.y) % c.z);

    float3 clampedCount = uint3(
        c.x == 1 ? 1 : c.x - 1,
        c.y == 1 ? 1 : c.y - 1,
        c.z == 1 ? 1 : c.z - 1);

    float3 zeroAdjustedSize = float3(
        c.x == 1 ? 0 : Size.x,
        c.y == 1 ? 0 : Size.y,
        c.z == 1 ? 0 : Size.z);

    float3 pos = SizeMode > 0.5 ? (cell / clampedCount) - (Pivot + 0.5)
                                : cell - clampedCount * (Pivot + 0.5);

    pos *= zeroAdjustedSize;

    ResultPoints[index].Color = Color;
    ResultPoints[index].FX1 = FX1;
    ResultPoints[index].FX2 = FX2;
    ResultPoints[index].Scale = PointScale;

    if (Tiling < 0.5)
    {
        pos += Center;
        ResultPoints[index].Position = pos;
        ResultPoints[index].Rotation = qFromAngleAxis(OrientationAngle * PI / 180, normalize(OrientationAxis));
        return;
    }

    // Triangular
    if (Tiling < 1.5)
    {
        int hexAttrIndex = cell.x % 2 + ((cell.y + 3) % 6) * 2;
        float2 offsetAndAngles = HexOffsetsAndAngles[hexAttrIndex];

        float gridWidth = SizeMode > 0.5 ? zeroAdjustedSize.x / (c.x - 1)
                                         : zeroAdjustedSize.x;
        pos.x += offsetAndAngles.x * gridWidth.x * 0.3333;

        const float HexScale = sqrt(3.0); // 0.578f * 3;
        pos.x *= HexScale;
        float rotDelta = (180 + offsetAndAngles.y) * ToRad;

        pos += Center;
        ResultPoints[index].Position = pos;
        ResultPoints[index].FX1 = FX1 * (2 / 3.0);
        ResultPoints[index].Rotation = qFromAngleAxis(OrientationAngle * PI / 180 + rotDelta, normalize(OrientationAxis));
        return;
    }

    // Honeycomb
    if (Tiling < 2.5)
    {
        float3 gridSize = SizeMode > 0.5 ? zeroAdjustedSize / (c - 1)
                                         : zeroAdjustedSize;

        bool isOddRow = cell.x % 2 > 0;
        pos.y += isOddRow ? (gridSize.y / 2) : 0;

        bool isOddLayer = cell.z % 2 > 0;
        pos.x += isOddLayer ? (gridSize.x * 0.45) : 0;
        pos.y += isOddLayer ? (gridSize.y / 2) : 0;

        pos.x *= sqrt(3.0) / 2;
        pos.z *= sqrt(3.0) / 2;
        pos += Center;
        ResultPoints[index].Position = pos;
        ResultPoints[index].Rotation = qFromAngleAxis((OrientationAngle)*PI / 180, normalize(OrientationAxis));
    }

    // Diagonal
    if (Tiling < 3.5)
    {
        bool isOddRow = cell.x % 2 > 0;
        pos.x /= SizeMode > 0.5 ? 1 : 2;
        pos += Center;

        if (isOddRow)
        {
            // SizeMode > 0.5 ? (cell / clampedCount) - (Pivot + 0.5)
            //                : cell - clampedCount *(Pivot + 0.5);

            pos += SizeMode > 0.5 ? float3(0, 0.5, 0.5) * zeroAdjustedSize / clampedCount
                                  : float3(0, 0.5, 0.5) * zeroAdjustedSize;
        }

        ResultPoints[index].Position = pos;
        ResultPoints[index].Rotation = qFromAngleAxis(OrientationAngle * PI / 180, normalize(OrientationAxis));
        return;
    }
}
