#include "shared/hash-functions.hlsl"
#include "shared/noise-functions.hlsl"
#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer IntParams : register(b0)
{
    int FrameIndex;
    int Reset;
    uint CallCount;
    uint InputBufferSize;
    int Ascending;
}

cbuffer Transforms : register(b1)
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

cbuffer CallParams : register(b2)
{
    int CallIndex;
}
StructuredBuffer<Point> SourcePoints : t0;
RWStructuredBuffer<Point> ResultPoints : u0;
RWStructuredBuffer<uint> IndexBuffer : u1;

uint3 StageCalc3(uint i){
    //inverse of y=x*(x+1)/2
    uint StageCounter=uint(floor((sqrt((i+1)*8-1)-1)/2)); //verified up to 512k
    uint StageOffset=(StageCounter*(StageCounter+1))/2;
    uint PassCounter=StageCounter+StageOffset-i;
// uint gsize=1<<PassCounter;   
    return uint3(PassCounter,StageCounter,i-StageOffset);
}
uint2 GetSwapPair(uint i, uint ip, uint TotalCount){
    uint totalsteps=uint(round(log2(TotalCount)));
    totalsteps=totalsteps*(totalsteps+1)/2;

    ip=ip%totalsteps;

    uint3 sc=StageCalc3(ip);
    uint gs=1<<sc.x;
    
    uint2 sgi=uint2(i%gs,(i/gs)*gs*2);
    uint2 pair=(i/gs)*gs*2+uint2((i%gs), ((sc.z==0)&&(sc.x>0))?(gs*2-1-(i%gs)):((i%gs)+gs));
    return pair;
};

uint2 GetSwapPairBitonic(uint i, uint ip, uint TotalCount){
    uint totalsteps=uint(round(log2(TotalCount)));
    totalsteps=totalsteps*(totalsteps+1)/2;

    ip=ip%totalsteps;

    uint3 sc=StageCalc3(ip);
    uint gs=1<<sc.x;
    
    uint2 pair=(i%gs)+((i/gs)*gs*2)+(((i/(1<<sc.y))%2)?uint2(gs,0):uint2(0,gs));
    return pair;
};


float c2k(Point c){
    float3 p=c.Position.xyz;
    if(isnan(c.Scale.x)){return -1;}
    float k=length(c.Position.xyz-CameraToWorld[3].xyz);
    // float k=-mul(float4(c.Position.xyz,1),WorldToCamera).z;//viewspace z
    if(Ascending>0)k=-k;
    return k;
}




[numthreads(64, 1, 1)] void main_lookup(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    SourcePoints.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
        return;
    uint ib=IndexBuffer[i.x].x;

    Point p = SourcePoints[i.x];
    p = SourcePoints[ib];

    ResultPoints[i.x] = p;
}




[numthreads(64, 1, 1)] void main_sort(uint3 i : SV_DispatchThreadID)
{
    uint numStructs, stride;
    IndexBuffer.GetDimensions(numStructs, stride);
    if (i.x >= numStructs)
        return;

    uint TotalPoints=numStructs;
    if(i.x*2+1>=numStructs)return;
    
    uint PassIndex=uint(CallIndex);
    uint pid=max(0,FrameIndex*CallCount+PassIndex);


    int2 pair=GetSwapPair(i.x,pid,TotalPoints);
    int idx1=pair.x;
    int idx2=pair.y;

    uint pi1=IndexBuffer[idx1].x;
    uint pi2=IndexBuffer[idx2].x;
    if(Reset){pi1=idx1;pi2=idx2;}

    if(uint(pi2)>=TotalPoints)return;
    if(uint(idx2)>=TotalPoints)return;
    Point c1=SourcePoints[pi1];
    Point c2=SourcePoints[pi2];
    float k1=c2k(c1);
    float k2=c2k(c2);


    // uint totalsteps=uint(round(log2(TotalPoints)));
    // totalsteps=totalsteps*(totalsteps+1)/2;

    PassIndex=(PassIndex+1);
    if(k1<k2){
        IndexBuffer[idx1]=pi2;
        IndexBuffer[idx2]=pi1;
        // IndexBuffer[idx1]=uint2(pi2,0);
        // IndexBuffer[idx2]=uint2(pi1,0);
    }else{
        // if(Reset){IndexBuffer[idx1]=pi1;IndexBuffer[idx2]=pi2;}
        //may not need to write this? only if reset
        IndexBuffer[idx1]=pi1;
        IndexBuffer[idx2]=pi2;
        // IndexBuffer[idx1]=uint2(pi1,0);
        // IndexBuffer[idx2]=uint2(pi2,0);
    }
    
}
