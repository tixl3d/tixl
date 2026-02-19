#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

static const float3 Corners[] =
    {
        float3(0, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(0, 1, 0),
        float3(0, -1, 0),
};

cbuffer Params : register(b0)
{
    float4 Color;

    float Size;
    float ShrinkWithDistance;
    float OffsetU;
    float UvScale;

    float FadeTooLong;
    float PointsPerShape;    // 0 = use all points for one shape, >0 = specific number of points per shape
    float ThicknessDirection; // New parameter: -1 = left, 0 = center, 1 = right
};

cbuffer Params : register(b1)
{
    int UvMode;
    int WidthFX;
    int UseWForU;
    int UseWForWidth;
};

cbuffer Transforms : register(b2)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

cbuffer FogParams : register(b3)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 texCoord : TEXCOORD;
    float fog : FOG;
};

sampler texSampler : register(s0);

StructuredBuffer<Point> Points : t0;
Texture2D<float4> texture2 : register(t1);

// Helper function to get point with wrapping for closed shapes
uint GetWrappedIndex(uint index, uint totalPoints, uint pointsPerShape)
{
    if (pointsPerShape > 0)
    {
        uint shapeIndex = index % pointsPerShape;
        uint shapeStart = (index / pointsPerShape) * pointsPerShape;
        return shapeStart + shapeIndex;
    }
    else
    {
        return index % totalPoints;
    }
}

psInput vsMain(uint id : SV_VertexID)
{
    psInput output;
    float discardFactor = 1;

    uint SegmentCount, Stride;
    Points.GetDimensions(SegmentCount, Stride);

    // Calculate actual number of segments we'll draw
    uint pointsPerShape = (uint)PointsPerShape;
    uint actualSegmentCount = SegmentCount;
    
    if (pointsPerShape > 0)
    {
        // Multiple shapes - calculate how many complete shapes we have
        uint numShapes = SegmentCount / pointsPerShape;
        actualSegmentCount = numShapes * pointsPerShape;
    }

    float4 aspect = float4(CameraToClipSpace[1][1] / CameraToClipSpace[0][0], 1, 1, 1);
    int quadIndex = id % 6;
    uint segmentId = id / 6;
    
    // Skip if we're beyond the actual segments
    if (segmentId >= actualSegmentCount)
    {
        output.position = float4(0, 0, 0, 0);
        output.color = float4(0, 0, 0, 0);
        output.texCoord = float2(0, 0);
        output.fog = 0;
        return output;
    }

    float3 cornerFactors = Corners[quadIndex];

    // Calculate which shape we're in and our position within that shape
    uint currentShapeIndex = 0;
    uint segmentInShape = segmentId;
    
    if (pointsPerShape > 0)
    {
        currentShapeIndex = segmentId / pointsPerShape;
        segmentInShape = segmentId % pointsPerShape;
    }
    
    uint shapeStartPoint = currentShapeIndex * pointsPerShape;
    uint shapePointCount = pointsPerShape > 0 ? pointsPerShape : actualSegmentCount;

    // Get current segment points with proper wrapping WITHIN the current shape
    uint currentIndex = shapeStartPoint + (segmentInShape % shapePointCount);
    uint nextIndex = shapeStartPoint + ((segmentInShape + 1) % shapePointCount);
    
    Point pointA = Points[currentIndex];
    Point pointB = Points[nextIndex];

    // Get previous point for normal calculation (wrapped within shape)
    uint prevSegmentInShape = (segmentInShape > 0) ? segmentInShape - 1 : shapePointCount - 1;
    uint prevIndex = shapeStartPoint + prevSegmentInShape;
    Point pointAA = Points[prevIndex];

    // Get next next point for normal calculation (wrapped within shape)
    uint nextNextSegmentInShape = (segmentInShape + 2) % shapePointCount;
    uint nextNextIndex = shapeStartPoint + nextNextSegmentInShape;
    Point pointBB = Points[nextNextIndex];

    float3 pointAPos = pointA.Position;
    float3 pointBPos = pointB.Position;

    float len = length(pointAPos - pointBPos);
    float fade = smoothstep(2 * FadeTooLong, FadeTooLong, len);
    if (fade < 0.001)
        discardFactor = 0;

    float f = cornerFactors.x;
    float3 posInObject = f < 0.5 ? pointAPos : pointBPos;

    // Transform all points to screen space for consistent normal calculations
    float4 aaInScreen = mul(float4(pointAA.Position, 1), ObjectToClipSpace) * aspect;
    aaInScreen /= aaInScreen.w;
    
    float4 aInScreen = mul(float4(pointA.Position, 1), ObjectToClipSpace) * aspect;
    if (aInScreen.z < -0)
        discardFactor = 0;
    aInScreen /= aInScreen.w;

    float4 bInScreen = mul(float4(pointB.Position, 1), ObjectToClipSpace) * aspect;
    if (bInScreen.z < -0)
        discardFactor = 0;
    bInScreen /= bInScreen.w;
    
    float4 bbInScreen = mul(float4(pointBB.Position, 1), ObjectToClipSpace) * aspect;
    bbInScreen /= bbInScreen.w;

    // Calculate directions with proper wrapping
    float3 directionA = (aaInScreen - aInScreen).xyz;
    float3 direction = (aInScreen - bInScreen).xyz;
    float3 directionB = (bInScreen - bbInScreen).xyz;

    // Ensure directions are valid (not zero length)
    if (length(directionA) < 0.0001) directionA = direction;
    if (length(directionB) < 0.0001) directionB = direction;

    float3 normal = normalize(cross(direction, float3(0, 0, 1)));
    float3 normalA = normalize(cross(directionA, float3(0, 0, 1)));
    float3 normalB = normalize(cross(directionB, float3(0, 0, 1)));

    // Handle edge cases for normals
    if (isnan(pointAA.Scale.x) || isinf(pointAA.Scale.x) || any(isnan(normalA)))
    {
        normalA = normal;
    }
    if (isnan(pointBB.Scale.x) || isinf(pointBB.Scale.x) || any(isnan(normalB)))
    {
        normalB = normal;
    }

    // Smoothly blend normals at junctions
    float3 neighborNormal = lerp(normalA, normalB, f);
    float3 meterNormal = (normal + neighborNormal) * 0.1;
    
    // Ensure meterNormal is valid
    if (any(isnan(meterNormal)) )
    {
        meterNormal = normal;
    }

    float4 pos = lerp(aInScreen, bInScreen, f);

    float4 posInCamSpace = mul(float4(posInObject, 1), ObjectToCamera);
    posInCamSpace.xyz /= posInCamSpace.w;
    posInCamSpace.w = 1;

    float pFx1 = lerp(pointA.FX1, pointB.FX1, f);
    float pFx2 = lerp(pointA.FX2, pointB.FX2, f);

    float texFxFactor = WidthFX == 0 ? 1 : ((WidthFX == 1) ? pFx1 : pFx2);

    // Calculate UV coordinates
    float u = f;
    
    switch (UvMode)
    {
    case 0:
        u = (segmentInShape + f) / shapePointCount;
        break;
    case 1:
        u = pFx1;
        break;
    case 2:
        u = pFx2;
        break;
    }

    output.texCoord = float2(u * UvScale + OffsetU, cornerFactors.y / 2 + 0.5);

    float widthAtPoint = lerp(pointA.Scale.x, pointB.Scale.x, f);
    float widthFxFactor = WidthFX == 0 ? 1 : ((WidthFX == 1) ? pFx1 : pFx2);
    float thickness = Size * discardFactor * lerp(1, 1 / (posInCamSpace.z), ShrinkWithDistance) * widthFxFactor;
    thickness *= widthAtPoint;

    // Improved miter calculation with safety checks
    float miter = dot(-meterNormal, normal);
    miter = clamp(miter, -1.0, -0.01);
    
    // Apply thickness direction control
    float directionOffset = ThicknessDirection ; // Scale to get appropriate offset
    float cornerOffset = cornerFactors.y + directionOffset;
    
    pos += cornerOffset * 0.1 * thickness * float4(meterNormal, 0) / miter;

    output.position = pos / aspect;

    output.fog = pow(saturate(-posInCamSpace.z / FogDistance), FogBias);

    output.color = Color * lerp(pointA.Color, pointB.Color, cornerFactors.x);
    output.color.a *= fade;

    return output;
}

float4 psMain(psInput input) : SV_TARGET
{
    float4 imgColor = texture2.Sample(texSampler, input.texCoord);
    float4 col = input.color * imgColor;
    col.rgb = lerp(col.rgb, FogColor.rgb, input.fog);
    return clamp(col, float4(0, 0, 0, 0), float4(1000, 1000, 1000, 1));
}