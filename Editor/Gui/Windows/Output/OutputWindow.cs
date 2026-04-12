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
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
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
        SaveStateToProject();
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
        SyncStateWithProject();

        // Sync copy-based fields to State every frame so saves always capture current values
        SyncCopyFieldsToState();

        ImGui.BeginChild("##content",
                         new Vector2(0, ImGui.GetWindowHeight()),
                         ImGuiChildFlags.None,
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
            ImGui.SetCursorPos(ImGui.GetCursorStartPos() + new Vector2(0, 40));
            // ImGui 1.91 sets an internal IsSetPos flag on SetCursorPos and asserts in End()
            // if no item is submitted afterwards. The image canvas draws to the raw draw list
            // and does not emit items, so submit an empty Dummy as an extent marker.
            ImGui.Dummy(Vector2.Zero);

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
        ImGui.SetCursorPos(ImGui.GetCursorStartPos());

        // Calculate available width
        var availableWidth = ImGui.GetWindowSize().X;
        var toolbarHeight = ImGui.GetTextLineHeight() + 22;
        
        // Begin a horizontally scrollable child region
        ImGui.BeginChild("##toolbar_scroll", new Vector2(availableWidth, toolbarHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

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
                    RenderProcess.TryStartVideoExport();
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
        EvaluationContext.ShowGizmos = State.ShowGizmos;
        EvaluationContext.TransformGizmoMode = State.TransformGizmoMode;
        EvaluationContext.BypassCameras = _camSelectionHandling.BypassCamera;
        RequestedResolution = RenderProcess.TryGetActiveExportResolution(out var overrideResolution)
            ? overrideResolution
            : _selectedResolution.ComputeResolution();
        EvaluationContext.RequestedResolution = RequestedResolution;

        // Set camera
        if (_camSelectionHandling.CameraForRendering != null)
        {
            EvaluationContext.SetViewFromCamera(_camSelectionHandling.CameraForRendering);
        }

        EvaluationContext.BackgroundColor = _backgroundColor;

        const string overrideSampleVariableName = "OverrideMotionBlurSamples";

        if (RenderProcess.IsExporting)
        {
            var samples = RenderProcess.GetActiveOrRequestedSettings().OverrideMotionBlurSamples;
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

    #region State persistence

    /// <summary>
    /// Checks if the project root changed, and if so saves the current state
    /// to the previous project and loads the new project's state.
    /// Uses root instance (not current composition) because output window state is project-global.
    /// </summary>
    private void SyncStateWithProject()
    {
        var rootInstance = ProjectView.Focused?.RootInstance;
        var symbolUi = rootInstance?.Symbol.GetSymbolUi();
        var symbolId = symbolUi?.Symbol.Id ?? Guid.Empty;

        if (symbolId == _lastSyncedSymbolId)
            return;

        // Save current state to the PREVIOUS project before switching
        SaveStateToProject();

        _lastSyncedSymbolId = symbolId;
        _lastSyncedSymbolUi = symbolUi;

        if (symbolUi == null)
            return;

        // Load state for new project
        var instanceIndex = OutputWindowInstances.IndexOf(this);
        if (instanceIndex < 0)
            return;

        if (symbolUi.OutputWindowStates is { } states && instanceIndex < states.Count)
        {
            LoadStateFrom(states[instanceIndex]);
        }
    }

    internal void SaveStateToProject()
    {
        var symbolUi = _lastSyncedSymbolUi;
        if (symbolUi == null)
            return;

        var instanceIndex = OutputWindowInstances.IndexOf(this);
        if (instanceIndex < 0)
            return;

        symbolUi.OutputWindowStates ??= [];

        // Grow the list if needed
        while (symbolUi.OutputWindowStates.Count <= instanceIndex)
            symbolUi.OutputWindowStates.Add(new OutputWindowState());

        var state = symbolUi.OutputWindowStates[instanceIndex];
        SaveStateTo(state);
        symbolUi.FlagAsModified();
    }

    /// <summary>
    /// Returns the persisted state for this output window instance, creating it if needed.
    /// ShowGizmos and TransformGizmoMode are read/written directly from this object.
    /// </summary>
    internal OutputWindowState State
    {
        get
        {
            var symbolUi = _lastSyncedSymbolUi;
            if (symbolUi == null)
                return _fallbackState;

            var instanceIndex = OutputWindowInstances.IndexOf(this);
            if (instanceIndex < 0)
                return _fallbackState;

            symbolUi.OutputWindowStates ??= [];
            while (symbolUi.OutputWindowStates.Count <= instanceIndex)
                symbolUi.OutputWindowStates.Add(new OutputWindowState());

            return symbolUi.OutputWindowStates[instanceIndex];
        }
    }

    private readonly OutputWindowState _fallbackState = new();

    private void FlagSymbolUiAsModified()
    {
        _lastSyncedSymbolUi?.FlagAsModified();
    }

    /// <summary>
    /// Syncs fields that are still owned by OutputWindow components (not yet direct-backed by State)
    /// to the State object every frame. This ensures saves always capture current values.
    /// </summary>
    private void SyncCopyFieldsToState()
    {
        if (_lastSyncedSymbolUi == null)
            return;

        var state = State;
        if (state == _fallbackState)
            return;

        state.BackgroundColor = [_backgroundColor.X, _backgroundColor.Y, _backgroundColor.Z, _backgroundColor.W];
        state.ResolutionTitle = _selectedResolution.Title;
        state.ResolutionWidth = _selectedResolution.Size.Width;
        state.ResolutionHeight = _selectedResolution.Size.Height;
        state.ResolutionUseAsAspectRatio = _selectedResolution.UseAsAspectRatio;
        state.CameraSpeed = UserSettings.Config.CameraSpeed;

        _camSelectionHandling.SaveStateTo(state);
        Pinning.SaveStateTo(state);
    }

    private void SaveStateTo(OutputWindowState state)
    {
        // ShowGizmos and TransformGizmoMode are already on the state object (direct backing store)
        state.BackgroundColor = [_backgroundColor.X, _backgroundColor.Y, _backgroundColor.Z, _backgroundColor.W];
        state.CameraSpeed = UserSettings.Config.CameraSpeed;

        // Resolution
        state.ResolutionTitle = _selectedResolution.Title;
        state.ResolutionWidth = _selectedResolution.Size.Width;
        state.ResolutionHeight = _selectedResolution.Size.Height;
        state.ResolutionUseAsAspectRatio = _selectedResolution.UseAsAspectRatio;

        _camSelectionHandling.SaveStateTo(state);
        Pinning.SaveStateTo(state);
    }

    private void LoadStateFrom(OutputWindowState state)
    {
        // ShowGizmos and TransformGizmoMode are read directly from state — no copy needed

        if (state.BackgroundColor.Length == 4)
            _backgroundColor = new System.Numerics.Vector4(state.BackgroundColor[0], state.BackgroundColor[1], state.BackgroundColor[2], state.BackgroundColor[3]);

        UserSettings.Config.CameraSpeed = state.CameraSpeed;

        // Resolution — try to find by title first, fall back to size
        var resolution = ResolutionHandling.FindByTitle(state.ResolutionTitle)
                         ?? new ResolutionHandling.Resolution(
                             state.ResolutionTitle ?? "Custom",
                             state.ResolutionWidth,
                             state.ResolutionHeight,
                             state.ResolutionUseAsAspectRatio);
        _selectedResolution = resolution;

        _camSelectionHandling.LoadStateFrom(state);
        Pinning.LoadStateFrom(state);
    }

    private Guid _lastSyncedSymbolId;
    private SymbolUi? _lastSyncedSymbolUi;

    #endregion

    public static readonly List<Window> OutputWindowInstances = [];
    public ViewSelectionPinning Pinning { get; } = new();

    private System.Numerics.Vector4 _backgroundColor = new(0.1f, 0.1f, 0.1f, 1.0f);
    internal readonly EvaluationContext EvaluationContext = new();
    private readonly ImageOutputCanvas _imageCanvas = new();
    private readonly CameraSelectionHandling _camSelectionHandling;
    private static int _instanceCounter;
    private ResolutionHandling.Resolution _selectedResolution = ResolutionHandling.DefaultResolution;
    internal Int2 RequestedResolution { get; private set; }
    private readonly EditResolutionDialog _resolutionDialog = new();
}