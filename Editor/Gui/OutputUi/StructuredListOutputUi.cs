#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.TableView;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Lists a CPU list's values in an editable table. Lists of <see cref="Point"/> can also be rendered as points.
/// </summary>
internal sealed class StructuredListOutputUi : OutputUi<StructuredList>
{
    private enum Modes
    {
        Points,
        Table,
    }

    public StructuredListOutputUi()
    {
        _tooltipForMode = TooltipForMode;
    }

    public override Type PresentedType => _isPresentingPoints ? typeof(Command) : Type;

    public override IOutputUi Clone()
    {
        return new StructuredListOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void Recompute(ISlot slot, EvaluationContext context)
    {
        _isPresentingPoints = false;

        // The list is pulled through the preview, so it needn't be evaluated on its own first.
        if (slot is Slot<StructuredList> listSlot
            && _mode == Modes.Points
            && IsPointList(listSlot.Value)
            && !PreviewLayout.IsCompactView())
        {
            _isPresentingPoints = _pointsPreview.Render(listSlot, context);
            if (_isPresentingPoints)
                return;
        }

        base.Recompute(slot, context);
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<StructuredList> typedSlot)
        {
            Debug.Assert(false);
            return;
        }

        var list = typedSlot.Value;
        if (list == null)
        {
            _isPresentingPoints = false;
            ImGui.TextUnformatted("NULL?");
            return;
        }

        if (!IsPointList(list) || PreviewLayout.IsCompactView())
        {
            _isPresentingPoints = false;
            DrawTable(typedSlot, list);
            return;
        }

        if (_isPresentingPoints)
            _pointsPreview.DrawRendered(viewId);

        PreviewLayout.IndentControls();
        CustomComponents.SegmentedButton(ref _mode, null, _tooltipForMode);
        UpdateSummary(list.NumElements);
        PreviewLayout.DrawSummary(_summary);

        if (_mode == Modes.Table)
        {
            PreviewLayout.IndentControls();
            DrawTable(typedSlot, list);
        }
    }

    private void DrawTable(Slot<StructuredList> slot, StructuredList list)
    {
        // Ops like [ReadPointsFromGpu] output a new list per read; an unchanged reference means nothing changed.
        _previousList = list != _shownList ? _shownList : null;
        _shownList = list;

        var modified = TableList.Draw(list, _previousList);
        if (modified)
            slot.DirtyFlag.Invalidate();
    }

    private static bool IsPointList(StructuredList? list)
    {
        return list != null && list.Type == typeof(Point);
    }

    private static string? TooltipForMode(Modes mode)
    {
        return mode switch
                   {
                       Modes.Points => "Render the points like [ListToBuffer] into [VisualizePoints] with default settings.",
                       _            => "List the values of the points.",
                   };
    }

    /** Rebuilt only when the count changes, so drawing stays allocation-free. */
    private void UpdateSummary(int count)
    {
        if (count == _summaryCount)
            return;

        _summaryCount = count;
        _summary = count == 1 ? "1 point" : $"{count:N0} points";
    }

    private static readonly Guid PreviewSymbolId = Guid.Parse("0c3eb144-a156-4d72-bc7e-afdf13ba61f7");
    private static readonly Guid PreviewInputId = Guid.Parse("37e80d44-7206-4046-9c11-2a8ba34e6929");

    private readonly CommandPreview<StructuredList> _pointsPreview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewCPoints");
    private readonly Func<Modes, string?> _tooltipForMode;

    /** Shared by all point list outputs, so switching ops keeps the chosen view. */
    private static Modes _mode = Modes.Points;
    private bool _isPresentingPoints;

    private StructuredList? _shownList;
    private StructuredList? _previousList;

    private string _summary = string.Empty;
    private int _summaryCount = -1;
}
