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
    int SourcePointCount;
    int MaxSteps;
    int MaxReflections;
    int WriteDistanceTo;

    int WriteStepCountTo;
}

StructuredBuffer<Point> SourcePoints : register(t0);
RWStructuredBuffer<Point> ResultPoints : register(u0);

sampler ClampedSampler : register(s0);

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


#define MODE_MOVEPOINTS 0
#define MODE_REFLECTION_LINES 1
#define MODE_RAYMARCH_STEPS 2

[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    int sourceIndex = i.x;
    if (sourceIndex >= SourcePointCount)
        return;

    Point p = SourcePoints[sourceIndex];

    float sumD = 0;
    float3 n;

    n = qRotateVec3(float3(0, 0, -1), p.Rotation);


    int pointsPerLine = (MaxReflections + 3); // Source + (Reflection + 1)  + Sep

    int outIndex = sourceIndex * pointsPerLine;
    int maxIndexForLine = outIndex + pointsPerLine -1;
    

    // Keep source point
        if (WriteDistanceTo == 1)
        {
            p.FX1 = 0;
        }
        else if (WriteDistanceTo == 2)
        {
            p.FX2 = 0;
        }

        if (WriteStepCountTo == 1)
        {
            p.FX1 = 0;
        }
        else if (WriteStepCountTo == 2)
        {
            p.FX2 = 0;
        }

    ResultPoints[outIndex++] = p;

    // Raymarch and keep reflections

    for (int reflectionIndex = 0; reflectionIndex <= MaxReflections; reflectionIndex++)
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
                // Get surface normal and move slightly back
                float3 surfaceNormal = -GetNormal(p.Position);
                n = reflect(n, surfaceNormal);
                ResultPoints[outIndex++] = p;   // Write surface step

                p.Position -= n *  MinDistance * 1;

                break;
            }

            if (sumD > MaxDistance)
            {
                ResultPoints[outIndex++] = p;
                reflectionIndex = MaxReflections+1; // Cancel 
                break;
            }

            p.Position -= n * d * StepDistanceFactor;

        }

        if(stepIndex == MaxSteps)
        {
            p.FX2 = outIndex;

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
            ResultPoints[outIndex++] = p;   // Write surface step
        }

    }
    
    // Fill up rest with separators
    p.Scale = float3(NAN, NAN, NAN);
    for (; outIndex <= maxIndexForLine; outIndex++)
    {
        ResultPoints[outIndex] = p;
    }
    return;
}
