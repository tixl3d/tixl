using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    private void DrawAnnotation(MagGraphAnnotation magAnnotation, ImDrawListPtr drawList, GraphUiContext context)
    {
        var canvas = context.View;
        var area = ImRect.RectWithSize(magAnnotation.Annotation.PosOnCanvas,
                                       magAnnotation.Annotation.Size);

        if (!IsRectVisible(area))
            return;

        var pMin = TransformPosition(magAnnotation.DampedPosOnCanvas);
        var pMax = TransformPosition(magAnnotation.DampedPosOnCanvas + magAnnotation.DampedSize);

        drawList.PushClipRect(pMin, pMax, true); // Start with a simple rectangular clip 
        // Background
        var backgroundColor = ColorVariations.AnnotationBackground.Apply(magAnnotation.Annotation.Color).Fade(0.8f);

        var rounding = 8;// * canvas.Scale.X; 
        var flags = ImDrawFlags.RoundCornersTop | ImDrawFlags.RoundCornersBottomLeft;


        drawList.AddRectFilled(pMin + Vector2.One,
                               pMax,
                               backgroundColor,
                               rounding, flags);

        var isNodeSelected = context.Selector.IsNodeSelected(magAnnotation.Annotation);

        
        // Outline
        var borderColor = isNodeSelected ? UiColors.ForegroundFull 
                                 : ColorVariations.AnnotationOutline.Apply(magAnnotation.Annotation.Color);
        drawList.AddRect(pMin,
                         pMax,
                         borderColor.Fade(_context.GraphOpacity),
                         rounding,
                         flags);

        // Keep height of title area at a minimum height when zooming out
        var screenArea = new ImRect(pMin, pMax);

        var clickableArea = new ImRect(pMin, pMax);
        clickableArea.Max.Y = clickableArea.Min.Y + MathF.Min(16 * T3Ui.UiScaleFactor, screenArea.GetHeight());

        // Header
        ImGui.SetCursorScreenPos(clickableArea.Min);
        ImGui.InvisibleButton("##annotationHeader", clickableArea.GetSize());

        DrawUtils.DebugItemRect();
        var isHeaderHovered = ImGui.IsItemHovered() && context.StateMachine.CurrentState == GraphStates.Default;
        if (isHeaderHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        //const float backgroundAlpha = 0.2f;
        const float headerHoverAlpha = 0.1f;
        drawList.AddRectFilled(clickableArea.Min, clickableArea.Max,
                               UiColors.ForegroundFull.Fade(isHeaderHovered
                                                                ? headerHoverAlpha
                                                                : 0), rounding, ImDrawFlags.RoundCornersTop);

        // Clicked -> Drag
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.GetIO().KeyAlt)
        {
            context.ActiveAnnotationId = magAnnotation.Id;
            context.StateMachine.SetState(GraphStates.DragAnnotation, context);
        }

        // Double-Click -> Rename
        var shouldRename = (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left));
        if (shouldRename)
        {
            context.ActiveAnnotationId = magAnnotation.Id;
            context.StateMachine.SetState(GraphStates.RenameAnnotation, context);
        }

        // Label and description
        if (context.ActiveAnnotationId != magAnnotation.Id || context.StateMachine.CurrentState != GraphStates.RenameAnnotation)
        {
            var labelHeight = 0f;
            var canvasScale = canvas.Scale.X;
            {
                if (!string.IsNullOrEmpty(magAnnotation.Annotation.Label))
                {
                    var fade = MathUtils.SmootherStep(0.1f, 0.2f, canvasScale) * 0.8f * _context.GraphOpacity;
                    var fontSize = canvasScale > 1
                                       ? Fonts.FontLarge.FontSize
                                       : canvasScale > 0.333 / Fonts.FontLarge.Scale
                                           ? Fonts.FontLarge.FontSize
                                           : Fonts.FontLarge.FontSize * canvasScale * 3;

                    drawList.AddText(Fonts.FontLarge,
                                     fontSize,
                                     pMin + new Vector2(8, 3),
                                     ColorVariations.OperatorLabel.Apply(magAnnotation.Annotation.Color.Fade(fade)),
                                     magAnnotation.Annotation.Label);
                    labelHeight = Fonts.FontLarge.FontSize;
                }
            }

            if (!string.IsNullOrEmpty(magAnnotation.Annotation.Title))
            {
                var font = magAnnotation.Annotation.Title.StartsWith("# ") ? Fonts.FontLarge : Fonts.FontNormal;
                drawList.PushClipRect(pMin, pMax, true);
                var labelPos = pMin + new Vector2(8, 8 + labelHeight) * T3Ui.DisplayScaleFactor;

                var fade = MathUtils.SmootherStep(0.25f, 0.6f, canvasScale) * 0.8f;
                var fontSize = canvasScale > 1
                                   ? font.FontSize
                                   : canvasScale > Fonts.FontSmall.Scale / Fonts.FontNormal.Scale
                                       ? font.FontSize
                                       : font.FontSize * canvasScale;
                drawList.AddText(font,
                                 fontSize,
                                 labelPos,
                                 ColorVariations.OperatorLabel.Apply(magAnnotation.Annotation.Color.Fade(fade)),
                                 magAnnotation.Annotation.Title);
                drawList.PopClipRect();
            }
        }

        // Resize handle
        {
            ImGui.PushID(magAnnotation.Id.GetHashCode());
            
            var thumbSize = (int)10 * T3Ui.UiScaleFactor;

            ImGui.SetCursorScreenPos(screenArea.Max - new Vector2(11, 11) * T3Ui.UiScaleFactor);

            ImGui.InvisibleButton("##resize", new Vector2(10, 10) * T3Ui.UiScaleFactor);

            if (ImGui.IsItemHovered()){
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                context.ActiveAnnotationId = magAnnotation.Id;
                context.StateMachine.SetState(GraphStates.ResizeAnnotation, context);
            }
            drawList.AddTriangleFilled(screenArea.Max - new Vector2(11, 1) * T3Ui.UiScaleFactor, screenArea.Max - new Vector2(1, 11) * T3Ui.UiScaleFactor, screenArea.Max - new Vector2(1, 1) * T3Ui.UiScaleFactor, UiColors.BackgroundButton);
            drawList.PopClipRect();
            ImGui.PopID();
        }
    }
}