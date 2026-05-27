#nullable enable

using ImGuiNET;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// The transport / record toolbar that sits at the bottom of the graph window above the
/// timeline. Owns the timeline-fold toggle, the play / scrub controls, the image-background
/// tool cluster, and (live-session recording feature, see
/// <c>.agentic/Plans/Plan_LiveSessionRecording.md</c> Phase 4) the record toggle.
/// </summary>
/// <remarks>
/// Extracted from <c>UiElements.DrawProjectControlToolbar</c> so the recording UX has a
/// natural home alongside the rest of the timeline window code. Behaviour-preserving move
/// of the original method plus the new record button.
/// </remarks>
internal static class TimelineToolbar
{
    public static void Draw(ProjectView components)
    {
        TimeControls.HandleTimeControlActions();
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
        {
            var icon = components.TimeLineCanvas.FoldingHeight.UsingCustomTimelineHeight
                           ? Icon.ChevronDown
                           : Icon.ChevronUp;
            if (CustomComponents.IconButton(icon, TimeControls.ControlSize, CustomComponents.ButtonStates.Dimmed))
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

            DrawRecordButton();
            ImGui.PopStyleVar();
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// Toggle button that starts / stops a paired audio + IO recording session. Phase 4b
    /// behaviour: one click drives <see cref="WasapiAudioInput"/> and
    /// <see cref="IoDataSetRecorder"/> in lockstep — same lifetime, same destination
    /// directory, separate files per the shared session-index scheme in
    /// <see cref="RecordingPaths"/>. The settings popup (Phase 4c) will let the user pick
    /// audio source / IO sources; for now the button is "record both with defaults".
    /// </summary>
    /// <remarks>
    /// Visual: a filled red circle while recording (pulsing via a local <see cref="ImGui.GetTime"/>
    /// sine), a hollow outline at rest. This is a draw-list placeholder until a proper
    /// "record" glyph is added to <see cref="Icon"/> — the atlas doesn't ship one today.
    /// </remarks>
    private static void DrawRecordButton()
    {
        ImGui.SameLine(0, 6 * T3Ui.UiScaleFactor);

        var isRecording = WasapiAudioInput.IsRecording || IoDataSetRecorder.IsRecording;
        var size = TimeControls.ControlSize;

        var clicked = ImGui.InvisibleButton("##RecordToggle", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var center = (min + max) * 0.5f;
        var radius = MathF.Min(size.X, size.Y) * 0.32f;

        var drawList = ImGui.GetWindowDrawList();
        var hovered = ImGui.IsItemHovered();

        if (isRecording)
        {
            // Pulse via sine. Local computation; if more toolbar items need pulse later,
            // promote to a shared helper.
            var pulse = MathF.Sin((float)ImGui.GetTime() * 4f) * 0.25f + 0.75f;
            var color = UiColors.StatusAttention.Fade(pulse);
            drawList.AddCircleFilled(center, radius, color);
        }
        else
        {
            var color = hovered ? UiColors.StatusAttention : UiColors.TextMuted;
            drawList.AddCircle(center, radius, color, 0, 2 * T3Ui.UiScaleFactor);
        }

        if (hovered)
        {
            ImGui.BeginTooltip();
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
            ImGui.EndTooltip();
        }

        if (clicked)
        {
            if (isRecording)
            {
                WasapiAudioInput.EndRecording();
                IoDataSetRecorder.EndRecording();
            }
            else
            {
                // Best-effort paired start. Either side failing logs a warning via the
                // recorder helpers themselves; the other side still runs so the user gets
                // a partial session rather than nothing.
                WasapiAudioInput.BeginRecording();
                IoDataSetRecorder.BeginRecording();
            }
        }
    }
}
