namespace T3.Graphics;

/// <summary>
/// How many bytes one pixel of a format takes. Needed wherever tightly packed CPU data meets a texture:
/// upload row pitches, readback buffers, the screenshot and video paths.
/// </summary>
public static class FormatSizes
{
    /// <summary>Returns 0 for block-compressed and other formats that have no per-pixel size.</summary>
    public static int BytesPerPixel(Format format)
    {
        return format switch
                   {
                       Format.R32G32B32A32_Typeless or Format.R32G32B32A32_Float or Format.R32G32B32A32_UInt or Format.R32G32B32A32_SInt => 16,

                       Format.R32G32B32_Typeless or Format.R32G32B32_Float or Format.R32G32B32_UInt or Format.R32G32B32_SInt => 12,

                       Format.R16G16B16A16_Typeless or Format.R16G16B16A16_Float or Format.R16G16B16A16_UNorm or Format.R16G16B16A16_UInt
                           or Format.R16G16B16A16_SNorm or Format.R16G16B16A16_SInt
                           or Format.R32G32_Typeless or Format.R32G32_Float or Format.R32G32_UInt or Format.R32G32_SInt
                           or Format.R32G8X24_Typeless or Format.D32_Float_S8X24_UInt => 8,

                       Format.R8G8B8A8_Typeless or Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb or Format.R8G8B8A8_UInt
                           or Format.R8G8B8A8_SNorm or Format.R8G8B8A8_SInt
                           or Format.B8G8R8A8_Typeless or Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb
                           or Format.B8G8R8X8_Typeless or Format.B8G8R8X8_UNorm or Format.B8G8R8X8_UNorm_SRgb
                           or Format.R16G16_Typeless or Format.R16G16_Float or Format.R16G16_UNorm or Format.R16G16_UInt
                           or Format.R16G16_SNorm or Format.R16G16_SInt
                           or Format.R32_Typeless or Format.D32_Float or Format.R32_Float or Format.R32_UInt or Format.R32_SInt
                           or Format.R24G8_Typeless or Format.D24_UNorm_S8_UInt
                           or Format.R10G10B10A2_Typeless or Format.R10G10B10A2_UNorm or Format.R10G10B10A2_UInt
                           or Format.R11G11B10_Float or Format.R9G9B9E5_Sharedexp => 4,

                       Format.R8G8_Typeless or Format.R8G8_UNorm or Format.R8G8_UInt or Format.R8G8_SNorm or Format.R8G8_SInt
                           or Format.R16_Typeless or Format.R16_Float or Format.D16_UNorm or Format.R16_UNorm or Format.R16_UInt
                           or Format.R16_SNorm or Format.R16_SInt
                           or Format.B5G6R5_UNorm or Format.B5G5R5A1_UNorm or Format.B4G4R4A4_UNorm => 2,

                       Format.R8_Typeless or Format.R8_UNorm or Format.R8_UInt or Format.R8_SNorm or Format.R8_SInt or Format.A8_UNorm => 1,

                       _ => 0,
                   };
    }
}
