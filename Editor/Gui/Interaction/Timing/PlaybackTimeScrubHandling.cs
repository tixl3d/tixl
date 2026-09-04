using ImGuiNET;
using T3.Core.Animation;
using T3.Editor.App;
using T3.Editor.Gui.Interaction.Keyboard;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction.Timing;

internal static class PlaybackTimeScrubHandling
{
    internal static void ProcessFrame()
    {
        if (Playback.Current.IsRenderingToFile)
        {
            _isTimeScrubbing = false;
            return;
        }

        if (!_isTimeScrubbing)
        {
            if (!UserActions.PlaybackScrubTime.Triggered())
                return;

            _isTimeScrubbing = true;
            _timeScrubNeedsRestart = true;
            _timeScrubStartTimeInBars = Playback.Current.TimeInBars;
            _timeScrubAnchor = ImGui.GetMousePos();
        }

        if (!IsShortcutDown(UserActions.PlaybackScrubTime))
        {
            _isTimeScrubbing = false;
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            Playback.Current.TimeInBars = _timeScrubStartTimeInBars;
            _isTimeScrubbing = false;
            return;
        }

        var timeInBars = Playback.Current.TimeInBars;
        var hasSoundtrackRange = TryGetMainSoundtrackRangeInBars(out var minTimeInBars, out var maxTimeInBars);
        InfinitySliderOverlay.Draw(ref timeInBars,
                                  _timeScrubNeedsRestart,
                                  _timeScrubAnchor,
                                  min: minTimeInBars,
                                  max: maxTimeInBars,
                                  scale: 0.25f,
                                  clampMin: hasSoundtrackRange,
                                  clampMax: hasSoundtrackRange,
                                  disableRounding:true);
        _timeScrubNeedsRestart = false;
        Playback.Current.TimeInBars = timeInBars;

        // Avoid pointer conflicts with regular UI while scrub gizmo is active.
        FrameStats.Current.OpenedPopupCapturedMouse = true;
    }

    private static bool IsShortcutDown(UserActions action)
    {
        var io = ImGui.GetIO();
        foreach (var binding in KeyMapSwitching.CurrentKeymap.Bindings)
        {
            if (binding.Action != action)
                continue;

            if (!binding.KeyCombination.ModifiersMatch(io))
                continue;

            if (ImGui.IsKeyDown(binding.KeyCombination.Key.ToImGuiKey()))
                return true;
        }

        return false;
    }

    private static bool TryGetMainSoundtrackRangeInBars(out double minTimeInBars, out double maxTimeInBars)
    {
        minTimeInBars = double.NegativeInfinity;
        maxTimeInBars = double.PositiveInfinity;

        if (!PlaybackUtils.TryFindingSoundtrack(out var soundtrack, out _))
            return false;

        var clip = soundtrack.Clip;
        minTimeInBars = clip.TimeRange.Start;

        if (clip.TimeRange.End > clip.TimeRange.Start)
        {
            maxTimeInBars = clip.TimeRange.End;
        }
        else if (clip.LengthInSeconds > 0)
        {
            maxTimeInBars = clip.TimeRange.Start + Playback.Current.BarsFromSeconds(clip.LengthInSeconds);
        }
        else
        {
            maxTimeInBars = clip.TimeRange.Start;
        }

        if (maxTimeInBars < minTimeInBars)
        {
            var temp = minTimeInBars;
            minTimeInBars = maxTimeInBars;
            maxTimeInBars = temp;
        }

        return true;
    }

    private static bool _isTimeScrubbing;
    private static bool _timeScrubNeedsRestart;
    private static double _timeScrubStartTimeInBars;
    private static Vector2 _timeScrubAnchor;
}
