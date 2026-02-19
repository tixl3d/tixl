using System.Runtime.InteropServices;

namespace Mediapipe.Gpu;

[StructLayout(LayoutKind.Sequential)]
public struct GlTextureInfo
{
    public int GlInternalFormat;
    public uint GlFormat;
    public uint GlType;
    public int Downscale;
}