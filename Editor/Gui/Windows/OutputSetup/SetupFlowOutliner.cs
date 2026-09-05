#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
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
/// at the right end. Rows are <see cref="EntityItem"/>s (surfaces nest by <see cref="Surface.ParentId"/>,
/// slices under their source, patches under their output); the relationships between them light up
/// through the gutters and the connections drawn between the columns. CONTENT lists the live <see cref="IOutputSink"/> ops, everything else the active
/// setup; LOCAL BINDINGS is this machine's inventory of plugs.
/// </summary>
internal sealed class SetupFlowOutliner
{
    /// <summary>One outliner per output window, sharing the window's <see cref="EntityItem"/> with its canvas
    /// views — so rename state and menus stay per-window instead of bleeding between open windows.</summary>
    public SetupFlowOutliner(EntityItem entityItem)
    {
        _entityItem = entityItem;
    }

    /// <param name="onToggleCollapse">Collapses the strip to its header bar, or expands it again.</param>
    /// <param name="bodyVisible">False while collapsed: only the header row draws.</param>
    public void Draw(SetupEntitySelection selection, Action? onToggleCollapse, bool bodyVisible)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
        {
            CustomComponents.EmptyWindowMessage("No project focused");
            return;
        }

        // (The hover cross-highlight border was removed — selection now shows its source via the "→|" marker.)

        // Resolved once: TryResolve prunes the target list behind a closure, and every row asks for the
        // primary when deciding whether to offer an in-gutter toggle.
        if (!selection.TryResolve(setup, out _primaryKind, out _primaryId))
        {
            _primaryKind = SetupEntitySelection.EntityKind.None;
            _primaryId = Guid.Empty;
        }

        _pendingHoveredKind = SetupEntitySelection.EntityKind.None;
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

        DrawHeader(setup, selection, onToggleCollapse, bodyVisible);

        if (bodyVisible)
            DrawColumns(setup, machineConfig, selection);

        _hoveredKind = _pendingHoveredKind;
        _hoveredId = _pendingHoveredId;
    }

    /// <summary>Setup switcher · breadcrumb of the primary's path · collapse toggle at the right.</summary>
    private void DrawHeader(Setup setup, SetupEntitySelection selection, Action? onToggleCollapse, bool bodyVisible)
    {
        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetFrameHeight();
        var rowPos = ImGui.GetCursorScreenPos();

        DrawSetupSwitcher(setup, selection, SwitcherWidth * scale);

        // Breadcrumb: what feeds the primary → the primary → what shows it, refreshed on a change of primary
        // or of the structure (renames, re-routing), never per frame.
        if (_breadcrumbKind != _primaryKind || _breadcrumbId != _primaryId || _breadcrumbVersion != _cacheVersion)
        {
            _breadcrumb = BuildBreadcrumb(setup);
            _breadcrumbKind = _primaryKind;
            _breadcrumbId = _primaryId;
            _breadcrumbVersion = _cacheVersion;
        }

        ImGui.SetCursorScreenPos(new Vector2(rowPos.X + (SwitcherWidth + 12) * scale, rowPos.Y));
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(_breadcrumb, Fonts.FontSmall, UiColors.TextMuted);

        if (onToggleCollapse != null)
        {
            ImGui.SetCursorScreenPos(new Vector2(rowPos.X + ImGui.GetContentRegionAvail().X - height, rowPos.Y));
            if (CustomComponents.IconButton(bodyVisible ? Icon.ChevronDown : Icon.ChevronUp, Vector2.Zero))
                onToggleCollapse();

            CustomComponents.TooltipForLastItem(bodyVisible ? "Collapse the outliner to its header" : "Expand the outliner");
        }

        ImGui.SetCursorScreenPos(new Vector2(rowPos.X, rowPos.Y + height + 2 * scale));
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>
    /// The columns side by side inside one shared vertical scroll region: the four flow columns split the
    /// width left of the shelf equally. Each column draws its header and rows at its own x; the tallest one
    /// sets the scroll extent.
    /// </summary>
    private void DrawColumns(Setup setup, MachineConfig machineConfig, SetupEntitySelection selection)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.BeginChild("##outlinerBody", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        // Connections and dividers go under the items: split once, items on top, merge at the end. The items stay the
        // click targets; connections are display-only for now.
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        _anchors.Clear();
        var shelfWidth = MathF.Min(ShelfWidth * scale, avail.X * 0.25f);
        var columnWidth = MathF.Max((avail.X - shelfWidth) / 4, 60 * scale);
        var maxY = origin.Y;

        // Items are inset from the column boundaries so the gutters between columns have room for the connections.
        var gap = ColumnGap * scale;

        BeginColumn(origin.X + gap * 0.5f, origin.Y, columnWidth - gap);
        DrawColumnHeader("CONTENT", "##addContent", selection, SetupActions.AddContentSend);
        DrawContentSends(selection, setup);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + columnWidth + gap * 0.5f, origin.Y, columnWidth - gap);
        DrawColumnHeader("SURFACES", "##addSurface", selection, SetupActions.AddSurface);
        DrawSurfaces(selection, setup);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + 2 * columnWidth + gap * 0.5f, origin.Y, columnWidth - gap);
        DrawColumnHeader("OUTPUTS", "##addOutput", selection, SetupActions.AddOutput);
        DrawOutputs(selection, setup, machineConfig);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + 3 * columnWidth + gap * 0.5f, origin.Y, columnWidth - gap);
        DrawColumnHeader("LOCAL BINDINGS", null, selection, null);
        DrawLocalBindings(selection, setup, machineConfig);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        // The shelf: kinds outside the flow, stacked as two small groups.
        BeginColumn(origin.X + 4 * columnWidth + gap * 0.5f, origin.Y, shelfWidth - gap);
        DrawColumnHeader("REFERENCE IMAGES", "##addRefImage", selection, SetupActions.AddReferenceImage);
        DrawReferenceImages(selection, setup);
        FormInputs.AddVerticalSpace(6);
        DrawColumnHeader("PROPS", "##addProp", selection, SetupActions.AddProp);
        DrawProps(selection, setup);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        // Column dividers span the visible body, or the content when it scrolls past it.
        dl.ChannelsSetCurrent(0);
        var dividerBottom = MathF.Max(maxY, origin.Y + avail.Y);
        for (var i = 1; i <= 4; i++)
        {
            var x = (float)Math.Round(origin.X + i * columnWidth);
            dl.AddLine(new Vector2(x, origin.Y), new Vector2(x, dividerBottom), UiColors.BackgroundFull, 1 * scale);
        }

        DrawConnections(dl, setup, machineConfig, selection);
        dl.ChannelsMerge();

        // One item at the tallest column's end claims the scroll extent for all of them.
        _columnWidth = 0;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, maxY));
        ImGui.Dummy(Vector2.Zero);
        ImGui.EndChild();
    }

    /// <summary>
    /// The routing as connections between items: slice → surface, slice → patch, surface → output (one per mapping,
    /// so a fan-out reads as two lines), output → plug. Blue like every "linked" state; faded at rest, full
    /// and thicker while either end is hovered or selected. An output nothing presents gets an attention stub.
    /// Items folded under a collapsed parent attach to that parent.
    /// </summary>
    private void DrawConnections(ImDrawListPtr dl, Setup setup, MachineConfig machineConfig, SetupEntitySelection selection)
    {
        for (var i = 0; i < _connections.Count; i++)
        {
            var c = _connections[i];
            DrawConnection(dl, selection, c.FromKind, c.FromId, c.ToKind, c.ToId, setup);
        }

        // Nothing presents these outputs: a short stub in the attention color, where the plug connection would start.
        var scale = T3Ui.UiScaleFactor;
        for (var i = 0; i < _unboundOutputIds.Count; i++)
        {
            if (!TryGetAnchor(setup, SetupEntitySelection.EntityKind.Output, _unboundOutputIds[i], out var anchor))
                continue;

            var from = new Vector2(anchor.Right, anchor.Y);
            dl.AddLine(from, from + new Vector2(ConnectionStubLength * scale, 0), UiColors.StatusAttention.Fade(0.8f), 2 * scale);
        }
    }

    /// <summary>
    /// Rebuilds what only changes with the structure: the connection list, the unbound outputs, and the
    /// derived labels (a slice's or patch's "… N" name depends on list order). Runs on a structure-version
    /// tick or a setup switch, so the per-frame draw only looks things up.
    /// </summary>
    private void RefreshCaches(Setup setup, MachineConfig machineConfig)
    {
        _cacheVersion = OutputSetupHandling.StructureVersion;
        _cacheSetupId = setup.Id;
        _connections.Clear();
        _unboundOutputIds.Clear();
        _sliceLabels.Clear();
        _patchLabels.Clear();

        foreach (var slice in setup.Slices)
            _sliceLabels[slice.Id] = SetupActions.SliceLabel(setup, slice);

        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId != Guid.Empty)
                _connections.Add(new Connection(SetupEntitySelection.EntityKind.Slice, surface.SliceId, SetupEntitySelection.EntityKind.Surface, surface.Id));

            foreach (var mapping in surface.OutputMappings)
                _connections.Add(new Connection(SetupEntitySelection.EntityKind.Surface, surface.Id, SetupEntitySelection.EntityKind.Output, mapping.OutputId));
        }

        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Default)
                continue;

            foreach (var patch in output.Patches)
            {
                _patchLabels[patch.Id] = SetupActions.PatchLabel(output, patch);
                if (patch.SliceId != Guid.Empty)
                    _connections.Add(new Connection(SetupEntitySelection.EntityKind.Slice, patch.SliceId, SetupEntitySelection.EntityKind.Patch, patch.Id));
            }

            var binding = machineConfig.TryGetBinding(output.Id);
            if (binding != null)
                _connections.Add(new Connection(SetupEntitySelection.EntityKind.Output, output.Id, SetupEntitySelection.EntityKind.None, DisplayRowId(binding.DisplayIndex)));
            else
                _unboundOutputIds.Add(output.Id);
        }
    }

    private readonly record struct Connection(SetupEntitySelection.EntityKind FromKind, Guid FromId,
                                              SetupEntitySelection.EntityKind ToKind, Guid ToId);

    private void DrawConnection(ImDrawListPtr dl, SetupEntitySelection selection,
                          SetupEntitySelection.EntityKind fromKind, Guid fromId,
                          SetupEntitySelection.EntityKind toKind, Guid toId, Setup setup)
    {
        if (!TryGetAnchor(setup, fromKind, fromId, out var from) || !TryGetAnchor(setup, toKind, toId, out var to))
            return;

        var scale = T3Ui.UiScaleFactor;
        var emphasized = IsEmphasized(selection, fromKind, fromId) || IsEmphasized(selection, toKind, toId);
        var color = emphasized ? UiColors.StatusAutomated : UiColors.StatusAutomated.Fade(0.35f);
        var thickness = (emphasized ? 2.5f : 1.5f) * scale;

        var a = new Vector2(from.Right + 2 * scale, from.Y);
        var b = new Vector2(to.Left - 2 * scale, to.Y);
        var reach = MathF.Max(24 * scale, MathF.Abs(b.X - a.X) * 0.4f);
        dl.AddBezierCubic(a, a + new Vector2(reach, 0), b - new Vector2(reach, 0), b, color, thickness);
    }

    private bool IsEmphasized(SetupEntitySelection selection, SetupEntitySelection.EntityKind kind, Guid id)
    {
        return (_hoveredKind == kind && _hoveredId == id) || (kind != SetupEntitySelection.EntityKind.None && selection.IsSelected(kind, id));
    }

    /// <summary>The item's connection attachment, or its nearest drawn parent's when it is folded away.</summary>
    private bool TryGetAnchor(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, out Anchor anchor)
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
                case SetupEntitySelection.EntityKind.Slice:
                    var source = setup.FindSource(setup.FindSlice(id)?.SourceId ?? Guid.Empty);
                    if (source == null)
                        goto fail;

                    kind = SetupEntitySelection.EntityKind.ContentSource;
                    id = source.SymbolChildId;
                    break;

                case SetupEntitySelection.EntityKind.Patch:
                    if (setup.FindPatch(id, out var owner) == null || owner == null)
                        goto fail;

                    kind = SetupEntitySelection.EntityKind.Output;
                    id = owner.Id;
                    break;

                case SetupEntitySelection.EntityKind.Surface:
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
    private readonly record struct Anchor(SetupEntitySelection.EntityKind Kind, Guid Id, float Left, float Right, float Y);

    /// <summary>Points the cursor at a column's top and tells the rows how wide they are.</summary>
    private void BeginColumn(float x, float y, float width)
    {
        _columnMinX = x;
        _columnWidth = width;
        ImGui.SetCursorScreenPos(new Vector2(x, y));
    }

    /// <summary>A column's persistent muted title with its `+` at the right end (none for the plug inventory).</summary>
    private void DrawColumnHeader(string title, string? addButtonId, SetupEntitySelection selection, Action<SetupEntitySelection>? onAdd)
    {
        var scale = T3Ui.UiScaleFactor;
        // Anchored on the column's x: a spacer before a second header (the shelf) resets the cursor's x to the window.
        var pos = new Vector2(_columnMinX, ImGui.GetCursorScreenPos().Y);
        var height = ImGui.GetFrameHeight();

        ImGui.SetCursorScreenPos(new Vector2(pos.X + 8 * scale, pos.Y));
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(title, Fonts.FontSmall, UiColors.TextMuted);

        if (addButtonId != null && onAdd != null)
        {
            ImGui.SetCursorScreenPos(new Vector2(pos.X + _columnWidth - height - 4 * scale, pos.Y));
            ImGui.PushID(addButtonId);
            if (CustomComponents.IconButton(Icon.Plus, Vector2.Zero))
            {
                onAdd(selection);
                OutputSetupHandling.SaveActive();
            }

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

            var hasPatches = output.Patches.Count > 0;
            var isExpanded = !_collapsedOutputs.Contains(output.Id);
            var args = new EntityItem.Args
                           {
                               Kind = SetupEntitySelection.EntityKind.Output,
                               Id = output.Id,
                               Name = output.Name,
                               LeadingIcon = Icon.Projector,
                               IsExpanded = hasPatches ? isExpanded : null,
                               ReserveExpander = true,
                               // A paused output (Send off) reads the same as a non-rendering surface.
                               Muted = !output.Send,
                               StrikeLeadingIcon = !output.Send,
                           };
            if (DrawRow(selection, setup, ref args) == EntityItem.ItemAction.ToggleExpanded)
            {
                if (!_collapsedOutputs.Add(output.Id))
                    _collapsedOutputs.Remove(output.Id);
            }

            if (!hasPatches || !isExpanded)
                continue;

            // Patches under their output, like regions under a surface: the direct pipe's canvas cuts.
            for (var p = 0; p < output.Patches.Count; p++)
                DrawPatchRow(selection, setup, output, output.Patches[p]);
        }
    }

    /// <summary>
    /// This machine's plugs — the displays today, streams later — as an inventory: every plug is listed,
    /// the bound ones read normal (their connection says which output), the free ones recede. Items are
    /// not entities (no selection, no menu); binding happens on the output's item.
    /// </summary>
    private void DrawLocalBindings(SetupEntitySelection selection, Setup setup, MachineConfig machineConfig)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            string? boundTo = null;
            foreach (var binding in machineConfig.Bindings)
            {
                if (binding.DisplayIndex != i)
                    continue;

                boundTo = setup.FindOutput(binding.OutputId)?.Name;
                break;
            }

            var args = new EntityItem.Args
                           {
                               Kind = SetupEntitySelection.EntityKind.None,
                               Id = DisplayRowId(i),
                               Name = DisplayLabel(i),
                               Status = ResolutionLabel(i, screens[i].Bounds.Width, screens[i].Bounds.Height),
                               LeadingIcon = Icon.PlayOutput,
                               Muted = boundTo == null,
                           };
            DrawRow(selection, setup, ref args);
        }
    }

    private void DrawReferenceImages(SetupEntitySelection selection, Setup setup)
    {
        for (var i = 0; i < setup.ReferenceImages.Count; i++)
        {
            var image = setup.ReferenceImages[i];
            var args = new EntityItem.Args
                           {
                               Kind = SetupEntitySelection.EntityKind.ReferenceImage,
                               Id = image.Id,
                               Name = image.Name,
                           };
            DrawRow(selection, setup, ref args);
        }
    }

    private void DrawProps(SetupEntitySelection selection, Setup setup)
    {
        for (var i = 0; i < setup.Props.Count; i++)
        {
            var prop = setup.Props[i];
            var args = new EntityItem.Args
                           {
                               Kind = SetupEntitySelection.EntityKind.Prop,
                               Id = prop.Id,
                               Name = prop.Kind,
                           };
            DrawRow(selection, setup, ref args);
        }
    }

    /// <summary>"Local / Display N" — cached per index; the per-frame plug rows must not build strings.</summary>
    private static string DisplayLabel(int displayIndex)
    {
        while (_displayLabels.Count <= displayIndex)
            _displayLabels.Add($"Local / Display {_displayLabels.Count + 1}");

        return _displayLabels[displayIndex];
    }

    private static string ResolutionLabel(int displayIndex, int width, int height)
    {
        while (_resolutionLabels.Count <= displayIndex)
            _resolutionLabels.Add(string.Empty);

        // Rebuilt only when the display's mode changed since the last look.
        var cached = _resolutionLabels[displayIndex];
        if (cached.Length == 0 || !cached.StartsWith(width.ToString()))
            _resolutionLabels[displayIndex] = cached = $"{width}×{height}";

        return cached;
    }

    // A stable per-display row id, so ImGui ids and hover pulses stay put across frames.
    private static Guid DisplayRowId(int displayIndex) => new(displayIndex + 1, 0x5c4e, 0x4e21, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>"what feeds it → the primary → what shows it", one hop each way, from <see cref="SetupRelations"/>.</summary>
    private string BuildBreadcrumb(Setup setup)
    {
        if (_primaryKind == SetupEntitySelection.EntityKind.None)
            return string.Empty;

        SetupRelations.CollectRelated(setup, _primaryKind, _primaryId, _breadcrumbScratch);
        _breadcrumbBuilder.Clear();
        foreach (var relation in _breadcrumbScratch)
        {
            if (relation.IsConsumer)
                continue;

            _breadcrumbBuilder.Append(SetupActions.NameForEntity(relation.Kind, relation.Id)).Append(" → ");
        }

        _breadcrumbBuilder.Append(SetupActions.NameForEntity(_primaryKind, _primaryId));

        foreach (var relation in _breadcrumbScratch)
        {
            if (relation.IsConsumer)
                _breadcrumbBuilder.Append(" → ").Append(SetupActions.NameForEntity(relation.Kind, relation.Id));
        }

        return _breadcrumbBuilder.ToString();
    }

    private void DrawContentSends(SetupEntitySelection selection, Setup setup)
    {
        var sinks = OutputSinkRegistry.Sinks;
        if (sinks.Count == 0)
        {
            ImGui.Indent(8 * T3Ui.UiScaleFactor);
            CustomComponents.StylizedText("no SendToOutput ops", Fonts.FontSmall, UiColors.TextMuted.Fade(0.6f));
            ImGui.Unindent(8 * T3Ui.UiScaleFactor);
            return;
        }

        _sendContext ??= new EvaluationContext();
        _sendContext.Reset();

        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is not Instance instance)
                continue;

            var childId = instance.SymbolChildId;
            var source = setup.FindSourceByChildId(childId);
            var sliceCount = source == null ? 0 : SetupRelations.CountSlicesOfSource(setup, source.Id);
            var expanded = !_collapsedSources.Contains(childId);

            var args = new EntityItem.Args
                           {
                               Kind = SetupEntitySelection.EntityKind.ContentSource,
                               Id = childId,
                               Name = SetupActions.SendName(instance),
                               LeadingIcon = Icon.FileImage,
                               IsExpanded = sliceCount > 0 ? expanded : null,
                               ReserveExpander = true,
                               // Nothing shows this source, so it steps back visually.
                               Muted = source == null || SetupRelations.CountConsumersOfSource(setup, source.Id) == 0,
                           };
            if (DrawRow(selection, setup, ref args) == EntityItem.ItemAction.ToggleExpanded)
                ToggleSourceExpanded(childId);

            if (source == null || sliceCount == 0 || !expanded)
                continue;

            foreach (var slice in setup.Slices)
            {
                if (slice.SourceId != source.Id)
                    continue;

                DrawSliceRow(selection, setup, slice);
            }
        }
    }

    /// <summary>A slice under its source. A slice nothing shows reads as "unused".</summary>
    private void DrawSliceRow(SetupEntitySelection selection, Setup setup, Slice slice)
    {
        var args = new EntityItem.Args
                       {
                           Kind = SetupEntitySelection.EntityKind.Slice,
                           Id = slice.Id,
                           Name = _sliceLabels.TryGetValue(slice.Id, out var sliceLabel) ? sliceLabel : SetupActions.SliceLabel(setup, slice),
                           LeadingIcon = Icon.Slice,
                           Depth = 1,
                           Muted = !SetupRelations.IsSliceShown(setup, slice.Id),
                       };
        DrawRow(selection, setup, ref args);
    }

    /// <summary>Out-gutter for a slice: the target-type icon plus a count when it feeds more than one. No
    /// label — the fade already says "unused", and where it lands is the icon; a name adds noise.</summary>
    /// <summary>A patch under its output; unfed patches step back.</summary>
    private void DrawPatchRow(SetupEntitySelection selection, Setup setup, OutputDefinition output, OutputDefinition.Patch patch)
    {
        var args = new EntityItem.Args
                       {
                           Kind = SetupEntitySelection.EntityKind.Patch,
                           Id = patch.Id,
                           Name = _patchLabels.TryGetValue(patch.Id, out var patchLabel) ? patchLabel : SetupActions.PatchLabel(output, patch),
                           LeadingIcon = Icon.Patch,
                           Depth = 1,
                           Muted = setup.FindSlice(patch.SliceId) == null,
                       };
        DrawRow(selection, setup, ref args);
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
                DrawSurfaceRow(selection, setup, setup.Surfaces[i], 0);
        }
    }

    private void DrawSurfaceRow(SetupEntitySelection selection, Setup setup, Surface surface, int depth)
    {
        var surfaceId = surface.Id;
        var hasChildren = SetupRelations.CountChildren(setup, surfaceId) > 0;
        var isExpanded = !_collapsedSurfaces.Contains(surfaceId);

        var args = new EntityItem.Args
                       {
                           Kind = SetupEntitySelection.EntityKind.Surface,
                           Id = surface.Id,
                           Name = surface.Name,
                           LeadingIcon = Icon.Grid,
                           Depth = depth,
                           IsExpanded = hasChildren ? isExpanded : null,
                           ReserveExpander = true,
                           // A surface that won't render reads as unused (dimmed) and is struck through its icon.
                           Muted = !surface.Render,
                           StrikeLeadingIcon = !surface.Render,
                       };
        if (DrawRow(selection, setup, ref args) == EntityItem.ItemAction.ToggleExpanded)
            ToggleSurfaceExpanded(surfaceId);

        if (!hasChildren || !isExpanded)
            return;

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == surfaceId)
                DrawSurfaceRow(selection, setup, setup.Surfaces[i], depth + 1);
        }
    }

    private void ToggleSurfaceExpanded(Guid surfaceId)
    {
        if (!_collapsedSurfaces.Add(surfaceId))
            _collapsedSurfaces.Remove(surfaceId);
    }

    private void DrawSetupSwitcher(Setup setup, SetupEntitySelection selection, float switcherWidth)
    {
        var scale = T3Ui.UiScaleFactor;
        var pos = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        if (ImGui.InvisibleButton("##setupSwitcher", new Vector2(switcherWidth, height)))
            ImGui.OpenPopup("##setupMenu");

        // Label + chevron drawn over the button so the chevron sits next to the name (not far-right like a combo).
        ImGui.SetCursorScreenPos(new Vector2(pos.X + 2 * scale, pos.Y));
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(setup.Name, Fonts.FontNormal, UiColors.Text);
        ImGui.SameLine(0, 4 * scale);
        Icons.DrawInlineGlyph(Icon.ChevronDown, UiColors.TextMuted.Rgba);

        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height));

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

            ImGui.EndPopup();
        }
    }

    /// <summary>Outliner-side item wrapper: injects the column rect and the bind context every item needs,
    /// records its anchor for the connections, and its hover for their emphasis next frame.</summary>
    private EntityItem.ItemAction DrawRow(SetupEntitySelection selection, Setup setup, ref EntityItem.Args args)
    {
        args.ColumnMinX = _columnMinX;
        args.ColumnWidth = _columnWidth;
        args.PrimaryKind = _primaryKind;
        args.PrimaryId = _primaryId;
        var action = _entityItem.DrawRow(selection, setup, in args, out var hovered);
        var rect = _entityItem.LastRowRect;
        _anchors.Add(new Anchor(args.Kind, args.Id, rect.Min.X, rect.Max.X, (rect.Min.Y + rect.Max.Y) * 0.5f));
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
    private static readonly List<string> _displayLabels = [];
    private static readonly List<string> _resolutionLabels = [];
    private static EvaluationContext? _sendContext;

    // The column the rows currently draw into (screen x + width); 0 width = whole window.
    private float _columnMinX;
    private float _columnWidth;

    // Header breadcrumb cache — rebuilt on a primary change or every ~half second, not per frame.
    private string _breadcrumb = string.Empty;
    private SetupEntitySelection.EntityKind _breadcrumbKind;
    private Guid _breadcrumbId;
    private int _breadcrumbVersion = -1;

    // Per-structure caches (see RefreshCaches), keyed on the structure version and the setup.
    private int _cacheVersion = -1;
    private Guid _cacheSetupId;
    private readonly List<Connection> _connections = [];
    private readonly List<Guid> _unboundOutputIds = [];
    private readonly Dictionary<Guid, string> _sliceLabels = [];
    private readonly Dictionary<Guid, string> _patchLabels = [];
    private readonly List<SetupRelations.Relation> _breadcrumbScratch = [];
    private readonly System.Text.StringBuilder _breadcrumbBuilder = new();

    // Items drawn this frame, for the connections (cleared per frame; a few dozen entries, searched linearly).
    private readonly List<Anchor> _anchors = [];

    private const float ConnectionStubLength = 14; // unscaled px
    private const float ColumnGap = 28; // unscaled px; the gutter the connections run through
    private const float SwitcherWidth = 180; // unscaled px
    private const float ShelfWidth = 200; // unscaled px

    // Surfaces whose children are folded away; expanded is the default, so only collapses are tracked.
    private readonly HashSet<Guid> _collapsedSurfaces = [];
    private readonly HashSet<Guid> _collapsedSources = [];
    private readonly HashSet<Guid> _collapsedOutputs = [];
    private readonly EntityItem _entityItem;
    private SetupEntitySelection.EntityKind _primaryKind;
    private Guid _primaryId;

    // The item hovered this frame (committed at end of Draw) — its connections draw emphasized next frame.
    private SetupEntitySelection.EntityKind _hoveredKind;
    private Guid _hoveredId;
    private SetupEntitySelection.EntityKind _pendingHoveredKind;
    private Guid _pendingHoveredId;
}
