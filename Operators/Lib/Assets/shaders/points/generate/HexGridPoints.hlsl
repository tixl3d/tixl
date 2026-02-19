#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float3 Count;
    float __padding1;

    float3 Size;
    float __padding3;

    float3 Center;
    float W;    

    float3 OrientationAxis;
    float OrientationAngle;

    float3 Pivot;
    float SizeMode;

    float testParam;
}


static const float colBOffset = 0;
static const float2 HexOffsetsAndAngles[] = 
{
  float2( -1,  90),  float2(   0,   30),  // 0
  float2(  0, 150),  float2(  -1,  -30),  // 1
  float2( -1,-150),  float2(   0,  -90),  // 2
  float2(  0,  30),  float2(  -1,   90),  // 3
  float2( -1, -30),  float2(   0,  150),  // 4
  float2(  0, -90),  float2(  -1, -150),  // 5
};



RWStructuredBuffer<LegacyPoint> ResultPoints : u0;    // output
static const float ToRad = 3.141578 / 180;

[numthreads(64,4,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint index = i.x; 

    // Note: We assume that 0 count have been clamped earlier
    uint3 c = (uint3)Count;

    uint3 cell = int3(
        index % c.x,
        index / c.x % c.y,
        index / (c.x * c.y) % c.z);

    float3 clampedCount = uint3( 
        c.x == 1 ? 1 : c.x-1,
        c.y == 1 ? 1 : c.y-1,
        c.z == 1 ? 1 : c.z-1
        );


    float3 zeroAdjustedSize = float3(
        c.x == 1 ? 0 : Size.x,
        c.y == 1 ? 0 : Size.y,
        c.z == 1 ? 0 : Size.z
    );


    int Pattern = 2;
                                
    // Triangular-pattern
    if(Pattern == 1)
    {
        bool isOdd = cell.x % 2 > 0;
        float3 verticalOffset= isOdd
                            ? (0.331f * Size.y) 
                            : 0;
                            
        const float TriangleScale = 0.581f;
        float3 pos =float3((float) ((cell.x - c.x/2 + 0.5f) * Size.x * TriangleScale),
                        (float) ((cell.y - c.y/2 + 0.5f) * Size.y + verticalOffset),
                        (float) (0));

        float rotZ=  isOdd ? 60 * ToRad : 0;
        pos+= Center;
        ResultPoints[index].Position = pos;
        ResultPoints[index].W = W;

        ResultPoints[index].Rotation = qFromAngleAxis((OrientationAngle) *PI/180 + rotZ, normalize(OrientationAxis));

    }
    // Hexa-pattern
    else if(Pattern == 2)
    {
        // bool isOddColumn = index.x % 2 == 0;
        // bool isOddRow = cell.y % 2 > 0;
        // bool isOddLayer = cell.z % 2 > 0;
        
        // bool isOdd2Column = index.x % 4 == 0;

        float3 pos = SizeMode > 0.5 ? zeroAdjustedSize * (cell / clampedCount) - zeroAdjustedSize * (Pivot  + 0.5)
                                    : zeroAdjustedSize * cell - zeroAdjustedSize * clampedCount * (Pivot  + 0.5);

        int hexAttrIndex = cell.x % 2 + ((cell.y +3 ) % 6) * 2;
        float2 offsetAndAngles =  HexOffsetsAndAngles[hexAttrIndex];
        pos.x+= offsetAndAngles.x * zeroAdjustedSize.x * 0.3333;

        const float HexScale = 0.578f;
        pos.x *= HexScale * 3;
        float rotDelta = (180 +offsetAndAngles.y ) * ToRad ;

        pos+= Center;
        ResultPoints[index].Position = pos;
        ResultPoints[index].W = W;
        ResultPoints[index].Rotation = qFromAngleAxis(OrientationAngle*PI/180 + rotDelta, normalize(OrientationAxis));
    }                            
    else {
        float3 pos = SizeMode > 0.5 ? zeroAdjustedSize * (cell / clampedCount) - zeroAdjustedSize * (Pivot  + 0.5)
                                    : zeroAdjustedSize * cell - zeroAdjustedSize * clampedCount * (Pivot  + 0.5);

        pos+= Center;
        ResultPoints[index].Position = pos;
        ResultPoints[index].W = W;
        ResultPoints[index].Rotation = qFromAngleAxis(OrientationAngle*PI/180, normalize(OrientationAxis));
    }
    ResultPoints[index].Color = 1;
    ResultPoints[index].Selected = 1;
    ResultPoints[index].Stretch = 1;
}

