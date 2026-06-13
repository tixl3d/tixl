#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;

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
                           Variation? selected, string label, float width, VariationBaseCanvas? canvas = null)
    {
        Variation? chosen = null;
        var scale = T3Ui.UiScaleFactor;
        var frameHeight = ImGui.GetFrameHeight();

        var triggerClicked = DrawTriggerButton(label, width, frameHeight);
        var triggerMin = ImGui.GetItemRectMin();
        var triggerMax = ImGui.GetItemRectMax();
        if (triggerClicked)
        {
            ImGui.OpenPopup(PopupId);
            _searchString = string.Empty;
            _justOpened = true;
            _initHighlight = true;
        }

        // Wider than the trigger, but still left-aligned to it.
        _popupWidth = MathF.Max(width, 320 * scale);

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

            SortByCanvasPosition(variations);
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
                chosen = DrawList(composition, selected);
            }

            // Hover preview: while enabled in list mode, apply the highlighted variation to the
            // output (restoring on change/close) — the same transient mechanism as the Variations
            // window. The canvas drives its own preview, so skip there.
            if (UserSettings.Config.VariationHoverPreview && !inCanvasMode && _filtered.Count > 0)
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
        else if (_previewedVariationId != Guid.Empty)
        {
            // Popup closed — drop any lingering hover preview.
            pool.StopHover();
            _previewedVariationId = Guid.Empty;
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        return chosen;
    }

    private Variation? DrawList(Instance composition, Variation? selected)
    {
        Variation? chosen = null;
        var scale = T3Ui.UiScaleFactor;

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
        var hoveredIndex = -1;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, RowSpacing * scale));
        ImGui.BeginChild("##variationList", new Vector2(0, listHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        for (var i = 0; i < _filtered.Count; i++)
        {
            var variation = _filtered[i];
            var highlighted = i == _highlightIndex;
            if (DrawRow(composition, variation, variation == selected, highlighted))
            {
                chosen = variation;
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered())
                hoveredIndex = i;

            if (highlighted && scrollToHighlight)
                ImGui.SetScrollHereY();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        if (mouseMoved && hoveredIndex >= 0)
            _highlightIndex = hoveredIndex;

        return chosen;
    }

    private static bool DrawTriggerButton(string label, float width, float frameHeight)
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
        drawList.AddText(textPos, UiColors.Text, label);
        drawList.PopClipRect();

        Icons.DrawIconAtScreenPosition(Icon.ChevronDown,
                                       new Vector2(max.X - frameHeight + 2 * scale, min.Y + (frameHeight - Icons.FontSize) / 2).Floor(),
                                       drawList, UiColors.TextMuted);
        return clicked;
    }

    private bool DrawRow(Instance composition, Variation variation, bool isSelected, bool isHighlighted)
    {
        var scale = T3Ui.UiScaleFactor;
        var rowHeight = RowHeight * scale;
        var thumbWidth = rowHeight * 4 / 3;

        var clicked = ImGui.InvisibleButton("##row" + variation.Id, new Vector2(-1, rowHeight));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        if (isSelected)
            drawList.AddRectFilled(min, max, UiColors.BackgroundActive.Fade(0.4f), 4 * scale);
        else if (isHighlighted)
            drawList.AddRectFilled(min, max, UiColors.BackgroundActive.Fade(0.2f), 4 * scale);

        // Thumbnail: a rounded border with the image inset 1px so the outline doesn't overlap it.
        var rounding = 2 * scale;
        var borderMin = new Vector2(min.X + 2 * scale, min.Y + 2 * scale);
        var borderMax = new Vector2(borderMin.X + thumbWidth, max.Y - 2 * scale);
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

        var thumbMax = borderMax;

        // Index + title (extra gap between them)
        var textX = thumbMax.X + 8 * scale;
        ImGui.PushFont(Fonts.FontSmall);
        drawList.AddText(new Vector2(textX, min.Y + 4 * scale), UiColors.TextMuted, variation.ActivationIndex.ToString("00"));
        ImGui.PopFont();

        drawList.AddText(Fonts.FontBold, Fonts.FontBold.FontSize,
                         new Vector2(textX, min.Y + 4 * scale + Fonts.FontSmall.FontSize + 4 * scale),
                         isSelected ? UiColors.ForegroundFull : UiColors.Text,
                         GetTitle(variation));

        return clicked;
    }

    private static string GetTitle(Variation variation)
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

    /// <summary>
    /// Reading order of the 3-column auto-layout: band Y into rows (so small per-row jitter
    /// doesn't misorder), then left-to-right within the row.
    /// </summary>
    private void SortByCanvasPosition(IReadOnlyList<Variation> variations)
    {
        _sorted.Clear();
        _sorted.AddRange(variations);

        if (_sorted.Count == 0)
            return;

        var minY = float.MaxValue;
        foreach (var v in _sorted)
            minY = MathF.Min(minY, v.PosOnCanvas.Y);

        var stepHeight = MathF.Max(1, VariationThumbnail.ThumbnailSize.Y + VariationThumbnail.SnapPadding.Y);

        _sorted.Sort((a, b) =>
                     {
                         var rowA = (int)MathF.Round((a.PosOnCanvas.Y - minY) / stepHeight);
                         var rowB = (int)MathF.Round((b.PosOnCanvas.Y - minY) / stepHeight);
                         var byRow = rowA.CompareTo(rowB);
                         return byRow != 0 ? byRow : a.PosOnCanvas.X.CompareTo(b.PosOnCanvas.X);
                     });
    }

    private enum Modes { List, Canvas }

    private const string PopupId = "##variationPickerPopup";
    private const float RowHeight = 40;
    private const float RowSpacing = 2;
    private const float MaxListHeight = 360;
    private const float CanvasHeight = 300;

    private readonly List<Variation> _sorted = new();
    private readonly List<Variation> _filtered = new();
    private int _highlightIndex;
    private string _searchString = string.Empty;
    private bool _justOpened;
    private bool _initHighlight;
    private float _popupWidth = 220;
    private Modes _mode = Modes.List;
    private Guid _previewedVariationId;
}
