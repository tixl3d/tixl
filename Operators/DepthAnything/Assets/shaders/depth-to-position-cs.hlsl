// Unprojects a depth map to camera-space positions: x right, y up, z the distance in front of the camera.
// The w channel is 1 where the depth was usable and 0 where it was not (NaN, infinite or not in front of the camera).

Texture2D<float> Depth : register(t0);
RWTexture2D<float4> Positions : register(u0);

cbuffer Params : register(b0)
{
    float TanHalfFovY;
    float DepthScale;
    uint DepthIsDisparity;
    float _padding;
}

static const float MinDisparity = 0.00001;

[numthreads(16, 16, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Depth.GetDimensions(width, height);
    if (id.x >= width || id.y >= height)
        return;

    float value = Depth.Load(int3(id.xy, 0));
    float z = DepthIsDisparity != 0 ? DepthScale / max(value, MinDisparity) : value * DepthScale;
    // Bit test: the compiler may fold isnan() away under its default float optimizations
    bool valid = (asuint(z) & 0x7fffffff) < 0x7f800000 && z > 0;
    if (!valid)
    {
        Positions[id.xy] = float4(0, 0, 0, 0);
        return;
    }

    float aspect = (float)width / height;
    float2 uv = (float2(id.xy) + 0.5) / float2(width, height);
    float2 ndc = float2(uv.x * 2 - 1, 1 - uv.y * 2);
    Positions[id.xy] = float4(ndc.x * TanHalfFovY * aspect * z, ndc.y * TanHalfFovY * z, z, 1);
}
