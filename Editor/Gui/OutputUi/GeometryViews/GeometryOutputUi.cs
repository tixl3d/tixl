#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Output view for <see cref="Geometry"/>: renders polygons like [GeometryToMesh] into [VisualizeMesh] and curves
/// like [CurvesToPoints] into [DrawLines]. A sidebar (see <see cref="PreviewSidebar"/>) lists points, vertices, edges,
/// primitives or parts with their attributes, or shows counts, bounds and attributes. Rows selected in the table are
/// highlighted on the render, and the hovered row's element too. The text is rebuilt only when the geometry changes.
/// </summary>
internal sealed class GeometryOutputUi : OutputUi<Geometry>
{
    public GeometryOutputUi()
    {
        _sidebar = new PreviewSidebar(_sidebarState);
    }

    public override Type PresentedType => _isPresentingRender ? typeof(Command) : Type;

    public override IOutputUi Clone()
    {
        return new GeometryOutputUi
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size
                   };
    }

    protected override void Recompute(ISlot slot, EvaluationContext context)
    {
        _isPresentingRender = false;

        // The geometry is pulled through the preview, so it needn't be evaluated on its own first.
        if (slot is Slot<Geometry> geometrySlot && _sidebar.ShowsPreview && !PreviewLayout.IsCompactView())
        {
            _sidebar.BeginView(context);
            _projection.CaptureView(context);
            _isPresentingRender = _preview.Render(geometrySlot, context);
            _sidebar.EndView(context);
            if (_isPresentingRender)
                return;
        }

        EvaluateWithoutGizmos(slot, context);
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<Geometry> typedSlot)
        {
            Debug.Assert(false);
            return;
        }

        var geometry = typedSlot.Value;
        if (geometry == null)
        {
            _lastGeometry = null;
            _isPresentingRender = false;
            CustomComponents.EmptyWindowMessage("No geometry");
            return;
        }

        if (!ReferenceEquals(geometry, _lastGeometry) || geometry.Version != _lastVersion)
        {
            _lastGeometry = geometry;
            _lastVersion = geometry.Version;
            RebuildStats(geometry);
            RebuildCounts(geometry);
        }

        if (PreviewLayout.IsCompactView())
        {
            _isPresentingRender = false;
            DrawStats(showHeadline: true);
            return;
        }

        var childId = slot.Parent.SymbolChildId;
        UpdateDomain(geometry, childId);
        var showsTable = TryGetDomain(_sidebar.Selected, out _);
        if (showsTable || _tableView.SelectedCount > 0)
            _model.Update(geometry, _domain);

        if (_isPresentingRender)
        {
            DrawRendered(viewId);
            _selectionOverlay.DrawSelectionAttribute(geometry, _projection);
            _selectionOverlay.Draw(geometry, _model, _tableView, _projection);
            if (showsTable)
                _selectionOverlay.DrawHovered(geometry, _model, _tableView.HoveredRow, _projection);
        }

        var disabledMask = geometry.Parts.Length > 0 ? 0 : 1 << PartsSegment;
        if (!_sidebar.Begin(Segments, _counts, disabledMask))
        {
            _tableView.ClearHover();
            UpdateSummary();
            PreviewLayout.DrawSummary(_summary);
            return;
        }

        // A segment picked in this frame shows its table right away
        if (UpdateDomain(geometry, childId))
            _model.Update(geometry, _domain);

        if (TryGetDomain(_sidebar.Selected, out _))
        {
            _tableView.Draw(_model);
        }
        else
        {
            _tableView.ClearHover();
            DrawStats(showHeadline: true);
        }

        _sidebar.End();
    }

    /** Into the rectangle right of the sidebar while it is open, otherwise onto the output canvas as usual. */
    private void DrawRendered(string viewId)
    {
        if (_sidebar.TryGetViewRect(out var viewRect))
        {
            _projection.SetTargetRect(viewRect);
            _preview.DrawRenderedInRect(viewRect);
            return;
        }

        _projection.SetTargetRect(null);
        _preview.DrawRendered(viewId);
    }

    /// <returns>True when the shown domain changed.</returns>
    private bool UpdateDomain(Geometry geometry, Guid childId)
    {
        var changed = false;
        if (TryGetDomain(_sidebar.Selected, out var domain) && domain != _domain)
        {
            _domain = domain;
            changed = true;
        }

        _tableView.SyncSelection(childId, (int)_domain, UnifiedGeometryTableModel.GetRowCount(geometry, _domain));
        return changed;
    }

    private static bool TryGetDomain(int segment, out UnifiedGeometryTableModel.Domains domain)
    {
        domain = segment switch
                     {
                         PointsSegment     => UnifiedGeometryTableModel.Domains.Points,
                         VerticesSegment   => UnifiedGeometryTableModel.Domains.Vertices,
                         EdgesSegment      => UnifiedGeometryTableModel.Domains.Edges,
                         PrimitivesSegment => UnifiedGeometryTableModel.Domains.Primitives,
                         _                 => UnifiedGeometryTableModel.Domains.Parts,
                     };
        return segment is >= PointsSegment and <= PartsSegment;
    }

    /** Element counts for the segments, rebuilt with the geometry. */
    private void RebuildCounts(Geometry geometry)
    {
        _counts[PointsSegment] = PreviewSidebar.FormatCount(geometry.PointCount);
        _counts[VerticesSegment] = PreviewSidebar.FormatCount(geometry.VertexCount);
        _counts[EdgesSegment] = PreviewSidebar.FormatCount(geometry.Edges.Edges.Length);
        _counts[PrimitivesSegment] = PreviewSidebar.FormatCount(geometry.PrimitiveCount);
        _counts[PartsSegment] = PreviewSidebar.FormatCount(geometry.Parts.Length);
    }

    private void DrawStats(bool showHeadline)
    {
        ImGui.BeginChild("GeometryStats");
        ImGui.Indent(10 * T3Ui.UiScaleFactor);
        FormInputs.AddVerticalSpace(5);
        _panel.Draw(showHeadline);
        ImGui.Unindent(10 * T3Ui.UiScaleFactor);
        ImGui.EndChild();
    }

    private void RebuildStats(Geometry geometry)
    {
        int polygonCount = 0, curveCount = 0, closedCurveCount = 0, triangleCount = 0;
        for (var primitive = 0; primitive < geometry.PrimitiveCount; primitive++)
        {
            if (geometry.PrimitiveKind[primitive] == PrimitiveKinds.Curve)
            {
                curveCount++;
                if (geometry.PrimitiveClosed[primitive])
                    closedCurveCount++;

                continue;
            }

            polygonCount++;
            var vertices = geometry.GetPrimitiveVertexCount(primitive);
            if (vertices >= 3)
                triangleCount += vertices - 2;
        }

        var partCount = geometry.Parts.Length;
        var contents = polygonCount > 0 && curveCount > 0
                           ? $"{polygonCount:N0} polygons, {curveCount:N0} curves"
                           : curveCount > 0
                               ? $"{curveCount:N0} curves"
                               : polygonCount > 0
                                   ? $"{polygonCount:N0} polygons"
                                   : $"{geometry.PointCount:N0} points";
        _panel.Clear(partCount > 1 ? $"{contents} in {partCount:N0} parts" : contents);

        _panel.AddLine("Points", $"{geometry.PointCount:N0}", UiColors.Text);
        _panel.AddLine("Vertices", $"{geometry.VertexCount:N0}", UiColors.Text);
        if (polygonCount > 0)
            _panel.AddLine("Triangles", $"{triangleCount:N0}", UiColors.Text);

        if (curveCount > 0)
            _panel.AddLine("Curves", $"{closedCurveCount:N0} of {curveCount:N0} closed", UiColors.Text);

        if (geometry.PointCount > 0)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in geometry.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            _panel.AddLine("Size", GeometryStatsPanel.FormatSize(max - min), UiColors.Text);
            _panel.AddLine("Bounds", GeometryStatsPanel.FormatBounds(min, max), UiColors.Text);
        }

        if (polygonCount > 0)
        {
            var openEdges = 0;
            foreach (var edge in geometry.Edges.Edges)
            {
                if (edge.Face1 < 0)
                    openEdges++;
            }

            if (openEdges == 0)
            {
                _panel.AddLine("Surface", "closed", UiColors.StatusControlled);
            }
            else
            {
                _panel.AddLine("Surface", $"{openEdges:N0} open edges", UiColors.StatusAttention);
            }
        }

        _panel.SetAttributes(geometry.Attributes);
    }

    /** Rebuilt only when one of its numbers changes, so drawing stays allocation-free. */
    private void UpdateSummary()
    {
        var selectedCount = _tableView.SelectedCount;
        if (ReferenceEquals(_summaryHeadline, _panel.Headline) && selectedCount == _summarySelectedCount && _domain == _summaryDomain)
            return;

        _summaryHeadline = _panel.Headline;
        _summarySelectedCount = selectedCount;
        _summaryDomain = _domain;
        _summary = selectedCount == 0
                       ? _panel.Headline
                       : $"{_panel.Headline}, {selectedCount:N0} {DomainLabels[(int)_domain]} selected";
    }

    private const int PointsSegment = 1;
    private const int VerticesSegment = 2;
    private const int EdgesSegment = 3;
    private const int PrimitivesSegment = 4;
    private const int PartsSegment = 5;

    private static readonly PreviewSidebar.Segment[] Segments =
        [
            new(Icon.Hidden, "Hide the sidebar; the render uses the whole view."),
            new(Icon.DomainPoints, "Points: one row per point. Curve handles are point attributes, as offsets."),
            new(Icon.DomainVertices, "Vertices: one row per vertex, with its primitive and point.\nPer-face values like UVs often live here."),
            new(Icon.DomainEdges, "Edges: one row per unique polygon edge, with the primitives on either side (-1 for an open edge)."),
            new(Icon.DomainPrimitives, "Primitives: one row per polygon or curve, with its first vertex and vertex count."),
            new(Icon.DomainParts, "Parts: one row per part, e.g. a glyph or a fracture cell."),
            new(Icon.Info, "Stats: counts, bounds, whether the surface is closed, and the attributes."),
        ];

    private static readonly Guid PreviewSymbolId = Guid.Parse("70c1d6d4-1e97-4c9d-93c8-93702000ab5b");
    private static readonly Guid PreviewInputId = Guid.Parse("76e0cf44-0f1a-451f-a0ce-bd5a9c2fb5ff");
    private static readonly string[] DomainLabels = ["points", "vertices", "primitives", "edges", "parts"];

    /** Shared by all geometry outputs, so switching ops keeps the layout. */
    private static readonly PreviewSidebar.State _sidebarState = new();

    private static UnifiedGeometryTableModel.Domains _domain = UnifiedGeometryTableModel.Domains.Points;

    private readonly CommandPreview<Geometry> _preview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewGeometry");
    private readonly PreviewSidebar _sidebar;
    private readonly string?[] _counts = new string?[Segments.Length];
    private bool _isPresentingRender;

    private readonly GeometryStatsPanel _panel = new();
    private readonly UnifiedGeometryTableModel _model = new();
    private readonly GeometryTableView _tableView = new();
    private readonly GeometrySelectionOverlay _selectionOverlay = new();
    private readonly PreviewProjection _projection = new();
    private Geometry? _lastGeometry;
    private int _lastVersion;

    private string _summary = string.Empty;
    private string? _summaryHeadline;
    private int _summarySelectedCount = -1;
    private UnifiedGeometryTableModel.Domains _summaryDomain;
}
