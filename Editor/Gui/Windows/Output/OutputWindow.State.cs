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
        FlagSymbolUiAsModified();
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

    /// <summary>
    /// Read-only symbols keep view-state changes in memory but must not be flagged as modified —
    /// merely viewing them would prompt to save them as a copy.
    /// </summary>
    private void FlagSymbolUiAsModified()
    {
        if (_lastSyncedSymbolUi is { ReadOnly: false })
        {
            _lastSyncedSymbolUi.FlagAsModified();
        }
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
}
