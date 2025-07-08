#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

/*{ADDITIONAL_INCLUDES}*/

cbuffer Params : register(b0)
{
    float MinDistance;
    float StepDistanceFactor;
    float NormalSamplingDistance;
    float MaxDistance;
}

cbuffer Params : register(b1)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer Params : register(b2)
{
    int MaxSteps;
    int MaxReflections;
    int PointMode;
    int WriteDistanceTo;

    int WriteStepCountTo;
    int PointCountPerLine;
    int PointCountPerLineReflections;
}

StructuredBuffer<Point> SourcePoints : t0;
RWStructuredBuffer<Point> ResultPoints : u0;

sampler ClampedSampler :s0;

//=== Additional Resources ==========================================
/*{RESOURCES(t1)}*/

//=== Global functions ==============================================
/*{GLOBALS}*/

//=== Field functions ===============================================
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------------------------------
float4 GetField(float4 p)
{
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

inline float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}

float3 GetNormal(float3 p)
{
    return normalize(
        GetDistance(p + float3(NormalSamplingDistance, -NormalSamplingDistance, -NormalSamplingDistance)) * float3(1, -1, -1) +
        GetDistance(p + float3(-NormalSamplingDistance, NormalSamplingDistance, -NormalSamplingDistance)) * float3(-1, 1, -1) +
        GetDistance(p + float3(-NormalSamplingDistance, -NormalSamplingDistance, NormalSamplingDistance)) * float3(-1, -1, 1) +
        GetDistance(p + float3(NormalSamplingDistance, NormalSamplingDistance, NormalSamplingDistance)) * float3(1, 1, 1));
}
//===================================================================

static const float NoisePhase = 0;

#define ModeOverride 0
#define ModeAdd 1
#define ModeSub 2
#define ModeMultiply 3
#define ModeInvert 4

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    int sourceIndex = i.x;
    if (sourceIndex >= numStructs)
        return;

    Point p = SourcePoints[sourceIndex];

    // if (isnan(p.Scale.x))
    // {
    //     // Todo Write this properly...
    //     // ResultPoints[sourceIndex] = p;
    //     return;
    // }

    float sumD = 0;
    float3 n;

    n = qRotateVec3(float3(0, 0, -1), p.Rotation);

    if (PointMode == 0)
    {
        int outBaseIndex = sourceIndex * PointCountPerLineReflections;
        ResultPoints[outBaseIndex] = p;
        for (int reflectionIndex = 0; reflectionIndex < MaxReflections; reflectionIndex++)
        {
            for (int stepIndex = 0; stepIndex < MaxSteps; stepIndex++)
            {
                float d = GetDistance(p.Position);
                sumD += d;

                if (WriteDistanceTo == 1)
                {
                    p.FX1 = sumD;
                }
                else if (WriteDistanceTo == 2)
                {
                    p.FX2 = sumD;
                }

                if (WriteStepCountTo == 1)
                {
                    p.FX1 = reflectionIndex;
                }
                else if (WriteStepCountTo == 2)
                {
                    p.FX2 = reflectionIndex;
                }

                if (abs(d) < MinDistance)
                {
                    ResultPoints[outBaseIndex + reflectionIndex + 1] = p;
                    float3 surfaceNormal = -GetNormal(p.Position);
                    n = reflect(n, surfaceNormal);
                    p.Position -= n * MinDistance * 100;
                    break;
                }

                if (sumD > MaxDistance)
                {
                    // p.Position += MaxDistance * n + float3(0, 1, 0);
                    ResultPoints[outBaseIndex + reflectionIndex + 1] = p;
                    break;
                }

                p.Position -= n * d * StepDistanceFactor;
            }
        }

        // ResultPoints[outBaseIndex + reflectionIndex + 1] = p;
        p.Scale = float3(NAN, NAN, NAN);
        for (; reflectionIndex <= MaxReflections; reflectionIndex++)
        {
            ResultPoints[outBaseIndex + reflectionIndex] = p;
        }
        return;
    }
    else
    {

        int outBaseIndex = sourceIndex * PointCountPerLineReflections;
        for (int reflectionIndex = 0; reflectionIndex <= MaxReflections; reflectionIndex++)
        {
            for (int stepIndex = 0; stepIndex < MaxSteps; stepIndex++)
            {
                float d = GetDistance(p.Position);

                if (WriteDistanceTo == 1)
                {
                    p.FX1 = d;
                }
                else if (WriteDistanceTo == 2)
                {
                    p.FX2 = d;
                }

                if (WriteStepCountTo == 1)
                {
                    p.FX1 = stepIndex;
                }
                else if (WriteStepCountTo == 2)
                {
                    p.FX2 = stepIndex;
                }

                ResultPoints[outBaseIndex + reflectionIndex * PointCountPerLine + stepIndex] = p;

                sumD += d;

                if (abs(d) < MinDistance || sumD > MaxDistance)
                {
                    float3 surfaceNormal = -GetNormal(p.Position);
                    n = reflect(n, surfaceNormal);
                    p.Position -= n * MinDistance * 10;
                    break;
                }

                p.Position -= n * d * StepDistanceFactor;
            }

            p.Scale = float3(NAN, NAN, NAN);

            // including MaxSteps for seperator
            for (; stepIndex <= MaxSteps; stepIndex++)
            {
                ResultPoints[outBaseIndex + reflectionIndex * PointCountPerLine + stepIndex] = p;
            }
            p.Scale = SourcePoints[i.x].Scale;
        }
    }
}
