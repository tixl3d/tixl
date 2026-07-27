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

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// The output window's setup sidebar: setup switcher, then one section per entity kind
/// (CONTENT / SURFACES / OUTPUTS / REFERENCE IMAGES / PROPS). Surfaces form their own tree (nested by
/// <see cref="Surface.ParentId"/>); the relationships between content, surfaces, and outputs are shown
/// per row. CONTENT lists the live <see cref="IOutputSink"/> ops, everything else the active setup.
/// </summary>
internal static class SetupPanel
{
    /// <summary>Installs the Guid-list parameter hooks so SendToOutput.TargetIds shows target names and a
    /// surface/output picker in the op parameter window. Called from UI registration at startup, so it works
    /// even before the setup sidebar has been drawn (which is what a lazy static ctor would have waited for).</summary>
    internal static void RegisterGuidListHooks()
    {
        GuidListLabels.Resolver = ResolveTargetLabel;
        GuidListLabels.Picker = PickTarget;
    }

    public static void Draw(SetupEntitySelection selection, Action? onCollapse = null)
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

        // CONTENT — live SendToOutput sinks (their targeting lives on the op, so they aren't setup entities).
        if (DrawSection("CONTENT", "##addContent", selection, SetupActions.AddContentSink))
            DrawContentSinks(selection, setup);

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
                var bindable = output.Kind is OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display;
                var args = new EntityItem.Args
                               {
                                   Kind = SetupEntitySelection.EntityKind.Output,
                                   Id = output.Id,
                                   Name = output.Name,
                                   Status = status,
                                   LeadingIcon = Icon.Projector,
                                   // A paused output (Send off) reads the same as a non-rendering surface.
                                   Muted = !output.Send,
                                   StrikeLeadingIcon = !output.Send,
                                   CanRename = true,
                                   ShowBindingSubMenu = bindable,
                               };
                DrawRow(selection, setup, ref args);
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
                                   CanRename = true,
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

        DrawPropertiesFooter(selection, setup, machineConfig);

        _hoveredKind = _pendingHoveredKind;
        _hoveredId = _pendingHoveredId;
    }

    // Properties card for the selected entity, at the bottom of the panel.
    private static void DrawPropertiesFooter(SetupEntitySelection selection, Setup setup, MachineConfig machineConfig)
    {
        if (!selection.TryResolve(setup, out var kind, out var id))
            return;

        FormInputs.AddVerticalSpace(12);
        // Divider above the card, matching the section top edge, plus a small gap before the first item.
        DrawPanelDivider();
        FormInputs.AddVerticalSpace(3);
        ImGui.Indent(6 * T3Ui.UiScaleFactor); // 6px margin to the sidebar edges (right reserved inside the inputs).
        // Match FormInputs' field background (the default FrameBg is near-black in the panel).
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
                DrawSurfaceCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.Output:
                DrawOutputCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.ContentSource:
                DrawContentCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.Slice:
                DrawSliceCard(setup, id);
                break;
        }

        ImGui.PopStyleColor();
        ImGui.Unindent(6 * T3Ui.UiScaleFactor);
    }

    private static void DrawSurfaceCard(Setup setup, Guid id)
    {
        var surface = setup.FindSurface(id);
        if (surface == null)
            return;

        FormInputsNarrow.DrawCardHeader("Surface");

        var render = surface.Render;
        if (FormInputsNarrow.DrawCheckbox("Render", ref render, "Skip drawing this surface without removing it."))
        {
            surface.Render = render;
            OutputSetupHandling.SaveActive();
        }

        // Name is renamed inline in the tree (double-click the row) rather than via a field here.
        var pivot = surface.Placement?.Pivot ?? Vector2.Zero;
        var position = surface.Placement?.Pose.Position ?? Vector3.Zero;
        Span<float> pos = [position.X, position.Y, position.Z];
        var posState = FormInputsNarrow.DrawFloats("Position (m)", pos);
        if ((posState & InputEditStateFlags.Modified) != 0)
        {
            var placement = surface.Placement ??= new Surface.StagePlacement();
            placement.Pose = new Pose(new Vector3(pos[0], pos[1], pos[2]), placement.Pose.Orientation);
        }

        // A Layout child inherits its parent's plane, so it's placed in the parent's local space instead of the stage.
        if (surface.Kind == Surface.SurfaceKinds.Layout)
        {
            Span<float> local = [surface.LocalPosition.X, surface.LocalPosition.Y];
            var localState = FormInputsNarrow.DrawFloats("Position in parent (m)", local,
                                                        "Bottom-left corner, in metres from the parent's anchor (X right, Y up).");
            if ((localState & InputEditStateFlags.Modified) != 0)
                surface.LocalPosition = new Vector2(local[0], local[1]);

            if ((localState & InputEditStateFlags.Finished) != 0)
                OutputSetupHandling.SaveActive();
        }

        Span<float> size = [surface.SizeInMeters.X, surface.SizeInMeters.Y];
        var sizeState = FormInputsNarrow.DrawFloats("Size (m)", size,
                                                    "Resizes the surface's footprint — the corner pin follows, so it covers a different area of the wall.",
                                                    reserveRight: 44);

        // Locking keeps the current width/height ratio while resizing — the edited axis drives, the other follows.
        ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
        if (CustomComponents.IconButton(Icon.Link, Vector2.Zero,
                                        surface.LockAspect ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
        {
            surface.LockAspect = !surface.LockAspect;
            OutputSetupHandling.SaveActive();
        }

        CustomComponents.TooltipForLastItem("Lock aspect ratio", "Resizing keeps the current width-to-height ratio.");

        // Measuring is a different act from resizing: it states how big the rect you already aligned really
        // is, and must leave the projection alone. Explicit icon + Apply, rather than overloading the field.
        ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
        if (CustomComponents.IconButton(Icon.Scale, Vector2.Zero))
        {
            _measuredEdit = surface.SizeInMeters;
            ImGui.OpenPopup(MeasuredSizePopupId);
        }

        CustomComponents.TooltipForLastItem("Set measured dimensions",
                                            "Declares how big this surface really is, without moving the projection.");
        DrawMeasuredSizePopup(surface);

        if ((sizeState & InputEditStateFlags.Started) != 0)
            _resizeOldState = new ResizeSurfaceCommand.State(surface);

        if ((sizeState & InputEditStateFlags.Modified) != 0)
            SurfaceGeometry.ResizeAnchored(surface, ConstrainSize(surface.SizeInMeters, new Vector2(size[0], size[1]), surface.LockAspect));

        // Resizing re-projects the corner pins, so it has to be undoable as one step.
        if ((sizeState & InputEditStateFlags.Finished) != 0 && _resizeOldState != null)
        {
            UndoRedoStack.Add(new ResizeSurfaceCommand(surface.Id, _resizeOldState.Value, new ResizeSurfaceCommand.State(surface)));
            _resizeOldState = null;
        }

        var showGrid = surface.ShowGrid;
        if (FormInputsNarrow.DrawCheckbox("Show size raster", ref showGrid,
                                          "Projects a real-world grid (no content needed) so you can hand-align the corner-pin to physical wall features."))
        {
            surface.ShowGrid = showGrid;
            OutputSetupHandling.SaveActive();
        }

        var gridCellState = InputEditStateFlags.Nothing;
        if (surface.ShowGrid)
        {
            Span<int> subdivisions = [surface.GridSubdivisions];
            gridCellState = FormInputsNarrow.DrawInts("Subdivisions / m", subdivisions,
                                                      "Minor lines per metre; 1 draws metre lines only. They fade out once too dense to resolve.");
            if ((gridCellState & InputEditStateFlags.Modified) != 0)
                surface.GridSubdivisions = Math.Clamp(subdivisions[0], 1, 100);
        }

        Span<float> anchor = [pivot.X, pivot.Y];
        var anchorState = FormInputsNarrow.DrawFloats("Anchor (0..1)", anchor);
        if ((anchorState & InputEditStateFlags.Modified) != 0)
            (surface.Placement ??= new Surface.StagePlacement()).Pivot = new Vector2(anchor[0], anchor[1]);

        // Value applied live above; persist once when the drag/edit completes.
        if (((posState | sizeState | anchorState | gridCellState) & InputEditStateFlags.Finished) != 0)
            OutputSetupHandling.SaveActive();
    }

    private static void DrawOutputCard(Setup setup, Guid id)
    {
        var output = setup.FindOutput(id);
        if (output == null)
            return;

        FormInputsNarrow.DrawCardHeader("Output");

        var send = output.Send;
        if (FormInputsNarrow.DrawCheckbox("Send", ref send, "Pause presenting without dropping the display binding."))
        {
            output.Send = send;
            OutputSetupHandling.SaveActive();
        }

        // Name is renamed inline in the tree (double-click the row).
        // The display binding and the list of feeders are both visible in the tree (row gutter / cross-
        // highlight), so they don't earn card space here. Render order will move to an "Adjust render order"
        // context-menu action + popup rather than a passive list.
    }

    private static void DrawContentCard(Setup setup, Guid childId)
    {
        var instance = SetupActions.FindSinkInstance(childId);
        if (instance is not IOutputSink sink)
            return;

        FormInputsNarrow.DrawCardHeader("Content");

        _sinkContext ??= new EvaluationContext();
        _sinkContext.Reset();

        // Reset() leaves RequestedResolution at 0×0; pulling the content preview at that size makes the
        // graph's auto-sized RenderTargets bail ("invalid texture size") and stop updating. Preview at
        // the resolution the content would render at when bound.
        _sinkContext.RequestedResolution = ContentPreviewResolution(setup);

        var update = sink.GetUpdateEnabled(_sinkContext);
        if (FormInputsNarrow.DrawCheckbox("Update", ref update, "When off, freezes this content at its last frame."))
            sink.SetUpdateEnabled(update);

        Span<int> resolution = [1, 1];
        var content = sink.GetContent(_sinkContext);
        if (content is { IsDisposed: false })
        {
            resolution[0] = content.Description.Width;
            resolution[1] = content.Description.Height;
        }

        FormInputsNarrow.DrawInts("Resolution (px)", resolution, "Comes from the source texture (read-only).", readOnly: true);
    }

    private static void DrawSliceCard(Setup setup, Guid id)
    {
        var slice = setup.FindSlice(id);
        if (slice == null)
            return;

        FormInputsNarrow.DrawCardHeader("Slice");

        // Pixels come from the source texture; without a live source there's nothing to measure against, so
        // fall back to the normalized rect.
        var source = setup.FindSource(slice.SourceId);
        var texW = 0;
        var texH = 0;
        if (source != null && OutputManager.TryGetSourceContent(source.SymbolChildId, out _, out var content)
            && content is { IsDisposed: false })
        {
            texW = content.Description.Width;
            texH = content.Description.Height;
        }

        var uv = slice.UvRect;
        if (texW <= 0 || texH <= 0)
        {
            FormInputsNarrow.DrawLabel("Position", "Connect the source op to edit in pixels.");
            Span<float> posUv = [uv.X, uv.Y];
            FormInputsNarrow.DrawFloats("Position (uv)", posUv, readOnly: true);
            Span<float> sizeUv = [uv.Z - uv.X, uv.W - uv.Y];
            FormInputsNarrow.DrawFloats("Size (uv)", sizeUv, readOnly: true);
            return;
        }

        var widthUv = MathF.Max(uv.Z - uv.X, MinSliceSize);
        var heightUv = MathF.Max(uv.W - uv.Y, MinSliceSize);

        Span<int> position = [(int)MathF.Round(uv.X * texW), (int)MathF.Round(uv.Y * texH)];
        if ((FormInputsNarrow.DrawInts("Position (px)", position) & InputEditStateFlags.Modified) != 0)
        {
            var nx = Math.Clamp(position[0] / (float)texW, 0f, 1f - widthUv);
            var ny = Math.Clamp(position[1] / (float)texH, 0f, 1f - heightUv);
            slice.UvRect = new Vector4(nx, ny, nx + widthUv, ny + heightUv);
            OutputSetupHandling.SaveActive();
        }

        Span<int> size = [(int)MathF.Round(widthUv * texW), (int)MathF.Round(heightUv * texH)];
        if ((FormInputsNarrow.DrawInts("Size (px)", size) & InputEditStateFlags.Modified) != 0)
        {
            var nw = Math.Clamp(size[0] / (float)texW, MinSliceSize, 1f - uv.X);
            var nh = Math.Clamp(size[1] / (float)texH, MinSliceSize, 1f - uv.Y);
            slice.UvRect = new Vector4(uv.X, uv.Y, uv.X + nw, uv.Y + nh);
            OutputSetupHandling.SaveActive();
        }
    }

    /// <summary>Smallest slice fraction — mirrors <c>SetupOutputView.MinSliceSize</c>.</summary>
    private const float MinSliceSize = 0.01f;

    // Fills _referenced with the rows related to the currently-hovered one, and which gutter to light on each:
    // upstream producers (a shown source/slice, a mapped surface) get their trailing output gutter; downstream
    // consumers (a surface/output that shows the hovered feed) get their left input arrow. So a hover traces the
    // content → slice → surface → output chain in both directions without lighting whole rows.
    private static void ComputeReferenced(Setup setup)
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
                    AddSourceOfSlice(setup, output.SliceId); // or a full-frame slice shown directly
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
    private static void AddOutputsOfSurface(Setup setup, Surface? surface)
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
    private static void AddSourceOfSlice(Setup setup, Guid sliceId)
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
    private static void AddConsumersOfSource(Setup setup, Guid sourceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (SetupActions.IsSliceOf(setup, surface.SliceId, sourceId))
                _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id, true));
        }

        foreach (var output in setup.Outputs)
        {
            if (SetupActions.IsSliceOf(setup, output.SliceId, sourceId))
                _referenced.Add((SetupEntitySelection.EntityKind.Output, output.Id, true));
        }
    }

    /// <summary>Surfaces and outputs showing this exact slice — consumers, lit on their input arrow.</summary>
    private static void AddConsumersOfSlice(Setup setup, Guid sliceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id, true));
        }

        foreach (var output in setup.Outputs)
        {
            if (output.SliceId == sliceId)
                _referenced.Add((SetupEntitySelection.EntityKind.Output, output.Id, true));
        }
    }

    private static bool IsHoverInputHighlighted(SetupEntitySelection.EntityKind kind, Guid id)
    {
        for (var i = 0; i < _referenced.Count; i++)
        {
            if (_referenced[i].onInput && _referenced[i].kind == kind && _referenced[i].id == id)
                return true;
        }

        return false;
    }

    private static bool IsHoverTrailingHighlighted(SetupEntitySelection.EntityKind kind, Guid id)
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
    private static bool IsSourceOfPrimary(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
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
                return output != null && kind == SetupEntitySelection.EntityKind.Slice && output.SliceId == id;

            case SetupEntitySelection.EntityKind.Slice:
                var slice = setup.FindSlice(_primaryId);
                var source = slice == null ? null : setup.FindSource(slice.SourceId);
                return source != null && kind == SetupEntitySelection.EntityKind.ContentSource && source.SymbolChildId == id;

            default:
                return false;
        }
    }

    private static void DrawContentSinks(SetupEntitySelection selection, Setup setup)
    {
        var sinks = OutputSinkRegistry.Sinks;
        if (sinks.Count == 0)
        {
            ImGui.Indent(8 * T3Ui.UiScaleFactor);
            CustomComponents.StylizedText("no SendToOutput ops", Fonts.FontSmall, UiColors.TextMuted.Fade(0.6f));
            ImGui.Unindent(8 * T3Ui.UiScaleFactor);
            return;
        }

        _sinkContext ??= new EvaluationContext();
        _sinkContext.Reset();

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
                               Name = SetupActions.SinkName(instance),
                               Status = text,
                               LeadingIcon = Icon.FileImage,
                               TrailingIcon = icon,
                               IsExpanded = sliceCount > 0 ? expanded : null,
                               ReserveExpander = true,
                               // No surface shows this source, so it steps back visually.
                               Muted = icon == null,
                               // A source *is* its op, so renaming it renames the op (and cascades back through the sync).
                               CanRename = true,
                               HasAdoptedSource = source != null,
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
    private static void DrawSliceRow(SetupEntitySelection selection, Setup setup, Slice slice)
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
                           CanRename = true,
                       };
        DrawRow(selection, setup, ref args);
    }

    /// <summary>Out-gutter for a slice: the target-type icon plus a count when it feeds more than one. No
    /// label — the fade already says "unused", and where it lands is the icon; a name adds noise.</summary>
    private static (Icon? icon, string? text) DescribeSliceTargetGutter(Setup setup, Slice slice)
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

        // Or an output showing the slice full-frame (the direct path).
        return setup.Outputs.Exists(o => o.SliceId == slice.Id) ? (Icon.Projector, null) : (null, null);
    }

    /// <summary>"×N" once there's more than one target; nothing for a single one.</summary>
    private static string? CountSuffix(int count) => count > 1 ? "×" + count : null;

    private static void ToggleSourceExpanded(Guid childId)
    {
        if (!_collapsedSources.Add(childId))
            _collapsedSources.Remove(childId);
    }

    // Out-gutter for a content send: the first target's type icon + short label, "+N" for extra targets.
    private static (Icon? icon, string text) DescribeSinkTargetsGutter(Setup setup, IReadOnlyList<Guid> targets)
    {
        if (targets.Count == 0)
            return (null, "unbound");

        var (icon, name) = DescribeSingleTarget(setup, targets[0]);
        return (icon, targets.Count > 1 ? $"{name} +{targets.Count - 1}" : name);
    }

    private static (Icon? icon, string name) DescribeSingleTarget(Setup setup, Guid targetId)
    {
        var surface = setup.FindSurface(targetId);
        if (surface != null)
            return (Icon.Grid, SetupActions.SurfaceShortLabel(surface));

        var output = setup.FindOutput(targetId);
        if (output != null)
            return (Icon.Projector, string.IsNullOrEmpty(output.Name) ? "output" : SetupActions.Abbreviate(output.Name));

        return (Icon.Grid, "?");
    }

    /// <summary>
    /// Applies a typed size, optionally preserving the previous ratio: the axis that changed more drives, the
    /// other follows. Keeps the driven axis exact so the number the user typed is what lands.
    /// </summary>
    private static Vector2 ConstrainSize(Vector2 old, Vector2 typed, bool lockAspect)
    {
        if (!lockAspect || old.X <= 0 || old.Y <= 0)
            return typed;

        var dx = MathF.Abs(typed.X - old.X);
        var dy = MathF.Abs(typed.Y - old.Y);
        if (dx >= dy)
            return new Vector2(typed.X, typed.X * (old.Y / old.X));

        return new Vector2(typed.Y * (old.X / old.Y), typed.Y);
    }

    /// <summary>Out-gutter for a content row: the surface-target icon plus a count when more than one surface
    /// shows this source's slices. No label; the fade already reads as "unused".</summary>
    private static (Icon? icon, string? text) DescribeSourceGutter(Setup setup, Guid symbolChildId)
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
    private static void DrawSurfaces(SetupEntitySelection selection, Setup setup)
    {
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == Guid.Empty)
                DrawSurfaceRow(selection, setup, setup.Surfaces[i], 0);
        }
    }

    private static void DrawSurfaceRow(SetupEntitySelection selection, Setup setup, Surface surface, int depth)
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
                           CanRename = true,
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

    private static void ToggleSurfaceExpanded(Guid surfaceId)
    {
        if (!_collapsedSurfaces.Add(surfaceId))
            _collapsedSurfaces.Remove(surfaceId);
    }

    // Out-gutter for a surface: the projector icon + a count when mapped to more than one output (edge blends).
    private static (Icon? icon, string? text) DescribeSurfaceOutputGutter(Setup setup, Surface surface)
    {
        return surface.OutputMappings.Count == 0
                   ? (null, null)
                   : (Icon.Projector, CountSuffix(surface.OutputMappings.Count));
    }

    /// <summary>Info card shown in the output view for a selected/pinned setup entity.</summary>
    public static void DrawEntityCard(SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        ImGui.SetCursorPos(ImGui.GetWindowSize() * 0.5f - new Vector2(120, 60) * T3Ui.UiScaleFactor);
        ImGui.BeginGroup();
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.ReferenceImage:
            {
                var image = setup.FindReferenceImage(id);
                if (image != null)
                {
                    CustomComponents.StylizedText($"Reference Image · {image.Kind}", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(image.Name, Fonts.FontLarge, UiColors.Text);
                    var fileInfo = string.IsNullOrEmpty(image.FilePath)
                                       ? "Drop a photo here, or pick an asset"
                                       : $"{image.FilePath}  ({image.Width}×{image.Height})";
                    CustomComponents.StylizedText(fileInfo, Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.FindSurface(id);
                if (surface != null)
                {
                    CustomComponents.StylizedText($"Surface · {surface.Kind}", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(surface.Name, Fonts.FontLarge, UiColors.Text);
                    CustomComponents.StylizedText($"{surface.SizeInMeters.X:0.##} × {surface.SizeInMeters.Y:0.##} m · {surface.PixelsPerMeter:0} px/m",
                                                  Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
            case SetupEntitySelection.EntityKind.Prop:
            {
                var prop = setup.FindProp(id);
                if (prop != null)
                {
                    CustomComponents.StylizedText("Prop", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(prop.Kind, Fonts.FontLarge, UiColors.Text);
                    CustomComponents.StylizedText($"{prop.HeightInMeters:0.##} m", Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
            case SetupEntitySelection.EntityKind.Output:
            {
                var output = setup.FindOutput(id);
                if (output != null)
                {
                    CustomComponents.StylizedText($"Output · {output.Kind}", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(output.Name, Fonts.FontLarge, UiColors.Text);
                    var binding = machineConfig.TryGetBinding(output.Id);
                    var bindingInfo = binding == null ? "unbound" : $"→ Display {binding.DisplayIndex + 1}";
                    CustomComponents.StylizedText($"{output.CanvasResolution.Width}×{output.CanvasResolution.Height} px · {bindingInfo}",
                                                  Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
            case SetupEntitySelection.EntityKind.ContentSource:
            {
                var instance = SetupActions.FindSinkInstance(id);
                if (instance is IOutputSink sink)
                {
                    _sinkContext ??= new EvaluationContext();
                    _sinkContext.Reset();
                    CustomComponents.StylizedText("Content · SendToOutput", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(SetupActions.SinkName(instance), Fonts.FontLarge, UiColors.Text);
                    var (_, targetText) = DescribeSourceGutter(setup, instance.SymbolChildId);
                    CustomComponents.StylizedText(targetText ?? "unused", Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
        }

        ImGui.EndGroup();
    }

    private static void DrawSetupSwitcher(Setup setup, SetupEntitySelection selection, Action? onCollapse)
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

    /// <summary>Full-width dropdown for a SendToOutput target-id list item: lists the active setup's surfaces
    /// then outputs; picking one returns the new id. The row's ImGui ID stack keeps each item's popup distinct.</summary>
    private static bool PickTarget(Guid current, float width, out Guid picked)
    {
        picked = current;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
        {
            ImGui.BeginDisabled();
            ImGui.Button("(no setup)", new Vector2(width, 0));
            ImGui.EndDisabled();
            return false;
        }

        // Stable "###" id so the button keeps its identity as its label changes.
        if (ImGui.Button(ResolveTargetLabel(current) + "###pickTarget", new Vector2(width, 0)))
            ImGui.OpenPopup("##pickTargetPopup");

        var changed = false;
        if (ImGui.BeginPopup("##pickTargetPopup"))
        {
            for (var i = 0; i < setup.Surfaces.Count; i++)
            {
                var surface = setup.Surfaces[i];
                if (ImGui.Selectable($"{surface.Name}##s{i}", surface.Id == current))
                {
                    picked = surface.Id;
                    changed = true;
                }
            }

            if (setup.Surfaces.Count > 0 && setup.Outputs.Count > 0)
                ImGui.Separator();

            for (var i = 0; i < setup.Outputs.Count; i++)
            {
                var output = setup.Outputs[i];
                if (ImGui.Selectable($"{output.Name}##o{i}", output.Id == current))
                {
                    picked = output.Id;
                    changed = true;
                }
            }

            ImGui.EndPopup();
        }

        return changed;
    }

    /// <summary>Names a SendToOutput target id for the parameter-window Guid list: a surface's short label
    /// or an output's name; "(missing)" when it resolves to nothing in the active setup (e.g. a target whose
    /// surface was deleted).</summary>
    private static string ResolveTargetLabel(Guid id)
    {
        if (id == Guid.Empty)
            return "(none)";

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return id.ToString("D")[..8];

        var surface = setup.FindSurface(id);
        if (surface != null)
            return SetupActions.SurfaceShortLabel(surface);

        var output = setup.FindOutput(id);
        if (output != null)
            return output.Name;

        return "(missing)";
    }

    /// <summary>
    /// "Set measured dimensions": states the surface's real size without touching its corner pins. Used after
    /// the rect is already aligned on the wall — you're correcting the measurement, not moving the projection.
    /// The declared size drives the calibration raster's density and the straighten hypothesis.
    /// </summary>
    private static void DrawMeasuredSizePopup(Surface surface)
    {
        ImGui.SetNextWindowSize(new Vector2(230 * T3Ui.UiScaleFactor, 0));
        if (!ImGui.BeginPopup(MeasuredSizePopupId))
            return;

        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted("Set measured dimensions");
        ImGui.PopFont();

        CustomComponents.StylizedText("The projection stays put — this only records\nhow big the surface really is.",
                                      Fonts.FontSmall, UiColors.TextMuted);

        Span<float> measured = [_measuredEdit.X, _measuredEdit.Y];
        FormInputsNarrow.DrawFloats("Width × Height (m)", measured);
        _measuredEdit = new Vector2(measured[0], measured[1]);

        FormInputs.AddVerticalSpace(4);
        if (ImGui.Button("Apply"))
        {
            ApplyMeasuredSize(surface, _measuredEdit);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static void ApplyMeasuredSize(Surface surface, Vector2 measured)
    {
        var oldState = new ResizeSurfaceCommand.State(surface);

        // Deliberately not SurfaceGeometry.ResizeAnchored: the quads must not move.
        surface.SizeInMeters = new Vector2(MathF.Max(measured.X, SurfaceGeometry.MinSize),
                                           MathF.Max(measured.Y, SurfaceGeometry.MinSize));

        UndoRedoStack.Add(new ResizeSurfaceCommand(surface.Id, oldState, new ResizeSurfaceCommand.State(surface)));
        OutputSetupHandling.SaveActive();
    }

    // A valid render resolution for previewing a content graph: the first output's canvas size, else a
    // 1080p fallback. Never 0×0 (which auto-sized RenderTargets treat as invalid and skip).
    private static T3.Core.DataTypes.Vector.Int2 ContentPreviewResolution(Setup setup)
    {
        for (var i = 0; i < setup.Outputs.Count; i++)
        {
            var r = setup.Outputs[i].CanvasResolution;
            if (r.Width > 0 && r.Height > 0)
                return r;
        }

        return new T3.Core.DataTypes.Vector.Int2(1920, 1080);
    }

    // An icon drawn as a font glyph on the current text line — aligns with AlignTextToFramePadding'd text,
    // unlike DrawAtCursor which adds its own vertical offset.
    // A collapsible section header: chevron toggle + label. Returns whether the section is expanded.
    /// <summary>The black divider line + rounded-NW corner notch that tops each section — a shared edge so the
    /// properties card can reuse the exact look.</summary>
    private static void DrawPanelDivider()
    {
        var scale = T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();
        var edgeY = (float)Math.Round(ImGui.GetCursorScreenPos().Y);
        var winMinX = ImGui.GetWindowPos().X;
        dl.AddLine(new Vector2(winMinX, edgeY), new Vector2(winMinX + ImGui.GetWindowWidth(), edgeY), UiColors.BackgroundFull, 1 * scale);
        Icons.DrawIconAtScreenPosition(Icon.RoundingNW, new Vector2(winMinX, edgeY), dl, UiColors.BackgroundFull.Fade(0.5f));
    }

    private static bool DrawSectionLabel(string title)
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

    private static bool DrawSection(string title, string addButtonId, SetupEntitySelection selection, Action<SetupEntitySelection> onAdd)
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
    private static EntityItem.ItemAction DrawRow(SetupEntitySelection selection, Setup setup, ref EntityItem.Args args)
    {
        args.PrimaryKind = _primaryKind;
        args.PrimaryId = _primaryId;
        args.HighlightInputArrow = IsHoverInputHighlighted(args.Kind, args.Id);
        args.HighlightTrailing = IsSourceOfPrimary(setup, args.Kind, args.Id) || IsHoverTrailingHighlighted(args.Kind, args.Id);

        var action = EntityItem.DrawRow(selection, setup, in args, out var hovered);
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
    private static readonly Dictionary<string, bool> _expandedSections = [];
    private static EvaluationContext? _sinkContext;

    // Pre-edit rectangle snapshot while a Size (m) field is being dragged, so the resize undoes as one step.
    private static ResizeSurfaceCommand.State? _resizeOldState;

    // Surfaces whose children are folded away; expanded is the default, so only collapses are tracked.
    private static readonly HashSet<Guid> _collapsedSurfaces = [];
    private static readonly HashSet<Guid> _collapsedSources = [];
    private static SetupEntitySelection.EntityKind _primaryKind;
    private static Guid _primaryId;

    private const string MeasuredSizePopupId = "##measuredSize";
    private static Vector2 _measuredEdit;

    // Cross-highlight: the row hovered this frame (committed at end of Draw), and the entities it references.
    private static SetupEntitySelection.EntityKind _hoveredKind;
    private static Guid _hoveredId;
    private static SetupEntitySelection.EntityKind _pendingHoveredKind;
    private static Guid _pendingHoveredId;
    // Rows related to the hovered one; onInput = light the left input arrow (consumer) vs the trailing gutter (producer).
    private static readonly List<(SetupEntitySelection.EntityKind kind, Guid id, bool onInput)> _referenced = [];
}
