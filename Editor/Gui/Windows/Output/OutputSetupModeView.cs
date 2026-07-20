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

        ImGui.BeginChild("##setupPanel",
                         new Vector2(240 * T3Ui.UiScaleFactor, ImGui.GetWindowHeight()),
                         ImGuiChildFlags.None,
                         ImGuiWindowFlags.None);
        SetupPanel.Draw(_entitySelection);
        ImGui.EndChild();
        ImGui.SameLine();
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
            else
                SetupPanel.DrawEntityCard(entityKind, entityId);
        }
        else
        {
            var sinkOutputId = (focusedInstance as IOutputSink)?.GetOutputId(context) ?? Guid.Empty;
            if (sinkOutputId == Guid.Empty)
                return false;

            _outputView.Draw(sinkOutputId);
        }

        return true;
    }

    /// <summary>The "Show Setup Panel" toggle — hung inside the output window's breadcrumb menu.</summary>
    public void DrawSetupPanelMenuItem()
    {
        if (ImGui.MenuItem("Show Setup Panel", "", _showSetupPanel))
            _showSetupPanel = !_showSetupPanel;
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
}
