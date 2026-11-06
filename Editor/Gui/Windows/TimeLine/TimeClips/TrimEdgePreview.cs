#nullable enable
using ImGuiNET;
using T3.Core.Animation;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// While a clip's start or end handle is dragged, moves the playback time onto the trimmed edge so the output
/// shows the frame the cut lands on. The playhead time is held and restored when the drag ends.
/// </summary>
internal static class TrimEdgePreview
{
    internal static bool IsActive { get; private set; }

    /// <summary>The time the playhead marker should be drawn at: the held time while a preview is shown.</summary>
    internal static double GetPlayheadTime(Playback playback)
    {
        return IsActive ? _heldPlayheadTimeInBars : playback.TimeInBars;
    }

    internal static void ShowStartEdge(Playback playback, TimeClip clip)
    {
        Show(playback, clip.TimeRange.Start);
    }

    /// <summary>The clip is inactive at its exclusive end, so this previews the last frame inside it.</summary>
    internal static void ShowEndEdge(Playback playback, TimeClip clip)
    {
        var lastFrameTime = clip.TimeRange.End - playback.BarsFromSeconds(LastFrameOffsetSec);
        Show(playback, Math.Max(lastFrameTime, clip.TimeRange.Start));
    }

    internal static void End(Playback playback)
    {
        if (!IsActive)
            return;

        playback.TimeInBars = _heldPlayheadTimeInBars;
        IsActive = false;
    }

    /// <summary>Restores the playhead if a trim drag ended without passing through the handle's release path.</summary>
    internal static void EndIfMouseReleased(Playback playback)
    {
        if (IsActive && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            End(playback);
    }

    private static void Show(Playback playback, double edgeTimeInBars)
    {
        // During playback the time keeps running from wherever it is set, so a preview can't be held.
        if (Math.Abs(playback.PlaybackSpeed) > 0.001)
        {
            End(playback);
            return;
        }

        if (!IsActive)
        {
            _heldPlayheadTimeInBars = playback.TimeInBars;
            IsActive = true;
        }

        playback.TimeInBars = edgeTimeInBars;
    }

    /** One output frame at 60 fps. */
    private const double LastFrameOffsetSec = 1 / 60.0;

    private static double _heldPlayheadTimeInBars;
}
