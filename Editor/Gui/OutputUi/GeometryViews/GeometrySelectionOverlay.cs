#nullable enable
using T3.Core.DataTypes.Vector;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Draws the elements of a <see cref="Geometry"/> selected in the table over the rendered preview: points, vertices
/// (pulled towards their primitive so shared points can be told apart), polygon outlines and curve shapes, edges, or
/// the primitives and pivot of parts. Also shows the geometry's Selection attribute on its domain.
/// </summary>
internal sealed class GeometrySelectionOverlay
{
    public void Draw(Geometry geometry, UnifiedGeometryTableModel model, GeometryTableView selection, PreviewProjection projection)
    {
        if (selection.SelectedCount == 0 || !BeginDraw(geometry, projection, UiColors.Selection))
            return;

        for (var row = 0; row < model.RowCount; row++)
        {
            if (selection.IsSelected(row))
                DrawElement(geometry, model, row, isHover: false);
        }
    }

    /// <summary>Marks the element of the hovered table row, in the hover color.</summary>
    public void DrawHovered(Geometry geometry, UnifiedGeometryTableModel model, int row, PreviewProjection projection)
    {
        if (row < 0 || row >= model.RowCount || !BeginDraw(geometry, projection, SelectionOverlayStyle.Hover))
            return;

        DrawElement(geometry, model, row, isHover: true);
    }

    private bool BeginDraw(Geometry geometry, PreviewProjection projection, Color color)
    {
        if (!projection.BeginDraw())
            return false;

        _projection = projection;
        _drawList = ImGui.GetWindowDrawList();
        _lineThickness = MathF.Max(1, MathF.Round(SelectionOverlayStyle.LineThickness * T3Ui.UiScaleFactor));
        _curves = geometry.Curves;
        _color = color;
        return true;
    }

    private void DrawElement(Geometry geometry, UnifiedGeometryTableModel model, int row, bool isHover)
    {
        switch (model.Domain)
        {
            case UnifiedGeometryTableModel.Domains.Points:
                if (row < geometry.PointCount && _projection!.TryProject(geometry.Positions[row], out var point))
                {
                    if (isHover)
                        SelectionOverlayStyle.DrawHoverRing(_drawList, point);
                    else
                        SelectionOverlayStyle.DrawDot(_drawList, point);
                }

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

    /// <summary>
    /// Shows the geometry's Selection attribute on its own domain, so a selection made by [SelectGeometry] is visible
    /// before an op acts on it: points get rings, vertices dots, primitives a fill and outline, parts their outlines and
    /// pivot. The fill fades with the weight.
    /// </summary>
    public void DrawSelectionAttribute(Geometry geometry, PreviewProjection projection)
    {
        var selection = FindSelection(geometry.Attributes);
        if (selection == null || !projection.BeginDraw())
            return;

        _projection = projection;
        _drawList = ImGui.GetWindowDrawList();
        _lineThickness = MathF.Max(1, MathF.Round(SelectionOverlayStyle.LineThickness * T3Ui.UiScaleFactor));
        _curves = geometry.Curves;
        _color = UiColors.Selection;
        var weights = selection.Values;

        switch (selection.Domain)
        {
            case AttributeDomain.Vertex:
                for (var primitive = 0; primitive < geometry.PrimitiveCount; primitive++)
                {
                    for (var vertex = geometry.PrimitiveVertexOffsets[primitive]; vertex < geometry.PrimitiveVertexOffsets[primitive + 1]; vertex++)
                    {
                        if (vertex < weights.Length && weights[vertex] > 0)
                            DrawVertex(geometry, vertex, primitive);
                    }
                }

                break;

            case AttributeDomain.Primitive:
                for (var primitive = 0; primitive < Math.Min(geometry.PrimitiveCount, weights.Length); primitive++)
                {
                    if (weights[primitive] <= 0)
                        continue;

                    FillPolygon(geometry, primitive, MathF.Min(weights[primitive], 1));
                    DrawPrimitive(geometry, primitive);
                }

                break;

            case AttributeDomain.Part:
                for (var part = 0; part < Math.Min(geometry.Parts.Length, weights.Length); part++)
                {
                    if (weights[part] > 0)
                        DrawPart(geometry, part);
                }

                break;

            default:
                DrawPointRings(geometry, projection);
                break;
        }
    }

    private static GeometryAttribute<float>? FindSelection(GeometryAttributes attributes)
    {
        for (var index = 0; index < attributes.Count; index++)
        {
            if (attributes[index] is GeometryAttribute<float> floats
                && string.Equals(floats.Name, GeometryAttributeNames.Selection, StringComparison.OrdinalIgnoreCase))
                return floats;
        }

        return null;
    }

    private void DrawPointRings(Geometry geometry, PreviewProjection projection)
    {
        if (_pointWeights.Length < geometry.PointCount)
            _pointWeights = new float[geometry.PointCount];

        if (!geometry.TryGetPointWeights(GeometryAttributeNames.Selection, _pointWeights))
            return;

        for (var point = 0; point < geometry.PointCount; point++)
        {
            var weight = _pointWeights[point];
            if (weight <= 0 || !projection.TryProject(geometry.Positions[point], out var onScreen))
                continue;

            SelectionOverlayStyle.DrawRing(_drawList, onScreen, MathF.Min(weight, 1));
        }
    }

    /** A triangle fan over the polygon; exact for convex faces, which is enough for a highlight. */
    private void FillPolygon(Geometry geometry, int primitive, float weight)
    {
        if (geometry.PrimitiveKind[primitive] != PrimitiveKinds.Polygon)
            return;

        var start = geometry.PrimitiveVertexOffsets[primitive];
        var end = geometry.PrimitiveVertexOffsets[primitive + 1];
        if (end - start < 3 || !_projection!.TryProject(geometry.Positions[geometry.VertexPoints[start]], out var first))
            return;

        var color = UiColors.Selection.Fade(SelectionFillOpacity * weight);
        var hasPrevious = _projection.TryProject(geometry.Positions[geometry.VertexPoints[start + 1]], out var previous);
        for (var vertex = start + 2; vertex < end; vertex++)
        {
            var isVisible = _projection.TryProject(geometry.Positions[geometry.VertexPoints[vertex]], out var current);
            if (hasPrevious && isVisible)
                _drawList.AddTriangleFilled(first, previous, current, color);

            previous = current;
            hasPrevious = isVisible;
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
                _drawList.AddLine(previous, current, _color, _lineThickness);

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
            SelectionOverlayStyle.DrawDot(_drawList, onScreen, _color);
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
            SelectionOverlayStyle.DrawPivot(_drawList, pivot, _lineThickness, _color);
    }

    private void DrawLine(Vector3 from, Vector3 to)
    {
        if (_projection!.TryProject(from, out var a) && _projection.TryProject(to, out var b))
            _drawList.AddLine(a, b, _color, _lineThickness);
    }

    /** How far a vertex's dot moves from its point towards the primitive's center. */
    private const float VertexInset = 0.2f;

    private const int SegmentSteps = 16;

    private const float SelectionFillOpacity = 0.35f;

    private PreviewProjection? _projection;
    private ImDrawListPtr _drawList;
    private float _lineThickness;
    private Color _color = UiColors.Selection;
    private Geometry.CurveEnumerable _curves;
    private float[] _pointWeights = [];
}
