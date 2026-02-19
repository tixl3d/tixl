
static const float3 Quad[] = 
{
  // xy front
  float3(-1, -1, 1),
  float3( 1, -1, 1), 
  float3( 1,  1, 1), 
  float3( 1,  1, 1), 
  float3(-1,  1, 1), 
  float3(-1, -1, 1), 
  // yz right
  float3(1, -1,  1),
  float3(1, -1, -1),
  float3(1,  1, -1),
  float3(1,  1, -1),
  float3(1,  1,  1), 
  float3(1, -1,  1),
  // xz top
  float3(-1, 1,  1),
  float3( 1, 1,  1),
  float3( 1, 1, -1),
  float3( 1, 1, -1),
  float3(-1, 1, -1),
  float3(-1, 1,  1),
  // xy back
  float3( 1, -1, -1),
  float3(-1, -1, -1),
  float3(-1,  1, -1),
  float3(-1,  1, -1),
  float3( 1,  1, -1),
  float3( 1, -1, -1),
  // yz left
  float3(-1, -1, -1),
  float3(-1, -1,  1),
  float3(-1,  1,  1),
  float3(-1,  1,  1),
  float3(-1,  1, -1),
  float3(-1, -1, -1),
  // xz bottom
  float3( 1, -1, -1),
  float3( 1, -1,  1),
  float3(-1, -1,  1),
  float3(-1, -1,  1),
  float3(-1, -1, -1),
  float3( 1, -1, -1),
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
    float4 Color;
    float3 Size;
};

Texture3D<float4> VolumeTexture : register(t0);
sampler texSampler : register(s0);


struct vsOutput
{
    float4 position : SV_POSITION;
    float3 posInWorld : POSITION0;
    float3 texCoord : TEXCOORD;
};

vsOutput vsMain(uint vertexId: SV_VertexID)
{
    vsOutput output;
    float3 quadVertex = Quad[vertexId].xyz;
    float3 quadVertexInObject = quadVertex * Size * 0.5;
    output.position = mul(float4(quadVertexInObject, 1), ObjectToClipSpace);
    output.texCoord = Quad[vertexId]*float3(0.5, 0.5, 0.5) + 0.5;
    output.posInWorld = mul(float4(quadVertexInObject, 1), ObjectToWorld);

    return output;
}

float4 psMainOnlyColor(vsOutput input) : SV_TARGET
{
    float3 uvw = input.texCoord;

    const int NUM_SAMPLES = 25;
    float4 vDir = float4(input.posInWorld - CameraToWorld[3].xyz, 0);
    float3 viewDirInObject = mul(vDir, WorldToObject );
    float3 sampleStep = normalize(viewDirInObject)/float(NUM_SAMPLES);
    float3 c = 0.0;
    float alpha = 0.0;
    for (int i = 0; i < NUM_SAMPLES; i++) {
        float3 s = VolumeTexture.Sample(texSampler, uvw);
        float a = s*(1.0 - alpha);
        c += s*a;
        alpha += a;
        if (alpha > 0.99)
            break;
        uvw += sampleStep;
        if (max(max(uvw.x, uvw.y), uvw.z) > 1.0)
            break;
        if (min(min(uvw.x, uvw.y), uvw.z) < 0.0)
            break;
    }

    // c = vDir;
    // c = viewDirInObject;
    c *= input.texCoord;
    return float4(c , alpha); 
    return float4(c , 1); 
}
