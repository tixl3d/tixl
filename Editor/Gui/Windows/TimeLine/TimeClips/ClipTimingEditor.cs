#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

internal static class ClipTimingEditor
{

    internal static void DrawPopUp(ClipArea.LayerContext context)
    {
        if (TimeClipEditorRequested)
        {
            ImGui.OpenPopup((string) TimeEditPopupId);
            TimeClipEditorRequested = false;
        }
        
        if (context.ClipSelection.Count == 0)
            return;
        
        ImGui.SetNextWindowSize(new Vector2(380 * T3Ui.UiScaleFactor, 0));

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10,4));
        if (ImGui.BeginPopup((string)TimeEditPopupId))
        {
            FrameStats.Current.OpenedPopUpName = TimeEditPopupId;
            DrawContent(context);
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();
    }

    
    private static void DrawContent(ClipArea.LayerContext context)
    {
        _clips.Clear();
        _clips.AddRange(context.ClipSelection.GetAllOrSelectedClips());
        
        InitCombinedRanges();
        var labelWidth = LabelWidth * T3Ui.UiScaleFactor;
        var width = 70 * T3Ui.UiScaleFactor;

        FormInputs.AddSectionSubHeader("Edit clip timing");

        ImGui.NewLine();
        ImGui.SameLine(labelWidth);
        ImGui.TextUnformatted("Start");

        ImGui.SameLine(labelWidth + width);
        ImGui.TextUnformatted("Duration");

        ImGui.SameLine(labelWidth + 2*width);
        ImGui.TextUnformatted("End");

        EditClipTimingRange(Ranges.ClipRange, "Clip (bars)", _combinedTimeRange);
        EditClipTimingRange(Ranges.SourceRange, SourceRowLabel(), _combinedSourceRange);
        FormInputs.AddVerticalSpace();

        ImGui.TextUnformatted("Speed");
        ImGui.SameLine(labelWidth);
        
        var size = new Vector2(100, 0);
        ImGui.PushID(0);
        if (SingleValueEdit.Draw(ref _combinedSpeedPercentage, size, min: 0, max: 999, clampMin: false, clampMax: false, scale: 0.01f, format: "{0:0.0}%") != InputEditStateFlags.Nothing)
        {
            // The percentage is real-time speed; convert to source units per timeline bar.
            var playbackBpm = Playback.Current?.Bpm ?? 120;
            foreach (var clip in _clips)
            {
                var rate = _combinedSpeedPercentage / 100;
                if (clip.SourceUnit == ClipTimeUnits.Seconds)
                    rate *= 240f / (float)playbackBpm;

                clip.SourceRange.End = clip.SourceRange.Start + clip.TimeRange.Duration * rate;
            }
        }
        ImGui.PopID();
    }

    private enum Ranges
    {
        ClipRange,
        SourceRange,
    }

    private static void EditClipTimingRange(Ranges range, string label, TimeRange r)
    {
        ImGui.PushID((int)range);
        
        ImGui.TextUnformatted(label);
        ImGui.SameLine(LabelWidth * T3Ui.UiScaleFactor);
        
        var size = new Vector2(70, 0) * T3Ui.UiScaleFactor;
        
        // Start
        ImGui.PushID(0);
        if (SingleValueEdit.Draw(ref r.Start, size, min: 0, max: 999) != InputEditStateFlags.Nothing)
        {
            if (!float.IsNaN(r.Start))
            {
                foreach (var clip in _clips)
                {
                    switch (range)
                    {
                        case Ranges.ClipRange:
                            clip.TimeRange.Start = r.Start;
                            break;
                        case Ranges.SourceRange:
                            clip.SourceRange.Start = r.Start;
                            break;
                    }
                }
            }
        }
        ImGui.PopID();
        
        // Duration
        ImGui.SameLine();
        ImGui.PushID(1);
        var duration = r.Duration;
        if (SingleValueEdit.Draw(ref duration, size, min: 0, max: 999) != InputEditStateFlags.Nothing)
        {
            if (!float.IsNaN(duration))
            {
                foreach (var clip in _clips)
                {
                    switch (range)
                    {
                        case Ranges.ClipRange:
                            clip.TimeRange.Duration = duration;
                            break;
                        case Ranges.SourceRange:
                            clip.SourceRange.Duration = duration;
                            break;
                    }
                }
            }
        }
        ImGui.PopID();
        
        ImGui.SameLine();
        ImGui.PushID(2);
        if (SingleValueEdit.Draw(ref r.End, size, min: 0, max: 999) == InputEditStateFlags.Finished)
        {
            if (!float.IsNaN(r.End))
            {
                foreach (var clip in _clips)
                {
                    switch (range)
                    {
                        case Ranges.ClipRange:
                            clip.TimeRange.End = r.End;
                            break;
                        case Ranges.SourceRange:
                            clip.SourceRange.End = r.End;
                            break;
                    }
                }
            }
        }
        ImGui.PopID();
        ImGui.PopID();
    }

    /// <summary>Source values are in the clips' source unit — seconds for media clips, bars for the rest.</summary>
    private static string SourceRowLabel()
    {
        var anySeconds = false;
        var anyBars = false;
        foreach (var clip in _clips)
        {
            if (clip.SourceUnit == ClipTimeUnits.Seconds)
                anySeconds = true;
            else
                anyBars = true;
        }

        if (anySeconds && anyBars)
            return "Source (mixed)";

        return anySeconds ? "Source (secs)" : "Source (bars)";
    }

    private static void InitCombinedRanges()
    {
        var isFirst = true;
        var playbackBpm = Playback.Current?.Bpm ?? 120;

        foreach (var clip in _clips)
        {
            var speed = (float)clip.GetPlaybackSpeed(playbackBpm) * 100;
            if (isFirst)
            {
                _combinedTimeRange.Start = clip.TimeRange.Start;
                _combinedTimeRange.End = clip.TimeRange.End;
                _combinedSourceRange.Start = clip.SourceRange.Start;
                _combinedSourceRange.End = clip.SourceRange.End;
                _combinedSpeedPercentage = speed;
                isFirst = false;
            }
            else
            {
                if (Math.Abs(_combinedTimeRange.Start - clip.TimeRange.Start) > 0.001f)  _combinedTimeRange.Start = float.NaN;
                if (Math.Abs(_combinedTimeRange.End - clip.TimeRange.End) > 0.001f)  _combinedTimeRange.End = float.NaN;
                if (Math.Abs(_combinedSourceRange.Start - clip.SourceRange.Start) > 0.001f)  _combinedSourceRange.Start = float.NaN;
                if (Math.Abs(_combinedSourceRange.End - clip.SourceRange.End) > 0.001f)  _combinedSourceRange.End = float.NaN;
                
                if(Math.Abs(speed - _combinedSpeedPercentage) > 0.001) _combinedSpeedPercentage= float.NaN;
            }
        }
    }

    private const float LabelWidth = 100;
    private static TimeRange _combinedTimeRange;
    private static TimeRange _combinedSourceRange;
    private static float _combinedSpeedPercentage = 1;

    private static readonly List<TimeClip> _clips =[];

    internal static bool TimeClipEditorRequested;
    private const string TimeEditPopupId = nameof(TimeEditPopupId);
}