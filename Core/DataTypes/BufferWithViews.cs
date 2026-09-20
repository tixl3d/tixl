using System;

namespace T3.Core.DataTypes;

public sealed class BufferWithViews : IDisposable
{
    public T3.Graphics.Compat.Buffer Buffer;
    public T3.Graphics.Compat.ShaderResourceView Srv;
    public T3.Graphics.Compat.UnorderedAccessView Uav;

        
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