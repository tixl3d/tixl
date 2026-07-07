#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Variations;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.Variations;

/// <summary>
/// A searchable, thumbnailed picker for variations (snapshots or presets) — a richer replacement
/// for a plain combo dropdown. Phase 1: list mode (search + rows + click-to-apply). Later phases
/// add an embedded-canvas mode, hover preview, drag-to-reorder and activation faders.
/// </summary>
/// <remarks>Reusable: the snapshot control view is the first host; a preset selector can adopt it.</remarks>
internal sealed class VariationPicker
{
    /// <summary>
    /// Draws a combo-style trigger showing <paramref name="label"/>; opens a popup listing
    /// <paramref name="variations"/> in canvas order. Returns the chosen variation, or null.
    /// </summary>
    public Variation? Draw(IReadOnlyList<Variation> variations, SymbolVariationPool pool, Instance composition,
                           Variation? selected, string label, float width, out bool renameRequested, VariationBaseCanvas? canvas = null,
                           Color? labelColor = null)
    {
        Variation? chosen = null;
        renameRequested = false;
        var scale = T3Ui.UiScaleFactor;
        var frameHeight = ImGui.GetFrameHeight();

        var triggerClicked = DrawTriggerButton(label, labelColor ?? UiColors.Text, width, frameHeight);
        // Double-clicking the trigger renames rather than opening the picker (caller's concern).
        renameRequested = ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        var triggerMin = ImGui.GetItemRectMin();
        var triggerMax = ImGui.GetItemRectMax();
        if (triggerClicked && !renameRequested)
        {
            ImGui.OpenPopup(PopupId);
            _searchString = string.Empty;
            _justOpened = true;
            _initHighlight = true;
            _weightDragId = Guid.Empty;
            // Persist the live weight vector across opens; only seed it the first time (the pool
            // otherwise resets it to the active variation on every activation).
            if (!pool.HasBlendWeights)
                pool.ResetBlendWeights(selected?.Id ?? Guid.Empty);
        }

        // Wider than the trigger, but still left-aligned to it.
        _popupWidth = MathF.Max(width, 400 * scale);

        // Estimate the popup height so it can be kept on-screen.
        float contentHeight;
        if (_mode == Modes.Canvas && canvas != null)
        {
            contentHeight = CanvasHeight * scale;
        }
        else
        {
            var rowStride = RowHeight * scale + RowSpacing * scale;
            var matchCount = 0;
            foreach (var v in variations)
            {
                if (Matches(v, _searchString))
                    matchCount++;
            }

            contentHeight = MathF.Min(matchCount * rowStride, MaxListHeight * scale);
        }

        var estHeight = 12 * scale + frameHeight + 10 * scale + contentHeight; // padding + search row + separator + content

        // Drop below the trigger; shift left / flip above to stay within the viewport's work area.
        var viewport = ImGui.GetMainViewport();
        var workMin = viewport.WorkPos;
        var workMax = viewport.WorkPos + viewport.WorkSize;
        var popupPos = new Vector2(triggerMin.X, triggerMax.Y + 2 * scale);
        if (popupPos.X + _popupWidth > workMax.X)
            popupPos.X = workMax.X - _popupWidth;
        popupPos.X = MathF.Max(popupPos.X, workMin.X);
        if (popupPos.Y + estHeight > workMax.Y)
        {
            var above = triggerMin.Y - 2 * scale - estHeight;
            popupPos.Y = above >= workMin.Y ? above : MathF.Max(workMin.Y, workMax.Y - estHeight);
        }

        ImGui.SetNextWindowPos(popupPos);
        ImGui.SetNextWindowSizeConstraints(new Vector2(_popupWidth, 0), new Vector2(_popupWidth, 800 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6) * scale);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);

        if (ImGui.BeginPopup(PopupId))
        {
            var hasCanvas = canvas != null;

            // Top row: search + view-mode toggles + hover-preview toggle.
            var toolCount = hasCanvas ? 3 : 2;
            var toolsWidth = toolCount * (frameHeight + ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetNextItemWidth(MathF.Max(40 * scale, ImGui.GetContentRegionAvail().X - toolsWidth));
            if (_justOpened)
            {
                ImGui.SetKeyboardFocusHere();
                _justOpened = false;
            }

            var searchBefore = _searchString;
            ImGui.InputTextWithHint("##variationSearch", "Search…", ref _searchString, 128);

            ImGui.SameLine();
            if (CustomComponents.IconButton(Icon.ViewList, Vector2.Zero,
                                            _mode == Modes.List ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
                _mode = Modes.List;
            CustomComponents.TooltipForLastItem("List");

            if (hasCanvas)
            {
                ImGui.SameLine();
                if (CustomComponents.IconButton(Icon.ViewGrid, Vector2.Zero,
                                                _mode == Modes.Canvas ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
                    _mode = Modes.Canvas;
                // The embedded canvas shares the pinned output with the Variations window; its live
                // preview only updates while that window is closed.
                CustomComponents.TooltipForLastItem("Canvas view (live preview needs the Variations window closed)");
            }

            ImGui.SameLine();
            var hoverPreview = UserSettings.Config.VariationHoverPreview;
            if (CustomComponents.ToggleIconButton(ref hoverPreview, Icon.HoverScrub, Vector2.Zero))
                UserSettings.Config.VariationHoverPreview = hoverPreview;
            CustomComponents.TooltipForLastItem("Preview on hover");

            CustomComponents.SeparatorLine();

            SortByActivationIndex(variations);
            _filtered.Clear();
            foreach (var v in _sorted)
            {
                if (Matches(v, _searchString))
                    _filtered.Add(v);
            }

            if (_searchString != searchBefore)
                _highlightIndex = 0;

            if (_initHighlight)
            {
                _highlightIndex = Math.Max(0, _filtered.IndexOf(selected!));
                _initHighlight = false;
            }

            var inCanvasMode = hasCanvas && _mode == Modes.Canvas;
            if (inCanvasMode)
            {
                ImGui.BeginChild("##variationCanvas", new Vector2(0, CanvasHeight * scale));
                canvas!.DrawBaseCanvas(ImGui.GetWindowDrawList(), hideHeader: true);
                ImGui.EndChild();
            }
            else
            {
                chosen = DrawList(composition, pool, selected, canvas);
            }

            // Hover preview: while enabled in list mode, apply the highlighted variation to the
            // output (restoring on change/close) — the same transient mechanism as the Variations
            // window. The canvas drives its own preview, so skip there.
            if (UserSettings.Config.VariationHoverPreview && !inCanvasMode && _weightDragId == Guid.Empty && _filtered.Count > 0)
            {
                var toPreview = _filtered[Math.Clamp(_highlightIndex, 0, _filtered.Count - 1)];
                if (toPreview.Id != _previewedVariationId)
                {
                    pool.BeginHover(composition, toPreview);
                    _previewedVariationId = toPreview.Id;
                }
            }
            else if (_previewedVariationId != Guid.Empty)
            {
                pool.StopHover();
                _previewedVariationId = Guid.Empty;
            }

            ImGui.EndPopup();
        }
        else
        {
            // Popup closed — drop any lingering hover preview and bake a pending fader blend.
            if (_previewedVariationId != Guid.Empty)
            {
                pool.StopHover();
                _previewedVariationId = Guid.Empty;
            }

            if (_weightDragId != Guid.Empty)
            {
                pool.ApplyCurrentBlend();
                _weightDragId = Guid.Empty;
            }
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        return chosen;
    }

    private Variation? DrawList(Instance composition, SymbolVariationPool pool, Variation? selected, VariationBaseCanvas? canvas)
    {
        Variation? chosen = null;
        var scale = T3Ui.UiScaleFactor;

        // Drag-to-reorder swaps ActivationIndex, so it only needs the unfiltered list (an active search
        // filters it, which would make adjacent-row swaps lie).
        var reorderEnabled = string.IsNullOrEmpty(_searchString) && _filtered.Count > 1;

        // Keyboard navigation — single-line search input leaves the arrows/Enter free.
        var scrollToHighlight = false;
        if (_filtered.Count > 0)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            {
                _highlightIndex = Math.Min(_highlightIndex + 1, _filtered.Count - 1);
                scrollToHighlight = true;
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            {
                _highlightIndex = Math.Max(_highlightIndex - 1, 0);
                scrollToHighlight = true;
            }

            _highlightIndex = Math.Clamp(_highlightIndex, 0, _filtered.Count - 1);

            if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
            {
                chosen = _filtered[_highlightIndex];
                ImGui.CloseCurrentPopup();
            }
        }

        var listHeight = MathF.Min(_filtered.Count * (RowHeight * scale + RowSpacing * scale), MaxListHeight * scale);

        // One highlight, driven by the keyboard. The mouse only moves it when the mouse actually
        // moves — so a stationary pointer never locks out arrow-key navigation.
        var mouseMoved = ImGui.GetIO().MouseDelta.LengthSquared() > 0;
        _hoveredRowThisFrame = -1;
        _weightsChangedThisFrame = false;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, RowSpacing * scale));
        ImGui.BeginChild("##variationList", new Vector2(0, listHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        for (var i = 0; i < _filtered.Count; i++)
        {
            var variation = _filtered[i];
            var highlighted = i == _highlightIndex;
            var clicked = DrawRow(composition, pool, variation, i, variation == selected, highlighted, reorderEnabled);

            if (clicked)
            {
                chosen = variation;
                ImGui.CloseCurrentPopup();
            }

            if (highlighted && scrollToHighlight)
                ImGui.SetScrollHereY();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        // Live weighted blend: re-apply while a fader is dragging, bake once on release.
        if (_weightDragId != Guid.Empty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            pool.ApplyCurrentBlend();
            _weightDragId = Guid.Empty;
        }
        else if (_weightsChangedThisFrame)
        {
            pool.ApplyBlendWeights(composition);
        }

        if (mouseMoved && _hoveredRowThisFrame >= 0)
            _highlightIndex = _hoveredRowThisFrame;

        return chosen;
    }

    /// <summary>
    /// Whole-row drag reorder: swaps the dragged snapshot's ActivationIndex with its neighbor as the
    /// mouse crosses each row (the classic ImGui swap-on-drag), committed as one undoable
    /// <see cref="ModifyVariationIndicesCommand"/> on release. Returns true while a drag is in
    /// progress / just ended, so the caller suppresses the click-to-apply.
    /// </summary>
    private bool HandleRowReorder(int index, SymbolVariationPool pool)
    {
        var variation = _filtered[index];

        if (ImGui.IsItemActivated())
        {
            _reorderId = variation.Id;
            _reorderDragged = false;
            _reorderVariations.Clear();
            foreach (var v in _sorted)
                _reorderVariations.Add(v);

            _reorderCommand = new ModifyVariationIndicesCommand(pool, _reorderVariations);
        }

        if (_reorderId != variation.Id)
            return false;

        if (ImGui.IsItemActive())
        {
            var rowStride = (RowHeight + RowSpacing) * T3Ui.UiScaleFactor;
            var dragY = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y;
            if (dragY < -rowStride * 0.5f && index > 0)
            {
                SwapActivationIndices(variation, _filtered[index - 1]);
                ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
                _reorderDragged = true;
            }
            else if (dragY > rowStride * 0.5f && index < _filtered.Count - 1)
            {
                SwapActivationIndices(variation, _filtered[index + 1]);
                ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
                _reorderDragged = true;
            }
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            var dragged = _reorderDragged;
            if (dragged && _reorderCommand != null)
            {
                _reorderCommand.StoreCurrentValues();
                UndoRedoStack.Add(_reorderCommand);
                pool.SaveVariationsToFile();
            }

            _reorderId = Guid.Empty;
            _reorderCommand = null;
            _reorderDragged = false;
            return dragged;
        }

        return _reorderDragged;
    }

    private static void SwapActivationIndices(Variation a, Variation b)
    {
        (a.ActivationIndex, b.ActivationIndex) = (b.ActivationIndex, a.ActivationIndex);
    }

    private static bool DrawTriggerButton(string label, Color labelColor, float width, float frameHeight)
    {
        var scale = T3Ui.UiScaleFactor;
        if (width <= 0)
            width = ImGui.GetContentRegionAvail().X;

        var clicked = ImGui.InvisibleButton("##variationTrigger", new Vector2(width, frameHeight));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        var bg = ImGui.IsItemHovered() ? UiColors.BackgroundHover : UiColors.BackgroundInputField;
        drawList.AddRectFilled(min, max, bg, 4 * scale);

        var textPos = new Vector2(min.X + 6 * scale, (min.Y + max.Y) / 2 - ImGui.GetFontSize() / 2);
        drawList.PushClipRect(min, new Vector2(max.X - frameHeight, max.Y), true);
        drawList.AddText(textPos, labelColor, label);
        drawList.PopClipRect();

        Icons.DrawIconAtScreenPosition(Icon.ChevronDown,
                                       new Vector2(max.X - frameHeight + 2 * scale, min.Y + (frameHeight - Icons.FontSize) / 2).Floor(),
                                       drawList, UiColors.TextMuted);
        return clicked;
    }

    private bool DrawRow(Instance composition, SymbolVariationPool pool,
                         Variation variation, int index, bool isSelected, bool isHighlighted, bool reorderEnabled)
    {
        var scale = T3Ui.UiScaleFactor;
        var rowHeight = RowHeight * scale;
        var thumbWidth = rowHeight * 4 / 3;
        var drawList = ImGui.GetWindowDrawList();

        var totalW = ImGui.GetContentRegionAvail().X;
        var rowMin = ImGui.GetCursorScreenPos();
        var rowMax = new Vector2(rowMin.X + totalW, rowMin.Y + rowHeight);

        var gripW = reorderEnabled ? Icons.FontSize + 4 * scale : 2 * scale;
        var weightW = WeightCellWidth * scale;
        var middleW = MathF.Max(10 * scale, totalW - gripW - weightW);

        // Hit regions left→right: grip (reorder), middle (apply), weight cell (fader). Separate
        // buttons so a fader drag never reorders and the grip is the only reorder handle.
        ImGui.InvisibleButton("##grip" + variation.Id, new Vector2(gripW, rowHeight));
        var draggedToReorder = reorderEnabled && HandleRowReorder(index, pool);
        var rowHovered = ImGui.IsItemHovered();
        ImGui.SameLine(0, 0);

        var midClicked = ImGui.InvisibleButton("##mid" + variation.Id, new Vector2(middleW, rowHeight));
        rowHovered |= ImGui.IsItemHovered();
        ImGui.SameLine(0, 0);

        // Weight fader hit region + interaction (visual is painted later, after the row bg).
        ImGui.InvisibleButton("##w" + variation.Id, new Vector2(weightW, rowHeight));
        var cellMin = ImGui.GetItemRectMin();
        var cellMax = ImGui.GetItemRectMax();
        var weightHovered = ImGui.IsItemHovered();
        rowHovered |= weightHovered;
        var io = ImGui.GetIO();
        var freeMode = io.KeyCtrl;
        // While a [BlendSnapshots] operator drives the pool, the faders are read-only so the two don't
        // fight — the cell still shows the live weight the operator is producing.
        var opDriven = pool.IsBlendDrivenByOperator;
        if (!opDriven && ImGui.IsItemActivated())
        {
            _weightDragId = variation.Id;
            pool.BeginBlendWeightDrag();
        }
        var weightDragging = !opDriven && _weightDragId == variation.Id;
        if (weightDragging && ImGui.IsItemActive())
        {
            var dx = io.MouseDelta.X;
            if (dx != 0)
            {
                var span = MathF.Max(1, cellMax.X - cellMin.X);
                var delta = dx / span * (io.KeyShift ? 0.03f : 0.3f);
                pool.SetBlendWeight(variation.Id, pool.GetBlendWeight(variation.Id) + delta, freeMode);
                _weightsChangedThisFrame = true;
            }
        }

        // A lone 100% fader has nowhere to send its weight, so it's locked — but only while idle.
        // Mid-drag it cross-fades against the drag-start ratio, so dragging back down still refills.
        var lockedFull = !freeMode && !weightDragging && pool.IsSoleFullWeight(variation.Id);
        // While another fader drags, flag the sources it's fading from so the blend-back target is visible.
        var flagSource = _weightDragId != Guid.Empty && !weightDragging && pool.IsDragSource(variation.Id);

        if (weightHovered || (weightDragging && ImGui.IsItemActive()))
            ImGui.SetMouseCursor(opDriven || lockedFull ? ImGuiMouseCursor.NotAllowed : ImGuiMouseCursor.ResizeEW);
        if (weightHovered && opDriven)
            CustomComponents.TooltipForLastItem("Blend is driven by a [BlendSnapshots] operator");
        else if (weightHovered && lockedFull)
            CustomComponents.TooltipForLastItem("Use the other sliders to reduce this level");

        if (rowHovered)
            _hoveredRowThisFrame = index;

        // --- visuals (painted after the invisible hit regions) ---
        if (isSelected)
        {
            drawList.AddRectFilled(rowMin, rowMax, UiColors.BackgroundActive.Fade(0.4f), 4 * scale);
        }
        else if (isHighlighted)
        {
            drawList.AddRectFilled(rowMin, rowMax, UiColors.BackgroundActive.Fade(0.2f), 4 * scale);
        }

        if (reorderEnabled)
        {
            var gripColor = isHighlighted ? UiColors.TextMuted : UiColors.TextMuted.Fade(0.3f);
            var gripPos = new Vector2(rowMin.X + 2 * scale, (rowMin.Y + rowMax.Y) / 2 - Icons.FontSize / 2).Floor();
            Icons.DrawIconAtScreenPosition(Icon.DragIndicator, gripPos, drawList, gripColor);
        }

        // Thumbnail: a rounded border with the image inset 1px so the outline doesn't overlap it.
        var rounding = 2 * scale;
        var borderMin = new Vector2(rowMin.X + gripW, rowMin.Y + 2 * scale);
        var borderMax = new Vector2(borderMin.X + thumbWidth, rowMax.Y - 2 * scale);
        var imageMin = borderMin + new Vector2(1 * scale);
        var imageMax = borderMax - new Vector2(1 * scale);
        var thumbnail = ThumbnailManager.GetThumbnail(variation.Id, composition.Symbol.SymbolPackage,
                                                      ThumbnailManager.Categories.PackageMeta,
                                                      fallbackCategory: ThumbnailManager.Categories.Temp);
        drawList.AddRectFilled(imageMin, imageMax, UiColors.BackgroundFull, rounding);
        if (thumbnail.IsReady && ThumbnailManager.AtlasSrv != null)
            drawList.AddImageRounded(ThumbnailManager.AtlasSrv.NativePointer, imageMin, imageMax,
                                     thumbnail.UvMin, thumbnail.UvMax, Color.White, rounding);

        drawList.AddRect(borderMin, borderMax, UiColors.ForegroundFull.Fade(0.2f), rounding);

        // Index + title (clipped so a long title can't run into the weight cell).
        var textX = borderMax.X + 8 * scale;
        drawList.PushClipRect(new Vector2(textX, rowMin.Y), new Vector2(cellMin.X - 4 * scale, rowMax.Y), true);
        ImGui.PushFont(Fonts.FontSmall);
        drawList.AddText(new Vector2(textX, rowMin.Y + 4 * scale), UiColors.TextMuted, variation.ActivationIndex.ToString("00"));
        ImGui.PopFont();
        drawList.AddText(Fonts.FontBold, Fonts.FontBold.FontSize,
                         new Vector2(textX, rowMin.Y + 4 * scale + Fonts.FontSmall.FontSize + 4 * scale),
                         isSelected ? UiColors.ForegroundFull : UiColors.Text,
                         GetTitle(variation));
        drawList.PopClipRect();

        DrawWeightCellVisual(drawList, cellMin, cellMax, pool.GetBlendWeight(variation.Id), weightDragging, freeMode, flagSource);

        return midClicked && !draggedToReorder && !weightDragging;
    }

    private static void DrawWeightCellVisual(ImDrawListPtr drawList, Vector2 cellMin, Vector2 cellMax, float weight, bool dragging, bool freeMode, bool flagSource)
    {
        var scale = T3Ui.UiScaleFactor;
        var pad = 2 * scale;
        var innerMin = new Vector2(cellMin.X + pad, cellMin.Y + pad);
        var innerMax = new Vector2(cellMax.X - pad, cellMax.Y - pad);
        var rounding = 3 * scale;
        drawList.AddRectFilled(innerMin, innerMax, UiColors.BackgroundInputField, rounding);

        // Blue while dragging in normalized mode; magenta when CTRL frees the clamp (over-drive).
        var accent = dragging && freeMode ? UiColors.StatusAttention : UiColors.StatusAutomated;
        var displayW = Math.Clamp(weight, 0f, 1f);
        if (displayW > 0.001f)
        {
            var fillColor = dragging ? accent.Fade(0.5f) : UiColors.StatusAutomated.Fade(0.2f);
            var fillMax = new Vector2(innerMin.X + (innerMax.X - innerMin.X) * displayW, innerMax.Y);
            drawList.AddRectFilled(innerMin, fillMax, fillColor, rounding);
        }

        if (dragging)
        {
            drawList.AddRect(innerMin, innerMax, accent, rounding, ImDrawFlags.None, 2 * scale);
        }
        else if (flagSource)
        {
            // A source for the fader currently dragging — dragging it back down refills here.
            drawList.AddRect(innerMin, innerMax, UiColors.StatusAutomated.Fade(0.5f), rounding, ImDrawFlags.None, 1 * scale);
        }

        var pct = (weight * 100f).ToString("0") + "%";
        ImGui.PushFont(Fonts.FontNormal);
        var ts = ImGui.CalcTextSize(pct);
        var textColor = dragging ? accent : (weight > 0.001f ? UiColors.Text : UiColors.TextMuted.Fade(0.5f));
        drawList.AddText(new Vector2((cellMin.X + cellMax.X) / 2 - ts.X / 2, (cellMin.Y + cellMax.Y) / 2 - ts.Y / 2).Floor(),
                         textColor, pct);
        ImGui.PopFont();
    }

    internal static string GetTitle(Variation variation)
    {
        return string.IsNullOrEmpty(variation.Title) || variation.Title == "untitled"
                   ? "Untitled"
                   : variation.Title!;
    }

    private static bool Matches(Variation variation, string search)
    {
        return string.IsNullOrWhiteSpace(search)
               || GetTitle(variation).Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Orders the list by ActivationIndex — the same order as the MIDI controller grid.</summary>
    private void SortByActivationIndex(IReadOnlyList<Variation> variations)
    {
        _sorted.Clear();
        _sorted.AddRange(variations);
        VariationBaseCanvas.SortByActivationIndex(_sorted);
    }

    private enum Modes { List, Canvas }

    private const string PopupId = "##variationPickerPopup";
    private const float RowHeight = 40;
    private const float RowSpacing = 2;
    private const float MaxListHeight = 360;
    private const float CanvasHeight = 300;
    private const float WeightCellWidth = 78;

    private readonly List<Variation> _sorted = new();
    private readonly List<Variation> _filtered = new();
    private int _highlightIndex;
    private string _searchString = string.Empty;
    private bool _justOpened;
    private bool _initHighlight;
    private float _popupWidth = 220;
    private Modes _mode = Modes.List;
    private Guid _previewedVariationId;

    // Drag-to-reorder state
    private Guid _reorderId;
    private bool _reorderDragged;
    private ModifyVariationIndicesCommand? _reorderCommand;
    private readonly List<Variation> _reorderVariations = new();

    // Activation-fader interaction state. The weight vector itself is session state on the pool, so
    // it persists across picker opens and stays coherent with the arrows / grid / MIDI activations.
    private Guid _weightDragId;
    private bool _weightsChangedThisFrame;
    private int _hoveredRowThisFrame = -1;
}
