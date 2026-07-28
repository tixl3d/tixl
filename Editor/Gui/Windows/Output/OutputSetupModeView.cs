#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// The output-editing side of an output window. There is no explicit Operator/Setup mode — the window
/// follows focus: a focused <see cref="IOutputSink"/> (SendToOutput) op shows its output's editing
/// canvas, and a picked panel entity shows that entity. Selecting any op in the graph drops a panel
/// edit (graph selection wins). Owns the side panel and the window's entity selection. One per OutputWindow.
/// </summary>
internal sealed class OutputSetupModeView
{
    /// <summary>Draws the setup outline side panel when enabled. Call first — it splits the window horizontally.</summary>
    public void DrawSidePanel()
    {
        if (!_showSetupPanel)
            return;

        var scale = T3Ui.UiScaleFactor;

        // Match the settings windows' content-section background so the light hover/selection fills read.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundPopup.Rgba);
        ImGui.BeginChild("##setupPanel",
                         new Vector2(_panelWidth * scale, ImGui.GetWindowHeight()),
                         ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoBackground);
        _collapsePanel ??= () => _showSetupPanel = false;
        _panel.Draw(_entitySelection, _collapsePanel);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        DrawPanelSplitter(scale);
    }

    /// <summary>A drag handle on the panel's right edge. Width is per-window session state (not persisted).</summary>
    private void DrawPanelSplitter(float scale)
    {
        var thickness = 4 * scale;
        ImGui.SameLine(0, 0);
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##panelSplitter", new Vector2(thickness, ImGui.GetWindowHeight()));

        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        if (active || hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        if (active)
            _panelWidth = Math.Clamp(_panelWidth + ImGui.GetIO().MouseDelta.X / scale, MinPanelWidth, MaxPanelWidth);

        var color = active ? UiColors.StatusActivated : (hovered ? UiColors.BackgroundHover : UiColors.BackgroundFull);
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(thickness, ImGui.GetWindowHeight()), color);

        ImGui.SameLine(0, 2 * scale);
    }

    /// <summary>
    /// Draws an output-editing view if one applies to the current focus, and returns true; returns false
    /// when the caller should draw the operator output instead. A picked panel entity takes precedence
    /// over a focused sink; both are dropped when the focused op changes (graph selection wins).
    /// </summary>
    public bool TryDrawEditingView(Instance? focusedInstance, EvaluationContext context)
    {
        // What's *drawn* here may be pinned, so it doesn't follow the graph. Which CONTENT row is selected
        // should follow the graph selection regardless — picking a send in the graph is how you get to it.
        var selectedInGraph = ProjectView.Focused?.NodeSelection.GetSelectedInstanceWithoutComposition();
        var focusedId = selectedInGraph?.SymbolChildId ?? Guid.Empty;
        if (focusedId != _lastFocusedId)
        {
            // One shared selection: focusing a SendToOutput in the graph selects its CONTENT row; a later
            // sidebar pick (a surface/output) simply replaces it — so we never show two selected rows.
            if (selectedInGraph is IOutputSink)
                _entitySelection.Select(SetupEntitySelection.EntityKind.ContentSource, focusedId);
            else
                _entitySelection.Clear();

            // The panel follows the OE-editing context: a focused sink opens it (its surfaces/outputs are
            // at hand); selecting any other op — or clicking the graph background — closes it. Only on the
            // transition, so it can still be toggled manually while the focus stays put.
            _showSetupPanel = selectedInGraph is IOutputSink;

            _lastFocusedId = focusedId;
        }

        if (TryGetShownEntity(out var entityKind, out var entityId))
        {
            if (entityKind == SetupEntitySelection.EntityKind.Output)
                // Pass the selection so a surface label on the canvas can still be clicked to select it, even
                // though the shown entity is the output itself (no surface focused).
                _outputView.Draw(entityId, selection: _entitySelection);
            else if (entityKind == SetupEntitySelection.EntityKind.Surface && TryGetSurfaceOutput(entityId, out var surfaceOutputId))
                _outputView.Draw(surfaceOutputId, entityId, _entitySelection); // labels on the canvas can re-pick
            else if (entityKind == SetupEntitySelection.EntityKind.ReferenceImage)
                _referenceImageView.Draw(entityId);
            else if (entityKind == SetupEntitySelection.EntityKind.ContentSource)
                // Slices live on the source, so selecting content opens it with every slice laid out on it.
                _outputView.DrawSourceCanvas(entityId, _entitySelection);
            else if (entityKind == SetupEntitySelection.EntityKind.Slice && TryGetSliceSource(entityId, out var sliceChildId))
                _outputView.DrawSourceCanvas(sliceChildId, _entitySelection, entityId);
            else
                _panel.DrawEntityCard(entityKind, entityId);
        }
        else
        {
            if (focusedInstance is not IOutputSink || !TryGetSinkOutput(focusedInstance, out var sinkOutputId))
                return false;

            // Pass the selection here too: with nothing selected in the sidebar (e.g. after ctrl-clicking the
            // selected output away) this is the path that draws the canvas, and a frame label still has to be
            // clickable to select its surface.
            _outputView.Draw(sinkOutputId, selection: _entitySelection);
        }

        return true;
    }

    /// <summary>
    /// The output a focused send's editing view should show: the first output reached by anything displaying
    /// one of its slices — a surface's mapping, or an output showing it full-frame.
    /// </summary>
    private static bool TryGetSinkOutput(Instance instance, out Guid outputId)
    {
        outputId = Guid.Empty;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return false;

        var source = setup.FindSourceByChildId(instance.SymbolChildId);
        if (source == null)
            return false;

        foreach (var slice in setup.Slices)
        {
            if (slice.SourceId != source.Id)
                continue;

            foreach (var output in setup.Outputs)
            {
                if (output.SliceId == slice.Id)
                {
                    outputId = output.Id;
                    return true;
                }
            }

            foreach (var surface in setup.Surfaces)
            {
                if (surface.SliceId == slice.Id && TryGetSurfaceOutput(surface.Id, out outputId))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The op supplying a slice's source, so selecting a slice can open the canvas it lives on.</summary>
    private static bool TryGetSliceSource(Guid sliceId, out Guid symbolChildId)
    {
        symbolChildId = Guid.Empty;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return false;

        var slice = setup.FindSlice(sliceId);
        var source = slice == null ? null : setup.FindSource(slice.SourceId);
        if (source == null)
            return false;

        symbolChildId = source.SymbolChildId;
        return true;
    }

    /// <summary>The "Show Setup Panel" toggle — hung inside the output window's breadcrumb menu.</summary>
    public void DrawSetupPanelMenuItem()
    {
        if (CustomComponents.DrawMenuItem(1, "Show Setup Panel", isChecked: _showSetupPanel))
            _showSetupPanel = !_showSetupPanel;
    }

    /// <summary>The sidebar toggle icon for the output toolbar. Only drawn while the panel is closed —
    /// the open panel's own header carries the collapse icon, and showing the control twice reads as two
    /// different buttons.</summary>
    public void DrawPanelToggleButton()
    {
        if (_showSetupPanel)
            return;

        if (CustomComponents.IconButton(Icon.SidePanelLeft, Vector2.Zero))
            _showSetupPanel = true;

        ImGui.SameLine();
    }

    /// <summary>
    /// The output a selected surface should be shown on — its first mapping's output. A Layout child carries
    /// no mapping of its own; it's shown wherever its parent is mapped, so walk up to the surface that
    /// actually holds the corner pin rather than reporting the child as unmapped.
    /// </summary>
    private static bool TryGetSurfaceOutput(Guid surfaceId, out Guid outputId)
    {
        outputId = Guid.Empty;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return false;

        var surface = setup.FindSurface(surfaceId);
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.OutputMappings.Count > 0)
            {
                outputId = surface.OutputMappings[0].OutputId;
                return true;
            }

            if (surface.ParentId == Guid.Empty)
                break;

            var parentId = surface.ParentId;
            surface = setup.FindSurface(parentId);
        }

        return false;
    }

    private bool TryGetShownEntity(out SetupEntitySelection.EntityKind kind, out Guid id)
    {
        kind = SetupEntitySelection.EntityKind.None;
        id = Guid.Empty;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return false;

        return _entitySelection.TryResolve(setup, out kind, out id);
    }

    private bool _showSetupPanel;
    private float _panelWidth = DefaultPanelWidth; // unscaled px; scaled at draw time
    private Action? _collapsePanel; // cached so the side-panel draw doesn't allocate a closure each frame
    private Guid _lastFocusedId;

    private const float DefaultPanelWidth = 240;
    private const float MinPanelWidth = 180;
    private const float MaxPanelWidth = 520;
    private readonly SetupEntitySelection _entitySelection = new();
    public OutputSetupModeView()
    {
        // One EntityItem per window: the panel rows and the canvas menus share its rename/menu state,
        // and separate windows can't bleed into each other.
        _panel = new SetupPanel(_entityItem);
        _outputView = new SetupOutputView(_entityItem);
    }

    private readonly EntityItem _entityItem = new();
    private readonly SetupPanel _panel;
    private readonly SetupOutputView _outputView;
    private readonly ReferenceImageView _referenceImageView = new();
}
