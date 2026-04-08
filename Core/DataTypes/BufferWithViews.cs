using System;

namespace T3.Core.DataTypes;

public sealed class BufferWithViews : IDisposable
{
#if PLATFORM_WINDOWS
    public SharpDX.Direct3D11.Buffer Buffer;
    public SharpDX.Direct3D11.ShaderResourceView Srv;
    public SharpDX.Direct3D11.UnorderedAccessView Uav;

    public void Dispose()
    {
        Buffer?.Dispose();
        Buffer = null;

        Srv?.Dispose();
        Srv = null;

        Uav?.Dispose();
        Uav = null;
    }
#else
    public T3.Core.Gpu.Buffer? Buffer;
    public T3.Core.Gpu.ShaderResourceView? Srv;
    public T3.Core.Gpu.UnorderedAccessView? Uav;

    public void Dispose()
    {
        Buffer?.Dispose();
        Buffer = null;

        Srv?.Dispose();
        Srv = null;

        Uav?.Dispose();
        Uav = null;
    }
#endif
}
