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

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

struct gsOutput
{
    uint faceId : SV_RENDERTARGETARRAYINDEX;
    float4 position : SV_POSITION;
    float3 normal : CUSTOM;
    float4 color : COLOR0;
};

vsOutput vsMain(uint vertexId : SV_VertexID)
{
    vsOutput output;

    // uint faceIndex = vertexId / 6;

    // float4 quadPos = float4(Quad[vertexId], 1) ;

    // output.uv= quadPos.xy * float2(0.5, -0.5) + 0.5;
    // output.position = quadPos;
    output.uv = float2((vertexId << 1) & 2, vertexId & 2);
    output.position = float4(output.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}

[maxvertexcount(18)] void gsMain(triangle vsOutput input[3], inout TriangleStream<gsOutput> output)
{
    for (int f = 0; f < 6; ++f)
    {
        for (int v = 0; v < 3; ++v)
        {
            gsOutput o;
            o.position = input[v].position;
            o.normal = UvAndIndexToBoxCoord(input[v].uv, f);
            o.color = colorOfBox(f); // float4(1,1,1,1);
            o.faceId = f;
            output.Append(o);
        }
        output.RestartStrip();
    }
}

float4 psMain(in gsOutput i) : SV_TARGET0
{
    // return float4(Orientation,0,0,1);
    // return i.color;
    // return float4( abs(i.normal.rgb),1);
    // return float4(0,1,0,1);
    float2 uv = ComputeUvFromNormal(i.normal) + float2(Orientation, 0);
    float4 col = Image.SampleLevel(texSampler, uv, 0);
    return col;
}
