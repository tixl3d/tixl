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
/// through the gutters. CONTENT lists the live <see cref="IOutputSink"/> ops, everything else the active
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

        // Relationship highlights follow last frame's hover (committed at the end of Draw), so related rows can
        // light their gutters before they're drawn this frame — a 1-frame lag that's imperceptible.
        if (_hoveredKind != SetupEntitySelection.EntityKind.None)
            SetupRelations.CollectRelated(setup, _hoveredKind, _hoveredId, _referenced);
        else
            _referenced.Clear();

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
        // and every ~half second (renames), never per frame.
        var frame = ImGui.GetFrameCount();
        if (_breadcrumbKind != _primaryKind || _breadcrumbId != _primaryId || frame - _breadcrumbFrame > 30)
        {
            _breadcrumb = BuildBreadcrumb(setup);
            _breadcrumbKind = _primaryKind;
            _breadcrumbId = _primaryId;
            _breadcrumbFrame = frame;
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
        var shelfWidth = MathF.Min(ShelfWidth * scale, avail.X * 0.25f);
        var columnWidth = MathF.Max((avail.X - shelfWidth) / 4, 60 * scale);
        var maxY = origin.Y;

        BeginColumn(origin.X, origin.Y, columnWidth);
        DrawColumnHeader("CONTENT", "##addContent", selection, SetupActions.AddContentSend);
        DrawContentSends(selection, setup);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + columnWidth, origin.Y, columnWidth);
        DrawColumnHeader("SURFACES", "##addSurface", selection, SetupActions.AddSurface);
        DrawSurfaces(selection, setup);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + 2 * columnWidth, origin.Y, columnWidth);
        DrawColumnHeader("OUTPUTS", "##addOutput", selection, SetupActions.AddOutput);
        DrawOutputs(selection, setup, machineConfig);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        BeginColumn(origin.X + 3 * columnWidth, origin.Y, columnWidth);
        DrawColumnHeader("LOCAL BINDINGS", null, selection, null);
        DrawLocalBindings(selection, setup, machineConfig);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        // The shelf: kinds outside the flow, stacked as two small groups.
        BeginColumn(origin.X + 4 * columnWidth, origin.Y, shelfWidth);
        DrawColumnHeader("REFERENCE IMAGES", "##addRefImage", selection, SetupActions.AddReferenceImage);
        DrawReferenceImages(selection, setup);
        FormInputs.AddVerticalSpace(6);
        DrawColumnHeader("PROPS", "##addProp", selection, SetupActions.AddProp);
        DrawProps(selection, setup);
        maxY = MathF.Max(maxY, ImGui.GetCursorScreenPos().Y);

        // Column dividers span the visible body, or the content when it scrolls past it.
        var dl = ImGui.GetWindowDrawList();
        var dividerBottom = MathF.Max(maxY, origin.Y + avail.Y);
        for (var i = 1; i <= 4; i++)
        {
            var x = (float)Math.Round(origin.X + i * columnWidth);
            dl.AddLine(new Vector2(x, origin.Y), new Vector2(x, dividerBottom), UiColors.BackgroundFull, 1 * scale);
        }

        // One item at the tallest column's end claims the scroll extent for all of them.
        _columnWidth = 0;
        ImGui.SetCursorScreenPos(new Vector2(origin.X, maxY));
        ImGui.Dummy(Vector2.Zero);
        ImGui.EndChild();
    }

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

            var binding = machineConfig.TryGetBinding(output.Id);
            var status = binding == null ? null : DisplayLabel(binding.DisplayIndex);
            var hasPatches = output.Patches.Count > 0;
            var isExpanded = !_collapsedOutputs.Contains(output.Id);
            var args = new EntityItem.Args
                           {
                               Kind = SetupEntitySelection.EntityKind.Output,
                               Id = output.Id,
                               Name = output.Name,
                               Status = status,
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
    /// the ones an output is bound to carry that output's name, the free ones recede. Rows are not
    /// entities (no selection, no menu); binding happens on the output's row.
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
                               Status = boundTo ?? ResolutionLabel(i, screens[i].Bounds.Width, screens[i].Bounds.Height),
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

    private bool IsHoverInputHighlighted(SetupEntitySelection.EntityKind kind, Guid id)
    {
        for (var i = 0; i < _referenced.Count; i++)
        {
            if (_referenced[i].IsConsumer && _referenced[i].Kind == kind && _referenced[i].Id == id)
                return true;
        }

        return false;
    }

    private bool IsHoverTrailingHighlighted(SetupEntitySelection.EntityKind kind, Guid id)
    {
        for (var i = 0; i < _referenced.Count; i++)
        {
            if (!_referenced[i].IsConsumer && _referenced[i].Kind == kind && _referenced[i].Id == id)
                return true;
        }

        return false;
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

            var (icon, text) = DescribeSourceGutter(setup, childId);
            var args = new EntityItem.Args
                           {
                               Kind = SetupEntitySelection.EntityKind.ContentSource,
                               Id = childId,
                               Name = SetupActions.SendName(instance),
                               Status = text,
                               LeadingIcon = Icon.FileImage,
                               TrailingIcon = icon,
                               IsExpanded = sliceCount > 0 ? expanded : null,
                               ReserveExpander = true,
                               // No surface shows this source, so it steps back visually.
                               Muted = icon == null,
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

    /// <summary>
    /// A slice under its source. The status carries its aspect, flagged when it disagrees with the surface
    /// showing it — a mismatch means the content lands stretched, which is invisible until it's on the wall.
    /// A slice nothing shows reads as "unused".
    /// </summary>
    private void DrawSliceRow(SetupEntitySelection selection, Setup setup, Slice slice)
    {
        var (targetIcon, targetText) = DescribeSliceTargetGutter(setup, slice);
        var args = new EntityItem.Args
                       {
                           Kind = SetupEntitySelection.EntityKind.Slice,
                           Id = slice.Id,
                           Name = SetupActions.SliceLabel(setup, slice),
                           Status = targetText,
                           LeadingIcon = Icon.Slice,
                           TrailingIcon = targetIcon,
                           Depth = 1,
                           // Nothing shows this slice, so it steps back visually.
                           Muted = targetIcon == null,
                       };
        DrawRow(selection, setup, ref args);
    }

    /// <summary>Out-gutter for a slice: the target-type icon plus a count when it feeds more than one. No
    /// label — the fade already says "unused", and where it lands is the icon; a name adds noise.</summary>
    private (Icon? icon, string? text) DescribeSliceTargetGutter(Setup setup, Slice slice)
    {
        if (setup.FindSource(slice.SourceId) == null)
            return (null, null);

        var count = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == slice.Id)
                count++;
        }

        if (count > 0)
            return (Icon.Grid, CountSuffix(count));

        // Or patches showing the slice on the direct pipe.
        var patches = 0;
        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId == slice.Id)
                    patches++;
            }
        }

        return patches > 0 ? (Icon.Projector, CountSuffix(patches)) : (null, null);
    }

    /// <summary>A patch under its output: what feeds it as the status; unfed patches step back.</summary>
    private void DrawPatchRow(SetupEntitySelection selection, Setup setup, OutputDefinition output, OutputDefinition.Patch patch)
    {
        var slice = setup.FindSlice(patch.SliceId);
        var args = new EntityItem.Args
                       {
                           Kind = SetupEntitySelection.EntityKind.Patch,
                           Id = patch.Id,
                           Name = SetupActions.PatchLabel(output, patch),
                           Status = slice == null ? null : SetupActions.SliceLabel(setup, slice),
                           LeadingIcon = Icon.Patch,
                           Depth = 1,
                           Muted = slice == null,
                       };
        DrawRow(selection, setup, ref args);
    }

    /// <summary>"×N" once there's more than one target; nothing for a single one.</summary>
    private static string? CountSuffix(int count) => count > 1 ? "×" + count : null;

    private void ToggleSourceExpanded(Guid childId)
    {
        if (!_collapsedSources.Add(childId))
            _collapsedSources.Remove(childId);
    }

    // Out-gutter for a content send: the first target's type icon + short label, "+N" for extra targets.
    private (Icon? icon, string text) DescribeSendTargetsGutter(Setup setup, IReadOnlyList<Guid> targets)
    {
        if (targets.Count == 0)
            return (null, "unbound");

        var (icon, name) = DescribeSingleTarget(setup, targets[0]);
        return (icon, targets.Count > 1 ? $"{name} +{targets.Count - 1}" : name);
    }

    private (Icon? icon, string name) DescribeSingleTarget(Setup setup, Guid targetId)
    {
        var surface = setup.FindSurface(targetId);
        if (surface != null)
            return (Icon.Grid, SetupActions.SurfaceShortLabel(surface));

        var output = setup.FindOutput(targetId);
        if (output != null)
            return (Icon.Projector, string.IsNullOrEmpty(output.Name) ? "output" : SetupActions.Abbreviate(output.Name));

        return (Icon.Grid, "?");
    }

    /// <summary>Out-gutter for a content row: the surface-target icon plus a count when more than one surface
    /// shows this source's slices. No label; the fade already reads as "unused".</summary>
    private (Icon? icon, string? text) DescribeSourceGutter(Setup setup, Guid symbolChildId)
    {
        var source = setup.FindSourceByChildId(symbolChildId);
        if (source == null)
            return (null, null);

        var count = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (SetupRelations.IsSliceOf(setup, surface.SliceId, source.Id))
                count++;
        }

        return count > 0 ? (Icon.Grid, CountSuffix(count)) : (null, null);
    }

    // Surfaces as a tree: roots first, each followed by its children (nested by ParentId). The mapped
    // output(s) are shown as the row status until the icon gutters land.
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

        var (outputIcon, outputText) = DescribeSurfaceOutputGutter(setup, surface);
        var args = new EntityItem.Args
                       {
                           Kind = SetupEntitySelection.EntityKind.Surface,
                           Id = surface.Id,
                           Name = surface.Name,
                           Status = outputText,
                           LeadingIcon = Icon.Grid,
                           TrailingIcon = outputIcon,
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

    // Out-gutter for a surface: the projector icon + a count when mapped to more than one output (edge blends).
    private (Icon? icon, string? text) DescribeSurfaceOutputGutter(Setup setup, Surface surface)
    {
        return surface.OutputMappings.Count == 0
                   ? (null, null)
                   : (Icon.Projector, CountSuffix(surface.OutputMappings.Count));
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

    /// <summary>Outliner-side row wrapper: injects the column rect and the cross-highlight context every row
    /// needs, and records hover for next frame's referenced-row highlighting.</summary>
    private EntityItem.ItemAction DrawRow(SetupEntitySelection selection, Setup setup, ref EntityItem.Args args)
    {
        args.ColumnMinX = _columnMinX;
        args.ColumnWidth = _columnWidth;
        args.PrimaryKind = _primaryKind;
        args.PrimaryId = _primaryId;
        args.HighlightInputArrow = IsHoverInputHighlighted(args.Kind, args.Id);
        // The "→|" source marker points at what feeds the primary; the hover trace brightens producers the same way.
        args.HighlightTrailing = SetupRelations.IsDirectSourceOf(setup, _primaryKind, _primaryId, args.Kind, args.Id)
                                 || IsHoverTrailingHighlighted(args.Kind, args.Id);

        var action = _entityItem.DrawRow(selection, setup, in args, out var hovered);
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
    private int _breadcrumbFrame;
    private readonly List<SetupRelations.Relation> _breadcrumbScratch = [];
    private readonly System.Text.StringBuilder _breadcrumbBuilder = new();

    private const float SwitcherWidth = 180; // unscaled px
    private const float ShelfWidth = 200; // unscaled px

    // Surfaces whose children are folded away; expanded is the default, so only collapses are tracked.
    private readonly HashSet<Guid> _collapsedSurfaces = [];
    private readonly HashSet<Guid> _collapsedSources = [];
    private readonly HashSet<Guid> _collapsedOutputs = [];
    private readonly EntityItem _entityItem;
    private SetupEntitySelection.EntityKind _primaryKind;
    private Guid _primaryId;

    // Cross-highlight: the row hovered this frame (committed at end of Draw), and the entities it references.
    private SetupEntitySelection.EntityKind _hoveredKind;
    private Guid _hoveredId;
    private SetupEntitySelection.EntityKind _pendingHoveredKind;
    private Guid _pendingHoveredId;
    // Rows related to the hovered one: consumers light their left input arrow, producers their trailing gutter.
    private readonly List<SetupRelations.Relation> _referenced = [];
}
