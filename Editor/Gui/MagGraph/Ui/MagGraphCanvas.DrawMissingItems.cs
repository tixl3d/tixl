#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    /// <summary>
    /// Draws operators whose symbol is missing, and their connections, as a non-interactive overlay that
    /// shows where the graph has gaps.
    /// </summary>
    private void DrawMissingItems(ImDrawListPtr drawList)
    {
        var layout = _context.Layout;
        if (layout.MissingItems.Count == 0)
            return;

        var color = UiColors.StatusAttention.Fade(_context.GraphOpacity);

        foreach (var connection in layout.MissingConnections)
        {
            var sourcePosOnCanvas = connection.SourceMissingItem != null
                                        ? connection.SourceMissingItem.GetOutputAnchorOnCanvas(connection.SourceMissingOutputIndex)
                                        : connection.SourceItem!.DampedPosOnCanvas
                                          + new Vector2(MagGraphItem.Width, (0.5f + connection.SourceItemVisibleIndex) * MagGraphItem.LineHeight);

            var targetPosOnCanvas = connection.TargetMissingItem != null
                                        ? connection.TargetMissingItem.GetInputAnchorOnCanvas(connection.TargetMissingInputLineIndex)
                                        : connection.TargetItem!.DampedPosOnCanvas
                                          + new Vector2(0, (0.5f + connection.TargetItemVisibleIndex) * MagGraphItem.LineHeight);

            var sourcePos = TransformPosition(sourcePosOnCanvas);
            var targetPos = TransformPosition(targetPosOnCanvas);
            var anchorRadius = 3 * CanvasScale.Clamp(0.2f, 2) * T3Ui.UiScaleFactor;

            // Items snapped below each other connect through their touching edges: a wire from the right to
            // the left side would loop around both. Mark the shared edge instead.
            if (TryGetVerticalSnapPosOnCanvas(connection, out var snapPosOnCanvas))
            {
                drawList.AddCircleFilled(TransformPosition(snapPosOnCanvas), anchorRadius * 1.5f, color);
                continue;
            }

            // Snapped side by side: the anchors coincide, a curve would only curl
            var distance = Vector2.Distance(sourcePos, targetPos);
            if (distance < 4 * anchorRadius)
            {
                drawList.AddCircleFilled((sourcePos + targetPos) * 0.5f, anchorRadius * 1.5f, color);
                continue;
            }

            var tangentLength = MathF.Min(MathF.Max(distance / 2, 20 * CanvasScale), distance);

            drawList.AddBezierCubic(sourcePos,
                                    sourcePos + new Vector2(tangentLength, 0),
                                    targetPos - new Vector2(tangentLength, 0),
                                    targetPos,
                                    color.Fade(0.7f),
                                    (2 * CanvasScale).Clamp(1, 2) * T3Ui.UiScaleFactor);

            drawList.AddCircleFilled(sourcePos, anchorRadius, color);
            drawList.AddCircleFilled(targetPos, anchorRadius, color);
        }

        foreach (var missingItem in layout.MissingItems)
        {
            var area = ImRect.RectWithSize(missingItem.PosOnCanvas, missingItem.Size);
            if (!IsRectVisible(area))
                continue;

            var pMin = TransformPosition(area.Min);
            var pMax = TransformPosition(area.Max);
            var rounding = CanvasScale < 0.5f ? 0 : 5 * CanvasScale;

            drawList.AddRectFilled(pMin, pMax, UiColors.BackgroundFull.Fade(0.6f * _context.GraphOpacity), rounding);
            drawList.AddRect(pMin, pMax, color, rounding, ImDrawFlags.None, 1.5f * T3Ui.UiScaleFactor);

            if (CanvasScale > 0.25f)
            {
                DrawMissingItemLabel(drawList, missingItem, pMin, color);
            }

            if (_context.PreventInteraction || !ImGui.IsWindowHovered() || !ImGui.IsMouseHoveringRect(pMin, pMax))
                continue;

            ImGui.BeginTooltip();
            ImGui.TextUnformatted(missingItem.Child.DisplayName);
            CustomComponents.StylizedText("This operator is not available. It is kept in the file and\n"
                                          + "returns once its package is installed.",
                                          Fonts.FontSmall, UiColors.TextMuted);
            CustomComponents.StylizedText($"Symbol {missingItem.Child.SymbolId}", Fonts.FontSmall, UiColors.TextMuted);
            ImGui.EndTooltip();
        }
    }

    private void DrawMissingItemLabel(ImDrawListPtr drawList, MagGraphMissingItem missingItem, Vector2 pMin, Color color)
    {
        var fontSize = Fonts.FontNormal.FontSize * (CanvasScale / T3Ui.UiScaleFactor).Clamp(0.1f, 1);
        // Centered in the first line, like the labels of real items
        var labelPos = pMin + new Vector2(8 * CanvasScale, (MagGraphItem.LineHeight * CanvasScale - fontSize) / 2);
        drawList.AddText(Fonts.FontNormal, fontSize, labelPos, color, missingItem.Label);

        // Single-line items have no room below the name
        if (missingItem.LineCount < 2)
            return;

        var smallFontSize = Fonts.FontSmall.FontSize * (CanvasScale / T3Ui.UiScaleFactor).Clamp(0.1f, 1);
        drawList.AddText(Fonts.FontSmall, smallFontSize, labelPos + new Vector2(0, fontSize * 1.1f), color.Fade(0.6f), "missing");
    }

    /// <summary>
    /// True if the target sits directly below the source with matching left edges, which is how the
    /// graph stacks an item onto the first input of the one below.
    /// </summary>
    private static bool TryGetVerticalSnapPosOnCanvas(MagGraphMissingConnection connection, out Vector2 posOnCanvas)
    {
        posOnCanvas = default;

        Vector2 sourcePos, sourceSize, targetPos;
        if (connection.SourceMissingItem != null)
        {
            sourcePos = connection.SourceMissingItem.PosOnCanvas;
            sourceSize = connection.SourceMissingItem.Size;
        }
        else
        {
            sourcePos = connection.SourceItem!.DampedPosOnCanvas;
            sourceSize = connection.SourceItem.Size;
        }

        targetPos = connection.TargetMissingItem?.PosOnCanvas ?? connection.TargetItem!.DampedPosOnCanvas;

        const float tolerance = 2;
        if (MathF.Abs(sourcePos.X - targetPos.X) > tolerance || MathF.Abs(sourcePos.Y + sourceSize.Y - targetPos.Y) > tolerance)
            return false;

        posOnCanvas = new Vector2(targetPos.X + MagGraphItem.Width * 0.5f, targetPos.Y);
        return true;
    }
}
