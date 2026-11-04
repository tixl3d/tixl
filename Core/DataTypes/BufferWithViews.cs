#nullable enable
using System;

namespace T3.Core.DataTypes;

/// <summary>
/// A GPU buffer with its views. Every field is filled in after construction, and a buffer created only for CPU
/// readback, or with its SRV switched off, never gets one. Dispose clears the fields, so the object can be set up again.
/// </summary>
public sealed class BufferWithViews : IDisposable
{
    public SharpDX.Direct3D11.Buffer? Buffer;
    public SharpDX.Direct3D11.ShaderResourceView? Srv;
    public SharpDX.Direct3D11.UnorderedAccessView? Uav;

    public int ElementStride => Buffer is { IsDisposed: false } buffer ? buffer.Description.StructureByteStride : 0;

    public int ElementCount
    {
        get
        {
            var stride = ElementStride;
            return stride > 0 ? Buffer!.Description.SizeInBytes / stride : 0;
        }
    }

    public void Dispose()
    {
        Buffer?.Dispose();
        Buffer = null;

        Srv?.Dispose();
        Srv = null;

        Uav?.Dispose();
        Uav = null;
    }
}
