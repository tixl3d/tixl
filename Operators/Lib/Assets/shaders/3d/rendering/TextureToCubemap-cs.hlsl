#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

// static const float3 Quad[] =
// {
//   // xy front
//   float3(-1, -1, 1),
//   float3( 1, -1, 1),
//   float3( 1,  1, 1),
//   float3( 1,  1, 1),
//   float3(-1,  1, 1),
//   float3(-1, -1, 1),
//   // yz right
//   float3(1, -1,  1),
//   float3(1, -1, -1),
//   float3(1,  1, -1),
//   float3(1,  1, -1),
//   float3(1,  1,  1),
//   float3(1, -1,  1),
//   // xz top
//   float3(-1, 1,  1),
//   float3( 1, 1,  1),
//   float3( 1, 1, -1),
//   float3( 1, 1, -1),
//   float3(-1, 1, -1),
//   float3(-1, 1,  1),
//   // xy back
//   float3( 1, -1, -1),
//   float3(-1, -1, -1),
//   float3(-1,  1, -1),
//   float3(-1,  1, -1),
//   float3( 1,  1, -1),
//   float3( 1, -1, -1),
//   // yz left
//   float3(-1, -1, -1),
//   float3(-1, -1,  1),
//   float3(-1,  1,  1),
//   float3(-1,  1,  1),
//   float3(-1,  1, -1),
//   float3(-1, -1, -1),
//   // xz bottom
//   float3(-1, -1,  1),
//   float3( 1, -1,  1),
//   float3( 1, -1, -1),
//   float3( 1, -1, -1),
//   float3(-1, -1, -1),
//   float3(-1, -1,  1),
// };

float4 colorOfBox(uint face)
{
    float4 c = float4(0, 0, 0, 1);

    if (face == 0) // posx (red)
    {
        c = float4(1, 0, 0, 1);
    }
    else if (face == 1) // negx (cyan)
    {
        c = float4(1, 1, 0, 1);
    }
    else if (face == 2) // posy (green)
    {
        c = float4(0, 1, 0, 1);
    }
    else if (face == 3) // negy (magenta)
    {
        c = float4(0, 1, 1, 1);
    }
    else if (face == 4) // posz (blue)
    {
        c = float4(0, 0, 1, 1);
    }
    else // if (i.face == 5) // negz (yellow)
    {
        c = float4(1, 0, 1, 1);
    }

    return c;
}

float3 UvAndIndexToBoxCoord(float2 uv, uint face)
{
    float3 n = float3(0, 0, 0);
    float3 t = float3(0, 0, 0);

    if (face == 0) // posx (red)
    {
        n = float3(1, 0, 0);
        t = float3(0, 1, 0);
    }
    else if (face == 1) // negx (cyan)
    {
        n = float3(-1, 0, 0);
        t = float3(0, 1, 0);
    }
    else if (face == 2) // posy (green)
    {
        n = float3(0, -1, 0);
        t = float3(0, 0, -1);
    }
    else if (face == 3) // negy (magenta)
    {
        n = float3(0, 1, 0);
        t = float3(0, 0, 1);
    }
    else if (face == 4) // posz (blue)
    {
        n = float3(0, 0, -1);
        t = float3(0, 1, 0);
    }
    else // if (i.face == 5) // negz (yellow)
    {
        n = float3(0, 0, 1);
        t = float3(0, 1, 0);
    }

    float3 x = cross(n, t);

    uv = uv * 2 - 1;

    n = n + t * uv.y + x * uv.x;
    n.y *= -1;
    n.z *= -1;
    return n;
}

// static const float Roughness = 0;
// static const int NumSamples = 1;

cbuffer Params : register(b0)
{
    float Orientation;
}

// TextureCube<float4> CubeMap : register(t0);
Texture2D Image : register(t0);
sampler texSampler : register(s0);

float2 ComputeUvFromNormal(float3 n)
{
    // float PI = 3.141578;
    float3 N = normalize(n);
    float2 uv = N.xy;
    uv.y = acos(N.y) / PI + 1;
    uv.x = atan2(N.x, N.z) / PI / 2 + 1;
    return uv;
}

// One full-screen triangle per cube face: 18 vertices, the face is vertexId / 3. The geometry shader only routes
// each triangle to its face's layer. Where there is no geometry stage (Metal), the vertex shader does that itself.
struct vsOutput
{
    float4 position : SV_POSITION;
    float3 normal : CUSTOM;
    nointerpolation uint face : FACE_INDEX;
#if defined(__SLANG__)
    uint layer : SV_RenderTargetArrayIndex;
#endif
};

struct gsInput
{
    float4 position : SV_POSITION;
    float3 normal : CUSTOM;
    nointerpolation uint face : FACE_INDEX;
};

struct gsOutput
{
    float4 position : SV_POSITION;
    float3 normal : CUSTOM;
    uint faceId : SV_RENDERTARGETARRAYINDEX;
};

struct psInput
{
    float4 position : SV_POSITION;
    float3 normal : CUSTOM;
};

vsOutput vsMain(uint vertexId : SV_VertexID)
{
    uint face = vertexId / 3;
    uint corner = vertexId % 3;

    vsOutput output;
    float2 uv = float2((corner << 1) & 2, corner & 2);
    output.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    output.normal = UvAndIndexToBoxCoord(uv, face);
    output.face = face;
#if defined(__SLANG__)
    output.layer = face;
#endif
    return output;
}

[maxvertexcount(3)] void gsMain(triangle gsInput input[3], inout TriangleStream<gsOutput> output)
{
    for (int v = 0; v < 3; ++v)
    {
        gsOutput o;
        o.position = input[v].position;
        o.normal = input[v].normal;
        o.faceId = input[0].face;
        output.Append(o);
    }
}

float4 psMain(in psInput i) : SV_TARGET0
{
    // return float4(Orientation,0,0,1);
    // return i.color;
    // return float4( abs(i.normal.rgb),1);
    // return float4(0,1,0,1);
    float2 uv = ComputeUvFromNormal(i.normal) + float2(Orientation, 0);
    float4 col = Image.SampleLevel(texSampler, uv, 0);
    return col;
}
