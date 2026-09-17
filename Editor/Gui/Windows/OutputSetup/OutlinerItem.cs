#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// One entity in the setup outliner: icon, name, inline rename, status, state coloring, and drag source. It
/// opens the shared <see cref="SetupEntityContextMenu"/> and carries no layout beyond the insets the outliner
/// passes in.
/// <para>Deliberately delegate-free: items draw every frame, so all per-kind behavior (rename, delete,
/// extra menu items) dispatches through <see cref="SetupActions"/> and <see cref="SetupEntityContextMenu"/>
/// instead of per-item callbacks. The rename state is editor-wide: a rename holds the keyboard focus, so
/// only one can be live at a time, and a canvas menu can open it without knowing which window's item it is.</para>
/// </summary>
internal sealed class OutlinerItem
{
    /// <summary>What the caller must react to; everything else (selection, rename, delete, menus) is
    /// handled internally.</summary>
    public enum Actions
    {
        None,

        /// <summary>The expander column was clicked — the caller owns the collapse state.</summary>
        ToggleExpanded,
    }

    public struct Args
    {
        public SetupEntityKinds Kind;
        public Guid Id;
        public string Name;

        /// <summary>Small muted text at the right end (a plug's resolution, an aspect warning) — never
        /// routing, which the outliner's connections show.</summary>
        public string? Status;

        /// <summary>Overrides the kind's icon (a stream plug differs from a display plug).</summary>
        public Icon? LeadingIcon;

        /// <summary>Tree depth; only the content indents — the background stays full width.</summary>
        public int Depth;

        /// <summary>null = no children, so no chevron.</summary>
        public bool? IsExpanded;

        /// <summary>Keep the chevron column even without children, so siblings align.</summary>
        public bool KeepsExpanderColumn;

        /// <summary>Nothing shows this entity — it recedes rather than competing with items in use.</summary>
        public bool IsMuted;

        /// <summary>Strike the leading icon (a paused output / non-rendered surface).</summary>
        public bool HasStruckIcon;

        /// <summary>The column the item spans, in screen px; a zero width means the whole window (the tree layout).</summary>
        public float ColumnMinX;

        public float ColumnWidth;
    }

    /// <summary>Screen rect of the item drawn last — where the outliner's connections attach.</summary>
    public (Vector2 Min, Vector2 Max) LastItemRect { get; private set; }

    /// <param name="hovered">Reported so the caller can track hover-driven cross-highlights.</param>
    public Actions DrawItem(SetupEntitySelection selection, Setup setup, in Args args, out bool hovered)
    {
        var action = Actions.None;
        var scale = T3Ui.UiScaleFactor;
        var rounding = 4 * scale;
        // Odd height so a 15px icon centers exactly ((23-15)/2 = 4).
        var height = (float)Math.Round(23 * scale);
        var indent = args.Depth * 12 * scale;

        var fade = args.IsMuted ? 0.45f : 1f;

        ImGui.PushID(args.Id.GetHashCode());

        // Rounded item inset 4px from the window edges (so the selection/outline never clips), pixel-snapped
        // to avoid a blurry sub-pixel edge.
        var entryPos = ImGui.GetCursorScreenPos();
        var windowPos = ImGui.GetWindowPos();
        var spanLeft = args.ColumnWidth > 0 ? args.ColumnMinX : windowPos.X;
        var spanRight = args.ColumnWidth > 0 ? args.ColumnMinX + args.ColumnWidth : windowPos.X + ImGui.GetWindowWidth();
        var rowMin = new Vector2((float)Math.Round(spanLeft + 4 * scale), (float)Math.Round(entryPos.Y));
        var rowMax = new Vector2((float)Math.Round(spanRight - 4 * scale), rowMin.Y + height);
        LastItemRect = (rowMin, rowMax);
        var dl = ImGui.GetWindowDrawList();
        var isSelected = selection.IsSelected(args.Kind, args.Id);
        var kindInfo = SetupEntityKindInfo.Of(args.Kind);
        var kindColor = kindInfo.Color;

        // Full-item hit test — a selectable spanning the padded item; its own header background is suppressed
        // so we can draw a rounded one instead.
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
        ImGui.SetCursorScreenPos(rowMin);
        var clicked = ImGui.Selectable("##row", isSelected, ImGuiSelectableFlags.None, new Vector2(rowMax.X - rowMin.X, height));
        ImGui.PopStyleColor(3);

        var isHovered = ImGui.IsItemHovered();
        hovered = isHovered;

        var canRename = kindInfo.CanRename
                        && (args.Kind != SetupEntityKinds.Plug || SetupActions.CanRenamePlug(args.Id));
        var isRenaming = canRename && _renamingId == args.Id;

        // Double-click a renamable item to edit its name inline. Suppress the click-select handling below so the
        // double-click doesn't also toggle/reselect while the field takes focus.
        if (canRename && !isRenaming && isHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            BeginRename(selection, args.Kind, args.Id, args.Name);
            isRenaming = true;
            clicked = false;
        }

        if (isRenaming)
            clicked = false;

        // The chevron shares the item's selectable rather than overlapping it with its own button — a click in
        // its column toggles instead of selecting.
        var chevronMaxX = rowMin.X + indent + 20 * scale;
        if (clicked && args.IsExpanded.HasValue && ImGui.GetMousePos().X < chevronMaxX)
        {
            action = Actions.ToggleExpanded;
        }
        else if (clicked && args.Kind != SetupEntityKinds.None)
        {
            var io = ImGui.GetIO();
            if (io.KeyCtrl)
            {
                selection.Toggle(args.Kind, args.Id);
            }
            else if (io.KeyShift)
            {
                selection.Add(args.Kind, args.Id);
            }
            else
            {
                selection.Select(args.Kind, args.Id);
                // A content item is a live op — a plain click selects it in the graph and brings it into view.
                if (args.Kind == SetupEntityKinds.ContentSource)
                    ContentSourceSync.RevealContentOpInGraph(args.Id);
            }
        }

        if (isHovered)
        {
            FrameStats.RequestCrossHighlight(args.Id);
            if (args.Kind == SetupEntityKinds.ContentSource)
                FrameStats.AddHoveredId(args.Id);
        }

        // A drag's first movement decides what it is: mostly vertical reorders the item among its siblings,
        // anything else is a routing drag to another column. Decided once per press, so it can't flip midway.
        var isActive = ImGui.IsItemActive();
        if (isActive && _dragDecidedId != args.Id)
        {
            var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left, 0f);
            if (delta.Length() > ImGui.GetIO().MouseDragThreshold)
            {
                _dragDecidedId = args.Id;
                _dragIsReorder = MathF.Abs(delta.Y) > MathF.Abs(delta.X) * 1.5f && SetupActions.CanReorder(args.Kind);
                if (_dragIsReorder)
                    _reorderOldJson = setup.ToJsonString();
            }
        }

        var reordering = isActive && _dragDecidedId == args.Id && _dragIsReorder;
        if (reordering)
        {
            // Leaving the column sideways means the drag was heading for another column after all: it becomes a
            // routing drag from here, and whatever reordering it did on the way stays as it is.
            var mouse = ImGui.GetMousePos();
            if (mouse.X < rowMin.X || mouse.X > rowMax.X)
            {
                _dragIsReorder = false;
                if (_reorderOldJson != null)
                {
                    SetupUndo.CommitGesture(setup, "Reorder", _reorderOldJson);
                    _reorderOldJson = null;
                }
            }
            else
            {
                // Past the row's edge the item trades places with its neighbour — after this frame's drawing, since
                // the list is being walked right now and a swap mid-walk draws the row twice. Next frame it is under
                // the cursor again in its new place.
                if (mouse.Y < rowMin.Y)
                    _pendingMove = (args.Kind, args.Id, -1);
                else if (mouse.Y > rowMax.Y)
                    _pendingMove = (args.Kind, args.Id, +1);
            }
        }

        if (ImGui.IsItemDeactivated() && _dragDecidedId == args.Id)
        {
            if (_dragIsReorder && _reorderOldJson != null)
                SetupUndo.CommitGesture(setup, "Reorder", _reorderOldJson);

            _reorderOldJson = null;
            _dragDecidedId = Guid.Empty;
            _dragIsReorder = false;
        }

        // The routing drag only starts once the press has been decided as one, so a reorder never carries a payload.
        HandleDragDrop(setup, kindInfo, args.Id, allowSource: _dragDecidedId == args.Id && !_dragIsReorder);

        if (args.Kind != SetupEntityKinds.None)
            SetupEntityContextMenu.DrawForLastItem(selection, setup, args.Kind, args.Id, args.Name);

        // While this item's context menu is open the pointer sits on the popup, not the item, so keep the item
        // lit anyway — otherwise it's no longer obvious which entity the menu belongs to. The popup id is
        // scoped by the item's PushID, so this only matches our own menu.
        var menuOpen = ImGui.IsPopupOpen("context_menu");

        // Hovered from the canvas (its frame is under the cursor) but not here: pulse so the eye is drawn to
        // the item that answers "which item is that frame?".
        var canvasPulse = !isHovered && !isSelected && !menuOpen ? FrameStats.CrossHighlightAmount(args.Id) : 0;

        // The item is a pill in its kind's colour, always: solid while selected, a light tint otherwise that
        // hovering (from here or from the canvas) merely lifts.
        if (isSelected)
        {
            dl.AddRectFilled(rowMin, rowMax, kindColor.Fade(0.6f), rounding);
        }
        else
        {
            var lift = isHovered || menuOpen ? 1f : canvasPulse;
            dl.AddRectFilled(rowMin, rowMax, kindColor.Fade(0.12f + 0.13f * lift), rounding);
            dl.AddRect(rowMin, rowMax, kindColor.Fade(0.45f + 0.45f * lift), rounding);
        }

        // Content over the background (the selectable is transparent), vertically centered in the fixed item
        // (the -1px nudges the label up so it isn't sitting low).
        var contentY = (float)Math.Round(rowMin.Y + (height - ImGui.GetTextLineHeight()) * 0.5f - 1 * scale);
        var iconY = contentY + 3 * scale; // glyphs render high vs the text baseline — drop them to match.

        var contentX = rowMin.X + 6 * scale + indent;
        if (args.IsExpanded.HasValue)
        {
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            Icons.DrawInlineGlyph(args.IsExpanded.Value ? Icon.ChevronDown : Icon.ChevronRight, UiColors.TextMuted.Fade(0.6f).Rgba);
            contentX = ImGui.GetItemRectMax().X + 3 * scale;
        }
        else if (args.Depth > 0 || args.KeepsExpanderColumn)
        {
            // Keep the chevron column even when this item has nothing to expand — otherwise a childless item
            // sits further left than its siblings and the tree reads as ragged. Drawing the same glyph fully
            // transparent reserves *exactly* the width the real one takes, rather than a guessed constant.
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            Icons.DrawInlineGlyph(Icon.ChevronRight, new Vector4(0, 0, 0, 0));
            contentX = ImGui.GetItemRectMax().X + 3 * scale;
        }

        if (args.Kind != SetupEntityKinds.None)
        {
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            Icons.DrawInlineGlyph(args.LeadingIcon ?? kindInfo.Icon, (isSelected ? UiColors.ForegroundFull : kindColor).Fade(fade).Rgba);

            // A disabled (non-rendered) surface is struck through its icon — visible at a glance without
            // stealing the gutter or the name.
            if (args.HasStruckIcon)
            {
                var iconMin = ImGui.GetItemRectMin();
                var iconMax = ImGui.GetItemRectMax();
                dl.AddLine(new Vector2(iconMin.X, iconMax.Y), new Vector2(iconMax.X, iconMin.Y),
                           UiColors.StatusAttention, 1.5f * scale);
            }

            contentX = ImGui.GetItemRectMax().X + 5 * scale;
        }

        if (isRenaming)
        {
            // Inline editor in place of the name. Full item height, seeded and focused on the first frame;
            // commits on Enter/blur, cancels on Escape.
            var fieldY = (float)Math.Round(rowMin.Y + (height - ImGui.GetFrameHeight()) * 0.5f);
            ImGui.SetCursorScreenPos(new Vector2(contentX, fieldY));
            ImGui.SetNextItemWidth(rowMax.X - contentX - 6 * scale);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundInputField.Rgba);
            if (_renameFocusPending)
            {
                ImGui.SetScrollHereY();
                ImGui.SetKeyboardFocusHere();
                _renameFocusPending = false;
            }

            ImGui.InputText("##rename", ref _renameBuffer, 256);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (!string.IsNullOrWhiteSpace(_renameBuffer))
                    SetupActions.RenameEntity(setup, args.Kind, args.Id, _renameBuffer.Trim());

                _renamingId = Guid.Empty;
            }
            else if (ImGui.IsItemDeactivated() || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _renamingId = Guid.Empty;
            }

            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.SetCursorScreenPos(new Vector2(contentX, contentY));
            CustomComponents.StylizedText(string.IsNullOrEmpty(args.Name) ? "untitled" : args.Name,
                                          isSelected ? Fonts.FontBold : Fonts.FontNormal,
                                          (isSelected ? UiColors.ForegroundFull : SetupColors.LabelFor(args.Kind)).Fade(fade));
        }

        // Right-aligned status text, small and muted (FontSmall is shorter than the item's baseline — centre it on its own height).
        if (!isRenaming && args.Status != null)
        {
            ImGui.PushFont(Fonts.FontSmall);
            var smallHeight = ImGui.GetTextLineHeight();
            var statusWidth = ImGui.CalcTextSize(args.Status).X;
            ImGui.PopFont();

            var statusY = (float)Math.Round(rowMin.Y + (height - smallHeight) * 0.5f - 1 * scale);
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - 6 * scale - statusWidth, statusY));
            CustomComponents.StylizedText(args.Status, Fonts.FontSmall, UiColors.TextMuted.Fade(fade));
        }

        // Next item starts a tight 2px below, independent of the content cursor above. Claimed with a zero-height
        // item (no spacing) rather than a bare cursor set: ImGui asserts on a window whose extent was only ever
        // extended by SetCursorPos, which is exactly what the last item of the last section would do.
        ImGui.SetCursorScreenPos(new Vector2(entryPos.X, rowMax.Y + 2 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopStyleVar();
        ImGui.PopID();
        return action;
    }

    /// <summary>Enters inline-rename mode for an item: selects it, seeds the buffer, and focuses the field next frame.</summary>
    public static void BeginRename(SetupEntitySelection selection, SetupEntityKinds kind, Guid id, string name)
    {
        selection.Select(kind, id);
        _renamingId = id;
        _renameBuffer = name ?? string.Empty;
        _renameFocusPending = true;
    }

    /// <summary>Applies the move a reorder drag asked for this frame; called once the columns are drawn.</summary>
    public static void ApplyPendingMove(Setup setup)
    {
        if (_pendingMove.Kind == SetupEntityKinds.None)
            return;

        SetupActions.MoveAmongSiblings(setup, _pendingMove.Kind, _pendingMove.Id, _pendingMove.Direction);
        _pendingMove = default;
    }

    /// <summary>The item a rename was just requested for and whose row must be brought into view — a collapsed
    /// parent expands, the strip scrolls to it. Cleared once the row has drawn its field.</summary>
    public static Guid RevealPendingId => _renameFocusPending ? _renamingId : Guid.Empty;

    private static void HandleDragDrop(Setup setup, SetupEntityKindInfo kindInfo, Guid id, bool allowSource)
    {
        // Every routable kind is both a drag source and a drop target — connections are direction-agnostic
        // (SetupRouting normalizes), so dragging an output onto a source works the same as the reverse.
        if (!kindInfo.IsRoutable)
            return;

        var kind = kindInfo.Kind;

        // The payload is only read while the item is active (the drag start), so skip the string build
        // for the idle case — the helper's deactivation cleanup doesn't use it.
        if (allowSource || !ImGui.IsItemActive())
        {
            var payload = ImGui.IsItemActive() ? SetupRouting.DragPayload(kind, id) : string.Empty;
            DragAndDropHandling.HandleDragSourceForLastItem(DragAndDropHandling.DragTypes.SetupEntity, payload);
        }

        if (!DragAndDropHandling.TryGetDragData(DragAndDropHandling.DragTypes.SetupEntity, out var dragData)
            || !SetupRouting.TryParseDrag(dragData, out var dragKind, out var dragId))
            return;

        if (!SetupRouting.CanConnect(dragKind, kind) || dragId == id)
            return;

        if (DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.SetupEntity, out _) == DragAndDropHandling.DragInteractionResult.Dropped)
            SetupRouting.ApplyDrop(setup, dragKind, dragId, kind, id);
    }

    // The press being dragged, and whether its first movement made it a reorder rather than a routing drag.
    private static Guid _dragDecidedId;
    private static bool _dragIsReorder;
    private static string? _reorderOldJson;
    private static (SetupEntityKinds Kind, Guid Id, int Direction) _pendingMove;

    private static Guid _renamingId;
    private static string _renameBuffer = string.Empty;
    private static bool _renameFocusPending;
}
