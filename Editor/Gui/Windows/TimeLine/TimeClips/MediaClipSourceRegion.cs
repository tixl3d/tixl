#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Animation;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Per-frame bridge between the clip area and the timeline ruler: clips with a known content extent
/// (media footage, or an authored source extent on a combined op) publish it here when hovered or
/// single-selected, and the ruler's <see cref="SourceRegionIndicator"/> reads it the following frame
/// (the ruler draws before the clips). A selected clip wins over a hovered one. The extent is in
/// source time (bars); media footage starts at 0, authored extents may not.
/// </summary>
internal static class MediaClipSourceRegion
{
    public static void PublishHovered(TimeClip clip, TimeRange contentExtent) => Publish(clip, contentExtent, selected: false);
    public static void PublishSelected(TimeClip clip, TimeRange contentExtent) => Publish(clip, contentExtent, selected: true);

    /// <summary>The candidate published this or the previous frame, if any.</summary>
    public static bool TryGetCurrent([NotNullWhen(true)] out TimeClip? clip, out TimeRange contentExtent)
    {
        clip = null;
        contentExtent = default;
        if (_clip == null || ImGui.GetFrameCount() - _frame > 1)
            return false;

        clip = _clip;
        contentExtent = _contentExtent;
        return true;
    }

    private static void Publish(TimeClip clip, TimeRange contentExtent, bool selected)
    {
        var frame = ImGui.GetFrameCount();
        if (frame != _frame)
        {
            _frame = frame;
            _clip = null;
            _isSelected = false;
        }

        if (_clip != null && _isSelected && !selected)
            return;

        _clip = clip;
        _contentExtent = contentExtent;
        _isSelected = selected;
    }

    private static TimeClip? _clip;
    private static TimeRange _contentExtent;
    private static bool _isSelected;
    private static int _frame;
}
