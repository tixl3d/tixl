#nullable enable

using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes.Vector;
using T3.Core.IO;
using T3.IoServices;
using T3.Core.Logging;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// The transport / record toolbar that sits at the bottom of the graph window above the
/// timeline. Owns the timeline-fold toggle, the play / scrub controls, the
/// image-background tool cluster, and the record toggle.
/// </summary>
internal static class TimelineToolbar
{
    public static void Draw(ProjectView components)
    {
        TimeControls.HandleTimeControlActions();

        // Extend the active recording's clip ranges before drawing so the visual reflects
        // the latest TimeRange.End even on frames where playback isn't advancing.
        RecordingSession.OnFrame();

        if (!UserSettings.Config.ShowToolbar)
            return;

        ImGui.SetCursorPos(new Vector2(1,
                                       ImGui.GetWindowSize().Y - TimeControls.ControlSize.Y - 1));
        // ImGui 1.91 sets an internal IsSetPos flag on SetCursorPos and asserts in End()
        // if no item is submitted afterwards. Dummy(0,0) submits an empty item which both
        // clears the flag and validates the extent without visually moving anything.
        ImGui.Dummy(Vector2.Zero);
        ImGui.BeginChild("TimeControls", Vector2.Zero, ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        // Transport buttons keep filled backgrounds so the toolbar reads as a continuous bar.
        CustomComponents.PushToolbarIconBackground();
        {
            var icon = components.TimeLineCanvas.FoldingHeight.UsingCustomTimelineHeight
                           ? Icon.ChevronDown
                           : Icon.ChevronUp;
            if (CustomComponents.IconButton(icon, TimeControls.ControlSize, CustomComponents.ButtonStates.Default))
            {
                components.TimeLineCanvas.FoldingHeight.Toggle();
                UserSettings.Config.ShowTimeline = true;
            }

            ImGui.SameLine();

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            TimeControls.DrawTimeControls(components.TimeLineCanvas, components.CompositionInstance);
            ImGui.PopStyleVar();
            // Use SameLine spacing instead of SetCursorPosX. SetCursorPosX flags the window
            // as having a manual cursor move, and ImGui 1.91 then asserts at EndChild if the
            // next code path does not submit an item.
            ImGui.SameLine(0, 10);

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5));
            components.GraphImageBackground.DrawToolbarItems();

            DrawRecordButton(components);
            ImGui.PopStyleVar();
        }
        CustomComponents.PopToolbarIconBackground();
        ImGui.EndChild();
    }

    /// <summary>
    /// Toggle button that starts / stops a paired audio + IO recording session via
    /// <see cref="RecordingSession"/>. One click captures both audio and IO data, and on
    /// stop drops the recordings onto the active composition's timeline.
    /// </summary>
    /// <remarks>
    /// Visual: <see cref="Icon.Record"/> glyph via <see cref="CustomComponents.IconButton"/>
    /// to match the look of the surrounding transport buttons. Uses
    /// <see cref="CustomComponents.ButtonStates.NeedsAttention"/> (red) while a session is
    /// active, <c>Disabled</c> when the sync mode can't host a clip, <c>Normal</c> at rest.
    /// </remarks>
    private static void DrawRecordButton(ProjectView components)
    {
        ImGui.SameLine(0, 6 * T3Ui.UiScaleFactor);

        var isRecording = RecordingSession.IsActive;
        var playback = Playback.Current;
        var inTimelineMode = playback?.Settings?.Playback is { UsesBeatTapping: false };

        // Recording places clips on the timeline at specific TimeRange positions; beat
        // tapping has no coherent playhead position to anchor them to, so the action is
        // disabled there. Don't disable mid-session — let the user stop a session even if
        // they've toggled away from Timeline since.
        var enabled = inTimelineMode || isRecording;

        var state = isRecording
                        ? CustomComponents.ButtonStates.NeedsAttention
                        : enabled
                            ? CustomComponents.ButtonStates.Emphasized
                            : CustomComponents.ButtonStates.Disabled;

        var clicked = CustomComponents.IconButton(Icon.Record, TimeControls.ControlSize, state);

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            if (!enabled)
            {
                ImGui.TextUnformatted("Recording needs a timeline playhead.");
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextUnformatted("In Composition Settings, set Project Setup to Animation\nor Sync Mode to Timeline.");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.TextUnformatted(isRecording
                                          ? "Stop recording (audio + IO data)"
                                          : "Start recording (audio + IO data)");
                if (isRecording)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    if (!string.IsNullOrEmpty(WasapiAudioInput.ActiveRecordingPath))
                        ImGui.TextUnformatted("audio → " + WasapiAudioInput.ActiveRecordingPath);
                    if (!string.IsNullOrEmpty(IoDataSetRecorder.ActiveRecordingPath))
                        ImGui.TextUnformatted("data  → " + IoDataSetRecorder.ActiveRecordingPath);
                    ImGui.PopStyleColor();
                }
            }
            ImGui.EndTooltip();
        }

        if (!clicked || !enabled)
            return;

        if (isRecording)
        {
            RecordingSession.Stop();
            return;
        }

        var composition = components.CompositionInstance;
        if (composition == null)
        {
            Log.Warning("Cannot start recording: no composition is active.");
            return;
        }

        // Auto-start playback so the playhead advances during recording. Without this the
        // clip's TimeRange still grows in wall-clock seconds, but the user won't see the
        // playhead progressing through it — confusing visual feedback.
        if (playback != null)
            playback.PlaybackSpeed = 1;

        RecordingSession.Start(composition);
    }
}
