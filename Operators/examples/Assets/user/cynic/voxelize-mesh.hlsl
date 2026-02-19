#include "shared/pbr.hlsl"

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

struct PsInput
{
    float4 posInClipSpace : SV_POSITION;
    float3 posInWorld : POS;
};

sampler texSampler : register(s0);

StructuredBuffer<PbrVertex> Vertices : t0;
StructuredBuffer<int3> FaceIndices : t1;
RWTexture3D<float4> OutputVolume : u0;

PsInput vsMain(uint id: SV_VertexID)
{
    PsInput output;

    int faceIndex = id / 3;//  (id % verticesPerInstance) / 3;
    int faceVertexIndex = id % 3;

    PbrVertex vertex = Vertices[FaceIndices[faceIndex][faceVertexIndex]];

    float4 posInObject = float4(vertex.Position, 1);
    output.posInClipSpace = mul(posInObject, ObjectToClipSpace);
    output.posInWorld = vertex.Position;

    float3 triNormal = normalize(vertex.Normal);
                                                                
    float xAmount = abs(dot(triNormal, float3(1,0,0)));         
    float yAmount = abs(dot(triNormal, float3(0,1,0)));         
    float zAmount = abs(dot(triNormal, float3(0,0,1)));         
    float3 zAxis = float3(0,0,1);                               
    float3 yAxis = float3(0,1,0);                               
    float3 xAxis = float3(1,0,0);                               
    float3 eye = float3(0, 0, -1);                              
                                                                
    if (xAmount > yAmount && xAmount > zAmount)                 
    {                                                           
        zAxis = float3(1,0,0);                                  
        yAxis = float3(0,1,0);                                  
        xAxis = -float3(0,0,-1);                                
        eye = float3(-1,0,0);                                   
    }                                                           
    else if (yAmount > xAmount && yAmount > zAmount)            
    {                                                           
        zAxis = float3(0,1,0);                                  
        yAxis = float3(1,0,0);                                  
        xAxis = float3(0,0,1);                                  
        eye = float3(-1,0,0);                                   
    }                                                           

 float3x3 rotWorldToCam = transpose(float3x3(xAxis, yAxis, zAxis));                                                                           
 float3 eyeInCam = -mul(rotWorldToCam, eye);                                                                                                  
 float4x4 worldToCamera = float4x4(float4(rotWorldToCam[0], 0),                                                                               
                                   float4(rotWorldToCam[1], 0),                                                                               
                                   float4(rotWorldToCam[2], 0),                                                                               
                                   float4(eyeInCam, 1));                                                                                      
                                                                                                                                                 
 // setup orthographic projection with origin at left/bottom and width/height of target volume                                                
 float left = -1, right = 1, bottom = -1, top = 1, zfarPlane = 10, znearPlane = -10;                                                         
 float4x4 camToProj = float4x4(2/(right - left),          0,                         0,                                 0,                    
                               0,                         2/(top - bottom),          0,                                 0,                    
                               0,                         0,                         1/(zfarPlane-znearPlane),          0,                    
                               (left+right)/(left-right), (top+bottom)/(bottom-top), znearPlane/(znearPlane-zfarPlane), 1);                   
                                                                                                                                                 
 float4x4 objectToProj = mul(worldToCamera, camToProj);                                                                                       
output.posInClipSpace = mul(posInObject, objectToProj);

    
    return output;
}


float4 psMain(PsInput pin) : SV_TARGET
{
    float3 pos= pin.posInClipSpace.xyz* float3(256, 256, 256) + float3(128, 128, 128);
    pos = pin.posInWorld * float3(100, 100, 100) + float3(128, 128, 128);
    OutputVolume[uint3(pos.x, pos.y, pos.z)] = float4(1, 1, 1, 1);
    return float4(1,1,0,1);
}
