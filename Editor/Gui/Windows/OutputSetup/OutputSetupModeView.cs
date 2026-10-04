#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Windows.Output;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The output-editing side of an output window. It has two modes: <b>Operator</b> shows the operator output
/// as always, <b>Setup</b> shows the Flow Outliner strip (which then hosts the toolbar) and makes the Board the
/// home view. The mode follows focus — a focused <see cref="IContentSupplier"/> (SendToOutput) op enters Setup,
/// any other op leaves it — and can be toggled by hand. Within either mode a picked entity shows its own
/// canvas. The entity selection is the one instance shared by all output windows
/// (<see cref="GlobalSelectionHandling.SetupEntities"/>) — what stays per window is the <b>pin</b>: a pinned
/// window keeps showing its target while the selection roams elsewhere. One per OutputWindow.
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
            if (!IsSetupMode)
                return 0;

            var scale = T3Ui.UiScaleFactor;
            var header = ImGui.GetFrameHeight() + 6 * scale;
            return SplitterThickness * scale + (_outlinerCollapsed ? header : _outlinerHeight * scale);
        }
    }

    /// <summary>Whether the strip is shown — and with it hosts the canvas' toolbar, so the window's own goes.</summary>
    public bool IsHeaderHostedByStrip => IsSetupMode;

    /// <summary>Draws the Flow Outliner strip: a full-width up/down splitter on its top edge, then the header
    /// and columns. Call after the canvas child, which leaves <see cref="OutlinerReservedHeight"/> free.</summary>
    public void DrawOutliner()
    {
        if (!IsSetupMode)
            return;

        var scale = T3Ui.UiScaleFactor;
        DrawOutlinerSplitter(scale);

        // Match the settings windows' content-section background so the light hover/selection fills read.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundPopup.Rgba);
        ImGui.BeginChild("##flowOutliner", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        _toggleOutlinerCollapse ??= () => _outlinerCollapsed = !_outlinerCollapsed;
        _drawGrip ??= DrawOutlinerGrip;
        _drawHostedHeader ??= _outputView.DrawHostedHeader;
        _drawStripMenuExtras ??= DrawStripMenuExtras;
        _leaveSetup ??= () => _viewMode = ViewModes.Operator;
        _outliner.Draw(_entitySelection, _toggleOutlinerCollapse, !_outlinerCollapsed, _drawGrip, _drawHostedHeader, _drawStripMenuExtras, _leaveSetup);
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Draws an output-editing view if one applies to the current focus, and returns true; returns false
    /// when the caller should draw the operator output instead. A picked entity takes precedence over a
    /// focused send op; both are dropped when the focused op changes (graph selection wins). A window pinned
    /// to an op (<paramref name="opPinned"/>) keeps showing it: neither the graph focus nor the shared entity
    /// selection pulls it into the setup.
    /// </summary>
    public bool TryDrawEditingView(Instance? focusedInstance, EvaluationContext context, bool opPinned = false)
    {
        _opPinned = opPinned;
        FollowGraphFocus();
        _outputView.IsHeaderHostedByStrip = IsSetupMode;

        var shown = ResolveShownView(focusedInstance);
        return Draw(shown);
    }

    /// <summary>
    /// Debug-protocol entry: selects the named entity (any kind, by its display name) and/or enters an edit
    /// mode (Board, Straight, Output) — what the outliner click and the header tab do.
    /// </summary>
    public bool TryDrive(string? entityName, string? mode, out string error)
    {
        error = string.Empty;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
        {
            error = "No active setup";
            return false;
        }

        _viewMode = ViewModes.Setup;
        if (!string.IsNullOrEmpty(entityName))
        {
            if (!SetupEntities.TryFindByName(setup, entityName, out var kind, out var id))
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

    /// <summary>The "Show Flow Outliner" toggle — hung inside the output window's breadcrumb menu.</summary>
    public void DrawOutlinerMenuItem()
    {
        if (CustomComponents.DrawMenuItem(1, "Show Flow Outliner", isChecked: IsSetupMode))
            _viewMode = IsSetupMode ? ViewModes.Operator : ViewModes.Setup;
    }

    /// <summary>The "Output Setup" button for the output toolbar. Only drawn while the strip is hidden — the
    /// open strip's own header carries the way back, and showing the control twice reads as two different
    /// buttons.</summary>
    public void DrawSetupButton()
    {
        if (IsSetupMode)
            return;

        if (CustomComponents.StateButton("Output Setup", CustomComponents.ButtonStates.Default))
            EnterSetupMode();

        CustomComponents.TooltipForLastItem(DrawSetupButtonTooltip);
    }

    /// <summary>Static method group, so the per-frame hover check doesn't allocate a closure.</summary>
    private static void DrawSetupButtonTooltip()
    {
        CustomComponents.StylizedText("Edit the output setup", Fonts.FontNormal, UiColors.Text);
        CustomComponents.StylizedText("Surfaces, outputs and what is routed where, with the Flow Outliner under the canvas.",
                                      Fonts.FontNormal, UiColors.Text.Fade(0.7f));
        FormInputs.AddVerticalSpace(5);
        OutputsIndicator.DrawActiveOutputsSummary();
    }

    /// <summary>Shows the setup view with its Flow Outliner, whatever the graph selection is.</summary>
    public void EnterSetupMode()
    {
        _viewMode = ViewModes.Setup;
    }

    /// <summary>Pin menu entry for the breadcrumb: pins the currently shown entity, or releases the pin.</summary>
    public void DrawPinMenuItem()
    {
        if (_pinnedKind != SetupEntityKinds.None)
        {
            if (CustomComponents.DrawMenuItem(_pinViewMenuId, $"Unpin view ({PinnedEntityName()})", isChecked: true))
                ClearPin();

            return;
        }

        if (!TryGetShownEntity(out var kind, out var id) || kind == SetupEntityKinds.None)
        {
            CustomComponents.DrawMenuItem(_pinViewMenuId, "Pin view", isEnabled: false);
            return;
        }

        if (CustomComponents.DrawMenuItem(_pinViewMenuId, $"Pin view to {SetupLabels.NameForEntity(kind, id)}"))
        {
            _pinnedKind = kind;
            _pinnedId = id;
        }
    }

    /// <summary>Toolbar indicator, drawn only while pinned — the visible reminder that this window
    /// won't follow the selection. Clicking releases the pin.</summary>
    public void DrawPinIndicator()
    {
        if (_pinnedKind == SetupEntityKinds.None)
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

    public OutputSetupModeView()
    {
        _outliner = new SetupFlowOutliner();
        _outputView = new SetupOutputView();
    }

    private enum ViewModes
    {
        Operator,
        Setup,
    }

    private enum ShownViews
    {
        /// <summary>Not ours: the caller draws the operator output.</summary>
        OperatorOutput,
        ReferenceCanvas,
        OutputCanvas,
        Board,
    }

    /// <summary>What this frame shows, resolved before anything is drawn.</summary>
    /// <param name="OutputId">The output whose canvas is shown (<see cref="ShownViews.OutputCanvas"/>).</param>
    /// <param name="EntityId">The surface given the exclusive affordances on that canvas or the Board; empty for none.</param>
    /// <param name="ImageId">The reference image whose space is open (<see cref="ShownViews.ReferenceCanvas"/>).</param>
    private readonly record struct ShownView(ShownViews Kind, Guid OutputId = default, Guid EntityId = default, Guid ImageId = default);

    private bool IsSetupMode => _viewMode == ViewModes.Setup;

    /** Whether the host window is pinned to an op this frame, so focus and selection leave its view alone. */
    private bool _opPinned;
    private Action? _leaveSetup;

    /// <summary>
    /// Mirrors a graph pick into the entity selection and lets the mode follow focus. What's <i>drawn</i> may be
    /// pinned, so it doesn't follow the graph; which CONTENT item is selected should follow the graph selection
    /// regardless — picking a send in the graph is how you get to it. Only a graph pick counts (it owns the
    /// inspection); an entity pick clears the graph too, and that change must not bounce back.
    /// </summary>
    private void FollowGraphFocus()
    {
        var selectedInGraph = ProjectView.Focused?.NodeSelection.GetSelectedInstanceWithoutComposition();
        var focusedId = selectedInGraph?.SymbolChildId ?? Guid.Empty;
        var graphOwnsInspection = GlobalSelectionHandling.InspectionTarget == GlobalSelectionHandling.InspectionTargets.GraphNode;
        var focusTransition = focusedId != _lastFocusedId || !_graphOwnedInspection;
        if (graphOwnsInspection && selectedInGraph is IContentSupplier
            && (focusTransition || !_entitySelection.IsSelected(SetupEntityKinds.ContentSource, focusedId)))
        {
            // The graph pick already cleared the entity selection; a focused SendToOutput mirrors back as its
            // CONTENT item so the canvas opens on its slices. Not only on the transition: re-picking the same
            // send (a second click on its item reveals it in the graph again) clears the item the same way and
            // must land back on it. With several output windows each runs this on the same frame — the writes
            // are identical, so the repetition is harmless.
            _entitySelection.Mirror(SetupEntityKinds.ContentSource, focusedId);
        }

        if (graphOwnsInspection && focusTransition && !_opPinned)
        {
            // A focused SendToOutput enters Setup (its surfaces/outputs are at hand); selecting any other op —
            // or clicking the graph background — leaves it. Only on the transition, so the mode can still be
            // toggled by hand while the focus stays put. A window pinned to an op is looking at that op.
            _viewMode = selectedInGraph is IContentSupplier ? ViewModes.Setup : ViewModes.Operator;
        }

        _lastFocusedId = focusedId;
        _graphOwnedInspection = graphOwnsInspection;
    }

    /// <summary>Pin → resolved selection → focused send op's output → the mode's home view. Pure: reads state, draws nothing.</summary>
    private ShownView ResolveShownView(Instance? focusedInstance)
    {
        if (TryGetShownEntity(out var entityKind, out var entityId) && OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
        {
            // Selecting never leaves the Board: the output-bound kinds draw through the tabbed view (which keeps
            // the Board while that tab is active), the others enter their own canvas only by double-click.
            var openedImageId = _outputView.OpenedReferenceImageId;
            if (openedImageId != Guid.Empty && IsInReferenceSpace(setup, openedImageId, entityKind, entityId))
            {
                // The image's space stays open while the selection moves between it and the surfaces traced on it.
                return new ShownView(ShownViews.ReferenceCanvas, ImageId: openedImageId);
            }
            else if (entityKind == SetupEntityKinds.Output)
            {
                return new ShownView(ShownViews.OutputCanvas, OutputId: entityId);
            }
            else if (entityKind == SetupEntityKinds.Surface && SetupRelations.TryGetSurfaceOutput(setup, entityId, out var surfaceOutputId))
            {
                return new ShownView(ShownViews.OutputCanvas, OutputId: surfaceOutputId, EntityId: entityId);
            }
            else if (entityKind == SetupEntityKinds.Patch && SetupRelations.TryGetPatchOutput(setup, entityId, out var patchOutputId))
            {
                // A patch lives on its output's canvas.
                return new ShownView(ShownViews.OutputCanvas, OutputId: patchOutputId);
            }
            else if (entityKind == SetupEntityKinds.Surface)
            {
                // Unmapped: the Board, or Straight on its photo.
                return new ShownView(ShownViews.Board, EntityId: entityId);
            }
            else
            {
                return new ShownView(ShownViews.Board);
            }
        }

        if (focusedInstance is IContentSupplier
            && OutputSetupHandling.TryGetActiveSetup(out var activeSetup, out _)
            && SetupRelations.TryGetSendOutput(activeSetup, focusedInstance.SymbolChildId, out var sendOutputId))
        {
            return new ShownView(ShownViews.OutputCanvas, OutputId: sendOutputId);
        }

        // Nothing focuses a space: Setup's home view is the Board; Operator shows the operator output as always.
        return IsSetupMode ? new ShownView(ShownViews.Board) : new ShownView(ShownViews.OperatorOutput);
    }

    /// <summary>Draws the resolved view; false when the caller draws the operator output instead. The selection
    /// is passed to every canvas so a label on it can still be clicked to (re-)pick its entity.</summary>
    private bool Draw(ShownView shown)
    {
        switch (shown.Kind)
        {
            case ShownViews.ReferenceCanvas:
                _outputView.DrawReferenceCanvas(shown.ImageId, _entitySelection);
                return true;

            case ShownViews.OutputCanvas:
                _outputView.Draw(shown.OutputId, shown.EntityId, _entitySelection);
                return true;

            case ShownViews.Board:
                _outputView.DrawBoardStandalone(_entitySelection, shown.EntityId);
                return true;

            default:
                return false;
        }
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

        var color = active || _gripActive ? UiColors.StatusActivated : (hovered || _gripHovered ? UiColors.BackgroundHover : UiColors.BackgroundFull);
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(width, thickness), color);
    }

    /// <summary>
    /// The grip at the strip header's left: the same drag as the splitter edge, but a target you can find — and
    /// it lights the edge while hovered, so the two read as one control.
    /// </summary>
    private void DrawOutlinerGrip()
    {
        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetFrameHeight();
        var width = 12 * scale;
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##outlinerGrip", new Vector2(width, height));
        _gripActive = ImGui.IsItemActive() && !_outlinerCollapsed;
        _gripHovered = ImGui.IsItemHovered() && !_outlinerCollapsed;
        if (_gripActive || _gripHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);

        if (_gripActive)
            _outlinerHeight = Math.Clamp(_outlinerHeight - ImGui.GetIO().MouseDelta.Y / scale, MinOutlinerHeight, MaxOutlinerHeight);

        // Two columns of three dots, centred in the control.
        var dl = ImGui.GetWindowDrawList();
        var color = (_gripActive ? UiColors.StatusActivated : _gripHovered ? UiColors.Text : UiColors.TextMuted).Fade(0.8f);
        var centre = p + new Vector2(width, height) * 0.5f;
        for (var column = -1; column <= 1; column += 2)
        {
            for (var row = -1; row <= 1; row++)
                dl.AddCircleFilled(centre + new Vector2(column * 2.5f, row * 4f) * scale, 1.2f * scale, color);
        }
    }

    /// <summary>Window-level entries for the setup menu: the strip's own toggle and the view pin.</summary>
    private void DrawStripMenuExtras()
    {
        DrawOutlinerMenuItem();
        DrawPinMenuItem();
    }

    private static bool IsInReferenceSpace(Setup setup, Guid imageId, SetupEntityKinds kind, Guid id)
    {
        if (kind == SetupEntityKinds.ReferenceImage)
            return id == imageId;

        if (kind != SetupEntityKinds.Surface)
            return false;

        // A region rides its traced ancestor's photo.
        var surface = setup.FindSurface(id);
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.Trace != null)
                return surface.Trace.ImageId == imageId;

            if (surface.ParentId == Guid.Empty)
                break;

            var parentId = surface.ParentId;
            surface = setup.FindSurface(parentId);
        }

        return false;
    }

    private bool TryGetShownEntity(out SetupEntityKinds kind, out Guid id)
    {
        kind = SetupEntityKinds.None;
        id = Guid.Empty;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return false;

        // A pinned window ignores the shared selection for what it *shows* (highlights still follow).
        // A pin whose entity is gone silently reverts to following — same pruning rule the selection uses.
        if (_pinnedKind != SetupEntityKinds.None)
        {
            if (SetupEntities.Exists(setup, _pinnedKind, _pinnedId))
            {
                kind = _pinnedKind;
                id = _pinnedId;
                return true;
            }

            ClearPin();
        }

        // The shared selection only steers windows that are in the setup; an operator view stays on its op.
        return IsSetupMode && _entitySelection.TryResolve(setup, out kind, out id);
    }

    private void ClearPin()
    {
        _pinnedKind = SetupEntityKinds.None;
        _pinnedId = Guid.Empty;
    }

    private string PinnedEntityName()
    {
        return SetupLabels.NameForEntity(_pinnedKind, _pinnedId);
    }

    private ViewModes _viewMode;
    private bool _outlinerCollapsed;
    private float _outlinerHeight = DefaultOutlinerHeight; // unscaled px; scaled at draw time
    private Action? _toggleOutlinerCollapse; // cached so the strip draw doesn't allocate a closure each frame
    private Guid _lastFocusedId;
    private bool _graphOwnedInspection;

    // The per-window pin: None = follow the shared selection (persisted via OutputWindowState).
    private SetupEntityKinds _pinnedKind;
    private Guid _pinnedId;
    private static readonly int _pinViewMenuId = nameof(_pinViewMenuId).GetHashCode();

    private const float DefaultOutlinerHeight = 220;
    private const float MinOutlinerHeight = 90;
    private const float MaxOutlinerHeight = 600;
    private const float SplitterThickness = 4;
    private readonly SetupEntitySelection _entitySelection = GlobalSelectionHandling.SetupEntities;
    private Action? _drawGrip;
    private Action? _drawHostedHeader;
    private Action? _drawStripMenuExtras;
    private bool _gripActive, _gripHovered;

    private readonly SetupFlowOutliner _outliner;
    private readonly SetupOutputView _outputView;
}
