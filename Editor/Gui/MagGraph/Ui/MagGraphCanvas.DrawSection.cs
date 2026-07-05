using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Interaction;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Commands.Sections;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Ui;

internal sealed partial class MagGraphView
{
    private void DrawSection(MagGraphSection magSection, ImDrawListPtr drawList, GraphUiContext context)
    {
        var canvas = context.View;

        var section = magSection.Section;
        var area = section.Collapsed
                       ? ImRect.RectWithSize(section.PosOnCanvas, new Vector2(section.Size.X, MagGraphItem.LineHeight))
                       : ImRect.RectWithSize(section.PosOnCanvas, section.Size);

        if (!IsRectVisible(area))
            return;

        // ImGui 1.91.2 ID-conflict guard: ensure all widgets in this section
        // get a unique ID per section so the "##sectionHeader" / "##resize"
        // labels don't collide between sections.
        ImGui.PushID(magSection.Id.GetHashCode());

        var pMin = TransformPosition(magSection.DampedPosOnCanvas);
        var dampedSize = section.Collapsed
                             ? new Vector2(magSection.DampedSize.X, MagGraphItem.LineHeight)
                             : magSection.DampedSize;
        var pMax = TransformPosition(magSection.DampedPosOnCanvas + dampedSize);

        drawList.PushClipRect(pMin, pMax, true); // Start with a simple rectangular clip
        // Background
        var backgroundColor = ColorVariations.SectionBackground.Apply(section.Color).Fade(0.8f);

        var rounding = 8; // * canvas.Scale.X;
        var flags = ImDrawFlags.RoundCornersTop | ImDrawFlags.RoundCornersBottomLeft;

        drawList.AddRectFilled(pMin + Vector2.One,
                               pMax,
                               backgroundColor,
                               rounding, flags);

        var isNodeSelected = context.Selector.IsNodeSelected(section);

        // Outline
        var borderColor = isNodeSelected
                              ? UiColors.ForegroundFull
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

        var canvasScale = canvas.Scale.X;

        // The title font size also drives the collapse toggle and the label indent

        float titleFontSize = 0;// Fonts.FontLarge.FontSize;

        if (section.Collapsed)
        {
            titleFontSize = canvasScale > 1
                ? Fonts.FontLarge.FontSize
                : canvasScale > 1f / Fonts.FontLarge.Scale
                    ? Fonts.FontLarge.FontSize
                    : Fonts.FontLarge.FontSize * canvasScale * 1;
        }
        else
        {
            titleFontSize = canvasScale > 1
                ? Fonts.FontLarge.FontSize
                : canvasScale > 0.333f / Fonts.FontLarge.Scale
                    ? Fonts.FontLarge.FontSize
                    : Fonts.FontLarge.FontSize * canvasScale * 3;
        }
    

        // Collapse toggle - scaled with the title font, with a clickable minimum when zoomed out.
        // Emitted before header and resize handles so it wins their overlap zones.
        var toggleSize = MathF.Max(titleFontSize, 13 * T3Ui.UiScaleFactor);
        {
            var togglePos = pMin + new Vector2(4, 3) * T3Ui.UiScaleFactor;
            ImGui.SetCursorScreenPos(togglePos);
            if (ImGui.InvisibleButton("##collapse", new Vector2(toggleSize, toggleSize)))
            {
                ToggleSectionCollapse(section, context);
            }

            var toggleColor = ColorVariations.OperatorLabel.Apply(section.Color)
                                             .Fade(ImGui.IsItemHovered() ? 1 : 0.7f);
            Icons.DrawIconAtScreenPosition(section.Collapsed ? Icon.ChevronRight : Icon.ChevronDown,
                                           togglePos,
                                           new Vector2(toggleSize, toggleSize),
                                           drawList,
                                           toggleColor);
        }

        // Resize handles on all edges and corners (ImGui gives hover to the first item
        // submitted at a position, so corners come before edges, and all before the header)
        if (!section.Collapsed)
        {
            var corner = 13 * T3Ui.UiScaleFactor;
            var edge = 5 * T3Ui.UiScaleFactor;

            // On small frames only the bottom-right corner is offered - full handles 
            // would swallow most of the frame's clickable area
            var largeEnough = screenArea.GetWidth() > 6 * corner && screenArea.GetHeight() > 4 * corner;
            if (largeEnough)
            {
                // No top-left corner handle - the collapse toggle occupies that spot;
                // top-left resizing works via the left and top edge strips
                EmitSectionResizeHandle(context, magSection, "##resizeTR", new ImRect(new Vector2(pMax.X - corner, pMin.Y), new Vector2(pMax.X, pMin.Y + corner)), SectionResizing.Handles.TopRight);
                EmitSectionResizeHandle(context, magSection, "##resizeBL", new ImRect(new Vector2(pMin.X, pMax.Y - corner), new Vector2(pMin.X + corner, pMax.Y)), SectionResizing.Handles.BottomLeft);
            }

            EmitSectionResizeHandle(context, magSection, "##resizeBR", new ImRect(pMax - new Vector2(corner, corner), pMax), SectionResizing.Handles.BottomRight);

            if (largeEnough)
            {
                EmitSectionResizeHandle(context, magSection, "##resizeL", new ImRect(new Vector2(pMin.X, pMin.Y + corner), new Vector2(pMin.X + edge, pMax.Y - corner)), SectionResizing.Handles.Left);
                EmitSectionResizeHandle(context, magSection, "##resizeR", new ImRect(new Vector2(pMax.X - edge, pMin.Y + corner), new Vector2(pMax.X, pMax.Y - corner)), SectionResizing.Handles.Right);
                EmitSectionResizeHandle(context, magSection, "##resizeT", new ImRect(new Vector2(pMin.X + corner, pMin.Y - edge), new Vector2(pMax.X - corner, pMin.Y - 1)), SectionResizing.Handles.Top);
                EmitSectionResizeHandle(context, magSection, "##resizeB", new ImRect(new Vector2(pMin.X + corner, pMax.Y - edge), new Vector2(pMax.X - corner, pMax.Y)), SectionResizing.Handles.Bottom);
            }

            // Bottom-right affordance triangle
            drawList.AddTriangleFilled(screenArea.Max - new Vector2(11, 1) * T3Ui.UiScaleFactor,
                                       screenArea.Max - new Vector2(1, 11) * T3Ui.UiScaleFactor,
                                       screenArea.Max - new Vector2(1, 1) * T3Ui.UiScaleFactor,
                                       UiColors.BackgroundButton);
        }

        ImGui.SetCursorScreenPos(clickableArea.Min);

        // When zoomed in so the frame covers most of the view, the user is working *inside*
        // the section - an invisible header row along the top edge would swallow clicks
        // meant for fence-selection (issue #1037). Grabbing it again requires zooming out.
        var visibleWidth = MathF.Min(pMax.X, canvas.WindowPos.X + canvas.WindowSize.X) - MathF.Max(pMin.X, canvas.WindowPos.X);
        var visibleHeight = MathF.Min(pMax.Y, canvas.WindowPos.Y + canvas.WindowSize.Y) - MathF.Max(pMin.Y, canvas.WindowPos.Y);
        var viewCoverage = MathF.Max(0, visibleWidth) * MathF.Max(0, visibleHeight)
                           / (canvas.WindowSize.X * canvas.WindowSize.Y);
        var headerEnabled = viewCoverage < 0.7f;

        var isRenaming = context.ActiveSectionId == magSection.Id &&
                         context.StateMachine.CurrentState == GraphStates.RenameSection;
        if (!isRenaming && headerEnabled)
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

            // Double-Click -> Rename
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                context.ActiveSectionId = magSection.Id;
                context.StateMachine.SetState(GraphStates.RenameSection, context);
            }
        }

        // Label and description
        if (context.ActiveSectionId != magSection.Id || context.StateMachine.CurrentState != GraphStates.RenameSection)
        {
            var labelHeight = 0f;
            {
                if (!string.IsNullOrEmpty(section.Label))
                {
                    var fade = MathUtils.SmootherStep(0.1f, 0.2f, canvasScale) * 0.8f * _context.GraphOpacity;
                    drawList.AddText(Fonts.FontLarge,
                                     titleFontSize,
                                     pMin + new Vector2(toggleSize + 8 * T3Ui.UiScaleFactor, 3 * T3Ui.UiScaleFactor),
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

        drawList.PopClipRect();
        ImGui.PopID(); // outer per-section PushID
    }

    /// <summary>
    /// Collapsing just folds the frame. Expanding reveals the stored bounds again and
    /// pushes neighboring frames and loose ops below out of the way — flag flip and
    /// pushes form one undoable unit.
    /// </summary>
    private static void ToggleSectionCollapse(Section section, GraphUiContext context)
    {
        var symbolUi = context.CompositionInstance.GetSymbolUi();
        if (!section.Collapsed)
        {
            UndoRedoStack.AddAndExecute(new ChangeSectionCollapseCommand(symbolUi, section, collapsed: true));
        }
        else
        {
            var macro = new MacroCommand("Expand Section");
            macro.AddAndExecCommand(new ChangeSectionCollapseCommand(symbolUi, section, collapsed: false));

            var expandedBounds = ImRect.RectWithSize(section.PosOnCanvas, section.Size);
            SectionTree.ResolveBoundsExpansion(symbolUi, section, expandedBounds, Vector2.UnitY, macro, context.Selector);

            UndoRedoStack.Add(macro);
        }

        context.Layout.FlagStructureAsChanged();
    }

    private static void EmitSectionResizeHandle(GraphUiContext context, MagGraphSection magSection, string id, ImRect rect,
                                                SectionResizing.Handles handles)
    {
        if (rect.GetWidth() < 1f || rect.GetHeight() < 1f)
            return;

        ImGui.SetCursorScreenPos(rect.Min);
        ImGui.InvisibleButton(id, rect.GetSize());
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(SectionResizing.GetCursorForHandles(handles));
            ImGui.GetWindowDrawList().AddRectFilled(rect.Min, rect.Max, UiColors.ForegroundFull.Fade(0.1f));
        }

        if (!ImGui.IsItemClicked(ImGuiMouseButton.Left))
            return;

        SectionResizing.ActiveHandles = handles;
        context.ActiveSectionId = magSection.Id;
        context.StateMachine.SetState(GraphStates.ResizeSection, context);
    }
}
