#include "shared/hash-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float BlendFactor;
    float BlendMode;
    float PairingMode;
    float Width;
    float Scatter;
}

StructuredBuffer<Point> PointsA : register(t0);        // input
StructuredBuffer<Point> PointsB : register(t1);        // input
RWStructuredBuffer<Point> ResultPoints : register(u0); // output

float SmootherStep(float x)
{
    x = saturate(x);
    return x * x * x * (x * (x * 6.0 - 15.0) + 10.0);
}

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint resultCount, countA, countB, stride;
    ResultPoints.GetDimensions(resultCount, stride);
    PointsA.GetDimensions(countA, stride);
    PointsB.GetDimensions(countB, stride);

    if (i.x > resultCount)
        return;

    uint aIndex = i.x;
    uint bIndex = i.x;

    float t = i.x / ((float)resultCount-1);

    if (PairingMode > 0.5 && countA != countB)
    {
        uint maxCount = max(countA, countB);

        // This calculates the first index i.x that would map to the current pair.
        // It's essentially the inverse of (count * i.x) / resultCount
        uint firstIxA = (uint)((aIndex * (float)resultCount) / countA);
        uint firstIxB = (uint)((bIndex * (float)resultCount) / countB);

        uint firstIx = max(firstIxA, firstIxB);

        if (i.x > firstIx)
            return;
    }

    Point A = PointsA[aIndex];
    Point B = PointsB[bIndex];

    float f = 0;

    if (BlendMode < 0.5)
    {
        f = BlendFactor;
    }
    else if (BlendMode < 1.5)
    {
        f = A.FX1;
    }
    else if (BlendMode < 2.5)
    {
        f = (1 - B.FX1);
    }

    // Ranged
    // see https://www.desmos.com/calculator/zxs1fy06uh
    else if (BlendMode < 3.5)
    {
        f = 1 - saturate((t - BlendFactor) / Width - BlendFactor + 1);
    }
    else
    {
        float b = BlendFactor % 2;
        if (b > 1)
        {
            b = 2 - b;
            t = 1 - t;
        }
        //f = 1 - smoothstep(0, 1, saturate((t - b) / Width - b + 1));
        f = 1 - SmootherStep(saturate((t - b) / Width - b + 1));
    }

    float fallOffFromCenter = smoothstep(0, 1, 1 - abs(f - 0.5) * 2);
    //float fallOffFromCenter = SmootherStep(1 - abs(f - 0.5) * 2);
    f += (hash11(t) - 0.5) * Scatter * fallOffFromCenter;

    bool noBlend = IsSeparator(A) || IsSeparator(B);


    f = noBlend ? (f< 0.5 ? 0 : 1) : f;

    ResultPoints[i.x].Scale = noBlend ? (f<0.1 ? A.Scale: B.Scale) : (lerp(A.Scale, B.Scale, f));
    ResultPoints[i.x].Rotation =   qSlerp(A.Rotation, B.Rotation, f);
    ResultPoints[i.x].FX1 = lerp(A.FX1, B.FX1, f);
    ResultPoints[i.x].FX2 = lerp(A.FX2, B.FX2, f);
    ResultPoints[i.x].Color = lerp(A.Color, B.Color, f);
    ResultPoints[i.x].Position = lerp(A.Position, B.Position, f);
    // ResultPoints[i.x].FX1 = f; why this overwriting ?
}
