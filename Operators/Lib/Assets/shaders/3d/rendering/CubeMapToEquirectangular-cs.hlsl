#include "shared/cubemap-eac.hlsl"

cbuffer Params : register(b0)
{
    float2 Size;
    float Orientation;
    float Lod;

    int EquiAngular;
}

TextureCube<float4> CubeMap : register(t0);
SamplerState LinearSampler : register(s0);
RWTexture2D<float4> Result : register(u0);

static const float PI = 3.14159265;

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)Size.x || id.y >= (uint)Size.y)
        return;

    // Inverse of the lookup [ProjectOntoStage] uses for equirectangular images.
    float2 uv = (id.xy + 0.5) / Size;
    float longitude = (uv.x - 0.5 - Orientation) * 2 * PI;
    float polarAngle = uv.y * PI;
    float3 direction = float3(sin(polarAngle) * sin(longitude), cos(polarAngle), sin(polarAngle) * cos(longitude));

    if (EquiAngular != 0)
        direction = EncodeEacDirection(direction);

    Result[id.xy] = CubeMap.SampleLevel(LinearSampler, direction, Lod);
}
