#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float InsertCount;
    float CloseShape;  // 0 = open, 1 = closed
}

StructuredBuffer<Point> SourcePoints : t0;         // input
RWStructuredBuffer<Point> ResultPoints : u0;    // output

// Helper function to check if a point is a separator
bool IsSeparator(Point p)
{
    // Check if Scale contains NaN values (separator marker)
    return isnan(p.Scale.x) && isnan(p.Scale.y) && isnan(p.Scale.z);
}

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount, stride);

    if(i.x >= pointCount) {
        return;
    }

    uint sourceCount, stride2;
    SourcePoints.GetDimensions(sourceCount, stride);

    // ORIGINAL CODE PATH - when not closing shape
    if (CloseShape < 0.5) {
        int subdiv = (int)(InsertCount + 1);

        int segmentIndex = i.x / (subdiv);
        int segmentPointIndex = (i.x % (subdiv));

        float f = (float)segmentPointIndex / subdiv;

        if(f <= 0.001)  {
            ResultPoints[i.x] = SourcePoints[segmentIndex];
        }
        else {
            ResultPoints[i.x].Position = lerp( SourcePoints[segmentIndex].Position,  SourcePoints[segmentIndex + 1].Position, f);
            ResultPoints[i.x].FX1 = lerp( SourcePoints[segmentIndex].FX1,  SourcePoints[segmentIndex + 1].FX1, f);
            ResultPoints[i.x].Rotation = qSlerp( SourcePoints[segmentIndex].Rotation,  SourcePoints[segmentIndex + 1].Rotation, f);
            ResultPoints[i.x].Color = lerp( SourcePoints[segmentIndex].Color,  SourcePoints[segmentIndex + 1].Color, f);
            ResultPoints[i.x].FX2 = lerp( SourcePoints[segmentIndex].FX2,  SourcePoints[segmentIndex + 1].FX2, f);
            ResultPoints[i.x].Scale = lerp( SourcePoints[segmentIndex].Scale,  SourcePoints[segmentIndex + 1].Scale, f);
        }
        return;
    }

    // CLOSED SHAPE CODE PATH - only executed when CloseShape is enabled
    if (sourceCount <= 1) {
        // Not enough points to process
        if (i.x < sourceCount) {
            ResultPoints[i.x] = SourcePoints[i.x];
        }
        return;
    }

    // Count actual segments for closed shape
    uint actualSegmentCount = 0;
    
    // First, count all regular segments between consecutive points
    for (uint j = 0; j < sourceCount - 1; j++) {
        if (!IsSeparator(SourcePoints[j]) && !IsSeparator(SourcePoints[j + 1])) {
            actualSegmentCount++;
        }
    }
    
    // For closed shape, add the segment from last to first point
    // Find first and last non-separator points
    uint firstValidIndex = 0;
    uint lastValidIndex = sourceCount - 1;
    
    while (firstValidIndex < sourceCount && IsSeparator(SourcePoints[firstValidIndex])) {
        firstValidIndex++;
    }
    while (lastValidIndex > 0 && IsSeparator(SourcePoints[lastValidIndex])) {
        lastValidIndex--;
    }
    
    // Only add closing segment if we have valid first and last points that are different
    if (firstValidIndex < lastValidIndex && 
        !IsSeparator(SourcePoints[firstValidIndex]) && 
        !IsSeparator(SourcePoints[lastValidIndex])) {
        actualSegmentCount++;
    }

    int subdiv = (int)(InsertCount + 1);
    uint totalResultPoints = actualSegmentCount * subdiv;
    
    if (i.x >= totalResultPoints) {
        return;
    }

    int segmentIndex = i.x / subdiv;
    int segmentPointIndex = i.x % subdiv;
    float f = (float)segmentPointIndex / subdiv;

    // Find the actual segment corresponding to segmentIndex
    uint currentSegment = 0;
    uint startIndex = 0;
    uint endIndex = 0;
    bool foundSegment = false;
    
    // First check regular segments between consecutive points
    for (uint j = 0; j < sourceCount - 1 && !foundSegment; j++) {
        if (!IsSeparator(SourcePoints[j]) && !IsSeparator(SourcePoints[j + 1])) {
            if (currentSegment == segmentIndex) {
                startIndex = j;
                endIndex = j + 1;
                foundSegment = true;
            }
            currentSegment++;
        }
    }
    
    // If not found in regular segments, check the closing segment
    if (!foundSegment && segmentIndex == currentSegment) {
        startIndex = lastValidIndex;
        endIndex = firstValidIndex;
        foundSegment = true;
    }

    if (!foundSegment) {
        // Fallback
        if (i.x < sourceCount) {
            ResultPoints[i.x] = SourcePoints[i.x];
        }
        return;
    }

    if (f <= 0.001) {
        ResultPoints[i.x] = SourcePoints[startIndex];
    } else {
        ResultPoints[i.x].Position = lerp(SourcePoints[startIndex].Position, SourcePoints[endIndex].Position, f);
        ResultPoints[i.x].FX1 = lerp(SourcePoints[startIndex].FX1, SourcePoints[endIndex].FX1, f);
        ResultPoints[i.x].Rotation = qSlerp(SourcePoints[startIndex].Rotation, SourcePoints[endIndex].Rotation, f);
        ResultPoints[i.x].Color = lerp(SourcePoints[startIndex].Color, SourcePoints[endIndex].Color, f);
        ResultPoints[i.x].FX2 = lerp(SourcePoints[startIndex].FX2, SourcePoints[endIndex].FX2, f);
        ResultPoints[i.x].Scale = lerp(SourcePoints[startIndex].Scale, SourcePoints[endIndex].Scale, f);
    }
}