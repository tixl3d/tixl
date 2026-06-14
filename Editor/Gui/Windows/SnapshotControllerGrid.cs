#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Midi;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Variations;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// The launchpad-style popup opened by clicking the index in the <see cref="SnapshotControlView"/>
/// selector bar. Lays the snapshots out by their <see cref="Variation.ActivationIndex"/> in a chosen
/// <see cref="ControllerGridLayout"/>, lets the user reassign indices by dragging a cell, and returns
/// the snapshot to apply when one is clicked.
/// </summary>
internal sealed class SnapshotControllerGrid
{
    internal void Open(Vector2 popupPosition)
    {
        _popupPosition = popupPosition;
        ImGui.OpenPopup(PopupId);
    }

    /// <summary>
    /// Draws the popup (when open) and returns the snapshot the user clicked to apply, or null.
    /// Hover-preview and index drag-reassignment are handled internally.
    /// </summary>
    internal Variation? Draw(SymbolVariationPool pool, Instance composition, Variation? active, IReadOnlyList<Variation> snapshots)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.SetNextWindowPos(_popupPosition);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8) * scale);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);

        Variation? picked = null;
        var hoveredAny = false;
        if (ImGui.BeginPopup(PopupId))
        {
            var layouts = ControllerGridLayouts.All;
            if (_gridLayoutIndex >= layouts.Count)
                _gridLayoutIndex = 0;
            var layout = layouts[_gridLayoutIndex];

            // Header row: title on the left, the layout dropdown and a doc button on the right.
            // The right cluster is drawn first (frame-height tall) so the title can be vertically
            // centered against it.
            var frameHeight = ImGui.GetFrameHeight();
            var iconSize = new Vector2(frameHeight, frameHeight);
            var headerStartX = ImGui.GetCursorPosX();
            var headerStartY = ImGui.GetCursorPosY();
            var headerGap = 4 * scale;

            var hasLayoutCombo = layouts.Count > 1;
            var comboWidth = 120 * scale;
            var clusterWidth = iconSize.X + (hasLayoutCombo ? comboWidth + headerGap : 0);
            // Flush the cluster's right edge to the content edge (RightAlign would inset it by an
            // extra window padding, leaving the doc button looking un-aligned).
            ImGui.SetCursorPosX(headerStartX + ImGui.GetContentRegionAvail().X - clusterWidth);
            if (hasLayoutCombo)
            {
                ImGui.SetNextItemWidth(comboWidth);
                var layoutIndex = _gridLayoutIndex;
                if (ImGui.Combo("##gridLayout", ref layoutIndex, GetLayoutNames()))
                    _gridLayoutIndex = layoutIndex;

                ImGui.SameLine(0, headerGap);
            }

            DocumentationButton.Draw(DocId, WikiUrl, iconSize);

            ImGui.SetCursorPos(new Vector2(headerStartX,
                                           headerStartY + MathF.Max(0, (frameHeight - ImGui.GetTextLineHeight()) * 0.5f)));
            CustomComponents.StylizedText("Edit controller index", Fonts.FontNormal, UiColors.TextMuted);

            ImGui.SetCursorPosY(headerStartY + frameHeight);
            FormInputs.AddVerticalSpace(4);

            var cell = 34 * scale;
            var gap = 3 * scale;
            var drawList = ImGui.GetWindowDrawList();

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
            ImGui.PushFont(Fonts.FontSmall);

            var mousePos = ImGui.GetMousePos();
            var draggedVariation = _gridDragSourceId != Guid.Empty ? FindSnapshotById(snapshots, _gridDragSourceId) : null;
            var isDragging = draggedVariation != null;
            var dropTargetIndex = -1;

            for (var row = 0; row < layout.Rows; row++)
            {
                for (var col = 0; col < layout.Columns; col++)
                {
                    if (col > 0)
                        ImGui.SameLine();

                    var index = layout.CellToIndex(row, col);
                    var snapshot = FindByActivationIndex(snapshots, index);

                    ImGui.PushID(index);
                    var clicked = ImGui.InvisibleButton("##cell", new Vector2(cell, cell));
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    var isActive = snapshot != null && snapshot == active;
                    var isHovered = ImGui.IsItemHovered();

                    // A filled slot is draggable — the move cursor signals that (a pointing hand
                    // would imply a plain click).
                    if (snapshot != null && isHovered)
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

                    // A populated cell can be dragged to reassign its controller index.
                    if (snapshot != null && !isDragging && ImGui.IsItemActive()
                        && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4 * scale))
                    {
                        _gridDragSourceId = snapshot.Id;
                        draggedVariation = snapshot;
                        isDragging = true;
                    }

                    var isDragSource = isDragging && snapshot != null && snapshot.Id == _gridDragSourceId;
                    var isDropTarget = isDragging && !isDragSource && new ImRect(min, max).Contains(mousePos);
                    if (isDropTarget)
                        dropTargetIndex = index;

                    // Mirror the APC Mini's LEDs: a filled slot is green (a controllable snapshot
                    // lives there), the one that's live is magenta. Keeps "green = controlled".
                    var bg = snapshot == null ? UiColors.BackgroundFull.Fade(0.4f)
                             : isActive ? UiColors.StatusAttention
                             : isHovered && !isDragging ? UiColors.StatusControlled
                             : UiColors.StatusControlled.Fade(0.7f);
                    drawList.AddRectFilled(min, max, bg, 4 * scale);

                    if (isDropTarget)
                        drawList.AddRect(min, max, UiColors.ForegroundFull, 4 * scale, ImDrawFlags.None, 2 * scale);

                    var label = index.ToString("00");
                    var textColor = snapshot == null ? UiColors.TextMuted.Fade(0.4f) : UiColors.ForegroundFull;
                    var textSize = ImGui.CalcTextSize(label);
                    drawList.AddText(((min + max) / 2 - textSize / 2).Floor(), textColor, label);

                    // Dim the lifted cell uniformly with a scrim — fading bg/text by alpha instead
                    // makes the muted (inactive) cells vanish while the bright active one survives.
                    if (isDragSource)
                        drawList.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.55f), 4 * scale);

                    // Hover preview, tooltip and click-to-apply stand down while a drag is in progress.
                    if (snapshot != null && isHovered && !isDragging)
                    {
                        hoveredAny = true;
                        CustomComponents.TooltipForLastItem(GetTitleOrDefault(snapshot));

                        if (UserSettings.Config.VariationHoverPreview && _gridPreviewedId != snapshot.Id)
                        {
                            pool.BeginHover(composition, snapshot);
                            _gridPreviewedId = snapshot.Id;
                        }

                        if (clicked)
                        {
                            picked = snapshot;
                            ImGui.CloseCurrentPopup();
                        }
                    }

                    ImGui.PopID();
                }
            }

            ImGui.PopFont();
            ImGui.PopStyleVar();

            if (isDragging)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

                // Carry a small chip with the dragged index and name so the gesture reads clearly.
                var chipLabel = draggedVariation!.ActivationIndex.ToString("00") + "  " + GetTitleOrDefault(draggedVariation);
                var chipPos = mousePos + new Vector2(12, 6) * scale;
                var chipPad = new Vector2(5, 2) * scale;
                var chipSize = ImGui.CalcTextSize(chipLabel);
                var foreground = ImGui.GetForegroundDrawList();
                foreground.AddRectFilled(chipPos - chipPad, chipPos + chipSize + chipPad, UiColors.BackgroundButton, 3 * scale);
                foreground.AddText(chipPos, UiColors.ForegroundFull, chipLabel);

                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    if (dropTargetIndex >= 0 && dropTargetIndex != draggedVariation.ActivationIndex)
                        ReassignActivationIndex(pool, snapshots, draggedVariation, dropTargetIndex);

                    _gridDragSourceId = Guid.Empty;
                }
            }

            if (!hoveredAny && _gridPreviewedId != Guid.Empty)
            {
                pool.StopHover();
                _gridPreviewedId = Guid.Empty;
            }

            ImGui.EndPopup();
        }
        else if (_gridPreviewedId != Guid.Empty)
        {
            pool.StopHover();
            _gridPreviewedId = Guid.Empty;
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        return picked;
    }

    private string GetLayoutNames()
    {
        if (_layoutNamesForCombo != null)
            return _layoutNamesForCombo;

        var names = "";
        foreach (var layout in ControllerGridLayouts.All)
            names += layout.Name + "\0";

        _layoutNamesForCombo = names;
        return _layoutNamesForCombo;
    }

    private static Variation? FindByActivationIndex(IReadOnlyList<Variation> snapshots, int activationIndex)
    {
        foreach (var s in snapshots)
        {
            if (s.ActivationIndex == activationIndex)
                return s;
        }

        return null;
    }

    private static Variation? FindSnapshotById(IReadOnlyList<Variation> snapshots, Guid id)
    {
        foreach (var s in snapshots)
        {
            if (s.Id == id)
                return s;
        }

        return null;
    }

    private static void ReassignActivationIndex(SymbolVariationPool pool, IReadOnlyList<Variation> snapshots, Variation dragged, int newIndex)
    {
        var displaced = FindByActivationIndex(snapshots, newIndex);
        UndoRedoStack.AddAndExecute(new ChangeVariationActivationIndexCommand(pool, dragged, newIndex, displaced));
    }

    private static string GetTitleOrDefault(Variation variation)
    {
        return string.IsNullOrEmpty(variation.Title) || variation.Title == "untitled" ? "Untitled" : variation.Title!;
    }

    private const string PopupId = "##controllerGrid";
    private const string DocId = "ControllerIndex";
    private const string WikiUrl = "https://github.com/tixl3d/tixl/wiki/help.PresetsAndSnapshots";

    private Vector2 _popupPosition;
    private Guid _gridPreviewedId;
    private Guid _gridDragSourceId;
    private int _gridLayoutIndex;
    private string? _layoutNamesForCombo;
}
