#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.Gui.OutputUi.Previews;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.OutputUi.GeometryViews;

/// <summary>
/// Draws the mesh elements selected in the table over the rendered preview: points, face outlines, corners (pulled
/// towards their face so shared points can be told apart), edges, or the faces and pivot of parts.
/// </summary>
internal sealed class MeshSelectionOverlay
{
    public void Draw(MeshGeometry geometry, MeshTableModel model, GeometryTableView selection, PreviewProjection projection)
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
                case MeshTableModel.Domains.Points:
                    if (row < geometry.PointCount && projection.TryProject(geometry.Positions[row], out var point))
                        SelectionOverlayStyle.DrawDot(_drawList, point);

                    break;
                case MeshTableModel.Domains.Faces:
                    DrawFace(geometry, row);
                    break;
                case MeshTableModel.Domains.Corners:
                    DrawCorner(geometry, row, model.GetCornerFace(row));
                    break;
                case MeshTableModel.Domains.Edges:
                    DrawEdge(geometry, row);
                    break;
                case MeshTableModel.Domains.Parts:
                    DrawPart(geometry, row);
                    break;
            }
        }
    }

    private void DrawFace(MeshGeometry geometry, int face)
    {
        if (face >= geometry.FaceCount)
            return;

        var start = geometry.FaceCornerOffsets[face];
        var end = geometry.FaceCornerOffsets[face + 1];
        for (var corner = start; corner < end; corner++)
        {
            var nextCorner = corner + 1 == end ? start : corner + 1;
            DrawLine(geometry.Positions[geometry.CornerPointIndices[corner]],
                     geometry.Positions[geometry.CornerPointIndices[nextCorner]]);
        }
    }

    private void DrawCorner(MeshGeometry geometry, int corner, int face)
    {
        if (corner >= geometry.CornerCount || face >= geometry.FaceCount)
            return;

        var position = geometry.Positions[geometry.CornerPointIndices[corner]];
        var towardsCenter = Vector3.Lerp(position, MeshTableModel.GetFaceCenter(geometry, face), CornerInset);
        if (_projection!.TryProject(towardsCenter, out var onScreen))
            SelectionOverlayStyle.DrawDot(_drawList, onScreen);
    }

    private void DrawEdge(MeshGeometry geometry, int edgeIndex)
    {
        var edges = geometry.Edges.Edges;
        if (edgeIndex >= edges.Length)
            return;

        DrawLine(geometry.Positions[edges[edgeIndex].PointA], geometry.Positions[edges[edgeIndex].PointB]);
    }

    private void DrawPart(MeshGeometry geometry, int partIndex)
    {
        if (partIndex >= geometry.Parts.Length)
            return;

        var part = geometry.Parts[partIndex];
        for (var face = part.FaceStart; face < part.FaceStart + part.FaceCount; face++)
            DrawFace(geometry, face);

        if (_projection!.TryProject(part.Pivot, out var pivot))
            SelectionOverlayStyle.DrawPivot(_drawList, pivot, _lineThickness);
    }

    private void DrawLine(Vector3 from, Vector3 to)
    {
        if (_projection!.TryProject(from, out var a) && _projection.TryProject(to, out var b))
            _drawList.AddLine(a, b, UiColors.Selection, _lineThickness);
    }

    /** How far a corner's dot moves from its point towards the face center. */
    private const float CornerInset = 0.2f;

    private PreviewProjection? _projection;
    private ImDrawListPtr _drawList;
    private float _lineThickness;
}
