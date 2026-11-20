#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows;

namespace T3.Editor.Gui.OutputUi.Geometry;

/// <summary>
/// Draws the curve elements selected in the table over the rendered preview: anchors with their handles, segments,
/// contours, or the contours and pivot of parts. Positions are projected with the camera the preview was rendered
/// with, so the highlight sits on the render without touching the curves.
/// </summary>
internal sealed class CurveSelectionOverlay
{
    /// <summary>Call while the preview renders, to project with the same camera and resolution.</summary>
    public void CaptureView(EvaluationContext context)
    {
        _objectToClip = context.ObjectToWorld * context.WorldToCamera * context.CameraToClipSpace;
        _resolution = context.RequestedResolution;
    }

    public void Draw(CurveGeometry curves, CurveTableModel model, CurveTableView selection)
    {
        var canvas = ImageOutputCanvas.Current;
        if (canvas == null || selection.SelectedCount == 0 || _resolution.Width <= 0 || _resolution.Height <= 0)
            return;

        _canvas = canvas;
        _topLeft = canvas.TransformPosition(Vector2.Zero);
        _drawList = ImGui.GetWindowDrawList();
        _lineThickness = MathF.Max(1, MathF.Round(LineThickness * T3Ui.UiScaleFactor));

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
        if (anchor >= curves.PointCount || !TryProject(curves.Positions[anchor], out var center))
            return;

        var scale = T3Ui.UiScaleFactor;
        DrawHandle(center, curves.HandlesIn[anchor]);
        DrawHandle(center, curves.HandlesOut[anchor]);
        _drawList.AddCircleFilled(center, (AnchorRadius + 1) * scale, UiColors.BackgroundFull);
        _drawList.AddCircleFilled(center, AnchorRadius * scale, UiColors.Selection);
    }

    private void DrawHandle(Vector2 anchorOnScreen, Vector3 handle)
    {
        if (!TryProject(handle, out var handleOnScreen) || Vector2.DistanceSquared(anchorOnScreen, handleOnScreen) < 1)
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

        if (!TryProject(part.Pivot, out var pivot))
            return;

        var arm = PivotArm * T3Ui.UiScaleFactor;
        _drawList.AddLine(pivot - new Vector2(arm, 0), pivot + new Vector2(arm, 0), UiColors.Selection, _lineThickness);
        _drawList.AddLine(pivot - new Vector2(0, arm), pivot + new Vector2(0, arm), UiColors.Selection, _lineThickness);
    }

    private void DrawSegment(CurveGeometry curves, int contour, int segment)
    {
        if (contour >= curves.ContourCount || segment >= curves.GetContourSegmentCount(contour))
            return;

        var hasPrevious = TryProject(curves.Evaluate(contour, segment, 0), out var previous);
        for (var step = 1; step <= SegmentSteps; step++)
        {
            var isVisible = TryProject(curves.Evaluate(contour, segment, step / (float)SegmentSteps), out var current);
            if (hasPrevious && isVisible)
                _drawList.AddLine(previous, current, UiColors.Selection, _lineThickness);

            previous = current;
            hasPrevious = isVisible;
        }
    }

    /** Positions behind the camera can't be drawn and are skipped. */
    private bool TryProject(Vector3 position, out Vector2 screenPosition)
    {
        var clip = Vector4.Transform(new Vector4(position, 1), _objectToClip);
        if (clip.W <= MinClipW)
        {
            screenPosition = default;
            return false;
        }

        var inViewport = new Vector2(_resolution.Width * (clip.X / clip.W * 0.5f + 0.5f),
                                     _resolution.Height * (0.5f - clip.Y / clip.W * 0.5f));
        screenPosition = _topLeft + _canvas!.TransformDirection(inViewport);
        return true;
    }

    private const float AnchorRadius = 4;
    private const float HandleSize = 5;
    private const float LineThickness = 3;
    private const float PivotArm = 6;
    private const int SegmentSteps = 16;
    private const float MinClipW = 1e-5f;

    private Matrix4x4 _objectToClip = Matrix4x4.Identity;
    private Int2 _resolution;
    private ImageOutputCanvas? _canvas;
    private Vector2 _topLeft;
    private ImDrawListPtr _drawList;
    private float _lineThickness;
}
