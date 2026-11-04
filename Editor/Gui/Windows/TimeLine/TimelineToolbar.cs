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
using T3.Core.Audio.Input;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Timing;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// The timeline's transport row: playback controls, audio mute, keyframe pinning and the record toggle.
/// Shown as an overlay at the bottom of the graph window while the timeline is attached, or as the
/// top row of the <see cref="TimelineWindow"/> while it is detached.
/// </summary>
internal static class TimelineToolbar
{
    /// <summary>Draws the row at the bottom of the graph window, including the timeline-fold toggle.</summary>
    public static void DrawAsGraphOverlay(ProjectView projectView)
    {
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
            var icon = projectView.TimeLineCanvas.FoldingHeight.UsingCustomTimelineHeight
                           ? Icon.ChevronDown
                           : Icon.ChevronUp;
            if (CustomComponents.IconButton(icon, TimeControls.ControlSize, CustomComponents.ButtonStates.Default))
            {
                projectView.TimeLineCanvas.FoldingHeight.Toggle();
                UserSettings.Config.ShowTimeline = true;
            }

            ImGui.SameLine();

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            TimeControls.DrawTimeControls(projectView.TimeLineCanvas, projectView.CompositionInstance);
            ImGui.PopStyleVar();

            DrawRecordButton(projectView);
        }
        CustomComponents.PopToolbarIconBackground();
        ImGui.EndChild();
    }

    /// <summary>
    /// Draws the header strip of the <see cref="TimelineWindow"/> at the cursor: idle motion, IO indicator and
    /// keyframe pinning on the left, time, transport and record centered, the keyframe view mode and mute on the right.
    /// </summary>
    public static void DrawAsWindowHeader(ProjectView projectView, Instance composition)
    {
        var playback = Playback.Current;
        PlaybackUtils.FindCompositionSettingsForInstance(composition, out _, out var settings);

        var min = ImGui.GetCursorScreenPos().Floor();
        var width = MathF.Floor(ImGui.GetContentRegionAvail().X);
        var max = min + new Vector2(width, MathF.Round(TimeControls.ControlSize.Y));

        // Same shade as the timeline ruler below, which draws the style's child background.
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.ChildBg));
        drawList.AddRectFilled(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y + 1), UiColors.BackgroundFull.Fade(0.5f));

        // Unlike the bar under the graph, the strip itself is the background: buttons only shade on hover.
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        {
            ImGui.SetCursorScreenPos(min);
            ImGui.BeginGroup();
            TimeControls.DrawIdleMotionToggle(playback);
            ImGui.SameLine();
            TimeControls.DrawIoIndicator();
            if (FrameStats.Last.HasAnimatedParameters)
            {
                ImGui.SameLine();
                TimeControls.DrawPinToggle();
            }

            ImGui.EndGroup();
            var leftGroupEnd = ImGui.GetItemRectMax().X;

            // Centered and right-aligned groups use the widths measured last frame.
            var groupGap = GroupGap;
            var centerX = MathF.Max(min.X + MathF.Floor((width - _lastCenterGroupWidth) / 2), leftGroupEnd + groupGap);
            ImGui.SetCursorScreenPos(new Vector2(centerX, min.Y));
            ImGui.BeginGroup();
            TimeControls.DrawCurrentTime(playback, settings);
            ImGui.SameLine();
            if (settings.Playback.UsesBeatTapping)
            {
                TimeControls.DrawBeatTapping(composition);
            }
            else
            {
                TimeControls.DrawTransport(playback);
            }

            DrawRecordButton(projectView);
            ImGui.EndGroup();
            _lastCenterGroupWidth = ImGui.GetItemRectSize().X;
            var centerGroupEnd = ImGui.GetItemRectMax().X;

            var rightX = MathF.Max(max.X - _lastRightGroupWidth, centerGroupEnd + groupGap);
            ImGui.SetCursorScreenPos(new Vector2(rightX, min.Y));
            ImGui.BeginGroup();
            if (!settings.Playback.UsesBeatTapping)
            {
                TimeControls.DrawModeToggle(projectView.TimeLineCanvas);
                ImGui.SameLine();
            }

            TimeControls.DrawMuteToggle();
            ImGui.EndGroup();
            _lastRightGroupWidth = ImGui.GetItemRectSize().X;
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y + 1));
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
                    if (!string.IsNullOrEmpty(AudioInput.ActiveRecordingPath))
                        ImGui.TextUnformatted("audio → " + AudioInput.ActiveRecordingPath);
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

    private static float GroupGap => 12 * T3Ui.UiScaleFactor;
    private static float _lastCenterGroupWidth;
    private static float _lastRightGroupWidth;
}
