#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Windows.Output;
using T3.Core.Output;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The one visual for a setup entity: icon, name, inline rename, status, state coloring, context menu,
/// and drag source. The setup panel lays it out as tree rows; a flow view lays the same item out as node
/// bodies — the item itself carries no layout beyond the insets the caller passes in.
/// <para>Deliberately delegate-free: rows draw every frame, so all per-kind behavior (rename, delete,
/// extra menu items) dispatches through <see cref="SetupActions"/> instead of per-item callbacks.</para>
/// </summary>
internal sealed class EntityItem
{
    /// <summary>What the caller must react to; everything else (selection, rename, delete, menus) is
    /// handled internally.</summary>
    public enum ItemAction
    {
        None,

        /// <summary>The expander column was clicked — the caller owns the collapse state.</summary>
        ToggleExpanded,
    }

    public struct Args
    {
        public SetupEntitySelection.EntityKind Kind;
        public Guid Id;
        public string Name;

        /// <summary>Small muted text at the right end (a plug's resolution, an aspect warning) — never
        /// routing, which the outliner's connections show.</summary>
        public string? Status;

        public Icon? LeadingIcon;

        /// <summary>Tree depth; only the content indents — the background stays full width.</summary>
        public int Depth;

        /// <summary>null = no children, so no chevron.</summary>
        public bool? IsExpanded;

        /// <summary>Keep the chevron column even without children, so siblings align.</summary>
        public bool ReserveExpander;

        /// <summary>Nothing shows this entity — it recedes rather than competing with rows in use.</summary>
        public bool Muted;

        /// <summary>Strike the leading icon (a paused output / non-rendered surface).</summary>
        public bool StrikeLeadingIcon;

        /// <summary>The selection primary, for the in-gutter bind toggle.</summary>
        public SetupEntitySelection.EntityKind PrimaryKind;

        public Guid PrimaryId;

        /// <summary>The column the row spans, in screen px; a zero width means the whole window (the tree layout).</summary>
        public float ColumnMinX;

        public float ColumnWidth;
    }

    /// <summary>Screen rect of the item drawn last — where the outliner's connections attach.</summary>
    public (Vector2 Min, Vector2 Max) LastRowRect { get; private set; }

    /// <param name="hovered">Reported so the caller can track hover-driven cross-highlights.</param>
    public ItemAction DrawRow(SetupEntitySelection selection, Setup setup, in Args args, out bool hovered)
    {
        var action = ItemAction.None;
        var scale = T3Ui.UiScaleFactor;
        var rounding = 4 * scale;
        // Odd height so a 15px icon centers exactly ((23-15)/2 = 4).
        var height = (float)Math.Round(23 * scale);
        var indent = args.Depth * 12 * scale;

        var fade = args.Muted ? 0.45f : 1f;

        // The in-gutter exists for one gesture: with a source selected, clicking the arrow on an item that
        // could take it binds or unbinds without dragging. It is reserved for every consumer item while any
        // such source is the primary, so items don't shift relative to each other, and gone otherwise — the
        // connections say what feeds what.
        // A source is not a bind context: which of its slices would bind is ambiguous, and the arrows lit on
        // every consumer of any slice read as a claim the connections don't make.
        var bindContext = args.PrimaryKind is SetupEntitySelection.EntityKind.Slice
                              or SetupEntitySelection.EntityKind.Surface;
        var hasInputGutter = bindContext
                             && args.Kind is SetupEntitySelection.EntityKind.Surface
                                 or SetupEntitySelection.EntityKind.Output
                                 or SetupEntitySelection.EntityKind.Patch;
        var gutterWidth = hasInputGutter ? Icons.FontSize + 4 * scale : 0;

        ImGui.PushID(args.Id.GetHashCode());

        // Rounded row inset 4px from the window edges (so the selection/outline never clips), pixel-snapped
        // to avoid a blurry sub-pixel edge.
        var entryPos = ImGui.GetCursorScreenPos();
        var windowPos = ImGui.GetWindowPos();
        var spanLeft = args.ColumnWidth > 0 ? args.ColumnMinX : windowPos.X;
        var spanRight = args.ColumnWidth > 0 ? args.ColumnMinX + args.ColumnWidth : windowPos.X + ImGui.GetWindowWidth();
        var rowMin = new Vector2((float)Math.Round(spanLeft + 4 * scale), (float)Math.Round(entryPos.Y));
        var rowMax = new Vector2((float)Math.Round(spanRight - 4 * scale), rowMin.Y + height);
        LastRowRect = (rowMin, rowMax);
        var dl = ImGui.GetWindowDrawList();
        var isSelected = selection.IsSelected(args.Kind, args.Id);
        var kindColor = SetupColors.ForKind(args.Kind);

        // Full-row hit test — a selectable spanning the padded row; its own header background is suppressed
        // so we can draw a rounded one instead.
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
        ImGui.SetCursorScreenPos(rowMin);
        var clicked = ImGui.Selectable("##row", isSelected, ImGuiSelectableFlags.None, new Vector2(rowMax.X - rowMin.X, height));
        ImGui.PopStyleColor(3);

        var isHovered = ImGui.IsItemHovered();
        hovered = isHovered;

        var canRename = SetupActions.CanRename(args.Kind);
        var isRenaming = canRename && _renamingId == args.Id;

        // Double-click a renamable row to edit its name inline. Suppress the click-select handling below so the
        // double-click doesn't also toggle/reselect while the field takes focus.
        if (canRename && !isRenaming && isHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            BeginRename(selection, args.Kind, args.Id, args.Name);
            isRenaming = true;
            clicked = false;
        }

        if (isRenaming)
            clicked = false;

        // The chevron shares the row's selectable rather than overlapping it with its own button — a click in
        // its column toggles instead of selecting.
        // A source is selected, so every row that could take it offers a click-target to bind or unbind.
        var isBound = false;
        var canBind = hasInputGutter
                      && SetupActions.TryDescribeInputToggle(setup, args.Kind, args.Id, args.PrimaryKind, args.PrimaryId, out isBound);
        var gutterMaxX = rowMin.X + gutterWidth;
        if (clicked && canBind && ImGui.GetMousePos().X < gutterMaxX)
        {
            SetupActions.ToggleInput(setup, args.Kind, args.Id, args.PrimaryKind, args.PrimaryId);
            clicked = false;
        }

        var chevronMaxX = rowMin.X + gutterWidth + indent + 20 * scale;
        if (clicked && args.IsExpanded.HasValue && ImGui.GetMousePos().X < chevronMaxX)
        {
            action = ItemAction.ToggleExpanded;
        }
        else if (clicked && args.Kind != SetupEntitySelection.EntityKind.None) // inventory rows (plugs) are inert
        {
            var io = ImGui.GetIO();
            if (io.KeyCtrl)
                selection.Toggle(args.Kind, args.Id);
            else if (io.KeyShift)
                selection.Add(args.Kind, args.Id);
            else
            {
                selection.Select(args.Kind, args.Id);
                // A content row is a live op — a plain click selects it in the graph and brings it into view.
                if (args.Kind == SetupEntitySelection.EntityKind.ContentSource)
                    SetupActions.RevealContentOpInGraph(args.Id);
            }
        }

        if (isHovered)
        {
            FrameStats.PulseItemWithId(args.Id);
            if (args.Kind == SetupEntitySelection.EntityKind.ContentSource)
                FrameStats.AddHoveredId(args.Id);
        }

        HandleDragDrop(setup, args.Kind, args.Id);

        if (args.Kind != SetupEntitySelection.EntityKind.None)
        {
            // Static context + a cached delegate: the menu body only runs for the row whose popup is open,
            // and ContextMenuForItem invokes it synchronously within this call — so per-row closures would
            // buy nothing but a per-frame allocation.
            _menuArgs = args;
            _menuSelection = selection;
            _menuSetup = setup;
            CustomComponents.ContextMenuForItem(_drawMenuItemsCached, null);
        }

        // While this row's context menu is open the pointer sits on the popup, not the row, so keep the row
        // lit anyway — otherwise it's no longer obvious which entity the menu belongs to. The popup id is
        // scoped by the row's PushID, so this only matches our own menu.
        var menuOpen = ImGui.IsPopupOpen("context_menu");

        // Hovered from the canvas (its frame is under the cursor) but not here: pulse so the eye is drawn to
        // the row that answers "which item is that frame?".
        var canvasPulse = !isHovered && !isSelected && !menuOpen ? FrameStats.GetPulse(args.Id) : 0;

        // The item wears its kind's colour: a solid pill while selected, a tinted one while hovered.
        if (isSelected)
        {
            dl.AddRectFilled(rowMin, rowMax, kindColor.Fade(0.6f), rounding);
        }
        else if (isHovered || menuOpen)
        {
            dl.AddRectFilled(rowMin, rowMax, kindColor.Fade(0.2f), rounding);
            dl.AddRect(rowMin, rowMax, kindColor.Fade(0.8f), rounding);
        }
        else if (canvasPulse > 0.001f)
        {
            // Match the mouse-hover look (light fill + outline) so a canvas-driven highlight reads the same.
            dl.AddRectFilled(rowMin, rowMax, kindColor.Fade(0.2f), rounding);
            dl.AddRect(rowMin, rowMax, kindColor.Fade(0.8f), rounding);
        }

        // Content over the background (the selectable is transparent), vertically centered in the fixed row
        // (the -1px nudges the label up so it isn't sitting low).
        var contentY = (float)Math.Round(rowMin.Y + (height - ImGui.GetTextLineHeight()) * 0.5f - 1 * scale);
        var iconY = contentY + 3 * scale; // glyphs render high vs the text baseline — drop them to match.

        if (canBind)
        {
            var overGutter = isHovered && ImGui.GetMousePos().X < gutterMaxX;
            var color = isBound ? UiColors.StatusActivated : UiColors.BackgroundFull.Fade(0.5f);
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X + 4 * scale, iconY));
            Icons.DrawInlineGlyph(Icon.ArrowRight, overGutter ? UiColors.ForegroundFull.Rgba : color.Rgba);
        }

        var contentX = rowMin.X + 6 * scale + gutterWidth + indent;
        if (args.IsExpanded.HasValue)
        {
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            Icons.DrawInlineGlyph(args.IsExpanded.Value ? Icon.ChevronDown : Icon.ChevronRight, UiColors.TextMuted.Fade(0.6f).Rgba);
            contentX = ImGui.GetItemRectMax().X + 3 * scale;
        }
        else if (args.Depth > 0 || args.ReserveExpander)
        {
            // Keep the chevron column even when this row has nothing to expand — otherwise a childless row
            // sits further left than its siblings and the tree reads as ragged. Drawing the same glyph fully
            // transparent reserves *exactly* the width the real one takes, rather than a guessed constant.
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            Icons.DrawInlineGlyph(Icon.ChevronRight, new Vector4(0, 0, 0, 0));
            contentX = ImGui.GetItemRectMax().X + 3 * scale;
        }

        if (args.LeadingIcon.HasValue)
        {
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            Icons.DrawInlineGlyph(args.LeadingIcon.Value, (isSelected ? UiColors.ForegroundFull : kindColor).Fade(fade).Rgba);

            // A disabled (non-rendered) surface is struck through its icon — visible at a glance without
            // stealing the gutter or the name.
            if (args.StrikeLeadingIcon)
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
            // Inline editor in place of the name. Full row height, seeded and focused on the first frame;
            // commits on Enter/blur, cancels on Escape.
            var fieldY = (float)Math.Round(rowMin.Y + (height - ImGui.GetFrameHeight()) * 0.5f);
            ImGui.SetCursorScreenPos(new Vector2(contentX, fieldY));
            ImGui.SetNextItemWidth(rowMax.X - contentX - 6 * scale);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundInputField.Rgba);
            if (_renameFocusPending)
            {
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
                                          (isSelected ? UiColors.ForegroundFull : UiColors.Text).Fade(fade));
        }

        // Right-aligned status text, small and muted (FontSmall is shorter than the row's baseline — centre it on its own height).
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

        // Next row starts a tight 2px below, independent of the content cursor above. Claimed with a zero-height
        // item (no spacing) rather than a bare cursor set: ImGui asserts on a window whose extent was only ever
        // extended by SetCursorPos, which is exactly what the last row of the last section would do.
        ImGui.SetCursorScreenPos(new Vector2(entryPos.X, rowMax.Y + 2 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopStyleVar();
        ImGui.PopID();
        return action;
    }

    /// <summary>Enters inline-rename mode for an item: selects it, seeds the buffer, and focuses the field next frame.</summary>
    public void BeginRename(SetupEntitySelection selection, SetupEntitySelection.EntityKind kind, Guid id, string name)
    {
        selection.Select(kind, id);
        _renamingId = id;
        _renameBuffer = name ?? string.Empty;
        _renameFocusPending = true;
    }

    private void HandleDragDrop(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        // Every routable kind is both a drag source and a drop target — connections are direction-agnostic
        // (ApplyDrop normalizes), so dragging an output onto a source works the same as the reverse.
        var routable = kind is SetupEntitySelection.EntityKind.Surface or SetupEntitySelection.EntityKind.ContentSource
                            or SetupEntitySelection.EntityKind.Slice or SetupEntitySelection.EntityKind.Output;
        if (!routable)
            return;

        // The payload is only read while the item is active (the drag start), so skip the string build
        // for the idle case — the helper's deactivation cleanup doesn't use it.
        var payload = ImGui.IsItemActive() ? $"{(int)kind}:{id}" : string.Empty;
        DragAndDropHandling.HandleDragSourceForLastItem(DragAndDropHandling.DragTypes.SetupEntity, payload);

        if (!DragAndDropHandling.TryGetDragData(DragAndDropHandling.DragTypes.SetupEntity, out var dragData)
            || !SetupActions.TryParseDrag(dragData, out var dragKind, out var dragId))
            return;

        if (!SetupActions.CanConnect(dragKind, kind) || dragId == id)
            return;

        if (DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.SetupEntity, out _) == DragAndDropHandling.DragInteractionResult.Dropped)
            SetupActions.ApplyDrop(setup, dragKind, dragId, kind, id);
    }

    private void DrawMenuItemsForCurrent()
    {
        if (_menuSelection == null || _menuSetup == null)
            return;

        DrawContextMenuItems(_menuSelection, _menuSetup, _menuArgs.Kind, _menuArgs.Id, _menuArgs.Name);
    }

    /// <summary>
    /// The one context menu for a setup entity — identical whether opened from a setup-panel row or a canvas
    /// label: kind-specific extras first, then the common Duplicate / Rename / Delete verbs wherever the
    /// kind supports them.
    /// </summary>
    public void DrawContextMenuItems(SetupEntitySelection selection, Setup setup,
                                            SetupEntitySelection.EntityKind kind, Guid id, string name)
    {
        // Right-clicking inside a multi-selection acts on the whole thing. The per-entity actions stay
        // visible but dimmed rather than vanishing, so the menu keeps its shape and it is obvious *why*
        // they can't be used.
        var multi = selection.IsSelected(kind, id) && selection.Count > 1;

        // These menus carry no toggles or icons, so their labels sit flush left.
        CustomComponents.MenuItemsFlushLeft = true;
        CustomComponents.MenuItemsDisabled = multi;
        ImGui.BeginDisabled(multi);

        DrawKindMenuItems(selection, setup, kind, id);

        if (SetupActions.CanDuplicate(kind) && CustomComponents.DrawMenuItem(5, "Duplicate"))
            SetupActions.DuplicateEntity(selection, setup, kind, id);

        if (SetupActions.CanRename(kind) && CustomComponents.DrawMenuItem(3, "Rename"))
            BeginRename(selection, kind, id, name);

        ImGui.EndDisabled();
        CustomComponents.MenuItemsDisabled = false;

        // Deleting is the one action that reads the selection rather than the item, so it is offered even
        // from an item that isn't itself deletable.
        if (multi)
        {
            var deletable = SetupActions.CountDeletable(selection);
            if (deletable > 0 && CustomComponents.DrawMenuItem(2, $"Delete {deletable}"))
                SetupActions.DeleteSelection(selection, setup);
        }
        else if (SetupActions.CanDeleteDirectly(kind) && CustomComponents.DrawMenuItem(2, "Delete"))
        {
            SetupActions.DeleteEntity(setup, kind, id);
        }

        CustomComponents.MenuItemsFlushLeft = false;
    }

    private void DrawKindMenuItems(SetupEntitySelection selection, Setup setup,
                                          SetupEntitySelection.EntityKind kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Output:
                var output = setup.FindOutput(id);
                if (output == null)
                    break;

                if (CustomComponents.DrawMenuItem(7, "Add Patch"))
                    SetupActions.AddPatch(selection, setup, output);

                if (CustomComponents.DrawMenuItem(10, "Split into 2×2"))
                    SetupActions.SplitOutput(selection, setup, output, 2, 2);

                if (CustomComponents.DrawMenuItem(11, "Split into 4×4"))
                    SetupActions.SplitOutput(selection, setup, output, 4, 4);

                if (CustomComponents.DrawMenuItem(14, "Clear Inputs", isEnabled: SetupActions.OutputHasInputs(setup, output)))
                    SetupActions.ClearOutputInputs(setup, output);

                CustomComponents.TooltipForLastItem("Disconnects every surface and patch from this output.",
                                                    "The surfaces and their content stay; only the routes into this canvas are removed.");

                if (output.Kind is not (OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display))
                    break;

                if (CustomComponents.DrawSubMenu(4, "Bind to display")
                    && OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
                {
                    ResolutionHandling.DrawBindingMenuItems(output, machineConfig);
                    ImGui.EndMenu();
                }

                break;

            case SetupEntitySelection.EntityKind.ContentSource:
                var source = setup.FindSourceByChildId(id);
                if (source != null && CustomComponents.DrawMenuItem(8, "Add slice"))
                    SetupActions.AddSlice(selection, setup, source);

                break;

            case SetupEntitySelection.EntityKind.Patch:
                if (CustomComponents.DrawMenuItem(12, "Use on Surface"))
                    SetupActions.PromotePatchToSurface(selection, setup, id);

                CustomComponents.TooltipForLastItem("Turns this patch into a surface with a corner pin.",
                                                    "The quad stays exactly where it is; the surface adds real size, raster and straightening.");
                break;

            case SetupEntitySelection.EntityKind.ReferenceImage:
                var image = setup.FindReferenceImage(id);
                if (image == null)
                    break;

                if (CustomComponents.DrawMenuItem(13, "Trace New Surface"))
                    SetupActions.TraceNewSurface(selection, setup, image);

                // Every root surface not yet traced anywhere can be traced here.
                for (var i = 0; i < setup.Surfaces.Count; i++)
                {
                    var candidate = setup.Surfaces[i];
                    if (candidate.Reference != null || candidate.ParentId != Guid.Empty)
                        continue;

                    if (CustomComponents.DrawMenuItem(100 + i, $"Trace {candidate.Name} Here"))
                        SetupActions.TraceSurfaceOnImage(selection, setup, candidate, image);
                }

                break;

            case SetupEntitySelection.EntityKind.Surface:
                var surface = setup.FindSurface(id);
                if (surface == null)
                    break;

                if (CustomComponents.DrawMenuItem(4, "Add region"))
                    SetupActions.AddSubRegion(selection, setup, surface);

                // Only meaningful once something is shown here — there's no aspect to match otherwise.
                if (surface.SliceId != Guid.Empty && CustomComponents.DrawMenuItem(9, "Adjust aspect to slice"))
                    SetupActions.MatchSurfaceToSliceAspect(setup, surface);

                if (CustomComponents.DrawMenuItem(6, "Clear content inputs"))
                    SetupActions.ClearContentInputs(surface.Id);

                break;
        }
    }

    public EntityItem()
    {
        // Cached once per instance, so the per-item ContextMenuForItem call allocates no closure per frame.
        _drawMenuItemsCached = DrawMenuItemsForCurrent;
    }

    // Menu context for the cached delegate: set per item before ContextMenuForItem, which invokes the body
    // synchronously for the (single) item whose popup is open — so these always hold that item's values.
    private readonly Action _drawMenuItemsCached;
    private Args _menuArgs;
    private SetupEntitySelection? _menuSelection;
    private Setup? _menuSetup;

    private Guid _renamingId;
    private string _renameBuffer = string.Empty;
    private bool _renameFocusPending;
}
