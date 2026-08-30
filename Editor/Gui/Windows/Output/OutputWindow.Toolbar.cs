#nullable enable

using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Video;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using SkillTraining = T3.Editor.Skills.Training.SkillTraining;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

internal sealed partial class OutputWindow
{
    /// <summary>Output-specific items appended to the breadcrumb menu. Display binding moved into the sidebar's
    /// OUTPUT rows (right-click → Bind to display); the panel toggle is now the toolbar's SidePanel icon.</summary>
    private void DrawOutputMenuExtras()
    {
        _setupMode.DrawSetupPanelMenuItem();
        _setupMode.DrawPinMenuItem();
    }

    private void DrawToolbar(Type? drawnType)
    {
        // Set cursor to top of the window
        ImGui.SetCursorPos(ImGui.GetCursorStartPos());

        // Calculate available width
        var availableWidth = ImGui.GetWindowSize().X;
        var toolbarHeight = ImGui.GetTextLineHeight() + 22;
        
        // Begin a horizontally scrollable child region
        ImGui.BeginChild("##toolbar_scroll", new Vector2(availableWidth, toolbarHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        // Keep filled backgrounds so the toolbar reads as a continuous bar.
        CustomComponents.PushToolbarIconBackground();

        _setupMode.DrawPanelToggleButton();
        _setupMode.DrawPinIndicator();
        Pinning.DrawPinning(_drawOutputMenuExtras);
        ImGui.SameLine();

        if (CustomComponents.StateButton("1:1",
                                         Math.Abs(_imageCanvas.Scale.X - 1f) < 0.001f
                                             ? CustomComponents.ButtonStates.Disabled
                                             : CustomComponents.ButtonStates.Emphasized))
        {
            _imageCanvas.SetScaleToMatchPixels();
            _imageCanvas.SetViewMode(ImageOutputCanvas.Modes.Pixel);
        }

        ImGui.SameLine();

        {
            if (CustomComponents.StateButton("Fit",
                                             _imageCanvas.ViewMode == ImageOutputCanvas.Modes.Fitted
                                                 ? CustomComponents.ButtonStates.Disabled
                                                 : CustomComponents.ButtonStates.Emphasized))
            {
                if (drawnType == typeof(Texture2D))
                {
                    _imageCanvas.SetViewMode(ImageOutputCanvas.Modes.Fitted);
                }
                else if (drawnType == typeof(Command))
                {
                    _camSelectionHandling.ResetView();
                }
            }

            var label = drawnType == typeof(Texture2D) ? "Fit image to view" : "Reset view or camera position";
            var shortCut = UserActions.FocusSelection.ListShortcuts();
            CustomComponents.TooltipForLastItem(label, shortCut);
        }

        // Show gizmos
        {
            ImGui.SameLine();

            var showGizmos = State.ShowGizmos != GizmoVisibility.Off;
            if (CustomComponents.ToggleIconButton(ref showGizmos, Icon.Grid, Vector2.One * ImGui.GetFrameHeight()))
            {
                State.ShowGizmos = showGizmos
                                       ? GizmoVisibility.On
                                       : GizmoVisibility.Off;
                FlagSymbolUiAsModified();
            }

            CustomComponents.TooltipForLastItem("Toggle gizmos and floor grid.",
                                                "Gizmos are available for selected transform operators and can be dragged to adjust their position.");
        }

        // Gizmo Transform mode
        if (State.ShowGizmos != GizmoVisibility.Off)
        {
            var size = Vector2.One * ImGui.GetFrameHeight(); // Calculate before pushing font

            var icon = State.TransformGizmoMode switch
                           {
                               TransformGizmoModes.None   => "" + (char)Icon.Hidden,
                               TransformGizmoModes.Select => "" + (char)Icon.Pipette,
                               TransformGizmoModes.Move   => "" + (char)Icon.Move,
                               TransformGizmoModes.Rotate => "" + (char)Icon.Rotate,
                               TransformGizmoModes.Scale  => "" + (char)Icon.Scale,
                               _                          => throw new ArgumentOutOfRangeException()
                           };

            ImGui.SameLine();
            ImGui.PushFont(Icons.IconFont);
            if (ImGui.Button(icon, size))
                ImGui.OpenPopup("_TransformGizmoSelection");

            ImGui.PopFont();

            if (ImGui.BeginPopup("_TransformGizmoSelection"))
            {
                if (CustomComponents.DrawMenuItem((int)Icon.Move, "Move", isChecked: State.TransformGizmoMode == TransformGizmoModes.Move))
                {
                    State.TransformGizmoMode = TransformGizmoModes.Move;
                    FlagSymbolUiAsModified();
                }

                if (CustomComponents.DrawMenuItem((int)Icon.Rotate, "Rotate",
                                                  isChecked: State.TransformGizmoMode == TransformGizmoModes.Rotate))
                {
                    State.TransformGizmoMode = TransformGizmoModes.Rotate;
                    FlagSymbolUiAsModified();
                }

                if (CustomComponents.DrawMenuItem((int)Icon.Scale, "Scale", isChecked: State.TransformGizmoMode == TransformGizmoModes.Scale))
                {
                    State.TransformGizmoMode = TransformGizmoModes.Scale;
                    FlagSymbolUiAsModified();
                }

                ImGui.EndPopup();
            }
        }

        ImGui.SameLine();

        _camSelectionHandling.DrawCameraControlSelection();

        // Camera speed
        {
            ImGui.SameLine();
            ImGui.PushID("CamSpeed");
            SingleValueEdit.Draw(ref UserSettings.Config.CameraSpeed, new Vector2(ImGui.GetFrameHeight() * 2, ImGui.GetFrameHeight()), min: 0.001f,
                                 max: 100,
                                 clampMin: true,
                                 clampMax: true,
                                 scale: 0.01f,
                                 format: "    {0:G3}");

            Icons.DrawIconOnLastItem(Icon.CameraSpeed,
                                     Math.Abs(UserSettings.Config.CameraSpeed - UserSettings.Defaults.CameraSpeed) < 0.001f
                                         ? UiColors.TextMuted
                                         : UiColors.Text, 0.1f);
            CustomComponents.TooltipForLastItem("Camera speed when flying with WASD keys.", "TIP: Use mouse wheel while flying to adjust on the fly.");
            ImGui.PopID();
        }

        // the background color button got me confused as it has no effect for Texture2D so I decided to only show it for Command
        if (drawnType == typeof(Command))
        {
            ImGui.SameLine();
            ColorEditButton.Draw(ref _backgroundColor, Vector2.Zero);
            CustomComponents.TooltipForLastItem("Adjust background color of view");
        }

        ImGui.SameLine();

        // Picking a resolution mutates _selectedResolution but nothing else flags it, so it was lost on restart.
        // The list holds singletons, so a reference change means a different one was chosen — persist the state.
        var resolutionBefore = _selectedResolution;
        ResolutionHandling.DrawSelector(ref _selectedResolution, _resolutionDialog);
        if (!ReferenceEquals(_selectedResolution, resolutionBefore))
            SaveStateToProject();

        // Screenshot and Render
        if (RenderProcess.State != RenderProcess.States.NoValidOutputType && RenderProcess.State != RenderProcess.States.NoOutputWindow)
        {
            //var texture = GetCurrentTexture();
            //if (drawnType == typeof(Texture2D) || drawnType == typeof(Command))
            //{
            ImGui.SameLine(0, 2);



            var screenshotState = !RenderProcess.IsExporting && RenderProcess.MainOutputType != null
                                      ? CustomComponents.ButtonStates.Emphasized
                                      : CustomComponents.ButtonStates.Disabled;

            if (CustomComponents.IconButton(Icon.Snapshot, Vector2.Zero, screenshotState))
            {
                RenderProcess.TryRenderScreenShot();
            }

            if(ImGui.IsAnyItemHovered())
                CustomComponents.TooltipForLastItem("Save screenshot",
                                                    UserActions.RenderScreenshot.ListKeyboardShortcutsForActionWithLabel());

            ImGui.SameLine();

            var renderAnimState = RenderProcess.IsExporting
                                      ? CustomComponents.ButtonStates.NeedsAttention
                                      : RenderProcess.MainOutputType != null
                                          ? CustomComponents.ButtonStates.Emphasized
                                          : CustomComponents.ButtonStates.Disabled;

            if (CustomComponents.IconButton(Icon.RenderAnimation, Vector2.Zero, renderAnimState))
            {
                if (RenderProcess.IsExporting)
                {
                    RenderProcess.Cancel();
                }
                else
                {
                    RenderProcess.TryStartVideoExport();
                }
            }

            if (ImGui.IsAnyItemHovered())
            {
                CustomComponents.TooltipForLastItem("Render Animation",
                                                    BuildRenderSummaryTooltip()
                                                    + UserActions.RenderAnimation.ListKeyboardShortcutsForActionWithLabel());
            }

            ImGui.SameLine();
            var renderSettingsOpen = WindowManager.IsAnyInstanceVisible<RenderWindow>();
            var renderSettingsState = renderSettingsOpen
                                          ? CustomComponents.ButtonStates.Activated
                                          : CustomComponents.ButtonStates.Emphasized;
            if (CustomComponents.IconButton(Icon.Settings2, Vector2.Zero, renderSettingsState))
            {
                WindowManager.ToggleInstanceVisibility<RenderWindow>();
            }

            if (ImGui.IsAnyItemHovered())
                CustomComponents.TooltipForLastItem("Toggle render settings",
                                                    "Open or close the \"Render To File\" window.");
        }

        CustomComponents.PopToolbarIconBackground();
        ImGui.EndChild();
    }

    // Render-icon tooltip: the range mode, then duration, resolution, format, and rough size / render-time estimates.
    private static string BuildRenderSummaryTooltip()
    {
        var s = RenderSettings.Current;
        if (!RenderProcess.TryGetRenderResolution(s, out var res))
            return string.Empty;

        var mode = s.TimeRange.ToString(); // Custom / Loop / Soundtrack / Continuous

        // Continuous has no fixed end, so a duration / size estimate would be meaningless.
        if (s.TimeRange == RenderSettings.TimeRanges.Continuous)
        {
            var clock = s.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime ? "realtime" : "deterministic";
            if (s.RenderMode == RenderSettings.RenderModes.Video)
            {
                var (cw, ch) = s.VideoCodec.RoundToEncoderBlock(res.Width, res.Height);
                return $"{mode} · {cw}×{ch} · {s.VideoCodec} · {clock} · {s.FrameRate:0} fps\n";
            }

            return $"{mode} · {res.Width}×{res.Height} · {s.FileFormat} sequence · {clock} · {s.FrameRate:0} fps\n";
        }

        var frames = RenderTiming.ComputeFrameCount(s);
        var dur = System.Math.Max(0, RenderTiming.ReferenceTimeToSeconds(s.EndInBars, s.TimeReference, s.FrameRate)
                                     - RenderTiming.ReferenceTimeToSeconds(s.StartInBars, s.TimeReference, s.FrameRate));

        if (s.RenderMode == RenderSettings.RenderModes.Video)
        {
            var (w, h) = s.VideoCodec.RoundToEncoderBlock(res.Width, res.Height);
            var bytes = RenderExportEstimate.EstimateBytes(s.VideoCodec, res, frames, dur, s.Bitrate);
            var renderSecs = RenderExportEstimate.EstimateSeconds(s.VideoCodec, res, frames, s.OverrideMotionBlurSamples);
            return $"{mode} · {dur / 60:0}:{dur % 60:00}s · {w}×{h} · {s.VideoCodec}\n"
                   + $"~{RenderExportEstimate.FormatBytes(bytes)} · {RenderExportEstimate.FormatDuration(renderSecs)} to render\n";
        }

        return $"{mode} · {dur / 60:0}:{dur % 60:00}s · {res.Width}×{res.Height} · {s.FileFormat} sequence ({frames} frames)\n";
    }

    private static void DrawRenderProgressBar()
    {
        if (!RenderProcess.IsExporting) return;
        var dl = ImGui.GetForegroundDrawList();
        var p = ImGui.GetWindowPos();
        var width = ImGui.GetWindowSize().X;
        var size = new Vector2(width, 2);
        dl.AddRectFilled(p, p + size, UiColors.BackgroundFull.Fade(0.4f));

        var progress = RenderProcess.Progress;
        if (progress < 0)
        {
            // Open-ended continuous capture: a sweeping segment instead of a determinate fill.
            const float segmentFraction = 0.3f;
            var segWidth = width * segmentFraction;
            var t = (float)((ImGui.GetTime() * 0.3) % 1.0);
            var segStart = p.X - segWidth + t * (width + segWidth);
            var segLeft = Math.Max(segStart, p.X);
            var segRight = Math.Min(segStart + segWidth, p.X + width);
            if (segRight > segLeft)
                dl.AddRectFilled(new Vector2(segLeft, p.Y), new Vector2(segRight, p.Y + size.Y), UiColors.StatusAttention);
            return;
        }

        dl.AddRectFilled(p, p + new Vector2(size.X * (float)progress, size.Y), UiColors.StatusAttention);
    }

    /// <summary>
    /// Update content with an <see cref="Core.Operator.EvaluationContext"/> and use the DrawImplementation for the given type to draw it. 
}
