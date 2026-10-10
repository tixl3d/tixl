#nullable enable
using T3.Core.Logging;
using T3.Graphics;
using T3.Graphics.Compat;

namespace T3.Editor.Gui.Windows.Analyze;

/// <summary>
/// Reports how the system presents the main swap chain: composed by the system, as a hardware overlay, or by
/// independent flip, and how many presented frames are still queued for display. Frame pacing differs between these
/// modes, so this is the first thing to check when frame intervals jitter.
/// </summary>
internal static class PresentationDiagnostics
{
    /// <summary>Unknown until the swap chain has reported statistics (the first presents report none).</summary>
    public static PresentationModes? CompositionMode { get; private set; }

    /// <summary>Frames handed to Present that the display hasn't shown yet; -1 when unknown.</summary>
    public static int QueuedFrames { get; private set; } = -1;

    /// <summary>Call once per frame after presenting the main swap chain.</summary>
    public static void Update(SwapChain swapChain)
    {
        if (!swapChain.TryGetPresentationStatistics(out var statistics))
        {
            QueuedFrames = -1;
            return;
        }

        QueuedFrames = statistics.QueuedFrames;
        if (statistics.Mode == CompositionMode)
            return;

        Log.Debug($"Main window presentation changed from {Describe(CompositionMode)} to {Describe(statistics.Mode)}");
        CompositionMode = statistics.Mode;
    }

    public static string Describe(PresentationModes? mode)
    {
        return mode switch
                   {
                       PresentationModes.ComposedBySystem   => "composed by DWM",
                       PresentationModes.HardwareOverlay    => "hardware overlay",
                       PresentationModes.IndependentFlip    => "independent flip",
                       PresentationModes.CompositionFailure => "composition failure",
                       _                                    => "unknown"
                   };
    }
}
