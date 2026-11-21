#nullable enable
using System;

namespace T3.Core.Output;

/// <summary>
/// Kill-switch for the app-level laser output driver (profiling / emergencies): when off, laser
/// ops are not pumped per frame and lasers go dark unless a view drives them.
/// Toggled from the debug protocol via setInput-free static access (debug builds only).
/// </summary>
public static class LaserDriverEnabled
{
    private static volatile bool _enabled = true;

    /// <summary>Read by the render thread each pump and written by the debug/UI thread; volatile
    /// keeps the kill-switch visible immediately instead of only after the next JIT reload.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
