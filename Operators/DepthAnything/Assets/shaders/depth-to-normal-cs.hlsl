// Surface normals from a depth map with a Sobel gradient. Edges clamp to the border pixel.

Texture2D<float> Depth : register(t0);
RWTexture2D<unorm float4> Normals : register(u0);

cbuffer Params : register(b0)
{
    float Strength;
    uint Invert;
    float2 _padding;
}

[numthreads(16, 16, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Depth.GetDimensions(width, height);
    if (id.x >= width || id.y >= height)
        return;

    int2 maxCoord = int2(width - 1, height - 1);
    int2 center = int2(id.xy);

    float tl = Depth.Load(int3(clamp(center + int2(-1, -1), int2(0, 0), maxCoord), 0));
    float t = Depth.Load(int3(clamp(center + int2(0, -1), int2(0, 0), maxCoord), 0));
    float tr = Depth.Load(int3(clamp(center + int2(1, -1), int2(0, 0), maxCoord), 0));
    float l = Depth.Load(int3(clamp(center + int2(-1, 0), int2(0, 0), maxCoord), 0));
    float r = Depth.Load(int3(clamp(center + int2(1, 0), int2(0, 0), maxCoord), 0));
    float bl = Depth.Load(int3(clamp(center + int2(-1, 1), int2(0, 0), maxCoord), 0));
    float b = Depth.Load(int3(clamp(center + int2(0, 1), int2(0, 0), maxCoord), 0));
    float br = Depth.Load(int3(clamp(center + int2(1, 1), int2(0, 0), maxCoord), 0));

    float sobelX = (tr + 2 * r + br) - (tl + 2 * l + bl);
    float sobelY = (bl + 2 * b + br) - (tl + 2 * t + tr);

    float3 normal = normalize(float3(-sobelX, -sobelY, 1.0 / max(Strength, 0.0001)));
    if (Invert != 0)
    {
        normal.z = -normal.z;
    }

    Normals[id.xy] = float4(normal * 0.5 + 0.5, 1);
}
