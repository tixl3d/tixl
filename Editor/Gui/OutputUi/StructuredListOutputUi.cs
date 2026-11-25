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
/// Lists a CPU list's values in an editable table. Lists of <see cref="Point"/> are rendered as points, with the table
/// or a count in a sidebar (see <see cref="PreviewSidebar"/>); hovering a row marks its point on the render.
/// </summary>
internal sealed class StructuredListOutputUi : OutputUi<StructuredList>
{
    public StructuredListOutputUi()
    {
        _sidebar = new PreviewSidebar(_sidebarState);
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
            && _sidebar.ShowsPreview
            && IsPointList(listSlot.Value)
            && !PreviewLayout.IsCompactView())
        {
            _sidebar.BeginView(context);
            var previousOverlay = context.PreviewOverlay;
            var overlay = previousOverlay;
            overlay.Hovered = GetHoveredPoint(listSlot.Value);
            context.PreviewOverlay = overlay;
            _isPresentingPoints = _pointsPreview.Render(listSlot, context);
            context.PreviewOverlay = previousOverlay;
            _sidebar.EndView(context);
            if (_isPresentingPoints)
                return;
        }

        EvaluateWithoutGizmos(slot, context);
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

        UpdateSummary(list.NumElements);
        if (_isPresentingPoints)
        {
            DrawRendered(viewId);
        }

        if (!_sidebar.Begin(Segments, _counts))
        {
            _hoveredRow = -1;
            PreviewLayout.DrawSummary(_summary);
            return;
        }

        if (_sidebar.Selected == TableSegment)
        {
            DrawTable(typedSlot, list);
            _hoveredRow = TableList.LastHoveredRow;
        }
        else
        {
            _hoveredRow = -1;
            ImGui.TextUnformatted(_summary);
        }

        _sidebar.End();
    }

    /** Into the rectangle right of the sidebar while it is open, otherwise onto the output canvas as usual. */
    private void DrawRendered(string viewId)
    {
        if (_sidebar.TryGetViewRect(out var viewRect))
        {
            _pointsPreview.DrawRenderedInRect(viewRect);
            return;
        }
        _pointsPreview.DrawRendered(viewId);
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

    /** Rebuilt only when the count changes, so drawing stays allocation-free. */
    private void UpdateSummary(int count)
    {
        if (count == _summaryCount)
            return;

        _summaryCount = count;
        _summary = count == 1 ? "1 point" : $"{count:N0} points";
        _counts[TableSegment] = PreviewSidebar.FormatCount(count);
    }

    private static readonly Guid PreviewSymbolId = Guid.Parse("0c3eb144-a156-4d72-bc7e-afdf13ba61f7");
    private static readonly Guid PreviewInputId = Guid.Parse("37e80d44-7206-4046-9c11-2a8ba34e6929");

    private const int TableSegment = 1;

    private static readonly PreviewSidebar.Segment[] Segments =
        [
            new(Icon.Hidden, "Hide the sidebar; the points use the whole view."),
            new(Icon.DomainPoints, "List the values of the points."),
            new(Icon.Info, "Point count."),
        ];

    /** Shared by all point list outputs, so switching ops keeps the layout. */
    private static readonly PreviewSidebar.State _sidebarState = new();

    private readonly CommandPreview<StructuredList> _pointsPreview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewCPoints");
    private readonly PreviewSidebar _sidebar;
    private readonly PreviewPointGeometry _hoveredPoint = new();

    /** The hovered table row's point, for the preview to highlight. */
    private Geometry? GetHoveredPoint(StructuredList? list)
    {
        var row = _hoveredRow;
        if (_sidebar.Selected != TableSegment || list is not StructuredList<Point> points || row < 0 || row >= points.NumElements)
            return null;

        return _hoveredPoint.Get(true, points.TypedElements[row].Position);
    }
    private readonly string?[] _counts = new string?[Segments.Length];
    private bool _isPresentingPoints;
    private int _hoveredRow = -1;

    private StructuredList? _shownList;
    private StructuredList? _previousList;

    private string _summary = string.Empty;
    private int _summaryCount = -1;
}
