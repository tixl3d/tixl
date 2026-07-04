using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    private void DrawSection(MagGraphSection magSection, ImDrawListPtr drawList, GraphUiContext context)
    {
        var canvas = context.View;

        var section = magSection.Section;
        var area = section.Collapsed
                       ? ImRect.RectWithSize(section.PosOnCanvas, new Vector2(section.Size.X, MagGraphItem.LineHeight))
                       :ImRect.RectWithSize(section.PosOnCanvas, section.Size) ;


        if (!IsRectVisible(area))
            return;

        // ImGui 1.91.2 ID-conflict guard: ensure all widgets in this section
        // get a unique ID per section so the "##sectionHeader" / "##resize"
        // labels don't collide between sections.
        ImGui.PushID(magSection.Id.GetHashCode());

        var pMin = TransformPosition(magSection.DampedPosOnCanvas);
        var dampedSize = section.Collapsed 
                             ? new Vector2( magSection.DampedSize.X,MagGraphItem.LineHeight)
                             : magSection.DampedSize;
        var pMax = TransformPosition(magSection.DampedPosOnCanvas + dampedSize);

        drawList.PushClipRect(pMin, pMax, true); // Start with a simple rectangular clip 
        // Background
        var backgroundColor = ColorVariations.SectionBackground.Apply(section.Color).Fade(0.8f);

        var rounding = 8;// * canvas.Scale.X; 
        var flags = ImDrawFlags.RoundCornersTop | ImDrawFlags.RoundCornersBottomLeft;


        drawList.AddRectFilled(pMin + Vector2.One,
                               pMax,
                               backgroundColor,
                               rounding, flags);

        var isNodeSelected = context.Selector.IsNodeSelected(section);

        
        // Outline
        var borderColor = isNodeSelected ? UiColors.ForegroundFull 
                                 : ColorVariations.SectionOutline.Apply(section.Color);
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
        
        {
            var positionInScreen = screenArea.Min  + new Vector2(-5,6) * T3Ui.UiScaleFactor;
            var labelPos = positionInScreen; // - new Vector2(2, Fonts.FontNormal.FontSize + 8);
            ImGui.SetCursorScreenPos(labelPos);
            bool isCollapsed = section.Collapsed;
            ImGui.PushID(section.Id.GetHashCode());
            if (CustomComponents.ToggleTwoIconsButton(ref isCollapsed, 
                                                      Icon.ChevronDown,
                                                      Icon.ChevronRight,
                                                      CustomComponents.ButtonStates.Emphasized,
                                                      CustomComponents.ButtonStates.Emphasized,
                                                      true, 
                                                      true))
            {
                if (isCollapsed)
                {
                    // Flag children as collapsed...
                    foreach (var item in context.Layout.Items.Values)
                    {
                        if (item.Variant != MagGraphItem.Variants.Operator || item.ChildUi == null)
                            continue;

                        
                        if(area.Contains(item.Area))
                            item.ChildUi.CollapsedIntoSectionFrameId = magSection.Id;
                    }
                }
                else
                {
                    // Reveal all children...
                    foreach (var item in context.Layout.Items.Values)
                    {
                        if (item.Variant != MagGraphItem.Variants.Operator || item.ChildUi == null)
                            continue;

                        if (item.ChildUi.CollapsedIntoSectionFrameId == magSection.Id)
                        {
                            item.ChildUi.CollapsedIntoSectionFrameId = Guid.Empty;
                        }
                            
                    }

                }
                context.Layout.FlagStructureAsChanged();
                section.Collapsed = !section.Collapsed;
            }
            ImGui.PopID();
        }

        
        ImGui.SetCursorScreenPos(clickableArea.Min );
        
        
        var isRenaming = context.ActiveSectionId == magSection.Id &&
                         context.StateMachine.CurrentState == GraphStates.RenameSection;
        if (!isRenaming)
        {        
            var headerSize = clickableArea.GetSize();
            if (headerSize.X < 1f) headerSize.X = 1f;
            if (headerSize.Y < 1f) headerSize.Y = 1f;
            ImGui.InvisibleButton("##sectionHeader", headerSize);

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
                context.ActiveSectionId = magSection.Id;
                context.StateMachine.SetState(GraphStates.DragSection, context);
            }
        }

        // Double-Click -> Rename
        var shouldRename = (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left));
        if (shouldRename)
        {
            context.ActiveSectionId = magSection.Id;
            context.StateMachine.SetState(GraphStates.RenameSection, context);
        }

        // Label and description
        if (context.ActiveSectionId != magSection.Id || context.StateMachine.CurrentState != GraphStates.RenameSection)
        {
            var labelHeight = 0f;
            var canvasScale = canvas.Scale.X;
            {
                if (!string.IsNullOrEmpty(section.Label))
                {
                    var fade = MathUtils.SmootherStep(0.1f, 0.2f, canvasScale) * 0.8f * _context.GraphOpacity;
                    var fontSize = canvasScale > 1
                                       ? Fonts.FontLarge.FontSize
                                       : canvasScale > 0.333 / Fonts.FontLarge.Scale
                                           ? Fonts.FontLarge.FontSize
                                           : Fonts.FontLarge.FontSize * canvasScale * 3;

                    drawList.AddText(Fonts.FontLarge,
                                     fontSize,
                                     pMin + new Vector2(8 + 10, 3) * T3Ui.UiScaleFactor,
                                     ColorVariations.OperatorLabel.Apply(section.Color.Fade(fade)),
                                     section.Label);
                    labelHeight = Fonts.FontLarge.FontSize;
                }
            }

            if (!string.IsNullOrEmpty(section.Title))
            {
                var font = section.Title.StartsWith("# ") ? Fonts.FontLarge : Fonts.FontNormal;
                drawList.PushClipRect(pMin, pMax, true);
                var labelPos = pMin + new Vector2(8, 8 + labelHeight) * T3Ui.UiScaleFactor;

                var fade = MathUtils.SmootherStep(0.25f, 0.6f, canvasScale) * 0.8f;
                var fontSize = canvasScale > 1
                                   ? font.FontSize
                                   : canvasScale > Fonts.FontSmall.Scale / Fonts.FontNormal.Scale
                                       ? font.FontSize
                                       : font.FontSize * canvasScale;
                drawList.AddText(font,
                                 fontSize,
                                 labelPos,
                                 ColorVariations.OperatorLabel.Apply(section.Color.Fade(fade)),
                                 section.Title);
                drawList.PopClipRect();
            }
        }

        // Resize handle
        {
            ImGui.PushID(magSection.Id.GetHashCode());
            
            var thumbSize = (int)10 * T3Ui.UiScaleFactor;

            ImGui.SetCursorScreenPos(screenArea.Max - new Vector2(11, 11) * T3Ui.UiScaleFactor);

            ImGui.InvisibleButton("##resize", new Vector2(10, 10) * T3Ui.UiScaleFactor);

            if (ImGui.IsItemHovered()){
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                context.ActiveSectionId = magSection.Id;
                context.StateMachine.SetState(GraphStates.ResizeSection, context);
            }
            drawList.AddTriangleFilled(screenArea.Max - new Vector2(11, 1) * T3Ui.UiScaleFactor, screenArea.Max - new Vector2(1, 11) * T3Ui.UiScaleFactor, screenArea.Max - new Vector2(1, 1) * T3Ui.UiScaleFactor, UiColors.BackgroundButton);
            drawList.PopClipRect();
            ImGui.PopID();
        }

        ImGui.PopID(); // outer per-section PushID
    }
}