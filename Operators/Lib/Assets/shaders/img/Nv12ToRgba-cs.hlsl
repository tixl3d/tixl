// Converts a hardware-decoded NV12 (8-bit) or P010/P016 (10/12-bit) frame to RGBA on the GPU, avoiding the
// CPU round-trip of the software path. The frame is a full-resolution luma plane plus a half-resolution
// interleaved Cb,Cr chroma plane, both bound as SRV slices of the D3D11VA decoder's output texture array. The
// host binds them as R8/R8G8 (NV12) or R16/R16G16 (P010), so samples arrive normalized to [0,1] either way.
//
// BT.709 limited (studio) range — the common case for HD/UHD H.264 & HEVC. The 10-bit limited-range end points
// differ from 8-bit by <0.5%, so the same constants serve both within tolerance. Color-matrix selection from
// the stream's color space (BT.601 for SD) and PQ/HLG tone-mapping are later refinements.
Texture2D<float>    LumaTexture   : register(t0); // R8/R16,     full resolution
Texture2D<float2>   ChromaTexture : register(t1); // R8G8/R16G16, half resolution (interleaved Cb,Cr)
RWTexture2D<float4>  Result       : register(u0);

[numthreads(16, 16, 1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height;
    Result.GetDimensions(width, height);
    if (i.x >= width || i.y >= height)
        return;

    float  luma   = LumaTexture[i.xy];
    float2 chroma = ChromaTexture[i.xy / 2];

    float yp = (luma     -  16.0 / 255.0) * (255.0 / 219.0);
    float cb = (chroma.x - 128.0 / 255.0) * (255.0 / 224.0);
    float cr = (chroma.y - 128.0 / 255.0) * (255.0 / 224.0);

    float3 rgb;
    rgb.r = yp + 1.5748 * cr;
    rgb.g = yp - 0.1873 * cb - 0.4681 * cr;
    rgb.b = yp + 1.8556 * cb;

    Result[i.xy] = float4(saturate(rgb), 1.0);
}
