
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
  float3(-1, -1,  1),
  float3( 1, -1,  1),
  float3( 1, -1, -1),
  float3( 1, -1, -1),
  float3(-1, -1, -1),
  float3(-1, -1,  1),
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

Texture2D<float4> InputTexture : register(t0);
sampler texSampler : register(s0);


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

vsOutput vsMain(uint vertexId: SV_VertexID)
{
    vsOutput output;
    float3 quadVertex = Quad[vertexId].xyz;
    float3 quadVertexInObject = quadVertex * Size * 0.5;
    output.position = mul(float4(quadVertexInObject, 1), ObjectToClipSpace);
    output.texCoord = (Quad[vertexId % 6]).xy*float2(0.5, -0.5) + 0.5;

    return output;
}

float4 psMainOnlyColor(vsOutput input) : SV_TARGET
{
    float2 uv = input.texCoord;
    float2 d = 2.0*abs(frac(uv)-0.5);
    float ddx_uv = abs(ddx(uv.x));
    float ddy_uv = abs(ddy(uv.y));
    // ddx_uv = fwidth(uv.x);
    // ddy_uv = fwidth(uv.y);
    float width = 1.0 - 0.03; // size in [0,2]
    float cornerLength = 1.0 - 0.4; // size in [0, 2]
    float f = max(min(smoothstep(width- ddx_uv, width+ ddx_uv, d.x),
                      smoothstep(cornerLength - ddy_uv, cornerLength + ddy_uv, d.y)),
                  min(smoothstep(width- ddy_uv, width+ ddx_uv, d.y),
                      smoothstep(cornerLength - ddx_uv, cornerLength + ddx_uv, d.x)));
    // f = max(smoothstep(width- ddx_uv, width+ ddx_uv, d.x),
    //         smoothstep(width- ddy_uv, width+ ddx_uv, d.y));

    float3 color = Color.rgb;
    return float4(color * f, Color.a * f); 
}
