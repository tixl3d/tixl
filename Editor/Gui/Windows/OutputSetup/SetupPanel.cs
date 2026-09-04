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
/// The output window's setup panel: setup switcher, then one section per entity kind
/// (CONTENT / SURFACES / OUTPUTS / REFERENCE IMAGES / PROPS). Surfaces form their own tree (nested by
/// <see cref="Surface.ParentId"/>); the relationships between content, surfaces, and outputs are shown
/// per row. CONTENT lists the live <see cref="IOutputSink"/> ops, everything else the active setup.
/// </summary>
internal sealed class SetupPanel
{
    /// <summary>One panel per output window, sharing the window's <see cref="EntityItem"/> with its canvas
    /// views — so rename state and menus stay per-window instead of bleeding between open windows.</summary>
    public SetupPanel(EntityItem entityItem)
    {
        _entityItem = entityItem;
    }

    public void Draw(SetupEntitySelection selection, Action? onCollapse = null)
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
        ComputeReferenced(setup);

        DrawSetupSwitcher(setup, selection, onCollapse);
        FormInputs.AddVerticalSpace(4);

        // CONTENT — live SendToOutput ops (their targeting lives on the op, so they aren't setup entities).
        if (DrawSection("CONTENT", "##addContent", selection, SetupActions.AddContentSend))
            DrawContentSends(selection, setup);

        if (DrawSection("SURFACES", "##addSurface", selection, SetupActions.AddSurface))
            DrawSurfaces(selection, setup);

        if (DrawSection("OUTPUTS", "##addOutput", selection, SetupActions.AddOutput))
        {
            for (var i = 0; i < setup.Outputs.Count; i++)
            {
                var output = setup.Outputs[i];
                // The Default output is the editor's internal preview, not something you present or map — hide it.
                if (output.Kind == OutputDefinition.Kinds.Default)
                    continue;

                var binding = machineConfig.TryGetBinding(output.Id);
                var status = binding == null ? null : $"Display {binding.DisplayIndex + 1}";
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

        if (DrawSection("REFERENCE IMAGES", "##addRefImage", selection, SetupActions.AddReferenceImage))
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

        if (DrawSection("PROPS", "##addProp", selection, SetupActions.AddProp))
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


        _hoveredKind = _pendingHoveredKind;
        _hoveredId = _pendingHoveredId;
    }

    // Fills _referenced with the rows related to the currently-hovered one, and which gutter to light on each:
    // upstream producers (a shown source/slice, a mapped surface) get their trailing output gutter; downstream
    // consumers (a surface/output that shows the hovered feed) get their left input arrow. So a hover traces the
    // content → slice → surface → output chain in both directions without lighting whole rows.
    private void ComputeReferenced(Setup setup)
    {
        _referenced.Clear();
        if (_hoveredKind == SetupEntitySelection.EntityKind.None)
            return;

        switch (_hoveredKind)
        {
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.FindSurface(_hoveredId);
                if (surface == null)
                    break;

                AddOutputsOfSurface(setup, surface); // where it goes → consumers' input arrow
                AddSourceOfSlice(setup, surface.SliceId); // what feeds it → producers' trailing gutter
                break;
            }
            case SetupEntitySelection.EntityKind.Output:
            {
                foreach (var surface in setup.Surfaces)
                {
                    if (!surface.OutputMappings.Exists(m => m.OutputId == _hoveredId))
                        continue;

                    _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id, false));
                    AddSourceOfSlice(setup, surface.SliceId); // the feed behind each mapped surface
                }

                var output = setup.FindOutput(_hoveredId);
                if (output != null)
                {
                    foreach (var patch in output.Patches)
                        AddSourceOfSlice(setup, patch.SliceId); // the feeds on the direct pipe
                }

                break;
            }
            case SetupEntitySelection.EntityKind.Patch:
            {
                var patch = setup.FindPatch(_hoveredId, out _);
                if (patch != null)
                    AddSourceOfSlice(setup, patch.SliceId);

                break;
            }
            case SetupEntitySelection.EntityKind.ContentSource:
            {
                var source = setup.FindSourceByChildId(_hoveredId);
                if (source != null)
                    AddConsumersOfSource(setup, source.Id);
                break;
            }
            case SetupEntitySelection.EntityKind.Slice:
            {
                AddConsumersOfSlice(setup, _hoveredId);
                break;
            }
            case SetupEntitySelection.EntityKind.ReferenceImage:
            {
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.Reference != null && surface.Reference.ImageId == _hoveredId)
                        _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id, true));
                }

                break;
            }
        }
    }

    /// <summary>The outputs a surface reaches — its own mappings, or a coplanar child's nearest mapped ancestor.
    /// Marked as consumers (input arrow), since they sit downstream of the surface.</summary>
    private void AddOutputsOfSurface(Setup setup, Surface? surface)
    {
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.OutputMappings.Count > 0)
            {
                foreach (var mapping in surface.OutputMappings)
                    _referenced.Add((SetupEntitySelection.EntityKind.Output, mapping.OutputId, true));

                return;
            }

            if (surface.ParentId == Guid.Empty)
                return;

            var parentId = surface.ParentId;
            surface = setup.FindSurface(parentId);
        }
    }

    /// <summary>The slice and its content source feeding a surface/output — producers (trailing gutter).</summary>
    private void AddSourceOfSlice(Setup setup, Guid sliceId)
    {
        if (sliceId == Guid.Empty)
            return;

        _referenced.Add((SetupEntitySelection.EntityKind.Slice, sliceId, false));
        var slice = setup.FindSlice(sliceId);
        var source = slice == null ? null : setup.FindSource(slice.SourceId);
        if (source != null)
            _referenced.Add((SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId, false));
    }

    /// <summary>Surfaces and outputs showing any slice of this source — consumers, lit on their input arrow.</summary>
    private void AddConsumersOfSource(Setup setup, Guid sourceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (SetupActions.IsSliceOf(setup, surface.SliceId, sourceId))
                _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id, true));
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (SetupActions.IsSliceOf(setup, patch.SliceId, sourceId))
                    _referenced.Add((SetupEntitySelection.EntityKind.Patch, patch.Id, true));
            }
        }
    }

    /// <summary>Surfaces and outputs showing this exact slice — consumers, lit on their input arrow.</summary>
    private void AddConsumersOfSlice(Setup setup, Guid sliceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id, true));
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId == sliceId)
                    _referenced.Add((SetupEntitySelection.EntityKind.Patch, patch.Id, true));
            }
        }
    }

    private bool IsHoverInputHighlighted(SetupEntitySelection.EntityKind kind, Guid id)
    {
        for (var i = 0; i < _referenced.Count; i++)
        {
            if (_referenced[i].onInput && _referenced[i].kind == kind && _referenced[i].id == id)
                return true;
        }

        return false;
    }

    private bool IsHoverTrailingHighlighted(SetupEntitySelection.EntityKind kind, Guid id)
    {
        for (var i = 0; i < _referenced.Count; i++)
        {
            if (!_referenced[i].onInput && _referenced[i].kind == kind && _referenced[i].id == id)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="kind"/>/<paramref name="id"/> is the immediate <em>source</em> feeding the
    /// primary-selected entity — the slice a selected surface (or output) shows, or the content source a
    /// selected slice belongs to. Used to point the "→|" source marker at that row.
    /// </summary>
    private bool IsSourceOfPrimary(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (id == Guid.Empty)
            return false;

        switch (_primaryKind)
        {
            case SetupEntitySelection.EntityKind.Surface:
                var surface = setup.FindSurface(_primaryId);
                return surface != null && kind == SetupEntitySelection.EntityKind.Slice && surface.SliceId == id;

            case SetupEntitySelection.EntityKind.Output:
                var output = setup.FindOutput(_primaryId);
                return output != null && kind == SetupEntitySelection.EntityKind.Slice && SetupActions.OutputShowsSlice(output, id);

            case SetupEntitySelection.EntityKind.Patch:
                var patch = setup.FindPatch(_primaryId, out _);
                return patch != null && kind == SetupEntitySelection.EntityKind.Slice && patch.SliceId == id;

            case SetupEntitySelection.EntityKind.Slice:
                var slice = setup.FindSlice(_primaryId);
                var source = slice == null ? null : setup.FindSource(slice.SourceId);
                return source != null && kind == SetupEntitySelection.EntityKind.ContentSource && source.SymbolChildId == id;

            default:
                return false;
        }
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
            var sliceCount = source == null ? 0 : SetupActions.CountSlicesOfSource(setup, source.Id);
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
            if (SetupActions.IsSliceOf(setup, surface.SliceId, source.Id))
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
        var hasChildren = SetupActions.CountChildren(setup, surfaceId) > 0;
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

    private void DrawSetupSwitcher(Setup setup, SetupEntitySelection selection, Action? onCollapse)
    {
        var scale = T3Ui.UiScaleFactor;
        var pos = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        // Leave room on the right for the collapse button so its clicks don't fall through to the switcher.
        var collapseWidth = onCollapse != null ? height + 2 * scale : 0;
        var switcherWidth = ImGui.GetContentRegionAvail().X - collapseWidth;
        if (ImGui.InvisibleButton("##setupSwitcher", new Vector2(switcherWidth, height)))
            ImGui.OpenPopup("##setupMenu");

        // Label + chevron drawn over the button so the chevron sits next to the name (not far-right like a combo).
        ImGui.SetCursorScreenPos(new Vector2(pos.X + 2 * scale, pos.Y));
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(setup.Name, Fonts.FontNormal, UiColors.Text);
        ImGui.SameLine(0, 4 * scale);
        Icons.DrawInlineGlyph(Icon.ChevronDown, UiColors.TextMuted.Rgba);

        if (onCollapse != null)
        {
            ImGui.SetCursorScreenPos(new Vector2(pos.X + switcherWidth + 2 * scale, pos.Y));
            if (CustomComponents.IconButton(Icon.SidePanelLeft, Vector2.Zero))
                onCollapse();

            CustomComponents.TooltipForLastItem("Hide the setup panel");
        }

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

    // An icon drawn as a font glyph on the current text line — aligns with AlignTextToFramePadding'd text,
    // unlike DrawAtCursor which adds its own vertical offset.
    // A collapsible section header: chevron toggle + label. Returns whether the section is expanded.
    /// <summary>The black divider line + rounded-NW corner notch that tops each section — a shared edge so the
    /// properties card can reuse the exact look.</summary>
    private void DrawPanelDivider()
    {
        var scale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();
        var edgeY = (float)Math.Round(ImGui.GetCursorScreenPos().Y);
        var winMinX = ImGui.GetWindowPos().X;
        dl.AddLine(new Vector2(winMinX, edgeY), new Vector2(winMinX + ImGui.GetWindowWidth(), edgeY), UiColors.BackgroundFull, 1 * scale);
        Icons.DrawIconAtScreenPosition(Icon.RoundingNW, new Vector2(winMinX, edgeY), dl, UiColors.BackgroundFull.Fade(0.5f));
    }

    private bool DrawSectionLabel(string title)
    {
        FormInputs.AddVerticalSpace(6);
        DrawPanelDivider();

        var expanded = _expandedSections.GetValueOrDefault(title, true);

        ImGui.PushID(title);
        if (CustomComponents.IconButton(expanded ? Icon.ChevronDown : Icon.ChevronRight, new Vector2(ImGui.GetFrameHeight())))
        {
            expanded = !expanded;
            _expandedSections[title] = expanded;
        }

        ImGui.SameLine(0, 2 * T3Ui.UiScaleFactor);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(title, Fonts.FontSmall, UiColors.TextMuted);
        ImGui.PopID();
        return expanded;
    }

    private bool DrawSection(string title, string addButtonId, SetupEntitySelection selection, Action<SetupEntitySelection> onAdd)
    {
        var expanded = DrawSectionLabel(title);
        CustomComponents.RightAlign(ImGui.GetFrameHeight());
        ImGui.PushID(addButtonId);
        if (CustomComponents.IconButton(Icon.Plus, Vector2.Zero))
        {
            onAdd(selection);
            OutputSetupHandling.SaveActive();
        }

        ImGui.PopID();
        return expanded;
    }

    /// <summary>Panel-side row wrapper: injects the cross-highlight context every row needs and records
    /// hover for next frame's referenced-row highlighting.</summary>
    private EntityItem.ItemAction DrawRow(SetupEntitySelection selection, Setup setup, ref EntityItem.Args args)
    {
        args.PrimaryKind = _primaryKind;
        args.PrimaryId = _primaryId;
        args.HighlightInputArrow = IsHoverInputHighlighted(args.Kind, args.Id);
        args.HighlightTrailing = IsSourceOfPrimary(setup, args.Kind, args.Id) || IsHoverTrailingHighlighted(args.Kind, args.Id);

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
    private readonly Dictionary<string, bool> _expandedSections = [];
    private static EvaluationContext? _sendContext;

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
    // Rows related to the hovered one; onInput = light the left input arrow (consumer) vs the trailing gutter (producer).
    private readonly List<(SetupEntitySelection.EntityKind kind, Guid id, bool onInput)> _referenced = [];
}
