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

        // Match the settings windows' content-section background so the light hover/selection fills read.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundPopup.Rgba);
        ImGui.BeginChild("##setupPanel",
                         new Vector2(240 * T3Ui.UiScaleFactor, ImGui.GetWindowHeight()),
                         ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoBackground);
        SetupPanel.Draw(_entitySelection);
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.SameLine(0,2);
    }

    /// <summary>
    /// Draws an output-editing view if one applies to the current focus, and returns true; returns false
    /// when the caller should draw the operator output instead. A picked panel entity takes precedence
    /// over a focused sink; both are dropped when the focused op changes (graph selection wins).
    /// </summary>
    public bool TryDrawEditingView(Instance? focusedInstance, EvaluationContext context)
    {
        var focusedId = focusedInstance?.SymbolChildId ?? Guid.Empty;
        if (focusedId != _lastFocusedId)
        {
            // One shared selection: focusing a SendToOutput in the graph selects its CONTENT row; a later
            // sidebar pick (a surface/output) simply replaces it — so we never show two selected rows.
            if (focusedInstance is IOutputSink)
                _entitySelection.Select(SetupEntitySelection.EntityKind.ContentSource, focusedId);
            else
                _entitySelection.Clear();

            // The panel follows the OE-editing context: a focused sink opens it (its surfaces/outputs are
            // at hand); selecting any other op — or clicking the graph background — closes it. Only on the
            // transition, so it can still be toggled manually while the focus stays put.
            _showSetupPanel = focusedInstance is IOutputSink;

            _lastFocusedId = focusedId;
        }

        if (TryGetShownEntity(out var entityKind, out var entityId))
        {
            if (entityKind == SetupEntitySelection.EntityKind.Output)
                _outputView.Draw(entityId);
            else if (entityKind == SetupEntitySelection.EntityKind.Surface && TryGetSurfaceOutput(entityId, out var surfaceOutputId))
                _outputView.Draw(surfaceOutputId, entityId);
            else if (entityKind == SetupEntitySelection.EntityKind.ReferenceImage)
                _referenceImageView.Draw(entityId);
            else if (entityKind == SetupEntitySelection.EntityKind.ContentSource && TryGetContentOutput(entityId, context, out var contentOutputId))
                _outputView.Draw(contentOutputId);
            else
                SetupPanel.DrawEntityCard(entityKind, entityId);
        }
        else
        {
            if (focusedInstance is not IOutputSink sink || !TryGetSinkOutput(sink, context, out var sinkOutputId))
                return false;

            _outputView.Draw(sinkOutputId);
        }

        return true;
    }

    /// <summary>The output a selected content row (a live sink, by child-id) resolves to.</summary>
    private static bool TryGetContentOutput(Guid childId, EvaluationContext context, out Guid outputId)
    {
        outputId = Guid.Empty;
        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            if (sink is Instance instance && instance.SymbolChildId == childId)
                return TryGetSinkOutput(sink, context, out outputId);
        }

        return false;
    }

    /// <summary>The output a focused sink's editing view should show: its target directly if that's an output,
    /// otherwise the target surface's first mapped output.</summary>
    private static bool TryGetSinkOutput(IOutputSink sink, EvaluationContext context, out Guid outputId)
    {
        outputId = Guid.Empty;
        var targets = sink.GetTargetIds(context);
        if (targets.Count == 0)
            return false;

        var targetId = targets[0];
        if (ActiveSetup.TryFindOutput(targetId) != null)
        {
            outputId = targetId;
            return true;
        }

        return TryGetSurfaceOutput(targetId, out outputId);
    }

    /// <summary>The "Show Setup Panel" toggle — hung inside the output window's breadcrumb menu.</summary>
    public void DrawSetupPanelMenuItem()
    {
        if (CustomComponents.DrawMenuItem(1, "Show Setup Panel", isChecked: _showSetupPanel))
            _showSetupPanel = !_showSetupPanel;
    }

    /// <summary>The sidebar toggle icon for the output toolbar — filled when open, dimmed when closed.</summary>
    public void DrawPanelToggleButton()
    {
        var state = _showSetupPanel ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default;
        if (CustomComponents.IconButton(Icon.SidePanelLeft, Vector2.Zero, state))
            _showSetupPanel = !_showSetupPanel;

        ImGui.SameLine();
    }

    /// <summary>The output a selected surface should be shown on — its first mapping's output.</summary>
    private static bool TryGetSurfaceOutput(Guid surfaceId, out Guid outputId)
    {
        outputId = Guid.Empty;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return false;

        var surface = setup.Surfaces.Find(s => s.Id == surfaceId);
        if (surface == null || surface.OutputMappings.Count == 0)
            return false;

        outputId = surface.OutputMappings[0].OutputId;
        return true;
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
    private Guid _lastFocusedId;
    private readonly SetupEntitySelection _entitySelection = new();
    private readonly SetupOutputView _outputView = new();
    private readonly ReferenceImageView _referenceImageView = new();
}
