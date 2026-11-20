#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Draws the curve elements selected in the table over the rendered preview: anchors with their handles, segments,
/// contours, or the contours and pivot of parts.
/// </summary>
internal sealed class CurveSelectionOverlay
{
    public void Draw(CurveGeometry curves, CurveTableModel model, GeometryTableView selection, PreviewProjection projection)
    {
        if (selection.SelectedCount == 0 || !projection.BeginDraw())
            return;

        _projection = projection;
        _drawList = ImGui.GetWindowDrawList();
        _lineThickness = MathF.Max(1, MathF.Round(SelectionOverlayStyle.LineThickness * T3Ui.UiScaleFactor));

        for (var row = 0; row < model.RowCount; row++)
        {
            if (!selection.IsSelected(row))
                continue;

            switch (model.Domain)
            {
                case CurveTableModel.Domains.Anchors:
                    DrawAnchor(curves, row);
                    break;
                case CurveTableModel.Domains.Segments:
                    DrawSegment(curves, model.GetSegmentContour(row), model.GetSegmentInContour(row));
                    break;
                case CurveTableModel.Domains.Contours:
                    DrawContour(curves, row);
                    break;
                case CurveTableModel.Domains.Parts:
                    DrawPart(curves, row);
                    break;
            }
        }
    }

    private void DrawAnchor(CurveGeometry curves, int anchor)
    {
        if (anchor >= curves.PointCount || !_projection!.TryProject(curves.Positions[anchor], out var center))
            return;

        DrawHandle(center, curves.HandlesIn[anchor]);
        DrawHandle(center, curves.HandlesOut[anchor]);
        SelectionOverlayStyle.DrawDot(_drawList, center);
    }

    private void DrawHandle(Vector2 anchorOnScreen, Vector3 handle)
    {
        if (!_projection!.TryProject(handle, out var handleOnScreen) || Vector2.DistanceSquared(anchorOnScreen, handleOnScreen) < 1)
            return;

        var halfSize = HandleSize * 0.5f * T3Ui.UiScaleFactor;
        var color = UiColors.Selection.Fade(0.7f);
        _drawList.AddLine(anchorOnScreen, handleOnScreen, color);
        _drawList.AddRectFilled(handleOnScreen - new Vector2(halfSize), handleOnScreen + new Vector2(halfSize), color);
    }

    private void DrawContour(CurveGeometry curves, int contour)
    {
        if (contour >= curves.ContourCount)
            return;

        var segmentCount = curves.GetContourSegmentCount(contour);
        for (var segment = 0; segment < segmentCount; segment++)
            DrawSegment(curves, contour, segment);
    }

    private void DrawPart(CurveGeometry curves, int partIndex)
    {
        if (partIndex >= curves.Parts.Length)
            return;

        var part = curves.Parts[partIndex];
        for (var contour = part.ContourStart; contour < part.ContourStart + part.ContourCount; contour++)
            DrawContour(curves, contour);

        if (_projection!.TryProject(part.Pivot, out var pivot))
            SelectionOverlayStyle.DrawPivot(_drawList, pivot, _lineThickness);
    }

    private void DrawSegment(CurveGeometry curves, int contour, int segment)
    {
        if (contour >= curves.ContourCount || segment >= curves.GetContourSegmentCount(contour))
            return;

        var hasPrevious = _projection!.TryProject(curves.Evaluate(contour, segment, 0), out var previous);
        for (var step = 1; step <= SegmentSteps; step++)
        {
            var isVisible = _projection.TryProject(curves.Evaluate(contour, segment, step / (float)SegmentSteps), out var current);
            if (hasPrevious && isVisible)
                _drawList.AddLine(previous, current, UiColors.Selection, _lineThickness);

            previous = current;
            hasPrevious = isVisible;
        }
    }

    private const float HandleSize = 5;
    private const int SegmentSteps = 16;

    private PreviewProjection? _projection;
    private ImDrawListPtr _drawList;
    private float _lineThickness;
}
