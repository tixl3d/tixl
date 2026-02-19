#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"
// #include "shared/hash-functions.hlsl"
// #include "points/spatial-hash-map/hash-map-settings.hlsl" 

cbuffer Params : register(b0)
{
    float CurrentStep_;           // 0
    float StepCount;             // 1
    float LinesPerSteps;         // 2
    float CellSize;  // 3
    float Time;                  // 4
    float ScatterLookUp;         // 5
    float TestIndex;             // 6
}

cbuffer Params : register(b1)
{
    int CurrentStep;           // 0
}

#include "points/spatial-hash-map/spatial-hash-map-lookup.hlsl"
StructuredBuffer<Point> points :register(t5);

RWStructuredBuffer<uint2> pointIndexPairs :register(u0);

#define THREADS_PER_GROUP 512


[numthreads( THREADS_PER_GROUP, 1, 1 )]
void ClearPoints(uint3 DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex)
{
    pointIndexPairs[DTid.x] = int2( 0, 0);
}


[numthreads( THREADS_PER_GROUP, 1, 1 )]
void ConnectPoints(uint3 DTid : SV_DispatchThreadID, uint GI: SV_GroupIndex)
{
    uint pointCount, __;
    points.GetDimensions(pointCount, __);

    uint pairCount;
    pointIndexPairs.GetDimensions(pairCount, __);

    // if(TestIndex >= 0) 
    // {
    //     int testPointIndex = (int)TestIndex;
    //     float3 position = points[testPointIndex].position;

    //     uint rangeStartIndex, rangeEndIndex;
    //     if(GridFind(position, rangeStartIndex, rangeEndIndex)) 
    //     {
    //         int x2=0;
    //         for(uint i=rangeStartIndex; i < rangeEndIndex; ++i) 
    //         {
    //             uint otherIndex = CellPointIndices[i];
    //             pointIndexPairs[DTid.x + x2] = uint2( testPointIndex, otherIndex);
    //             x2++;
    //         }
    //     } 
    //     return;
    // }


    uint indexWithingStep = DTid.x;
    if(indexWithingStep >= LinesPerSteps)
        return;

    uint stepIndex = CurrentStep % StepCount;
    uint pairIndex = stepIndex * (int)(LinesPerSteps + 0.5) + indexWithingStep;

    uint shuffle =  hash11(CurrentStep * 0.123 + indexWithingStep * 0.121 ) * ScatterLookUp * pointCount;
    uint pointIndex = (CurrentStep + (int)(shuffle + indexWithingStep) ) % pointCount;

    if(pairIndex >= pairCount)
        return;
        

    float3 position = points[pointIndex].Position;
    float3 jitter =  (hash33u( uint3(DTid.x, DTid.x + 134775813U, DTid.x + 1664525U) + position * 100 + Time % 123.1 ) -0.5f)  * CellSize ;
    position+= jitter;

    uint rangeStartIndex, rangeEndIndex;
    if(GridFind(position, rangeStartIndex, rangeEndIndex)) 
    {
        const uint particleCount = rangeEndIndex - rangeStartIndex;
        rangeEndIndex = min(rangeStartIndex + 64 , rangeEndIndex);

        float minDistance = 100000;
        uint closestIndex = -1;

        for(uint i=rangeStartIndex ; i < rangeEndIndex; ++i) 
        {
            uint otherIndex = CellPointIndices[i];
            
            if( otherIndex <= DTid.x)
                continue;

            float3 otherPos = points[otherIndex].Position;

            float3 direction = position - otherPos;
            float distance = length(position - otherPos);
            
            if(distance < minDistance)
            {
                minDistance = distance;
                closestIndex = otherIndex;
                //break;
            }
        }

        if(closestIndex != -1 && minDistance < CellSize)
        {
            pointIndexPairs[pairIndex] = int2( closestIndex, pointIndex);
        }
        else {
            pointIndexPairs[pairIndex] = int2( 10,0);
        }
    }
    
}