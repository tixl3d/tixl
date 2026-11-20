#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Draws the elements of a <see cref="Geometry"/> selected in the table over the rendered preview: points, vertices
/// (pulled towards their primitive so shared points can be told apart), polygon outlines and curve shapes, edges, or
/// the primitives and pivot of parts. Also rings the points of the geometry's Selection attribute.
/// </summary>
internal sealed class GeometrySelectionOverlay
{
    public void Draw(Geometry geometry, UnifiedGeometryTableModel model, GeometryTableView selection, PreviewProjection projection)
    {
        if (selection.SelectedCount == 0 || !projection.BeginDraw())
            return;

        _projection = projection;
        _drawList = ImGui.GetWindowDrawList();
        _lineThickness = MathF.Max(1, MathF.Round(SelectionOverlayStyle.LineThickness * T3Ui.UiScaleFactor));
        _curves = geometry.Curves;

        for (var row = 0; row < model.RowCount; row++)
        {
            if (!selection.IsSelected(row))
                continue;

            switch (model.Domain)
            {
                case UnifiedGeometryTableModel.Domains.Points:
                    if (row < geometry.PointCount && projection.TryProject(geometry.Positions[row], out var point))
                        SelectionOverlayStyle.DrawDot(_drawList, point);

                    break;
                case UnifiedGeometryTableModel.Domains.Vertices:
                    DrawVertex(geometry, row, model.GetVertexPrimitive(row));
                    break;
                case UnifiedGeometryTableModel.Domains.Primitives:
                    DrawPrimitive(geometry, row);
                    break;
                case UnifiedGeometryTableModel.Domains.Edges:
                    DrawEdge(geometry, row);
                    break;
                case UnifiedGeometryTableModel.Domains.Parts:
                    DrawPart(geometry, row);
                    break;
            }
        }
    }

    /// <summary>
    /// Rings the points carried by the geometry's Selection attribute (on any domain), so a selection made by
    /// [SelectGeometry] is visible before an op acts on it.
    /// </summary>
    public void DrawSelectionAttribute(Geometry geometry, PreviewProjection projection)
    {
        if (_pointWeights.Length < geometry.PointCount)
            _pointWeights = new float[geometry.PointCount];

        if (!geometry.TryGetPointWeights(GeometryAttributeNames.Selection, _pointWeights) || !projection.BeginDraw())
            return;

        var drawList = ImGui.GetWindowDrawList();
        for (var point = 0; point < geometry.PointCount; point++)
        {
            var weight = _pointWeights[point];
            if (weight <= 0 || !projection.TryProject(geometry.Positions[point], out var onScreen))
                continue;

            SelectionOverlayStyle.DrawRing(drawList, onScreen, MathF.Min(weight, 1));
        }
    }

    private void DrawPrimitive(Geometry geometry, int primitive)
    {
        if (primitive >= geometry.PrimitiveCount)
            return;

        if (geometry.PrimitiveKind[primitive] == PrimitiveKinds.Curve)
        {
            var curve = _curves[primitive];
            for (var segment = 0; segment < curve.SegmentCount; segment++)
                DrawCurveSegment(curve, segment);

            return;
        }

        var start = geometry.PrimitiveVertexOffsets[primitive];
        var end = geometry.PrimitiveVertexOffsets[primitive + 1];
        for (var vertex = start; vertex < end; vertex++)
        {
            var next = vertex + 1 == end ? start : vertex + 1;
            DrawLine(geometry.Positions[geometry.VertexPoints[vertex]], geometry.Positions[geometry.VertexPoints[next]]);
        }
    }

    private void DrawCurveSegment(CurveView curve, int segment)
    {
        var hasPrevious = _projection!.TryProject(curve.Evaluate(segment, 0), out var previous);
        for (var step = 1; step <= SegmentSteps; step++)
        {
            var isVisible = _projection.TryProject(curve.Evaluate(segment, step / (float)SegmentSteps), out var current);
            if (hasPrevious && isVisible)
                _drawList.AddLine(previous, current, UiColors.Selection, _lineThickness);

            previous = current;
            hasPrevious = isVisible;
        }
    }

    private void DrawVertex(Geometry geometry, int vertex, int primitive)
    {
        if (vertex >= geometry.VertexCount || primitive >= geometry.PrimitiveCount)
            return;

        var position = geometry.Positions[geometry.VertexPoints[vertex]];
        var towardsCenter = Vector3.Lerp(position, UnifiedGeometryTableModel.GetPrimitiveCenter(geometry, primitive), VertexInset);
        if (_projection!.TryProject(towardsCenter, out var onScreen))
            SelectionOverlayStyle.DrawDot(_drawList, onScreen);
    }

    private void DrawEdge(Geometry geometry, int edgeIndex)
    {
        var edges = geometry.Edges.Edges;
        if (edgeIndex >= edges.Length)
            return;

        DrawLine(geometry.Positions[edges[edgeIndex].PointA], geometry.Positions[edges[edgeIndex].PointB]);
    }

    private void DrawPart(Geometry geometry, int partIndex)
    {
        if (partIndex >= geometry.Parts.Length)
            return;

        var part = geometry.Parts[partIndex];
        for (var primitive = part.PrimitiveStart; primitive < part.PrimitiveStart + part.PrimitiveCount; primitive++)
            DrawPrimitive(geometry, primitive);

        if (_projection!.TryProject(part.Pivot, out var pivot))
            SelectionOverlayStyle.DrawPivot(_drawList, pivot, _lineThickness);
    }

    private void DrawLine(Vector3 from, Vector3 to)
    {
        if (_projection!.TryProject(from, out var a) && _projection.TryProject(to, out var b))
            _drawList.AddLine(a, b, UiColors.Selection, _lineThickness);
    }

    /** How far a vertex's dot moves from its point towards the primitive's center. */
    private const float VertexInset = 0.2f;

    private const int SegmentSteps = 16;

    private PreviewProjection? _projection;
    private ImDrawListPtr _drawList;
    private float _lineThickness;
    private Geometry.CurveEnumerable _curves;
    private float[] _pointWeights = [];
}
