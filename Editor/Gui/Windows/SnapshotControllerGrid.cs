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
using T3.Editor.Gui.UiHelpers.Thumbnails;
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
            // Every controller maps the canonical reading-order index onto its own pads, so the grid
            // always shows reading order.
            var layout = ControllerGridLayouts.ReadingOrder;

            // Header row: title on the left, a settings gear + doc button on the right. The right
            // cluster is drawn first (frame-height tall) so the title can be vertically centered.
            var frameHeight = ImGui.GetFrameHeight();
            var iconSize = new Vector2(frameHeight, frameHeight);
            var headerStartX = ImGui.GetCursorPosX();
            var headerStartY = ImGui.GetCursorPosY();
            var headerGap = 4 * scale;

            // Flush the cluster's right edge to the content edge (RightAlign would inset it by an
            // extra window padding, leaving the icons looking un-aligned).
            var clusterWidth = 2 * iconSize.X + headerGap;
            ImGui.SetCursorPosX(headerStartX + ImGui.GetContentRegionAvail().X - clusterWidth);

            if (CustomComponents.IconButton(Icon.Settings2, iconSize, CustomComponents.ButtonStates.Default))
                ImGui.OpenPopup(SettingsPopupId);
            CustomComponents.TooltipForLastItem("Grid settings");
            ImGui.SameLine(0, headerGap);

            DocumentationButton.Draw(DocId, WikiUrl, iconSize);

            DrawSettingsPopup();

            ImGui.SetCursorPos(new Vector2(headerStartX,
                                           headerStartY + MathF.Max(0, (frameHeight - ImGui.GetTextLineHeight()) * 0.5f)));
            CustomComponents.StylizedText("Edit controller index", Fonts.FontNormal, UiColors.TextMuted);

            ImGui.SetCursorPosY(headerStartY + frameHeight);
            FormInputs.AddVerticalSpace(4);

            // Thumbnail cells need the 4:3 preview area; index-only cells can be much smaller, square
            // pads like the hardware.
            var cellW = (_showThumbnails ? 68 : 34) * scale;
            var cellH = (_showThumbnails ? 51 : 34) * scale;
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
                    var clicked = ImGui.InvisibleButton("##cell", new Vector2(cellW, cellH));
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
                    // In thumbnail mode the colour moves to the cell border so the image stays clear.
                    var stateColor = isActive ? UiColors.StatusAttention
                                     : isHovered && !isDragging ? UiColors.StatusControlled
                                     : UiColors.StatusControlled.Fade(0.7f);

                    if (_showThumbnails && snapshot != null)
                    {
                        drawList.AddRectFilled(min, max, UiColors.BackgroundFull, 4 * scale);
                        var thumb = ThumbnailManager.GetThumbnail(snapshot.Id, composition.Symbol.SymbolPackage,
                                                                  ThumbnailManager.Categories.PackageMeta,
                                                                  fallbackCategory: ThumbnailManager.Categories.Temp);
                        if (thumb.IsReady && ThumbnailManager.AtlasSrv != null)
                            drawList.AddImageRounded(ThumbnailManager.AtlasSrv.NativePointer, min, max,
                                                     thumb.UvMin, thumb.UvMax, Color.White, 4 * scale);

                        drawList.AddRect(min, max, stateColor, 4 * scale, ImDrawFlags.None, 2 * scale);
                    }
                    else
                    {
                        var bg = snapshot == null ? UiColors.BackgroundFull.Fade(0.4f) : stateColor;
                        drawList.AddRectFilled(min, max, bg, 4 * scale);
                    }

                    if (isDropTarget)
                        drawList.AddRect(min, max, UiColors.ForegroundFull, 4 * scale, ImDrawFlags.None, 2 * scale);

                    var label = index.ToString("00");
                    var textColor = snapshot == null ? UiColors.TextMuted.Fade(0.4f) : UiColors.ForegroundFull;
                    var textSize = ImGui.CalcTextSize(label);
                    var textPos = ((min + max) / 2 - textSize / 2).Floor();

                    // A 1px drop shadow keeps the index legible over a thumbnail
                    // without a backing box that would obscure the image.
                    drawList.AddText(textPos + new Vector2(1, 1) * scale, UiColors.BackgroundFull.Fade(0.5f), label);
                    drawList.AddText(textPos, textColor, label);

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

    private void DrawSettingsPopup()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6) * T3Ui.UiScaleFactor);
        if (ImGui.BeginPopup(SettingsPopupId))
        {
            CustomComponents.DrawMenuItem(_showThumbnailsItemId, "Show Thumbnails", ref _showThumbnails, reserveIconColumn: false);
            ImGui.EndPopup();
        }

        ImGui.PopStyleVar();
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
    private const string SettingsPopupId = "##controllerGridSettings";
    private const string DocId = "ControllerIndex";
    private const string WikiUrl = "https://github.com/tixl3d/tixl/wiki/help.PresetsAndSnapshots";
    private static readonly int _showThumbnailsItemId = "controllerGridShowThumbnails".GetHashCode();

    private Vector2 _popupPosition;
    private Guid _gridPreviewedId;
    private Guid _gridDragSourceId;
    private bool _showThumbnails;
}
