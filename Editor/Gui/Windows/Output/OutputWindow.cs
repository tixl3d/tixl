using ImGuiNET;
using System.IO;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
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

    public static IEnumerable<OutputWindow> GetVisibleInstances()
    {
        foreach (var i in OutputWindowInstances)
        {
            if (!(i is OutputWindow outputWindow))
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

    public static OutputWindow GetPrimaryOutputWindow()
    {
        return GetVisibleInstances().FirstOrDefault();
    }

    public Texture2D GetCurrentTexture()
    {
        return _imageCanvas?.LastTexture;
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
                         ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollWithMouse);
        {
            // Very ugly hack to prevent scaling the output above window size
            var keepScale = T3Ui.UiScaleFactor;

            // Draw output
            _imageCanvas.SetAsCurrent();

            // Move down to avoid overlapping with toolbar
            ImGui.SetCursorPos(ImGui.GetWindowContentRegionMin() + new Vector2(0, 40)); // this line as no effect?

            var okay = Pinning.TryGetPinnedOrSelectedInstance(out var drawnInstance, out var graphCanvas);

            if (graphCanvas != null)
            {
                Pinning.TryGetPinnedEvaluationInstance(graphCanvas?.Structure, out var evaluationInstance);

                var drawnType = UpdateAndDrawOutput(drawnInstance, evaluationInstance);
                ImageOutputCanvas.Deactivate();
                _camSelectionHandling.Update(drawnInstance, drawnType);
                var editingFlags = _camSelectionHandling.PreventCameraInteraction | _camSelectionHandling.PreventImageCanvasInteraction |
                                   drawnType != typeof(Texture2D)
                                       ? T3Ui.EditingFlags.PreventMouseInteractions
                                       : T3Ui.EditingFlags.None;

                if ((editingFlags & T3Ui.EditingFlags.PreventMouseInteractions) != 0)
                    T3Ui.UiScaleFactor = 1;

                _imageCanvas.Update(editingFlags);

                T3Ui.UiScaleFactor = keepScale;

                if (KeyActionHandling.Triggered(UserActions.FocusSelection))
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

                DrawToolbar(drawnType);
            }

            CustomComponents.DrawWindowFocusFrame();
        }
        ImGui.EndChild();
    }

    private void DrawToolbar(Type drawnType)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, UiColors.BackgroundHover.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundHover.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.3f, 0.3f, 0.3f, 0.1f));

        // Set cursor to top of window
        ImGui.SetCursorPos(ImGui.GetWindowContentRegionMin());

        // Calculate available width
        var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;

        // Begin a horizontally scrollable child region
        ImGui.BeginChild("##toolbar_scroll", new Vector2(availableWidth, 40), false, ImGuiWindowFlags.HorizontalScrollbar);

        {
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
                var shortCut = KeyActionHandling.ListShortcuts(UserActions.FocusSelection);
                CustomComponents.TooltipForLastItem(label, shortCut);
            }

            // Show gizmos
            {
                ImGui.SameLine();
               
                var showGizmos = _evaluationContext.ShowGizmos != GizmoVisibility.Off;
                if (CustomComponents.ToggleIconButton(Icon.Grid, "##gizmos", ref showGizmos, Vector2.One * ImGui.GetFrameHeight()))
                {
                    _evaluationContext.ShowGizmos = showGizmos
                                                        ? GizmoVisibility.On
                                                        : GizmoVisibility.Off;
                }

                CustomComponents.TooltipForLastItem("Toggle gizmos and floor grid.",
                                                    "Gizmos are available for selected transform operators and can be dragged to adjust their position.");
            }

            // Gizmo Transform mode
            if (_evaluationContext.ShowGizmos != GizmoVisibility.Off)
            {
                var size = Vector2.One * ImGui.GetFrameHeight(); // Calculate before pushing font

                var icon = _evaluationContext.TransformGizmoMode switch
                {
                    TransformGizmoModes.None => "" + (char)Icon.Hidden,
                    TransformGizmoModes.Select => "" + (char)Icon.Pipette,
                    TransformGizmoModes.Move => "" + (char)Icon.Move,
                    TransformGizmoModes.Rotate => "" + (char)Icon.Rotate,
                    TransformGizmoModes.Scale => "" + (char)Icon.Scale,
                    _ => throw new ArgumentOutOfRangeException()
                };

                ImGui.SameLine();
                ImGui.PushFont(Icons.IconFont);
                if (ImGui.Button(icon, size))
                    ImGui.OpenPopup("_TransformGizmoSelection");

                ImGui.PopFont();

                if (ImGui.BeginPopup("_TransformGizmoSelection"))
                {
                    if (ImGui.MenuItem("Move", null, _evaluationContext.TransformGizmoMode == TransformGizmoModes.Move))
                    {
                        _evaluationContext.TransformGizmoMode = TransformGizmoModes.Move;
                    }

                    if (ImGui.MenuItem("Rotate", null, _evaluationContext.TransformGizmoMode == TransformGizmoModes.Rotate))
                    {
                        _evaluationContext.TransformGizmoMode = TransformGizmoModes.Rotate;
                    }

                    if (ImGui.MenuItem("Scale", null, _evaluationContext.TransformGizmoMode == TransformGizmoModes.Scale))
                    {
                        _evaluationContext.TransformGizmoMode = TransformGizmoModes.Scale;
                    }

                    ImGui.EndPopup();
                }
            }

            ImGui.SameLine();

            _camSelectionHandling.DrawCameraControlSelection();

            ResolutionHandling.DrawSelector(ref _selectedResolution, _resolutionDialog);

            ImGui.SameLine();
            // the background color button got me confused as it has no effect for Texture2D so I decided to only show it for Command
            if (drawnType == typeof(Command))
            {
                ColorEditButton.Draw(ref _backgroundColor, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()));
                CustomComponents.TooltipForLastItem("Adjust background color of view");
            }
            
        }

        ImGui.PopStyleColor(6);

        var texture = GetCurrentTexture();
        if (drawnType == typeof(Texture2D) || drawnType == typeof(Command))
        {
            ImGui.SameLine();

            var project = ProjectView.Focused?.OpenedProject;
            var projectFolder = project.Package.Folder;
            var folder = Path.Combine(projectFolder, "Screenshots");

            if (CustomComponents.IconButton(Icon.Snapshot, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight())))
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var filename = Path.Join(folder, $"{DateTime.Now:yyyy_MM_dd-HH_mm_ss_fff}.png");
                ScreenshotWriter.StartSavingToFile(texture, filename, ScreenshotWriter.FileFormats.Png);
                Log.Assert("Screenshot saved in: " + folder);
            }

            CustomComponents.TooltipForLastItem("Save screenshot");
        }

        ImGui.SameLine();
        ImGui.PushID("CamSpeed");
        var result = SingleValueEdit.Draw(ref UserSettings.Config.CameraSpeed, new Vector2(ImGui.GetFrameHeight() * 2, ImGui.GetFrameHeight()), min: 0.001f, max: 100,
                                          clampMin: true,
                                          clampMax: true,
                                          scale: 0.01f,
                                          format: "{0:G3}");
        CustomComponents.TooltipForLastItem("Camera speed when flying with WASD keys.", "TIP: Use mouse wheel while flying to adjust on the fly.");
        ImGui.PopID();

        ImGui.EndChild();
    }

    /// <summary>
    /// Update content with an <see cref="EvaluationContext"/> and use the DrawImplementation for the given type to draw it. 
    /// </summary>
    private Type UpdateAndDrawOutput(Instance instanceForOutput, Instance instanceForEvaluation = null)
    {
        if (instanceForEvaluation == null)
            instanceForEvaluation = instanceForOutput;

        if (instanceForEvaluation == null || instanceForEvaluation.Outputs.Count <= 0)
            return null;

        var evaluatedSymbolUi = instanceForEvaluation.GetSymbolUi();
        var evalOutput = Pinning.GetPinnedOrDefaultOutput(instanceForEvaluation.Outputs);

        if (evalOutput == null || !evaluatedSymbolUi.OutputUis.TryGetValue(evalOutput.Id, out IOutputUi evaluatedOutputUi))
            return null;

        if (_imageCanvas.ViewMode != ImageOutputCanvas.Modes.Fitted
            && evaluatedOutputUi is CommandOutputUi)
        {
            _imageCanvas.SetViewMode(ImageOutputCanvas.Modes.Fitted);
        }

        // Prepare context
        _evaluationContext.Reset();
        _evaluationContext.BypassCameras = _camSelectionHandling.BypassCamera;
        _evaluationContext.RequestedResolution = _selectedResolution.ComputeResolution();

        // Set camera
        if (_camSelectionHandling.CameraForRendering != null)
        {
            _evaluationContext.SetViewFromCamera(_camSelectionHandling.CameraForRendering);
        }

        _evaluationContext.BackgroundColor = _backgroundColor;

        const string overrideSampleVariableName = "OverrideMotionBlurSamples";
        if (BaseRenderWindow.IsToollRenderingSomething)
        {
            var samples = BaseRenderWindow.OverrideMotionBlurSamples;
            if (samples >= 0)
            {
                _evaluationContext.IntVariables[overrideSampleVariableName] = samples;
            }
        }
        else
        {
            _evaluationContext.IntVariables.Remove(overrideSampleVariableName);
        }

        // Ugly hack to hide final target
        if (instanceForOutput != instanceForEvaluation)
        {
            ImGui.BeginChild("hidden", Vector2.One);
            {
                evaluatedOutputUi.DrawValue(evalOutput, _evaluationContext, Config.Title);
            }
            ImGui.EndChild();

            if (instanceForOutput == null || instanceForOutput.Outputs.Count == 0)
                return null;

            var viewOutput = Pinning.GetPinnedOrDefaultOutput(instanceForOutput.Outputs);

            var viewSymbolUi = instanceForOutput.GetSymbolUi();
            if (viewOutput == null || !viewSymbolUi.OutputUis.TryGetValue(viewOutput.Id, out IOutputUi viewOutputUi))
                return null;

            // Render!
            viewOutputUi.DrawValue(viewOutput, _evaluationContext, Config.Title, recompute: false);
            return viewOutputUi.Type;
        }
        else
        {
            // Render!
            evaluatedOutputUi.DrawValue(evalOutput, _evaluationContext, Config.Title);
            return evalOutput.ValueType;
        }
    }

    public Instance ShownInstance
    {
        get
        {
            Pinning.TryGetPinnedOrSelectedInstance(out var instance, out _);
            return instance;
        }
    }

    public static readonly List<Window> OutputWindowInstances = new();
    public ViewSelectionPinning Pinning { get; } = new();

    private System.Numerics.Vector4 _backgroundColor = new(0.1f, 0.1f, 0.1f, 1.0f);
    private readonly EvaluationContext _evaluationContext = new();
    private readonly ImageOutputCanvas _imageCanvas = new();
    private readonly CameraSelectionHandling _camSelectionHandling;
    private static int _instanceCounter;
    private ResolutionHandling.Resolution _selectedResolution = ResolutionHandling.DefaultResolution;
    private readonly EditResolutionDialog _resolutionDialog = new();
}