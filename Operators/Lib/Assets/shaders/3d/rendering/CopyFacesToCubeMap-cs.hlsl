// The faces are rendered with right-handed views, while cube maps are addressed left-handed,
// so each face lands mirrored horizontally.
Texture2DArray<float4> Faces : register(t0);
RWTexture2DArray<float4> CubeMap : register(u0);

[numthreads(16, 16, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height, faceCount;
    Faces.GetDimensions(width, height, faceCount);
    if (i.x >= width || i.y >= height)
        return;

    CubeMap[i] = Faces[uint3(width - 1 - i.x, i.y, i.z)];
}
