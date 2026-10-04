#nullable enable
using System.Runtime.InteropServices;
using SharpDX.DXGI;
using T3.Core.Animation;
using T3.Core.Logging;

namespace T3.Editor.App;

/// <summary>
/// Watches how much video memory the driver is currently willing to let TiXL keep resident. Filling the card is
/// normal and harmless — what hurts is another application taking memory away, which drops our <b>budget</b>
/// below what we already hold. Everything above the budget is then paged over PCIe on the frames that need it,
/// which shows up as long waits inside Present and looks like the renderer having become slow.
/// </summary>
internal static class GpuMemoryBudget
{
    /// <summary>True while more memory is held than the driver currently grants — expect paging stalls.</summary>
    public static bool IsOversubscribed { get; private set; }

    public static long UsageBytes { get; private set; }
    public static long BudgetBytes { get; private set; }

    public static void Initialize(T3.Graphics.Compat.Device device)
    {
        // DXGI only knows the D3D11 device; the Vulkan backend would report its budget via VK_EXT_memory_budget.
        if (ProgramWindows.UseVulkanBackend)
            return;

        try
        {
            using var nativeDevice = new SharpDX.Direct3D11.Device(device.NativePointer);
            Marshal.AddRef(device.NativePointer);
            using var dxgiDevice = nativeDevice.QueryInterface<SharpDX.DXGI.Device>();
            using var adapter = dxgiDevice.Adapter;
            _adapter = adapter.QueryInterface<Adapter3>();
        }
        catch (Exception e)
        {
            // Pre-DXGI-1.4 driver or a host that doesn't expose the adapter: the editor runs, it just can't warn.
            Log.Debug($"Can't query the GPU memory budget on this adapter: {e.Message}");
        }
    }

    /// <summary>Samples the budget at most once every <see cref="PollIntervalSec"/>; call from the frame loop.</summary>
    public static void UpdateFrame()
    {
        if (_adapter == null)
            return;

        var now = Playback.RunTimeInSecs;
        if (now - _lastPollTime < PollIntervalSec)
            return;

        _lastPollTime = now;
        try
        {
            var info = _adapter.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
            UsageBytes = info.CurrentUsage;
            BudgetBytes = info.Budget;

            // A small margin: usage sits at the budget in normal operation, and a warning that flickers there
            // would be worse than none.
            IsOversubscribed = info.Budget > 0 && info.CurrentUsage > info.Budget + info.Budget / 50;
        }
        catch (Exception e)
        {
            Log.Debug($"Reading the GPU memory budget failed: {e.Message}");
            _adapter = null;
        }
    }

    public static void Release()
    {
        _adapter?.Dispose();
        _adapter = null;
    }

    /** Memory pressure changes on the scale of another app opening, not of a frame. */
    private const double PollIntervalSec = 2;

    private static Adapter3? _adapter;
    private static double _lastPollTime;
}
