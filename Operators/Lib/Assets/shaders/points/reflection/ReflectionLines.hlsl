#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
#include "shared/pbr.hlsl"

cbuffer Params : register(b0)
{
    float StepCount;
    float DecayW;
    float Extend;
    float SpreadColor;
    float SpreadColorShift;
}

StructuredBuffer<Point> SourcePoints : t0;
StructuredBuffer<PbrVertex> Vertices : t1;
StructuredBuffer<int3> Indices : t2;

RWStructuredBuffer<Point> ResultPoints : u0;

// Casual Moller-Trumbore GPU Ray-Triangle Intersection Routine
// bool intersectMT(
//     float3 orig, float3 dir,
//     float3 v0, float3 v1, float3 v2,
//     out float3 baryzentricUVW,
//     out float t)
// {
//     float3 e1 = v1 -  v0;
//     float3 e2 = v2 -  v0;
//     float3 normal = normalize(cross(e1, e2));
//     float b = dot(normal, dir);
//     float3 w0 = orig -  v0;
//     float a = -dot(normal, w0);
//     t = a / b;
//     float3 p = orig + t * dir;
//     float uu, vv, uv, wu, wv, inverseD;
//     //float2 baryzentricUV = 0;

//     uu = dot(e1, e1);
//     uv = dot(e1, e2);
//     vv = dot(e2, e2);
//     float3 w = p -  v0;
//     wu = dot(w, e1);
//     wv = dot(w, e2);
//     inverseD = uv * uv -  uu * vv;
//     inverseD = 1.0f / inverseD;
//     float u = (uv * wv -  vv * wu) * inverseD;

//     if (u < 0.0f || u > 1.0f)
//         //return -1.0f;
//         return false;

//     float v = (uv * wu -  uu * wv) * inverseD;
//     if (v < 0.0f || (u + v) > 1.0f)
//         return false;
//         //return -1.0f;

//     baryzentricUVW = float3(u,v, 1-u-v).xzz;
//     return true;
// }

static const float kEpsilon = 0.0001;

// From https://graphicscodex.courses.nvidia.com/app.html?page=_rn_rayCst#section4.2
bool intersect(
    float3 orig, float3 dir,
    float3 v0, float3 v1, float3 v2,
    out float3 b,
    out float t)
{
    // Edge vectors
    float3 e_1 = v1 - v0;
    float3 e_2 = v2 - v0;

    // Face normal
    float3 n = normalize(cross(e_1, e_2));

    float3 q = cross(dir, e_2);
    float a = dot(e_1, q);

    // Backfacing / nearly parallel, or close to the limit of precision?
    if ((dot(n, dir) >= 0) || (abs(a) <= kEpsilon))
        return false;

    float3 s = (orig - v0) / a;
    float3 r = cross(s, e_1);

    b[0] = dot(s, q);
    b[1] = dot(r, dir);
    b[2] = 1.0f - b[0] - b[1];

    t = dot(e_2, r);

    // Intersected inside triangle?
    return ((b[0] >= 0) && (b[1] >= 0) && (b[2] >= 0) && (t >= 0));
}

static const float NaN = sqrt(-1);

static const int RAY_THREAD_COUNT = 8;
static const int FACE_THREAD_COUNT = 512 / RAY_THREAD_COUNT;

groupshared int BestHitIntDistances[RAY_THREAD_COUNT];
groupshared int BestHitIndices[RAY_THREAD_COUNT];
groupshared float3 BestHitPositions[RAY_THREAD_COUNT];
groupshared float2 BestHitBaryUV[RAY_THREAD_COUNT];

[numthreads(RAY_THREAD_COUNT, FACE_THREAD_COUNT, 1)] void main(uint3 i : SV_DispatchThreadID, uint3 GTid : SV_GroupThreadID)
{
    uint rayCount, stride;
    SourcePoints.GetDimensions(rayCount, stride);

    uint faceCount;
    Indices.GetDimensions(faceCount, stride);

    uint rayId = i.x;
    uint rayThreadId = GTid.x;
    uint faceThreadId = i.y;

    uint stepCount = (uint)StepCount; // including separator
    uint rayGroupStartIndex = i.x * stepCount;

    Point p = SourcePoints[i.x];

    // Write ray start and seperator
    ResultPoints[rayGroupStartIndex + 0] = p;
    ResultPoints[rayGroupStartIndex + stepCount - 1].Scale = NaN;

    float3 rayOrigin = p.Position;
    float3 rayDirection = qRotateVec3(float3(0, 0, 1), p.Rotation);
    float fx1 = p.FX1;

    int _bestHitIndex = -1;
    float3 _bestHitPosition = rayOrigin + rayDirection * Extend;
    float2 _bestHitBaryUv = 0;
    for (uint stepIndex = 1; stepIndex < (stepCount - 1); stepIndex++)
    {
        if (faceThreadId == 0)
        {
            if (rayId < rayCount)
            {
                BestHitIntDistances[rayThreadId] = 99999999;
                BestHitIndices[rayThreadId] = -1;
                BestHitPositions[rayThreadId] = rayOrigin + rayDirection * Extend;

                _bestHitIndex = -1;
                _bestHitPosition = rayOrigin + rayDirection * Extend;
                _bestHitBaryUv = 0;
            }
        }
        GroupMemoryBarrierWithGroupSync();

        int faceGroupCount = faceCount / FACE_THREAD_COUNT;
        for (uint faceGroupStartIndex = 0; faceGroupStartIndex < faceCount; faceGroupStartIndex += FACE_THREAD_COUNT)
        {
            uint faceId = faceThreadId + faceGroupStartIndex;
            if (faceId < faceCount)
            {
                int3 f = Indices[faceId];
                float3 bary;
                float t;

                if (intersect(
                        rayOrigin,
                        rayDirection,
                        Vertices[f[0]].Position,
                        Vertices[f[1]].Position,
                        Vertices[f[2]].Position,
                        bary,
                        t))
                {
                    float org;
                    int intt = t * 1000;
                    InterlockedMin(BestHitIntDistances[rayThreadId], intt, org);
                    if (org > intt)
                    {
                        BestHitIndices[rayThreadId] = faceId;
                        BestHitBaryUV[rayThreadId] = bary.zx;
                        BestHitPositions[rayThreadId] = rayOrigin + rayDirection * t;

                        _bestHitIndex = faceId;
                        _bestHitBaryUv = bary.zx;
                        _bestHitPosition = rayOrigin + rayDirection * t;
                    }
                }
            }
            // GroupMemoryBarrierWithGroupSync();
        }
        GroupMemoryBarrierWithGroupSync();

        _bestHitIndex = BestHitIndices[rayThreadId];

        if (_bestHitIndex < 0)
        {
            rayOrigin += rayDirection * Extend;
            ResultPoints[rayGroupStartIndex + stepIndex].Rotation = p.Rotation;
            ResultPoints[rayGroupStartIndex + stepIndex].Position = rayOrigin;

            ResultPoints[rayGroupStartIndex + stepIndex].Scale = p.Scale;
            ResultPoints[rayGroupStartIndex + stepIndex].FX2 = p.FX2;
            ResultPoints[rayGroupStartIndex + stepIndex].Color = p.Color;

            ResultPoints[rayGroupStartIndex + stepIndex].Position = rayOrigin;

            ResultPoints[rayGroupStartIndex + stepIndex].FX1 = fx1;
        }
        else
        {
            _bestHitPosition = BestHitPositions[rayThreadId];
            rayOrigin = _bestHitPosition;
            ResultPoints[rayGroupStartIndex + stepIndex].Position = rayOrigin;
            ResultPoints[rayGroupStartIndex + stepIndex].Rotation = p.Rotation;
            ResultPoints[rayGroupStartIndex + stepIndex].FX1 = fx1;

            ResultPoints[rayGroupStartIndex + stepIndex].Scale = p.Scale;
            ResultPoints[rayGroupStartIndex + stepIndex].FX2 = p.FX2;
            ResultPoints[rayGroupStartIndex + stepIndex].Color = p.Color;

            float3 n0 = normalize(Vertices[Indices[_bestHitIndex][0]].Normal);
            float3 n1 = normalize(Vertices[Indices[_bestHitIndex][1]].Normal);
            float3 n2 = normalize(Vertices[Indices[_bestHitIndex][2]].Normal);

            _bestHitBaryUv = BestHitBaryUV[rayThreadId]; // <----
            float u = _bestHitBaryUv.x;
            float v = _bestHitBaryUv.y;

            float3 n = normalize(u * n0 + v * n1 + (1 - u - v) * n2);
            fx1 *= DecayW;

            // rayDirection= reflect( rayDirection, n * 1);
            float3 I = rayDirection;
            // float3 R = I - 2* ( dot(n,I)* n);

            float phi = acos(dot(n, I));
            phi += (fx1 - SpreadColorShift) * SpreadColor;
            float3 R = I - 2 * cos(phi) * n;
            rayDirection = R;
            // rayDirection= reflect( rayDirection, n);
        }

        GroupMemoryBarrierWithGroupSync();
        ResultPoints[rayGroupStartIndex + stepCount - 1].Scale = NaN;
    }
}
