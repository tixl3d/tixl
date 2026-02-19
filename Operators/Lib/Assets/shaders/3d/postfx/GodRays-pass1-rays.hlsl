
static const float3 Quad[] = 
{
  float3(-1, -1, 0),
  float3( 1, -1, 0), 
  float3( 1,  1, 0), 
  float3( 1,  1, 0), 
  float3(-1,  1, 0), 
  float3(-1, -1, 0), 
};



cbuffer ParamConstants : register(b0)
{
    float3 Position;
    float _padding;

    float4 RayColor;
    float4 OriginalColor;

    float Intensity;
    float Size;
    float Streaks;
    float Samples;

    float ShiftDepth;
    float Offset;
    float BlurSamples;
    float BlurSize;

    float BlurOffset;
    float Decay;
}


cbuffer CameraTransforms : register(b1)
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



struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float3 lightPosInCam: LIGHTPOS;
};


vsOutput vsMain(uint vertexId: SV_VertexID)
{
    vsOutput output;
    float4 quadPos = float4(Quad[vertexId], 1) ;
    output.texCoord = quadPos.xy * float2(0.5, -0.5) + 0.5;
    output.position = quadPos;

    float4 pointLigthPos4InWorld = float4(Position,1);
    float4 posInCam = mul(pointLigthPos4InWorld, WorldToClipSpace);
    posInCam.xyz /= posInCam.w;
    output.lightPosInCam = posInCam;
    return output;
}


Texture2D<float4> Image : register(t0);
Texture2D<float4> Depth : register(t1);
sampler samLinear : register(s0);
sampler samPoint : register(s1);


float4 psMain(vsOutput input) : SV_TARGET
{
    int height, width;
    Depth.GetDimensions(width, height);
    float aspectRatio = (float)width / height;

    //float4 c = Image.Sample(samPoint, input.texCoord);
    float depth = Depth.SampleLevel(samPoint, input.texCoord, 0).r;
    float4 viewTFragPos = float4(input.texCoord.x*2.0 - 1.0, -input.texCoord.y*2.0 + 1.0, depth, 1.0);
    float4 cameraTFragPos = mul(viewTFragPos, ClipSpaceToCamera); 
    cameraTFragPos /= cameraTFragPos.w;

    float sampleStep = 1;
    float2 sampleDir = viewTFragPos.xy - input.lightPosInCam.xy; 
    float2 normalizedDir = normalize(sampleDir);
    float length1= abs(dot(normalizedDir, float2(1,0)));
    sampleDir *=  lerp(1.1,  aspectRatio, pow(length1,1.3)); 
    //sampleDir.x *= aspectRatio;
    sampleDir.x = -sampleDir.x;
    float2 dir = sampleStep * Size / Samples * sampleDir;

    float2 pos = dir;
    float distanceToCenter;
    float4 c = 0;
    for (int i = 0; i < Samples; ++i)
    {
        float2 p = input.texCoord + pos;
        
        if (p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1)
        {
            float depth2 = Depth.SampleLevel(samLinear, p, 0).r;
            if (depth2 > input.lightPosInCam.z + ShiftDepth/100)
            {                
                distanceToCenter =  1 / pow(length(pos),Decay);
                c += distanceToCenter + Streaks;
            }
        }        
        pos += dir;
    }
    c *= 0.001 * Intensity * RayColor.a;
    c *= RayColor;
    return c;
}

