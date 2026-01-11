#nullable enable

using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.Skills;
using T3.Editor.UiModel;
using SkillTraining = T3.Editor.Skills.Training.SkillTraining;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

internal sealed class OutputWindow : Window
{
    #region Window implementation
    public OutputWindow()
    {
        Config.Title = LayoutHandling.OutputPrefix + _instanceCounter;
        Config.Visible = true;

        AllowMultipleInstances = true;
        Config.Visible = true;
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        _instanceCounter++;
        _camSelectionHandling = new CameraSelectionHandling();
        OutputWindowInstances.Add(this);
    }

    private static IEnumerable<OutputWindow> GetVisibleInstances()
    {
        foreach (var i in OutputWindowInstances)
        {
            if (i is not OutputWindow outputWindow)
                continue;

            if (!i.Config.Visible)
                continue;

            yield return outputWindow;
        }
    }

    // protected override void DrawAllInstances()
    // {
    //     // Convert to array to enable removing of members during iteration
    //     foreach (var w in OutputWindowInstances.ToArray())
    //     {
    //         w.DrawOneInstance();
    //     }
    // }

    public static bool TryGetPrimaryOutputWindow([NotNullWhen(true)] out OutputWindow? outputWindow)
    {
        foreach (var window in OutputWindowInstances)
        {
            if (!window.Config.Visible)
                continue;

            if (window is not OutputWindow outputWindow2)
                 continue;

            outputWindow = outputWindow2;
            return true;

        }

        outputWindow = null;
        return false;
    }

    public Texture2D? GetCurrentTexture()
    {
        return _imageCanvas.LastTexture;
    }

    protected override void Close()
    {
        OutputWindowInstances.Remove(this);
    }

    protected override void AddAnotherInstance()
    {
        // ReSharper disable once ObjectCreationAsStatement
        new OutputWindow();
    }

    internal override List<Window> GetInstances()
    {
        return OutputWindowInstances;
    }
    #endregion

    protected override void DrawContent()
    {
        ImGui.BeginChild("##content",
                         new Vector2(0, ImGui.GetWindowHeight()),
                         false,
                         ImGuiWindowFlags.NoScrollbar |
                         ImGuiWindowFlags.NoMove |
                         ImGuiWindowFlags.NoScrollWithMouse |
                         ImGuiWindowFlags.NoBackground
                        );
        {
            // Very ugly hack to prevent scaling the output above window size
            var keepScale = T3Ui.UiScaleFactor;

            // Draw output
            _imageCanvas.SetAsCurrent();

            // Move down to avoid overlapping with the toolbar
            ImGui.SetCursorPos(ImGui.GetWindowContentRegionMin() + new Vector2(0, 40)); // this line as no effect?

            Pinning.TryGetPinnedOrSelectedInstance(out var drawnInstance, out var graphCanvas);

            if (graphCanvas != null)
            {
                Pinning.TryGetPinnedEvaluationInstance(graphCanvas.Structure, out var evaluationInstance);

                var drawnType = UpdateAndDrawOutput(drawnInstance, evaluationInstance);
                ImageOutputCanvas.Deactivate();
                _camSelectionHandling.Update(drawnInstance, drawnType);
                var editingFlags = _camSelectionHandling.PreventCameraInteraction 
                                   | _camSelectionHandling.PreventImageCanvasInteraction
                                   | SkillTraining.IsInPlayMode
                                   | drawnType != typeof(Texture2D)
                                       ? T3Ui.EditingFlags.PreventMouseInteractions
                                       : T3Ui.EditingFlags.None;

                if ((editingFlags & T3Ui.EditingFlags.PreventMouseInteractions) != 0)
                    T3Ui.UiScaleFactor = 1;
                
                _imageCanvas.Update(editingFlags);

                T3Ui.UiScaleFactor = keepScale;

                if (UserActions.FocusSelection.Triggered())
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

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.BackgroundHover.Rgba);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundHover.Rgba);
                ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.3f, 0.3f, 0.3f, 0.1f));

                if (!SkillTraining.IsInPlayMode)
                {
                    DrawToolbar(drawnType);
                    DrawRenderProgressBar();
                }
                
                ImGui.PopStyleColor(6);
            }

            CustomComponents.DrawWindowFocusFrame();
        }
        ImGui.EndChild();
    }

    private void DrawToolbar(Type? drawnType)
    {
        // Set cursor to top of the window
        ImGui.SetCursorPos(ImGui.GetWindowContentRegionMin());

        // Calculate available width
        var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
        var toolbarHeight = ImGui.GetTextLineHeight() + 22;
        // Begin a horizontally scrollable child region
        ImGui.BeginChild("##toolbar_scroll", new Vector2(availableWidth, toolbarHeight), false, ImGuiWindowFlags.HorizontalScrollbar);

        Pinning.DrawPinning();

        if (CustomComponents.StateButton("1:1",
                                         Math.Abs(_imageCanvas.Scale.X - 1f) < 0.001f
                                             ? CustomComponents.ButtonStates.Disabled
                                             : CustomComponents.ButtonStates.Normal))
        {
            _imageCanvas.SetScaleToMatchPixels();
            _imageCanvas.SetViewMode(ImageOutputCanvas.Modes.Pixel);
        }

        ImGui.SameLine();

        {
            if (CustomComponents.StateButton("Fit",
                                             _imageCanvas.ViewMode == ImageOutputCanvas.Modes.Fitted
                                                 ? CustomComponents.ButtonStates.Disabled
                                                 : CustomComponents.ButtonStates.Normal))
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

            var showGizmos = EvaluationContext.ShowGizmos != GizmoVisibility.Off;
            if (CustomComponents.ToggleIconButton(ref showGizmos, Icon.Grid, Vector2.One * ImGui.GetFrameHeight()))
            {
                EvaluationContext.ShowGizmos = showGizmos
                                                    ? GizmoVisibility.On
                                                    : GizmoVisibility.Off;
            }

            CustomComponents.TooltipForLastItem("Toggle gizmos and floor grid.",
                                                "Gizmos are available for selected transform operators and can be dragged to adjust their position.");
        }

        // Gizmo Transform mode
        if (EvaluationContext.ShowGizmos != GizmoVisibility.Off)
        {
            var size = Vector2.One * ImGui.GetFrameHeight(); // Calculate before pushing font

            var icon = EvaluationContext.TransformGizmoMode switch
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
                if (CustomComponents.DrawMenuItem((int)Icon.Move, "Move", isChecked: EvaluationContext.TransformGizmoMode == TransformGizmoModes.Move))
                {
                    EvaluationContext.TransformGizmoMode = TransformGizmoModes.Move;
                }

                if (CustomComponents.DrawMenuItem((int)Icon.Rotate, "Rotate",
                                                  isChecked: EvaluationContext.TransformGizmoMode == TransformGizmoModes.Rotate))
                {
                    EvaluationContext.TransformGizmoMode = TransformGizmoModes.Rotate;
                }

                if (CustomComponents.DrawMenuItem((int)Icon.Scale, "Scale", isChecked: EvaluationContext.TransformGizmoMode == TransformGizmoModes.Scale))
                {
                    EvaluationContext.TransformGizmoMode = TransformGizmoModes.Scale;
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

        ResolutionHandling.DrawSelector(ref _selectedResolution, _resolutionDialog);

        // Screenshot and Render
        if (RenderProcess.State != RenderProcess.States.NoValidOutputType && RenderProcess.State != RenderProcess.States.NoOutputWindow)
        {
            //var texture = GetCurrentTexture();
            //if (drawnType == typeof(Texture2D) || drawnType == typeof(Command))
            //{
            ImGui.SameLine(0, 2);



            var screenshotState = !RenderProcess.IsExporting && RenderProcess.MainOutputType != null
                                      ? CustomComponents.ButtonStates.Normal
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
                                          ? CustomComponents.ButtonStates.Normal
                                          : CustomComponents.ButtonStates.Disabled;

            if (CustomComponents.IconButton(Icon.RenderAnimation, Vector2.Zero, renderAnimState))
            {
                if (RenderProcess.IsExporting)
                {
                    RenderProcess.Cancel();
                }
                else
                {
                    RenderProcess.TryStart(RenderSettings.Current);
                }
            }

            if(ImGui.IsAnyItemHovered())
                CustomComponents.TooltipForLastItem("Render Animation", UserActions.RenderAnimation.ListKeyboardShortcutsForActionWithLabel());

            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.Settings2, Vector2.Zero))
            {
                WindowManager.ToggleInstanceVisibility<RenderWindow>();
            }
        }

        ImGui.EndChild();
    }

    private static void DrawRenderProgressBar()
    {
        if (!RenderProcess.IsExporting) return;
        var dl = ImGui.GetForegroundDrawList();
        var p = ImGui.GetWindowPos();
        var size = new Vector2(ImGui.GetWindowSize().X, 2);
        dl.AddRectFilled(p, p + size, UiColors.BackgroundFull.Fade(0.4f));
        dl.AddRectFilled(p, p + new Vector2(size.X * (float)RenderProcess.Progress, size.Y), UiColors.StatusAttention);
    }

    /// <summary>
    /// Update content with an <see cref="Core.Operator.EvaluationContext"/> and use the DrawImplementation for the given type to draw it. 
    /// </summary>
    private Type? UpdateAndDrawOutput(Instance? instanceForOutput, Instance?instanceForEvaluation = null)
    {
        instanceForEvaluation ??= instanceForOutput;

        if (instanceForEvaluation == null || instanceForEvaluation.Outputs.Count <= 0)
            return null;

        var evaluatedSymbolUi = instanceForEvaluation.GetSymbolUi();
        var evalOutput = Pinning.GetPinnedOrDefaultOutput(instanceForEvaluation.Outputs);

        if (evalOutput == null || !evaluatedSymbolUi.OutputUis.TryGetValue(evalOutput.Id, out var evaluatedOutputUi))
            return null;

        if (_imageCanvas.ViewMode != ImageOutputCanvas.Modes.Fitted
            && evaluatedOutputUi is CommandOutputUi)
        {
            _imageCanvas.SetViewMode(ImageOutputCanvas.Modes.Fitted);
        }

        // Prepare context
        EvaluationContext.Reset();
        EvaluationContext.BypassCameras = _camSelectionHandling.BypassCamera;
        var requestedResolution = _selectedResolution.ComputeResolution();
        _lastLayoutResolution = requestedResolution;

        if (RenderProcess.IsExporting)
        {
            requestedResolution = RenderProcess.MainOutputRenderedSize;
        }
        EvaluationContext.RequestedResolution = requestedResolution;

        // Set camera
        if (_camSelectionHandling.CameraForRendering != null)
        {
            EvaluationContext.SetViewFromCamera(_camSelectionHandling.CameraForRendering);
        }

        EvaluationContext.BackgroundColor = _backgroundColor;

        const string overrideSampleVariableName = "OverrideMotionBlurSamples";

        if (RenderProcess.IsToollRenderingSomething)
        {
            // FIXME: Implement
            var samples = RenderSettings.Current.OverrideMotionBlurSamples;
            if (samples >= 0)
            {
                EvaluationContext.IntVariables[overrideSampleVariableName] = samples;
            }
        }
        else
        {
            EvaluationContext.IntVariables.Remove(overrideSampleVariableName);
        }

        // Ugly hack to hide final target
        if (instanceForOutput != instanceForEvaluation)
        {
            ImGui.BeginChild("hidden", Vector2.One);
            {
                evaluatedOutputUi.DrawValue(evalOutput, EvaluationContext, Config.Title);
            }
            ImGui.EndChild();

            if (instanceForOutput == null || instanceForOutput.Outputs.Count == 0)
                return null;

            var viewOutput = Pinning.GetPinnedOrDefaultOutput(instanceForOutput.Outputs);

            var viewSymbolUi = instanceForOutput.GetSymbolUi();
            if (viewOutput == null || !viewSymbolUi.OutputUis.TryGetValue(viewOutput.Id, out var viewOutputUi))
                return null;

            // Render!
            viewOutputUi.DrawValue(viewOutput, EvaluationContext, Config.Title, recompute: false);
            return viewOutputUi.Type;
        }

        // Render!
        evaluatedOutputUi.DrawValue(evalOutput, EvaluationContext, Config.Title);
        return evalOutput.ValueType;
    }

    public Instance? ShownInstance
    {
        get
        {
            Pinning.TryGetPinnedOrSelectedInstance(out var instance, out _);
            return instance;
        }
    }
    public Int2 GetResolution()
    {
        return _lastLayoutResolution.Width > 0 && _lastLayoutResolution.Height > 0
                   ? _lastLayoutResolution
                   : _selectedResolution.ComputeResolution();
    }

    public static readonly List<Window> OutputWindowInstances = [];
    public ViewSelectionPinning Pinning { get; } = new();

    private System.Numerics.Vector4 _backgroundColor = new(0.1f, 0.1f, 0.1f, 1.0f);
    internal readonly EvaluationContext EvaluationContext = new();
    private readonly ImageOutputCanvas _imageCanvas = new();
    private readonly CameraSelectionHandling _camSelectionHandling;
    private static int _instanceCounter;
    private ResolutionHandling.Resolution _selectedResolution = ResolutionHandling.DefaultResolution;
    private readonly EditResolutionDialog _resolutionDialog = new();
    private Int2 _lastLayoutResolution;
}