#nullable enable
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Animation;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Per-frame bridge between the clip area and the timeline ruler: media clips publish their footage
/// duration here when hovered or single-selected, and the ruler's <see cref="SourceRegionIndicator"/>
/// reads it the following frame (the ruler draws before the clips). A selected clip wins over a hovered one.
/// </summary>
internal static class MediaClipSourceRegion
{
    public static void PublishHovered(TimeClip clip, double footageBars) => Publish(clip, footageBars, selected: false);
    public static void PublishSelected(TimeClip clip, double footageBars) => Publish(clip, footageBars, selected: true);

    /// <summary>The candidate published this or the previous frame, if any.</summary>
    public static bool TryGetCurrent([NotNullWhen(true)] out TimeClip? clip, out double footageBars)
    {
        clip = null;
        footageBars = 0;
        if (_clip == null || ImGui.GetFrameCount() - _frame > 1)
            return false;

        clip = _clip;
        footageBars = _footageBars;
        return true;
    }

    private static void Publish(TimeClip clip, double footageBars, bool selected)
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
        _footageBars = footageBars;
        _isSelected = selected;
    }

    private static TimeClip? _clip;
    private static double _footageBars;
    private static bool _isSelected;
    private static int _frame;
}
