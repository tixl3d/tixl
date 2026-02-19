
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

Texture2D<float4> OriginalImage : register(t0);
Texture2D<float4> RaysImage : register(t1);
sampler samLinear : register(s0);
sampler samPoint : register(s1);


vsOutput vsMain(uint vertexId: SV_VertexID)
{
    vsOutput output;
    float4 quadPos = float4(Quad[vertexId], 1) ;
    output.texCoord = quadPos.xy * float2(0.5, -0.5) + 0.5;
    output.position = quadPos;

    float4 pointLigthPos4InWorld = float4(Position,1);
    float4 posInCam = mul(pointLigthPos4InWorld, WorldToClipSpace);
    posInCam.xyz /= posInCam.w;
    output.lightPosInCam = posInCam.xyz;
    return output;
}


static const int NUMWT = 10;
static const float Gauss[NUMWT] = { 0.93, 0.86, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.1 };

float4 psMain( vsOutput input ) : SV_Target
{
    float2 viewTFragPos = float2(input.texCoord.x*2.0 - 1.0, -input.texCoord.y*2.0 + 1.0);

    float sampleStep = 1; 
    float2 sampleDir = viewTFragPos.xy - input.lightPosInCam.xy;
    sampleDir.x = -sampleDir.x;
    float2 dir = sampleStep * BlurSize / BlurSamples * sampleDir;
    //dir =0;

    float4 c = float4(0, 0, 0, 0);
    float totalWeight = 0;
    float2 pos = dir - BlurSamples *0.5 * dir - dir * Offset;
    float distanceToCenter;
    for (int i = 0; i < BlurSamples; ++i)
    {
        float2 p = input.texCoord + pos;
        
        if (p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1)
        {
            float ir = i/floor(BlurSamples);
            float weightIndex = (int)(abs(ir*2-1)*1 * (NUMWT-1));
            float weight = lerp(Gauss[weightIndex], Gauss[(int)weightIndex + 1], frac(weightIndex));
            c += RaysImage.Sample(samLinear, p)*weight;
            totalWeight += weight;
        }        
        pos += dir;
    }

    float4 original = OriginalImage.Sample(samPoint, input.texCoord) * OriginalColor;
    float4 blurred = c.rgba/totalWeight;
    
     return float4(blurred.rgb + original.rgb, original.a + blurred.a);
}

