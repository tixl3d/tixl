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
/// like [CurvesToPoints] into [DrawLines], lists points, vertices, primitives, edges or parts with their attributes in
/// a table, or shows counts, bounds and attributes. Rows selected in the table are highlighted on the render. The text
/// is rebuilt only when the geometry changes.
/// </summary>
internal sealed class GeometryOutputUi : OutputUi<Geometry>
{
    private enum Modes
    {
        Render,
        Table,
        Stats,
    }

    public GeometryOutputUi()
    {
        _tooltipForMode = TooltipForMode;
        _isDomainDisabled = IsDomainDisabled;
        _tooltipForDomain = TooltipForDomain;
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
        if (slot is Slot<Geometry> geometrySlot && _mode == Modes.Render && !PreviewLayout.IsCompactView())
        {
            _projection.CaptureView(context);
            _isPresentingRender = _preview.Render(geometrySlot, context);
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
        }

        if (PreviewLayout.IsCompactView())
        {
            _isPresentingRender = false;
            DrawStats(showHeadline: true);
            return;
        }

        _tableView.SyncSelection(slot.Parent.SymbolChildId, (int)_domain, UnifiedGeometryTableModel.GetRowCount(geometry, _domain));
        if (_mode == Modes.Table || _tableView.SelectedCount > 0)
            _model.Update(geometry, _domain);

        if (_isPresentingRender)
        {
            _preview.DrawRendered(viewId);
            _selectionOverlay.DrawSelectionAttribute(geometry, _projection);
            _selectionOverlay.Draw(geometry, _model, _tableView, _projection);
        }

        PreviewLayout.IndentControls();
        CustomComponents.SegmentedButton(ref _mode, null, _tooltipForMode);

        if (_mode == Modes.Table)
        {
            _hasParts = geometry.Parts.Length > 0;
            ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
            if (CustomComponents.SegmentedButton(ref _domain, _isDomainDisabled, _tooltipForDomain))
            {
                _tableView.SyncSelection(slot.Parent.SymbolChildId, (int)_domain, UnifiedGeometryTableModel.GetRowCount(geometry, _domain));
                _model.Update(geometry, _domain);
            }
        }

        UpdateSummary();
        PreviewLayout.DrawSummary(_summary);

        if (_mode == Modes.Table)
        {
            PreviewLayout.IndentControls();
            _tableView.Draw(_model);
        }
        else if (_mode == Modes.Stats)
        {
            DrawStats(showHeadline: false);
        }
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
        var isTable = _mode == Modes.Table;
        var rowCount = isTable ? _model.RowCount : -1;
        var selectedCount = _tableView.SelectedCount;
        if (ReferenceEquals(_summaryHeadline, _panel.Headline)
            && rowCount == _summaryRowCount
            && selectedCount == _summarySelectedCount
            && _domain == _summaryDomain)
            return;

        _summaryHeadline = _panel.Headline;
        _summaryRowCount = rowCount;
        _summarySelectedCount = selectedCount;
        _summaryDomain = _domain;

        var domainLabel = DomainLabels[(int)_domain];
        var main = isTable ? $"{rowCount:N0} {domainLabel}" : _panel.Headline;
        _summary = selectedCount == 0
                       ? main
                       : isTable
                           ? $"{main}, {selectedCount:N0} selected"
                           : $"{main}, {selectedCount:N0} {domainLabel} selected";
    }

    private bool IsDomainDisabled(UnifiedGeometryTableModel.Domains domain) => domain == UnifiedGeometryTableModel.Domains.Parts && !_hasParts;

    private string? TooltipForDomain(UnifiedGeometryTableModel.Domains domain)
    {
        return domain switch
                   {
                       UnifiedGeometryTableModel.Domains.Points     => "One row per point. Curve handles are point attributes, as offsets.",
                       UnifiedGeometryTableModel.Domains.Vertices   => "One row per vertex: which primitive and which point.\nPer-face values like UVs often live here.",
                       UnifiedGeometryTableModel.Domains.Primitives => "One row per primitive: polygon or curve, with its first vertex and vertex count.",
                       UnifiedGeometryTableModel.Domains.Edges      => "One row per unique polygon edge, with the primitives on either side (-1 for an open edge).",
                       _ when !_hasParts                            => "This geometry has no parts.",
                       _                                            => "One row per part, e.g. a glyph or a fracture cell.",
                   };
    }

    private static string? TooltipForMode(Modes mode)
    {
        return mode switch
                   {
                       Modes.Render => "Render polygons like [GeometryToMesh] into [VisualizeMesh] and curves like [CurvesToPoints] into [DrawLines].\nRows selected in the table are highlighted.",
                       Modes.Table  => "List the elements of a domain with their attributes.\nClick rows to select them, Ctrl+click toggles, Shift+click extends.",
                       _            => "List counts, bounds, whether the surface is closed, and the attributes.",
                   };
    }

    private static readonly Guid PreviewSymbolId = Guid.Parse("70c1d6d4-1e97-4c9d-93c8-93702000ab5b");
    private static readonly Guid PreviewInputId = Guid.Parse("76e0cf44-0f1a-451f-a0ce-bd5a9c2fb5ff");
    private static readonly string[] DomainLabels = ["points", "vertices", "primitives", "edges", "parts"];

    private readonly CommandPreview<Geometry> _preview = new(PreviewSymbolId, PreviewInputId, "_OutputPreviewGeometry");
    private readonly Func<Modes, string?> _tooltipForMode;
    private readonly Func<UnifiedGeometryTableModel.Domains, bool> _isDomainDisabled;
    private readonly Func<UnifiedGeometryTableModel.Domains, string?> _tooltipForDomain;

    /** Shared by all geometry outputs, so switching ops keeps the chosen view. */
    private static Modes _mode = Modes.Render;
    private static UnifiedGeometryTableModel.Domains _domain = UnifiedGeometryTableModel.Domains.Points;
    private bool _isPresentingRender;
    private bool _hasParts;

    private readonly GeometryStatsPanel _panel = new();
    private readonly UnifiedGeometryTableModel _model = new();
    private readonly GeometryTableView _tableView = new();
    private readonly GeometrySelectionOverlay _selectionOverlay = new();
    private readonly PreviewProjection _projection = new();
    private Geometry? _lastGeometry;
    private int _lastVersion;

    private string _summary = string.Empty;
    private string? _summaryHeadline;
    private int _summaryRowCount = -1;
    private int _summarySelectedCount = -1;
    private UnifiedGeometryTableModel.Domains _summaryDomain;
}
