// Renders a cube map for the output window, one preview pixel per screen pixel. Side and Cross read texels
// unfiltered so they stay sharp when zoomed; Sphere and Environment sample the cube like a consumer would.

cbuffer Params : register(b0)
{
    float4x4 CameraToWorld;

    float2 ContentOrigin;   // screen position of texel (0,0), relative to the preview
    float2 TexelsPerPixel;

    float2 PreviewSize;
    float TanHalfFov;
    float CameraDistance;

    int Mode;
    int Face;
    int Mip;
    int MipSize;

    int EquiAngular;
}

TextureCube<float4> CubeMap : register(t0);
Texture2DArray<float4> Faces : register(t1);
SamplerState LinearSampler : register(s0);
RWTexture2D<float4> Result : register(u0);

static const int ModeSide = 0;
static const int ModeCross = 1;
static const int ModeSphere = 2;

// Faces by cell of a horizontal cross, 4 x 3 cells; -1 is empty.
static const int CrossFaces[12] = { -1, 2, -1, -1,
                                     1, 4, 0, 5,
                                    -1, 3, -1, -1 };

// Same as shared/cubemap-eac.hlsl in the Lib package, which editor shaders cannot include.
float3 EncodeEacDirection(float3 direction)
{
    float3 a = abs(direction);
    if (a.x >= a.y && a.x >= a.z)
        return float3(sign(direction.x), atan(direction.yz / a.x) * 1.27323954);

    if (a.y >= a.z)
    {
        float2 eac = atan(direction.xz / a.y) * 1.27323954;
        return float3(eac.x, sign(direction.y), eac.y);
    }

    return float3(atan(direction.xy / a.z) * 1.27323954, sign(direction.z));
}

float4 ReadTexel(float2 pixel, int columns, int rows)
{
    int2 texel = (int2)floor((pixel - ContentOrigin) * TexelsPerPixel);
    if (any(texel < 0) || texel.x >= MipSize * columns || texel.y >= MipSize * rows)
        return 0;

    int face = Face;
    if (Mode == ModeCross)
    {
        int2 cell = texel / MipSize;
        face = CrossFaces[cell.y * 4 + cell.x];
        if (face < 0)
            return 0;

        texel -= cell * MipSize;
    }

    return float4(Faces.Load(int4(texel, face, Mip)).rgb, 1);
}

float4 SampleCube(float3 direction)
{
    if (EquiAngular != 0)
        direction = EncodeEacDirection(direction);

    return float4(CubeMap.SampleLevel(LinearSampler, direction, Mip).rgb, 1);
}

float4 Render3d(float2 pixel)
{
    float2 ndc = (pixel + 0.5) / PreviewSize * 2 - 1;
    float aspect = PreviewSize.x / PreviewSize.y;
    float3 rayInCamera = normalize(float3(ndc.x * aspect * TanHalfFov, -ndc.y * TanHalfFov, -1));
    float3 ray = mul(rayInCamera, (float3x3)CameraToWorld);

    if (Mode != ModeSphere)
        return SampleCube(ray);

    // Unit sphere at the origin, seen from CameraDistance along the camera's back axis.
    float3 origin = CameraToWorld[2].xyz * CameraDistance;
    float b = dot(origin, ray);
    float h = b * b - dot(origin, origin) + 1;
    if (h < 0)
        return 0;

    float3 normal = normalize(origin + ray * (-b - sqrt(h)));
    return SampleCube(normal);
}

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)PreviewSize.x || id.y >= (uint)PreviewSize.y)
        return;

    float2 pixel = id.xy;
    Result[id.xy] = Mode == ModeSide  ? ReadTexel(pixel, 1, 1)
                  : Mode == ModeCross ? ReadTexel(pixel, 4, 3)
                                      : Render3d(pixel);
}
