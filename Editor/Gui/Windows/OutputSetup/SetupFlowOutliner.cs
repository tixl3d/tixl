#nullable enable
using T3.Editor.SystemUi;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Core.Output.Streaming;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.InputUi.ListInputs;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The Flow Outliner: the strip under the output canvas that lays the setup out along its content flow —
/// CONTENT → SURFACES → OUTPUTS → LOCAL BINDINGS as columns, with a shelf for reference images and props
/// at the right end. Items are <see cref="OutlinerItem"/>s (surfaces nest by <see cref="Surface.ParentId"/>,
/// slices under their source, patches under their output); the relationships between them light up
/// through the gutters and the connections drawn between the columns. CONTENT lists the live <see cref="IContentSupplier"/> ops, everything else the active
/// setup; LOCAL BINDINGS is this machine's inventory of plugs.
/// </summary>
internal sealed class SetupFlowOutliner
{
    public SetupFlowOutliner()
    {
        _requestAddPlugMenu = _ => _addPlugMenuRequested = true;
        _drawDisplayTooltip = DrawDisplayTooltip;
    }

    /// <param name="onToggleCollapse">Collapses the strip to its header bar, or expands it again.</param>
    /// <param name="bodyVisible">False while collapsed: only the header draws.</param>
    /// <param name="drawGrip">The strip's resize grip, drawn first in the header.</param>
    /// <param name="drawToolbar">The canvas' toolbar (mode switch and its actions), drawn after the setup switcher.</param>
    /// <param name="drawMenuExtras">Window-level entries appended to the setup menu (outliner toggle, pin).</param>
    /// <param name="onLeave">Leaves the setup for the operator view; drawn as the header's close button.</param>
    public void Draw(SetupEntitySelection selection, Action? onToggleCollapse, bool bodyVisible,
                     Action? drawGrip = null, Action? drawToolbar = null, Action? drawMenuExtras = null, Action? onLeave = null)
    {
        _drawMenuExtras = drawMenuExtras;
        _onLeave = onLeave;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
        {
            CustomComponents.EmptyWindowMessage("No project focused");
            return;
        }

        _pendingHoveredKind = SetupEntityKinds.None;
        _pendingHoveredId = Guid.Empty;

        // Labels and the connection list follow the structure, not the frame.
        if (_cacheVersion != OutputSetupHandling.StructureVersion || _cacheSetupId != setup.Id)
            RefreshCaches(setup, machineConfig);

        // Del removes the selection while the strip has focus — the same verb as the items' context menu.
        if (bodyVisible && selection.Count > 0
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            && !ImGui.IsAnyItemActive()
            && ImGui.IsKeyPressed(ImGuiKey.Delete, false))
        {
            SetupActions.DeleteSelection(selection, setup);
        }

        DrawHeader(setup, selection, onToggleCollapse, bodyVisible, drawGrip, drawToolbar);

        if (bodyVisible)
            DrawColumns(setup, machineConfig, selection);

        OutlinerItem.ApplyPendingMove(setup);
        _hoveredKind = _pendingHoveredKind;
        _hoveredId = _pendingHoveredId;
    }

    /// <summary>Grip · setup switcher · the canvas' toolbar · collapse toggle at the right — one strip that is
    /// the whole window's toolbar while the strip is shown.</summary>
    private void DrawHeader(Setup setup, SetupEntitySelection selection, Action? onToggleCollapse, bool bodyVisible,
                            Action? drawGrip, Action? drawToolbar)
    {
        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetFrameHeight();
        var rowPos = ImGui.GetCursorScreenPos();
        var rowRight = rowPos.X + ImGui.GetContentRegionAvail().X;

        ImGui.SetCursorScreenPos(new Vector2(rowPos.X + 4 * scale, rowPos.Y + 3 * scale));
        if (drawGrip != null)
        {
            drawGrip();
            ImGui.SameLine(0, 6 * scale);
        }

        DrawSetupSwitcher(setup, selection);

        if (drawToolbar != null)
        {
            ImGui.SameLine(0, 12 * scale);
            drawToolbar();
        }

        // Help sits left of the collapse toggle, like the help affordance of the settings panels: hovering
        // previews the view's overview in the Help window, clicking opens it there.
        var toggleWidth = onToggleCollapse != null ? height : 0;
        ImGui.SetCursorScreenPos(new Vector2(rowRight - toggleWidth - height, rowPos.Y + 3 * scale));
        DocumentationButton.Draw(HelpDocId, HelpWikiUrl, new Vector2(height, height));

        // The way out: back to the operator view, whatever the window is pinned to or the graph has focused.
        var leaveWidth = 0f;
        if (_onLeave != null)
        {
            leaveWidth = height;
            ImGui.SetCursorScreenPos(new Vector2(rowRight - toggleWidth - height * 2, rowPos.Y + 3 * scale));
            if (CustomComponents.IconButton(Icon.Close, Vector2.Zero))
                _onLeave();

            CustomComponents.TooltipForLastItem("Leave the output setup", "Back to the operator view; the Output Setup button in its toolbar returns here.");
        }

        // Reference images and props belong to the Board, not to any flow column, so they have no "+" of their
        // own. This one is theirs — reachable without having to find bare Board to right-click.
        ImGui.SetCursorScreenPos(new Vector2(rowRight - toggleWidth - leaveWidth - height * 2, rowPos.Y + 3 * scale));
        if (CustomComponents.IconButton(Icon.Plus, Vector2.Zero))
            ImGui.OpenPopup(AddBoardItemMenuId);

        CustomComponents.TooltipForLastItem("Add to the Board", "A reference photo to trace surfaces on, a prop for scale, or a whole room.");

        var openRoomDialog = false;
        if (ImGui.BeginPopup(AddBoardItemMenuId))
        {
            if (CustomComponents.DrawMenuItem(1, "Add Reference Image"))
                SetupActions.AddReferenceImage(selection);

            CustomComponents.TooltipForLastItem("Adds an empty image card; pick its photo in the Parameter window.", "Or drop an image file onto the Board.");

            if (CustomComponents.DrawMenuItem(2, "Add Prop"))
                SetupActions.AddProp(selection);

            CustomComponents.TooltipForLastItem("A box of known size on the Board — a doorway, a table — to judge the venue against.");

            // The dialog can't open from inside the menu (it would close with it), so it is opened once the menu is gone.
            if (CustomComponents.DrawMenuItem(3, "Add Floor Plan..."))
                openRoomDialog = true;

            CustomComponents.TooltipForLastItem("The venue seen from above: a footprint whose edges carry the walls, drawn at true scale.");

            ImGui.EndPopup();
        }

        if (openRoomDialog)
            ImGui.OpenPopup(AddRoomDialogId);

        DrawAddRoomDialog(setup, selection);

        if (onToggleCollapse != null)
        {
            ImGui.SetCursorScreenPos(new Vector2(rowRight - height, rowPos.Y + 3 * scale));
            if (CustomComponents.IconButton(bodyVisible ? Icon.ChevronDown : Icon.ChevronUp, Vector2.Zero))
                onToggleCollapse();

            CustomComponents.TooltipForLastItem(bodyVisible ? "Collapse the outliner to its header" : "Expand the outliner");
        }

        ImGui.SetCursorScreenPos(new Vector2(rowPos.X, rowPos.Y + height + 6 * scale));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>
    /// The columns side by side inside one shared vertical scroll region: the four flow columns split the
    /// width left of the shelf equally. Each column draws its header and items at its own x; the tallest one
    /// sets the scroll extent.
    /// </summary>
    private void DrawColumns(Setup setup, MachineConfig machineConfig, SetupEntitySelection selection)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.BeginChild("##outlinerBody", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);

        // Right-drag pans the list, as it does in the parameter popup. The item menus already ignore a right
        // release that came from a drag, so the two gestures don't fight over the button.
        CustomComponents.HandleDragScrolling(this);

        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        // Connections and dividers go under the items: split once, items on top, merge at the end. The items stay the
        // click targets; connections are display-only for now.
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        _anchors.Clear();
        _fenceCandidates.Clear();
        var columnWidth = MathF.Max(avail.X / 4, 60 * scale);
        var maxY = origin.Y;

        // Items are inset from the column boundaries so the gutters between columns have room for the connections.
        var gap = ColumnGap * scale;

        BeginColumn(origin.X + gap * 0.5f, origin.Y, columnWidth - gap);
        DrawColumnHeader("CONTENT", "##addContent", selection, ContentSourceSync.AddContentSend, SetupEntityKinds.ContentSource);
        DrawContentSends(selection, setup);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + columnWidth + gap * 0.5f, origin.Y, columnWidth - gap);
        DrawColumnHeader("SURFACES", "##addSurface", selection, SetupActions.AddSurface, SetupEntityKinds.Surface);
        DrawSurfaces(selection, setup);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + 2 * columnWidth + gap * 0.5f, origin.Y, columnWidth - gap);
        DrawColumnHeader("OUTPUTS", "##addOutput", selection, SetupActions.AddOutput, SetupEntityKinds.Output);
        DrawOutputs(selection, setup, machineConfig);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + 3 * columnWidth + gap * 0.5f, origin.Y, columnWidth - gap);
        DrawColumnHeader("LOCAL BINDINGS", "##addPlug", selection, _requestAddPlugMenu, SetupEntityKinds.Plug);
        DrawLocalBindings(selection, setup, machineConfig);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        // Reference images and props are Board cards, not flow items: they are added from the Board's own menu.

        // Column dividers span the visible body, or the content when it scrolls past it.
        dl.ChannelsSetCurrent(0);
        var dividerBottom = MathF.Max(maxY, origin.Y + avail.Y);
        for (var i = 1; i <= 3; i++)
        {
            var x = (float)Math.Round(origin.X + i * columnWidth);
            dl.AddLine(new Vector2(x, origin.Y), new Vector2(x, dividerBottom), UiColors.BackgroundFull, 1 * scale);
        }

        DrawConnections(dl, setup, machineConfig, selection);
        dl.ChannelsMerge();
        HandleFence(selection);

        // One item at the tallest column's end claims the scroll extent for all of them.
        _columnWidth = 0;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, maxY));
        ImGui.Dummy(Vector2.Zero);

        // A click on empty strip clears the selection — the way back to the Board from any entity's canvas.
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
            selection.Clear();

        ImGui.EndChild();
    }

    /// <summary>
    /// The routing as connections between items: slice → surface, slice → patch, surface → output (one per mapping,
    /// so a fan-out reads as two lines), output → plug. Each carries the colour of the kind it starts from; faded
    /// at rest, full and thicker while either end is hovered or selected. Items folded under a collapsed parent
    /// attach to that parent.
    /// </summary>
    private void DrawConnections(ImDrawListPtr dl, Setup setup, MachineConfig machineConfig, SetupEntitySelection selection)
    {
        for (var i = 0; i < _connections.Count; i++)
        {
            var c = _connections[i];
            DrawConnection(dl, selection, c.FromKind, c.FromId, c.ToKind, c.ToId, setup);
        }
    }

    /// <summary>
    /// Rebuilds what only changes with the structure: the connection list and the derived labels (a slice's
    /// or patch's "… N" name depends on list order). Runs on a structure-version tick or a setup switch, so
    /// the per-frame draw only looks things up.
    /// </summary>
    private void RefreshCaches(Setup setup, MachineConfig machineConfig)
    {
        _cacheVersion = OutputSetupHandling.StructureVersion;
        _cacheSetupId = setup.Id;
        _connections.Clear();
        _sliceLabels.Clear();
        _patchLabels.Clear();

        foreach (var slice in setup.Slices)
            _sliceLabels[slice.Id] = SetupLabels.SliceLabel(setup, slice);

        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId != Guid.Empty)
                _connections.Add(new Connection(SetupEntityKinds.Slice, surface.SliceId, SetupEntityKinds.Surface, surface.Id));

            foreach (var mapping in surface.OutputMappings)
                _connections.Add(new Connection(SetupEntityKinds.Surface, surface.Id, SetupEntityKinds.Output, mapping.OutputId));
        }

        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Default)
                continue;

            foreach (var patch in output.Patches)
            {
                _patchLabels[patch.Id] = SetupLabels.PatchLabel(output, patch);
                if (patch.SliceId != Guid.Empty)
                    _connections.Add(new Connection(SetupEntityKinds.Slice, patch.SliceId, SetupEntityKinds.Patch, patch.Id));
            }

            var binding = machineConfig.FindBinding(output.Id);
            if (binding != null)
                _connections.Add(new Connection(SetupEntityKinds.Output, output.Id, SetupEntityKinds.Plug, Plugs.BoundPlugId(binding)));
        }
    }

    private readonly record struct Connection(SetupEntityKinds FromKind, Guid FromId,
                                              SetupEntityKinds ToKind, Guid ToId);

    private void DrawConnection(ImDrawListPtr dl, SetupEntitySelection selection,
                          SetupEntityKinds fromKind, Guid fromId,
                          SetupEntityKinds toKind, Guid toId, Setup setup)
    {
        if (!TryGetAnchor(setup, fromKind, fromId, out var from) || !TryGetAnchor(setup, toKind, toId, out var to))
            return;

        var scale = T3Ui.UiScaleFactor;
        var emphasized = IsEmphasized(selection, fromKind, fromId) || IsEmphasized(selection, toKind, toId);
        var kindColor = SetupColors.ForKind(fromKind);
        // Resting lines must still read against the dark strip; emphasis adds weight, not visibility.
        var color = emphasized ? kindColor : kindColor.Fade(0.6f);
        var thickness = (emphasized ? 2.5f : 1.5f) * scale;

        // Flush with the items: a connection grows out of one pill and into the next.
        var a = new Vector2(from.Right, from.Y);
        var b = new Vector2(to.Left, to.Y);
        var reach = MathF.Max(24 * scale, MathF.Abs(b.X - a.X) * 0.4f);
        dl.AddBezierCubic(a, a + new Vector2(reach, 0), b - new Vector2(reach, 0), b, color, thickness);
    }

    private bool IsEmphasized(SetupEntitySelection selection, SetupEntityKinds kind, Guid id)
    {
        return (_hoveredKind == kind && _hoveredId == id) || (kind != SetupEntityKinds.None && selection.IsSelected(kind, id));
    }

    /// <summary>The item's connection attachment, or its nearest drawn parent's when it is folded away.</summary>
    private bool TryGetAnchor(Setup setup, SetupEntityKinds kind, Guid id, out Anchor anchor)
    {
        for (var guard = 0; guard < 8; guard++)
        {
            for (var i = 0; i < _anchors.Count; i++)
            {
                if (_anchors[i].Kind == kind && _anchors[i].Id == id)
                {
                    anchor = _anchors[i];
                    return true;
                }
            }

            // Not drawn — fold up one level and try again.
            switch (kind)
            {
                case SetupEntityKinds.Slice:
                    var source = setup.FindSource(setup.FindSlice(id)?.SourceId ?? Guid.Empty);
                    if (source == null)
                        goto fail;

                    kind = SetupEntityKinds.ContentSource;
                    id = source.SymbolChildId;
                    break;

                case SetupEntityKinds.Patch:
                    if (setup.FindPatch(id, out var owner) == null || owner == null)
                        goto fail;

                    kind = SetupEntityKinds.Output;
                    id = owner.Id;
                    break;

                case SetupEntityKinds.Surface:
                    var parentId = setup.FindSurface(id)?.ParentId ?? Guid.Empty;
                    if (parentId == Guid.Empty)
                        goto fail;

                    id = parentId;
                    break;

                default:
                    goto fail;
            }
        }

        fail:
        anchor = default;
        return false;
    }

    /// <summary>Where an item's connections attach: its left and right x and its vertical centre, in screen px.</summary>
    private readonly record struct Anchor(SetupEntityKinds Kind, Guid Id, float Left, float Right, float Y);

    /// <summary>
    /// A drag on empty strip space fences the items it crosses, as on the Board: plain replaces, Shift adds,
    /// Ctrl removes; a click on nothing clears. It never starts on an item, whose press is a pick or a drag.
    /// </summary>
    private void HandleFence(SetupEntitySelection selection)
    {
        // A fence starts only on the frame the button goes down, and only on empty space — a press on an item is
        // that item's (a pick, a routing drag), and picking it up mid-press would end as a click that clears.
        if (_fence.State == SelectionFence.States.Inactive
            && (!ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsAnyItemHovered() || ImGui.IsAnyItemActive()))
        {
            return;
        }

        switch (_fence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.Updated:
            case SelectionFence.States.CompletedAsArea:
                if (selectMode == SelectionFence.SelectModes.Replace)
                    selection.Clear();

                var bounds = _fence.BoundsInScreen;
                for (var i = 0; i < _fenceCandidates.Count; i++)
                {
                    var (kind, id, rect) = _fenceCandidates[i];
                    if (!bounds.Overlaps(rect))
                        continue;

                    if (selectMode == SelectionFence.SelectModes.Remove)
                        selection.Remove(kind, id);
                    else
                        selection.Add(kind, id);
                }

                break;

            case SelectionFence.States.CompletedAsClick:
                selection.Clear();
                break;
        }
    }

    private readonly SelectionFence _fence = new();
    private readonly List<(SetupEntityKinds Kind, Guid Id, ImRect Rect)> _fenceCandidates = [];

    /// <summary>Points the cursor at a column's top and tells the items how wide they are.</summary>
    private void BeginColumn(float x, float y, float width)
    {
        _columnMinX = x;
        _columnWidth = width;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
    }

    /// <summary>A column's persistent title, tinted in its kind's colour, with its `+` at the right end (none for the plug inventory).</summary>
    private void DrawColumnHeader(string title, string? addButtonId, SetupEntitySelection selection, Action<SetupEntitySelection>? onAdd,
                                  SetupEntityKinds kind = SetupEntityKinds.None)
    {
        var scale = T3Ui.UiScaleFactor;
        // Anchored on the column's x: a spacer before a second header (the shelf) resets the cursor's x to the window.
        var pos = new Vector2(_columnMinX, ImGui.GetCursorScreenPos().Y);
        var height = ImGui.GetFrameHeight();

        ImGui.SetCursorScreenPos(new Vector2(pos.X + 8 * scale, pos.Y));
        ImGui.AlignTextToFramePadding();
        var titleColor = kind == SetupEntityKinds.None ? UiColors.TextMuted : SetupColors.ForKind(kind).Fade(0.8f);
        CustomComponents.StylizedText(title, Fonts.FontSmall, titleColor);

        if (addButtonId != null && onAdd != null)
        {
            ImGui.SetCursorScreenPos(new Vector2(pos.X + _columnWidth - height - 4 * scale, pos.Y));
            ImGui.PushID(addButtonId);
            if (CustomComponents.IconButton(Icon.Plus, Vector2.Zero))
                onAdd(selection);

            ImGui.PopID();
        }

        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height + 2 * scale));
        ImGui.Dummy(Vector2.Zero);
    }

    private void DrawOutputs(SetupEntitySelection selection, Setup setup, MachineConfig machineConfig)
    {
        for (var i = 0; i < setup.Outputs.Count; i++)
        {
            var output = setup.Outputs[i];
            // The Default output is the editor's internal preview, not something you present or map — hide it.
            if (output.Kind == OutputDefinition.Kinds.Default)
                continue;

            // The implicit full-canvas patch is the output itself here: no item, and its slice connection
            // lands on the output item (TryGetAnchor folds an undrawn patch onto its output).
            var hasPatches = SetupRelations.CountListedPatches(output) > 0;
            // A rename requested from the canvas needs the row on screen: an output folded over that patch unfolds.
            var reveal = OutlinerItem.RevealPendingId;
            if (reveal != Guid.Empty && setup.FindPatch(reveal, out var revealOwner) != null && revealOwner?.Id == output.Id)
                _collapsedOutputs.Remove(output.Id);

            var isExpanded = !_collapsedOutputs.Contains(output.Id);
            var args = new OutlinerItem.Args
                           {
                               Kind = SetupEntityKinds.Output,
                               Id = output.Id,
                               Name = output.Name,
                               // Nothing presents it yet — said in words, where the plug connection would otherwise start.
                               Status = machineConfig.FindBinding(output.Id) == null ? "unbound" : null,
                               IsExpanded = hasPatches ? isExpanded : null,
                               KeepsExpanderColumn = true,
                               // A paused output (Send off) reads the same as a non-rendering surface.
                               IsMuted = !output.IsSending,
                               HasStruckIcon = !output.IsSending,
                           };
            if (DrawItem(selection, setup, ref args) == OutlinerItem.Actions.ToggleExpanded)
            {
                if (!_collapsedOutputs.Add(output.Id))
                    _collapsedOutputs.Remove(output.Id);
            }

            if (!hasPatches || !isExpanded)
                continue;

            // Patches under their output, like regions under a surface: the direct pipe's canvas cuts.
            for (var p = 0; p < output.Patches.Count; p++)
            {
                if (!SetupRelations.IsImplicitPatch(output, output.Patches[p]))
                    DrawPatchItem(selection, setup, output, output.Patches[p]);
            }
        }
    }

    /// <summary>
    /// This machine's plugs — the attached displays and its stream senders — as an inventory: every plug is
    /// listed, the bound ones read normal (their connection says which output), the free ones recede. A
    /// display an output is bound to but that isn't attached right now is listed too, so its connection has
    /// somewhere to land. Plug items aren't selectable; they take an output by drop and offer their menu.
    /// The column's "+" adds a stream sender of any kind whose package is loaded.
    /// </summary>
    private void DrawLocalBindings(SetupEntitySelection selection, Setup setup, MachineConfig machineConfig)
    {
        var screens = EditorUi.Instance.AllScreens;
        for (var i = 0; i < screens.Count; i++)
        {
            var plugId = Plugs.DisplayPlugId(i);
            _tooltipDisplayIndex = i;
            var args = new OutlinerItem.Args
                           {
                               Kind = SetupEntityKinds.Plug,
                               Id = plugId,
                               Name = Plugs.DisplayLabel(i),
                               Status = ResolutionLabel(i, screens[i].Bounds.Width, screens[i].Bounds.Height),
                               IsMuted = !IsPlugBound(setup, machineConfig, plugId),
                               DrawTooltip = _drawDisplayTooltip,
                           };
            DrawItem(selection, setup, ref args);
        }

        foreach (var binding in machineConfig.Bindings)
        {
            if (binding.IsStream || binding.DisplayIndex < screens.Count || setup.FindOutput(binding.OutputId) == null)
                continue;

            var args = new OutlinerItem.Args
                           {
                               Kind = SetupEntityKinds.Plug,
                               Id = Plugs.DisplayPlugId(binding.DisplayIndex),
                               Name = Plugs.DisplayLabel(binding.DisplayIndex),
                               Status = "not attached",
                               IsMuted = true,
                           };
            DrawItem(selection, setup, ref args);
        }

        for (var i = 0; i < machineConfig.StreamPlugs.Count; i++)
        {
            var stream = machineConfig.StreamPlugs[i];
            var available = Plugs.IsStreamKindAvailable(stream.Kind);
            var args = new OutlinerItem.Args
                           {
                               Kind = SetupEntityKinds.Plug,
                               Id = stream.Id,
                               Name = stream.Name,
                               Status = available ? stream.Kind : MissingPackageStatus(stream.Kind),
                               LeadingIcon = Icon.ConnectedOutput,
                               IsMuted = !available || !IsPlugBound(setup, machineConfig, stream.Id),
                           };
            DrawItem(selection, setup, ref args);
        }

        if (_addPlugMenuRequested)
        {
            ImGui.OpenPopup(AddPlugMenuId);
            _addPlugMenuRequested = false;
        }

        if (ImGui.BeginPopup(AddPlugMenuId))
        {
            CustomComponents.MenuGroupHeader("Add stream sender");
            var providers = OutputStreamRegistry.Providers;
            if (providers.Count == 0)
                CustomComponents.DrawMenuItem(0, "No stream packages loaded (Spout, NDI)", isEnabled: false);

            for (var i = 0; i < providers.Count; i++)
            {
                if (CustomComponents.DrawMenuItem(1 + i, providers[i].Kind))
                {
                    var stream = Plugs.AddStream(machineConfig, providers[i].Kind);
                    OutlinerItem.BeginRename(selection, SetupEntityKinds.Plug, stream.Id, stream.Name);
                }
            }

            ImGui.EndPopup();
        }
    }

    private static string MissingPackageStatus(string kind)
    {
        if (!_missingPackageStatus.TryGetValue(kind, out var status))
        {
            status = $"{kind} · package not loaded";
            _missingPackageStatus[kind] = status;
        }

        return status;
    }

    private static readonly Dictionary<string, string> _missingPackageStatus = [];

    /// <summary>
    /// Where the hovered display sits among the machine's screens. The arrangement is only ever asked about
    /// one display at a time, so it rides the row it belongs to instead of a window of its own.
    /// </summary>
    private void DrawDisplayTooltip()
    {
        DisplayLayoutView.DrawTooltip(_tooltipDisplayIndex);
    }

    private static bool IsPlugBound(Setup setup, MachineConfig machineConfig, Guid plugId)
    {
        foreach (var binding in machineConfig.Bindings)
        {
            if (Plugs.BoundPlugId(binding) == plugId && setup.FindOutput(binding.OutputId) != null)
                return true;
        }

        return false;
    }

    private static string ResolutionLabel(int displayIndex, int width, int height)
    {
        while (_resolutionLabels.Count <= displayIndex)
            _resolutionLabels.Add((0, 0, string.Empty));

        // Rebuilt only when the display's mode changed since the last look.
        var cached = _resolutionLabels[displayIndex];
        if (cached.Width != width || cached.Height != height)
            _resolutionLabels[displayIndex] = cached = (width, height, $"{width}×{height}");

        return cached.Label;
    }

    private void DrawContentSends(SetupEntitySelection selection, Setup setup)
    {
        var suppliers = ContentSupplierRegistry.Suppliers;
        if (suppliers.Count == 0)
        {
            ImGui.Indent(8 * T3Ui.UiScaleFactor);
            CustomComponents.StylizedText("no SendToOutput ops", Fonts.FontSmall, UiColors.TextMuted.Fade(0.6f));
            ImGui.Unindent(8 * T3Ui.UiScaleFactor);
            return;
        }

        for (var i = 0; i < suppliers.Count; i++)
        {
            if (suppliers[i] is not Instance instance)
                continue;

            var childId = instance.SymbolChildId;
            var source = setup.FindSourceByChildId(childId);
            // The implicit full-frame slice is the source itself here: no item, its connections start at the source.
            var sliceCount = source == null ? 0 : SetupRelations.CountListedSlicesOfSource(setup, source.Id);
            var expanded = !_collapsedSources.Contains(childId);

            var args = new OutlinerItem.Args
                           {
                               Kind = SetupEntityKinds.ContentSource,
                               Id = childId,
                               Name = ContentSourceSync.SendName(instance),
                               IsExpanded = sliceCount > 0 ? expanded : null,
                               KeepsExpanderColumn = true,
                               // Nothing shows this source, so it steps back visually.
                               IsMuted = source == null || SetupRelations.CountConsumersOfSource(setup, source.Id) == 0,
                           };
            if (DrawItem(selection, setup, ref args) == OutlinerItem.Actions.ToggleExpanded)
                ToggleSourceExpanded(childId);

            if (source == null || sliceCount == 0 || !expanded)
                continue;

            // Indexed: an item's context menu can delete its slice mid-walk, which an enumerator would refuse.
            for (var s = 0; s < setup.Slices.Count; s++)
            {
                var slice = setup.Slices[s];
                if (slice.SourceId != source.Id)
                    continue;

                DrawSliceItem(selection, setup, slice);
            }
        }
    }

    /// <summary>A slice under its source. A slice nothing shows reads as "unused".</summary>
    private void DrawSliceItem(SetupEntitySelection selection, Setup setup, Slice slice)
    {
        var args = new OutlinerItem.Args
                       {
                           Kind = SetupEntityKinds.Slice,
                           Id = slice.Id,
                           Name = _sliceLabels.TryGetValue(slice.Id, out var sliceLabel) ? sliceLabel : SetupLabels.SliceLabel(setup, slice),
                           Depth = 1,
                           IsMuted = !SetupRelations.IsSliceShown(setup, slice.Id),
                       };
        DrawItem(selection, setup, ref args);
    }

    /// <summary>A patch under its output; unfed patches step back.</summary>
    private void DrawPatchItem(SetupEntitySelection selection, Setup setup, OutputDefinition output, OutputDefinition.Patch patch)
    {
        var args = new OutlinerItem.Args
                       {
                           Kind = SetupEntityKinds.Patch,
                           Id = patch.Id,
                           Name = _patchLabels.TryGetValue(patch.Id, out var patchLabel) ? patchLabel : SetupLabels.PatchLabel(output, patch),
                           Depth = 1,
                           IsMuted = setup.FindSlice(patch.SliceId) == null,
                       };
        DrawItem(selection, setup, ref args);
    }

    private void ToggleSourceExpanded(Guid childId)
    {
        if (!_collapsedSources.Add(childId))
            _collapsedSources.Remove(childId);
    }

    // Surfaces as a tree: roots first, each followed by its children (nested by ParentId).
    private void DrawSurfaces(SetupEntitySelection selection, Setup setup)
    {
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == Guid.Empty)
                DrawSurfaceItem(selection, setup, setup.Surfaces[i], 0);
        }
    }

    private void DrawSurfaceItem(SetupEntitySelection selection, Setup setup, Surface surface, int depth)
    {
        var surfaceId = surface.Id;
        var hasChildren = SetupRelations.CountChildren(setup, surfaceId) > 0;
        var isExpanded = !_collapsedSurfaces.Contains(surfaceId);

        var args = new OutlinerItem.Args
                       {
                           Kind = SetupEntityKinds.Surface,
                           Id = surface.Id,
                           Name = surface.Name,
                           // A region that overrides its parent's pin no longer follows it — say so, since the
                           // item still sits nested under that parent.
                           Status = SetupActions.HasOwnPin(surface) ? "own pin" : null,
                           Depth = depth,
                           IsExpanded = hasChildren ? isExpanded : null,
                           KeepsExpanderColumn = true,
                           // A surface that won't render reads as unused (dimmed) and is struck through its icon.
                           IsMuted = !surface.IsRendered,
                           HasStruckIcon = !surface.IsRendered,
                       };
        if (DrawItem(selection, setup, ref args) == OutlinerItem.Actions.ToggleExpanded)
            ToggleSurfaceExpanded(surfaceId);

        if (!hasChildren || !isExpanded)
            return;

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == surfaceId)
                DrawSurfaceItem(selection, setup, setup.Surfaces[i], depth + 1);
        }
    }

    private void ToggleSurfaceExpanded(Guid surfaceId)
    {
        if (!_collapsedSurfaces.Add(surfaceId))
            _collapsedSurfaces.Remove(surfaceId);
    }

    /// <summary>The setup's name and its menu, as one outlined control: name and chevron belong together.</summary>
    private void DrawSetupSwitcher(Setup setup, SetupEntitySelection selection)
    {
        var scale = T3Ui.UiScaleFactor;
        var pos = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        var width = MathF.Max(60 * scale, ImGui.CalcTextSize(setup.Name).X + Icons.FontSize + 16 * scale);
        if (ImGui.InvisibleButton("##setupSwitcher", new Vector2(width, height)))
            ImGui.OpenPopup("##setupMenu");

        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(pos, pos + new Vector2(width, height), (hovered ? UiColors.Text : UiColors.TextMuted).Fade(0.4f), 3 * scale);

        // Label + chevron drawn over the button so the chevron sits next to the name (not far-right like a combo).
        ImGui.SetCursorScreenPos(new Vector2(pos.X + 6 * scale, pos.Y));
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(setup.Name, Fonts.FontNormal, UiColors.Text);
        ImGui.SameLine(0, 4 * scale);
        Icons.DrawInlineGlyph(Icon.ChevronDown, UiColors.TextMuted.Rgba);

        ImGui.SetCursorScreenPos(new Vector2(pos.X + width, pos.Y));
        ImGui.Dummy(Vector2.Zero); // the row continues to the right of the control

        if (ImGui.BeginPopup("##setupMenu"))
        {
            CustomComponents.MenuGroupHeader("Setups");
            _availableNames.Clear();
            OutputSetupHandling.GetAvailableSetupNames(_availableNames);
            for (var i = 0; i < _availableNames.Count; i++)
            {
                var name = _availableNames[i];
                if (CustomComponents.DrawMenuItem(i, name, isChecked: name == setup.Name) && name != setup.Name)
                {
                    if (OutputSetupHandling.TrySwitchTo(name))
                        selection.Clear();
                }
            }

            CustomComponents.SeparatorLine();
            if (CustomComponents.DrawMenuItem(900, "Duplicate current"))
            {
                OutputSetupHandling.TryDuplicateActive(GetFreeName(setup.Name + " copy"));
            }
            CustomComponents.TooltipForLastItem("Duplicates the setup for another venue.",
                                                "Entity ids are preserved, so operator bindings stay intact.");

            if (CustomComponents.DrawMenuItem(901, "New (empty)"))
            {
                if (OutputSetupHandling.TryCreateNew(GetFreeName("Setup")))
                    selection.Clear();
            }
            CustomComponents.TooltipForLastItem("Creates a fresh setup with new entity ids.",
                                                "Operator bindings into it will be unresolved until re-assigned.");

            if (_availableNames.Count > 1 && CustomComponents.DrawMenuItem(902, "Delete"))
            {
                if (OutputSetupHandling.TryDeleteActive())
                    selection.Clear();
            }

            if (_drawMenuExtras != null)
            {
                CustomComponents.SeparatorLine();
                _drawMenuExtras();
            }

            ImGui.EndPopup();
        }
    }

    /// <summary>Outliner-side item wrapper: injects the column rect every item needs, records its anchor for
    /// the connections, and its hover for their emphasis next frame.</summary>
    private OutlinerItem.Actions DrawItem(SetupEntitySelection selection, Setup setup, ref OutlinerItem.Args args)
    {
        args.ColumnMinX = _columnMinX;
        args.ColumnWidth = _columnWidth;
        var action = _outlinerItem.DrawItem(selection, setup, in args, out var hovered);
        var rect = _outlinerItem.LastItemRect;
        _anchors.Add(new Anchor(args.Kind, args.Id, rect.Min.X, rect.Max.X, (rect.Min.Y + rect.Max.Y) * 0.5f));
        _fenceCandidates.Add((args.Kind, args.Id, new ImRect(rect.Min, rect.Max)));
        if (hovered)
        {
            _pendingHoveredKind = args.Kind;
            _pendingHoveredId = args.Id;
        }

        return action;
    }

    private static string GetFreeName(string baseName)
    {
        _availableNames.Clear();
        OutputSetupHandling.GetAvailableSetupNames(_availableNames);
        if (!_availableNames.Contains(baseName))
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!_availableNames.Contains(candidate))
                return candidate;
        }

        return baseName + " new";
    }

    private static readonly List<string> _availableNames = [];
    private static readonly List<(int Width, int Height, string Label)> _resolutionLabels = [];

    private const string AddPlugMenuId = "##addPlugMenu";
    /// <summary>A rectangular footprint to start from; walls are raised per edge on the plan's card afterwards.</summary>
    private static void DrawAddRoomDialog(Setup setup, SetupEntitySelection selection)
    {
        ImGui.SetNextWindowSize(new Vector2(280 * T3Ui.UiScaleFactor, 0));
        if (!ImGui.BeginPopup(AddRoomDialogId))
            return;

        CustomComponents.StylizedText("Add Floor Plan", Fonts.FontBold, UiColors.Text);
        CustomComponents.StylizedText("A rectangle to start from; raise walls on its edges on its card.",
                                      Fonts.FontSmall, UiColors.TextMuted);

        FormInputs.AddFloat("Width (m)", ref _roomWidth, 0.1f, 1000, 0.05f, clampMin: true, clampMax: true, "Left to right.");
        FormInputs.AddFloat("Depth (m)", ref _roomDepth, 0.1f, 1000, 0.05f, clampMin: true, clampMax: true, "Near to far.");
        FormInputs.AddCheckBox("With floor surface", ref _roomWithFloor, "A surface lying on the footprint, for floor projection.");

        FormInputs.AddVerticalSpace(4);
        if (ImGui.Button("Create"))
        {
            SetupActions.AddFloorPlan(selection, new Vector2(_roomWidth, _roomDepth), _roomWithFloor);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private Action? _onLeave;
    private const string AddBoardItemMenuId = "##addBoardItemMenu";
    private const string AddRoomDialogId = "##addFloorPlanDialog";

    // The dialog's fields, kept between openings so a venue's numbers can be tweaked and re-added.
    private static float _roomWidth = 10;
    private static float _roomDepth = 8;
    private static bool _roomWithFloor = true;
    private const string HelpDocId = "OutputSetup";
    private const string HelpWikiUrl = "https://github.com/tixl3d/tixl/wiki/help.OutputSetup";
    private bool _addPlugMenuRequested;

    // The display whose row is being drawn, read by the tooltip; a field rather than a captured lambda, which
    // would allocate a closure for every plug row every frame.
    private int _tooltipDisplayIndex;
    private readonly Action _drawDisplayTooltip;
    private readonly Action<SetupEntitySelection> _requestAddPlugMenu;

    // The column the items currently draw into (screen x + width); 0 width = whole window.
    private float _columnMinX;
    private float _columnWidth;

    // Per-structure caches (see RefreshCaches), keyed on the structure version and the setup.
    private int _cacheVersion = -1;
    private Guid _cacheSetupId;
    private readonly List<Connection> _connections = [];
    private readonly Dictionary<Guid, string> _sliceLabels = [];
    private readonly Dictionary<Guid, string> _patchLabels = [];

    private Action? _drawMenuExtras;

    // Items drawn this frame, for the connections (cleared per frame; a few dozen entries, searched linearly).
    private readonly List<Anchor> _anchors = [];

    private const float ColumnGap = 28; // unscaled px; the gutter the connections run through

    // Surfaces whose children are folded away; expanded is the default, so only collapses are tracked.
    private readonly HashSet<Guid> _collapsedSurfaces = [];
    private readonly HashSet<Guid> _collapsedSources = [];
    private readonly HashSet<Guid> _collapsedOutputs = [];
    private readonly OutlinerItem _outlinerItem = new();

    // The item hovered this frame (committed at end of Draw) — its connections draw emphasized next frame.
    private SetupEntityKinds _hoveredKind;
    private Guid _hoveredId;
    private SetupEntityKinds _pendingHoveredKind;
    private Guid _pendingHoveredId;
}
