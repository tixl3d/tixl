#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Windows.Output;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The output-editing side of an output window. There is no explicit Operator/Setup mode — the window
/// follows focus: a focused <see cref="IOutputSink"/> (SendToOutput) op shows its output's editing
/// canvas, and a picked panel entity shows that entity. Selecting any op in the graph drops a panel
/// edit (graph selection wins). Owns the Flow Outliner; the entity selection is the one instance shared
/// by all output windows (<see cref="OutputSetupHandling.EntitySelection"/>) — what stays per window
/// is the <b>pin</b>: a pinned window keeps showing its target while the selection roams elsewhere.
/// One per OutputWindow.
/// </summary>
internal sealed class OutputSetupModeView
{
    /// <summary>
    /// The height the outliner strip takes at the bottom of the window (splitter included), so the canvas
    /// child above it can be sized to what remains. Zero while hidden.
    /// </summary>
    public float OutlinerReservedHeight
    {
        get
        {
            if (!_showOutliner)
                return 0;

            var scale = T3Ui.UiScaleFactor;
            var header = ImGui.GetFrameHeight() + 6 * scale;
            return SplitterThickness * scale + (_outlinerCollapsed ? header : _outlinerHeight * scale);
        }
    }

    /// <summary>Draws the Flow Outliner strip: a full-width up/down splitter on its top edge, then the header
    /// and columns. Call after the canvas child, which leaves <see cref="OutlinerReservedHeight"/> free.</summary>
    public void DrawOutliner()
    {
        if (!_showOutliner)
            return;

        var scale = T3Ui.UiScaleFactor;
        DrawOutlinerSplitter(scale);

        // Match the settings windows' content-section background so the light hover/selection fills read.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundPopup.Rgba);
        ImGui.BeginChild("##flowOutliner", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        _toggleOutlinerCollapse ??= () => _outlinerCollapsed = !_outlinerCollapsed;
        _outliner.Draw(_entitySelection, _toggleOutlinerCollapse, !_outlinerCollapsed);
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    /// <summary>The drag handle on the strip's top edge. Height is per-window session state (not persisted).</summary>
    private void DrawOutlinerSplitter(float scale)
    {
        var thickness = SplitterThickness * scale;
        var p = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.InvisibleButton("##outlinerSplitter", new Vector2(width, thickness));

        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        if ((active || hovered) && !_outlinerCollapsed)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);

        // Dragging the edge up grows the strip.
        if (active && !_outlinerCollapsed)
            _outlinerHeight = Math.Clamp(_outlinerHeight - ImGui.GetIO().MouseDelta.Y / scale, MinOutlinerHeight, MaxOutlinerHeight);

        var color = active ? UiColors.StatusActivated : (hovered ? UiColors.BackgroundHover : UiColors.BackgroundFull);
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(width, thickness), color);
    }

    /// <summary>
    /// Draws an output-editing view if one applies to the current focus, and returns true; returns false
    /// when the caller should draw the operator output instead. A picked panel entity takes precedence
    /// over a focused send op; both are dropped when the focused op changes (graph selection wins).
    /// </summary>
    public bool TryDrawEditingView(Instance? focusedInstance, EvaluationContext context)
    {
        // What's *drawn* here may be pinned, so it doesn't follow the graph. Which CONTENT row is selected
        // should follow the graph selection regardless — picking a send in the graph is how you get to it.
        // Only a graph pick counts (it owns the inspection); an entity pick clears the graph too, and that
        // change must not bounce back into the entity selection.
        var selectedInGraph = ProjectView.Focused?.NodeSelection.GetSelectedInstanceWithoutComposition();
        var focusedId = selectedInGraph?.SymbolChildId ?? Guid.Empty;
        var graphOwnsInspection = GlobalSelectionHandling.InspectionTarget == GlobalSelectionHandling.InspectionTargets.GraphNode;
        if (graphOwnsInspection && (focusedId != _lastFocusedId || !_graphOwnedInspection))
        {
            // The graph pick already cleared the entity selection; a focused SendToOutput mirrors back as its
            // CONTENT row so the canvas opens on its slices. With several output windows each runs this
            // transition on the same frame — the writes are identical, so the repetition is harmless.
            if (selectedInGraph is IOutputSink)
                _entitySelection.Mirror(SetupEntitySelection.EntityKind.ContentSource, focusedId);

            // The outliner follows the OE-editing context: a focused SendToOutput opens it (its surfaces/outputs are
            // at hand); selecting any other op — or clicking the graph background — closes it. Only on the
            // transition, so it can still be toggled manually while the focus stays put.
            _showOutliner = selectedInGraph is IOutputSink;
        }

        _lastFocusedId = focusedId;
        _graphOwnedInspection = graphOwnsInspection;

        if (TryGetShownEntity(out var entityKind, out var entityId) && OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
        {
            // Selecting never leaves the Board: the output-bound kinds draw through the tabbed view (which keeps
            // the Board while that tab is active), the others enter their own canvas only by double-click.
            var showsBoard = _outputView.ShowsBoard;
            var openedImageId = _outputView.OpenedReferenceImageId;
            if (openedImageId != Guid.Empty && IsInReferenceSpace(setup, openedImageId, entityKind, entityId))
                // The image's space stays open while the selection moves between it and the surfaces traced on it.
                _outputView.DrawReferenceCanvas(openedImageId, _entitySelection);
            else if (entityKind == SetupEntitySelection.EntityKind.Output)
                // Pass the selection so a surface label on the canvas can still be clicked to select it, even
                // though the shown entity is the output itself (no surface focused).
                _outputView.Draw(entityId, selection: _entitySelection);
            else if (entityKind == SetupEntitySelection.EntityKind.Surface && SetupRelations.TryGetSurfaceOutput(setup, entityId, out var surfaceOutputId))
                _outputView.Draw(surfaceOutputId, entityId, _entitySelection); // labels on the canvas can re-pick
            else if (entityKind == SetupEntitySelection.EntityKind.Patch && SetupRelations.TryGetPatchOutput(setup, entityId, out var patchOutputId))
                _outputView.Draw(patchOutputId, selection: _entitySelection); // a patch lives on its output's canvas
            else if (entityKind == SetupEntitySelection.EntityKind.ContentSource && !showsBoard)
                // Slices live on the source, so entering content opens it with every slice laid out on it.
                _outputView.DrawSourceCanvas(entityId, _entitySelection);
            else if (entityKind == SetupEntitySelection.EntityKind.Slice && !showsBoard && SetupRelations.TryGetSliceSource(setup, entityId, out var sliceChildId))
                _outputView.DrawSourceCanvas(sliceChildId, _entitySelection, entityId);
            else if (entityKind == SetupEntitySelection.EntityKind.Surface)
                _outputView.DrawBoardStandalone(_entitySelection, entityId); // unmapped: the Board, or Straight on its photo
            else
                _outputView.DrawBoardStandalone(_entitySelection);
        }
        else
        {
            if (focusedInstance is not IOutputSink
                || !OutputSetupHandling.TryGetActiveSetup(out var activeSetup, out _)
                || !SetupRelations.TryGetSendOutput(activeSetup, focusedInstance.SymbolChildId, out var sendOutputId))
            {
                // Nothing focuses a space: in the setup context (outliner shown) the Board is the home view;
                // otherwise the window shows the operator output as always.
                if (!_showOutliner)
                    return false;

                _outputView.DrawBoardStandalone(_entitySelection);
                return true;
            }

            // Pass the selection here too: with nothing selected in the setup panel (e.g. after ctrl-clicking the
            // selected output away) this is the path that draws the canvas, and a frame label still has to be
            // clickable to select its surface.
            _outputView.Draw(sendOutputId, selection: _entitySelection);
        }

        return true;
    }

    /// <summary>
    /// Debug-protocol entry: selects the named entity (any kind, by its display name) and/or enters an edit
    /// mode (Board, Content, Straight, Output, Calibrate) — what the outliner click and the header tab do.
    /// </summary>
    public bool TryDrive(string? entityName, string? mode, out string error)
    {
        error = string.Empty;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
        {
            error = "No active setup";
            return false;
        }

        _showOutliner = true;
        if (!string.IsNullOrEmpty(entityName))
        {
            if (!TryFindEntityByName(setup, entityName, out var kind, out var id))
            {
                error = $"No setup entity named '{entityName}'";
                return false;
            }

            _entitySelection.Select(kind, id);
        }

        if (!string.IsNullOrEmpty(mode) && !_outputView.TrySetEditMode(mode))
        {
            error = $"Unknown mode '{mode}'";
            return false;
        }

        return true;
    }

    private static bool TryFindEntityByName(Setup setup, string name, out SetupEntitySelection.EntityKind kind, out Guid id)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.Name == name)
            {
                kind = SetupEntitySelection.EntityKind.Surface;
                id = surface.Id;
                return true;
            }
        }

        foreach (var output in setup.Outputs)
        {
            if (output.Name == name)
            {
                kind = SetupEntitySelection.EntityKind.Output;
                id = output.Id;
                return true;
            }
        }

        foreach (var image in setup.ReferenceImages)
        {
            if (image.Name == name)
            {
                kind = SetupEntitySelection.EntityKind.ReferenceImage;
                id = image.Id;
                return true;
            }
        }

        foreach (var source in setup.ContentSources)
        {
            if (source.Name == name)
            {
                kind = SetupEntitySelection.EntityKind.ContentSource;
                id = source.SymbolChildId;
                return true;
            }
        }

        foreach (var slice in setup.Slices)
        {
            if (SetupActions.SliceLabel(setup, slice) == name)
            {
                kind = SetupEntitySelection.EntityKind.Slice;
                id = slice.Id;
                return true;
            }
        }

        kind = SetupEntitySelection.EntityKind.None;
        id = Guid.Empty;
        return false;
    }

    /// <summary>The "Show Flow Outliner" toggle — hung inside the output window's breadcrumb menu.</summary>
    public void DrawOutlinerMenuItem()
    {
        if (CustomComponents.DrawMenuItem(1, "Show Flow Outliner", isChecked: _showOutliner))
            _showOutliner = !_showOutliner;
    }

    /// <summary>The outliner toggle icon for the output toolbar. Only drawn while the strip is hidden — the
    /// open strip's own header carries the collapse icon, and showing the control twice reads as two
    /// different buttons.</summary>
    public void DrawOutlinerToggleButton()
    {
        if (_showOutliner)
            return;

        if (CustomComponents.IconButton(Icon.ViewList, Vector2.Zero))
            _showOutliner = true;

        ImGui.SameLine();
    }

    private static bool IsInReferenceSpace(Setup setup, Guid imageId, SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (kind == SetupEntitySelection.EntityKind.ReferenceImage)
            return id == imageId;

        return kind == SetupEntitySelection.EntityKind.Surface && setup.FindSurface(id)?.Reference?.ImageId == imageId;
    }

    private bool TryGetShownEntity(out SetupEntitySelection.EntityKind kind, out Guid id)
    {
        kind = SetupEntitySelection.EntityKind.None;
        id = Guid.Empty;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return false;

        // A pinned window ignores the shared selection for what it *shows* (highlights still follow).
        // A pin whose entity is gone silently reverts to following — same pruning rule the selection uses.
        if (_pinnedKind != SetupEntitySelection.EntityKind.None)
        {
            if (SetupEntitySelection.Exists(setup, _pinnedKind, _pinnedId))
            {
                kind = _pinnedKind;
                id = _pinnedId;
                return true;
            }

            ClearPin();
        }

        return _entitySelection.TryResolve(setup, out kind, out id);
    }

    /// <summary>Pin menu entry for the breadcrumb: pins the currently shown entity, or releases the pin.</summary>
    public void DrawPinMenuItem()
    {
        if (_pinnedKind != SetupEntitySelection.EntityKind.None)
        {
            if (CustomComponents.DrawMenuItem(_pinViewMenuId, $"Unpin view ({PinnedEntityName()})", isChecked: true))
                ClearPin();

            return;
        }

        if (!TryGetShownEntity(out var kind, out var id) || kind == SetupEntitySelection.EntityKind.None)
        {
            CustomComponents.DrawMenuItem(_pinViewMenuId, "Pin view", isEnabled: false);
            return;
        }

        if (CustomComponents.DrawMenuItem(_pinViewMenuId, $"Pin view to {SetupActions.NameForEntity(kind, id)}"))
        {
            _pinnedKind = kind;
            _pinnedId = id;
        }
    }

    /// <summary>Toolbar indicator, drawn only while pinned — the visible reminder that this window
    /// won't follow the selection. Clicking releases the pin.</summary>
    public void DrawPinIndicator()
    {
        if (_pinnedKind == SetupEntitySelection.EntityKind.None)
            return;

        if (CustomComponents.IconButton(Icon.Pin, Vector2.Zero, CustomComponents.ButtonStates.Activated))
            ClearPin();

        if (ImGui.IsItemHovered())
            CustomComponents.TooltipForLastItem($"Pinned to {PinnedEntityName()}", "This window keeps showing it while the selection moves. Click to unpin.");

        ImGui.SameLine();
    }

    public void SaveStateTo(OutputWindowState state)
    {
        state.PinnedEntityKind = _pinnedKind;
        state.PinnedEntityId = _pinnedId;
    }

    public void LoadStateFrom(OutputWindowState state)
    {
        // Restored blindly; the per-frame Exists check reverts a pin whose entity is gone.
        _pinnedKind = state.PinnedEntityKind;
        _pinnedId = state.PinnedEntityId;
    }

    private void ClearPin()
    {
        _pinnedKind = SetupEntitySelection.EntityKind.None;
        _pinnedId = Guid.Empty;
    }

    private string PinnedEntityName()
    {
        return SetupActions.NameForEntity(_pinnedKind, _pinnedId);
    }

    private bool _showOutliner;
    private bool _outlinerCollapsed;
    private float _outlinerHeight = DefaultOutlinerHeight; // unscaled px; scaled at draw time
    private Action? _toggleOutlinerCollapse; // cached so the strip draw doesn't allocate a closure each frame
    private Guid _lastFocusedId;
    private bool _graphOwnedInspection;

    // The per-window pin: None = follow the shared selection (persisted via OutputWindowState).
    private SetupEntitySelection.EntityKind _pinnedKind;
    private Guid _pinnedId;
    private static readonly int _pinViewMenuId = nameof(_pinViewMenuId).GetHashCode();

    private const float DefaultOutlinerHeight = 220;
    private const float MinOutlinerHeight = 90;
    private const float MaxOutlinerHeight = 600;
    private const float SplitterThickness = 4;
    private readonly SetupEntitySelection _entitySelection = OutputSetupHandling.EntitySelection;
    public OutputSetupModeView()
    {
        // One EntityItem per window: the outliner rows and the canvas menus share its rename/menu state,
        // and separate windows can't bleed into each other.
        _outliner = new SetupFlowOutliner(_entityItem);
        _outputView = new SetupOutputView(_entityItem);
    }

    private readonly EntityItem _entityItem = new();
    private readonly SetupFlowOutliner _outliner;
    private readonly SetupOutputView _outputView;
}
