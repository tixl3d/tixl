#if PLATFORM_WINDOWS
using SharpDX;
using SharpDX.Direct3D11;
#else
using T3.Core.Gpu;
#endif

namespace Lib.Utils;

internal sealed class GpuQuery : IDisposable
{
    public GpuQuery(Device device, QueryDescription desc)
    {
        _query = new Query(device, desc);
        _inBetweenQuery = false;
    }

    public void Dispose()
    {
        _inBetweenQuery = false;
        Utilities.Dispose(ref _query);
    }

    public void Begin(DeviceContext context)
    {
        if (_inBetweenQuery)
            return;

        _inBetweenQuery = true;
        context.Begin(_query);
    }

    public void End(DeviceContext context)
    {
        context.End(_query);
        _inBetweenQuery = false;
    }

    public bool GetData<T>(DeviceContext context, AsynchronousFlags flags, out T result) where T : struct
    {
        if (!_inBetweenQuery)
        {
            return context.GetData(_query, flags, out result);
        }

        result = new T();
        return false;
    }

    private Query _query;
    private bool _inBetweenQuery;
}