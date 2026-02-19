#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

static const float4 Factors[] = 
{
  //     x  y  z  w
  float4(0, 0, 0, 0), // 0 nothing
  float4(1, 0, 0, 0), // 1 for x
  float4(0, 1, 0, 0), // 2 for y
  float4(0, 0, 1, 0), // 3 for z
  float4(0, 0, 0, 1), // 4 for w
  float4(0, 0, 0, 0), // avoid rotation effects
};

cbuffer Transforms : register(b0)
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

cbuffer Params : register(b1)
{
    float3 Center;
    float SampleRadius; 
    float Smoothness;
    float Speed;
    float Steps;
    float ZoneLevels;
    float Curvature;
    float ZoneWidth;
    float ZoneCenter;
}

RWStructuredBuffer<LegacyPoint> Points : u0;   
Texture2D<float4> inputTexture : register(t1);
sampler texSampler : register(s0);
static float2 texSize;


void GetGradient(float2 uv, float radius, out float2 N, out float averageLevel) {
    float4 c = inputTexture.SampleLevel(texSampler, uv , 0.0);
    float2 d = SampleRadius *radius / texSize;

    float4 cx1 = inputTexture.SampleLevel(texSampler, uv + float2(-d.x,0),0);
    float4 cx2 = inputTexture.SampleLevel(texSampler, uv + float2(d.x,0),0);
    float4 cy1 = inputTexture.SampleLevel(texSampler, uv + float2(0, -d.y),0);
    float4 cy2 = inputTexture.SampleLevel(texSampler, uv + float2(0, d.y),0);

    float gx1 = (cx1.r + cx1.g + cx1.b)/3;
    float gx2 = (cx2.r + cx2.g + cx2.b)/3;
    float gy1 = (cy1.r + cy1.g + cy1.b)/3;
    float gy2 = (cy2.r + cy2.g + cy2.b)/3;

    N = float2 ( gx2 - gx1, gy2 - gy1);
    averageLevel = (gx1+gx2+gy1+gy2)/4;
}

[numthreads(256,4,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    int lineStepCount = (int)clamp(Steps,1,1000);

    uint pointCount, stride;
    Points.GetDimensions(pointCount,stride);

    if(i.x  > pointCount) {
        return;
    }

    uint lineStartIndex = int(i.x / lineStepCount) *lineStepCount; 

    uint sx,sy;
    inputTexture.GetDimensions(sx,sy);
    texSize = float2(sx,sy);

    
    LegacyPoint P = Points[lineStartIndex];

    float3 pos = P.Position;
    float4 rot = P.Rotation;
    float w = P.W;

    // Asign target Zonelevels to points
    int lineCount = pointCount / lineStepCount;
    int lineIndex = i.x / lineStepCount;

    float levelIndex = lineIndex % ZoneLevels;
    w = levelIndex / (ZoneLevels-1);
    float targetLevel = (w-0.5) * ZoneWidth + ZoneCenter;

    int index = lineStartIndex;
    for(int stepIndex = 0; stepIndex < lineStepCount; stepIndex++) 
    {
        if(index > pointCount)
            return;

        float3 posInObject = mul(float4(pos.xyz - Center,0), WorldToObject).xyz;
    
        float2 uv = posInObject.xy/2 * float2(1,-1) + float2(0.5, 0.5);

        float averageLevel;
        float2 N;
        GetGradient(uv, 1, N, averageLevel);

        float averageLevel2;
        float2 N2;
        GetGradient(uv, 15, N2, averageLevel2);
        N = lerp(N, N2, 0.1);
        averageLevel = lerp(averageLevel, averageLevel2, 0.1);

        if(length(N) > 0.0001) 
        {            
            float2 NN = normalize(N);
            float gradientAngle = atan2(N.x, N.y) ;// + clamp(correctionAngle * l , -1.6,1.6);

            //float2 slope = cross( float3(NN,0), float3(0,0,-1)).xy;
            //float slopAngle = atan2(slope.x, slope.y);

            // Smooth but strange offset
            float dLevel = (targetLevel - averageLevel) * Curvature;
            float adjustLevelFactor = smoothstep(0,1,abs(dLevel));
            adjustLevelFactor *= dLevel < 0 ? 1:-1;
            float adjustAngleOffset = adjustLevelFactor * 3.1415 / 2;
            float adjustedAngle = gradientAngle + adjustAngleOffset;
            float4 newRot = qFromAngleAxis(adjustedAngle,float3(0,0,1));            
            rot = qSlerp(newRot, rot, Smoothness);


            // Smooth but strange offset
            // float dLevel = targetLevel - averageLevel;
            // float adjustedAngle = gradientAngle;
            // if(abs(dLevel) > 0.001) {
            //     adjustedAngle += (dLevel < 0 ? 1 : -1) * 3.1415 / 2;                
            // }            

        }


        Points[index].Rotation = rot;

        Points[index].W = w;
        if(stepIndex == lineStepCount - 1) {
            Points[index].W = sqrt(-1);
        }

        float3 foreward = qRotateVec3(float3(1,0,0), rot) * Speed / 100 ;
        pos += foreward;
        Points[index].Position = pos;
        index++;
    }
}