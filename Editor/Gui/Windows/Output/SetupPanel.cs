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
                var outputId = output.Id;
                var status = binding == null ? null : $"Display {binding.DisplayIndex + 1}";
                var bindable = output.Kind is OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display;
                DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Output, output.Id, output.Name, status,
                              onDelete: () => SetupActions.DeleteOutput(setup, machineConfig, outputId), leadingIcon: Icon.Projector,
                              drawExtraMenuItems: bindable ? () => DrawOutputBindingSubMenu(output, machineConfig) : null,
                              // A paused output (Send off) reads the same as a non-rendering surface.
                              muted: !output.Send,
                              strikeLeadingIcon: !output.Send,
                              onRename: n => { output.Name = n; OutputSetupHandling.SaveActive(); });
            }
        }

        if (DrawSection("REFERENCE IMAGES", "##addRefImage", selection, SetupActions.AddReferenceImage))
        {
            for (var i = 0; i < setup.ReferenceImages.Count; i++)
            {
                var image = setup.ReferenceImages[i];
                DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.ReferenceImage, image.Id, image.Name, null,
                              onRename: n => { image.Name = n; OutputSetupHandling.SaveActive(); });
            }
        }

        if (DrawSection("PROPS", "##addProp", selection, SetupActions.AddProp))
        {
            for (var i = 0; i < setup.Props.Count; i++)
            {
                var prop = setup.Props[i];
                DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Prop, prop.Id, prop.Kind, null);
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

    // Drag-to-map: a surface dropped on an output adds a mapping; a content send dropped on a surface or
    // output retargets it. Call right after a row's Selectable so it acts as that item's source/target.
    private static void HandleRowDragDrop(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (kind is SetupEntitySelection.EntityKind.Surface or SetupEntitySelection.EntityKind.ContentSource
                 or SetupEntitySelection.EntityKind.Slice)
            DragAndDropHandling.HandleDragSourceForLastItem(DragAndDropHandling.DragTypes.SetupEntity, $"{(int)kind}:{id}");

        if (kind is not (SetupEntitySelection.EntityKind.Output or SetupEntitySelection.EntityKind.Surface))
            return;

        if (!DragAndDropHandling.TryGetDragData(DragAndDropHandling.DragTypes.SetupEntity, out var dragData)
            || !SetupActions.TryParseDrag(dragData, out var dragKind, out var dragId))
            return;

        var accepts = kind == SetupEntitySelection.EntityKind.Output
                          ? dragKind is SetupEntitySelection.EntityKind.Surface or SetupEntitySelection.EntityKind.ContentSource
                                     or SetupEntitySelection.EntityKind.Slice
                          : dragKind is SetupEntitySelection.EntityKind.ContentSource or SetupEntitySelection.EntityKind.Slice;
        if (!accepts || dragId == id)
            return;

        if (DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.SetupEntity, out _) == DragAndDropHandling.DragInteractionResult.Dropped)
            SetupActions.ApplyDrop(setup, dragKind, dragId, kind, id);
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
            DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.ContentSource, childId, SetupActions.SinkName(instance), text,
                          leadingIcon: Icon.FileImage, trailingIcon: icon,
                          drawExtraMenuItems: source == null ? null : () =>
                                                                      {
                                                                          if (CustomComponents.DrawMenuItem(8, "Add slice"))
                                                                              SetupActions.AddSlice(selection, setup, source);
                                                                      },
                          isExpanded: sliceCount > 0 ? expanded : null,
                          onToggleExpanded: () => ToggleSourceExpanded(childId),
                          reserveExpander: true,
                          // No surface shows this source, so it steps back visually.
                          muted: icon == null,
                          // A source *is* its op, so renaming it renames the op (and cascades back through the sync).
                          onRename: n => SetupActions.RenameContentSourceOp(childId, n));

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
        var sliceId = slice.Id;
        var (targetIcon, targetText) = DescribeSliceTargetGutter(setup, slice);

        DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Slice, sliceId,
                      SetupActions.SliceLabel(setup, slice), targetText,
                      onDelete: () => SetupActions.DeleteSlice(setup, sliceId),
                      leadingIcon: Icon.Slice,
                      trailingIcon: targetIcon,
                      depth: 1,
                      // Nothing shows this slice, so it steps back visually.
                      muted: targetIcon == null,
                      onRename: n => { slice.Name = n; OutputSetupHandling.SaveActive(); });
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

    /// <summary>Enters inline-rename mode for a row: selects it, seeds the buffer, and focuses the field next frame.</summary>
    private static void BeginRename(SetupEntitySelection selection, SetupEntitySelection.EntityKind kind, Guid id, string name)
    {
        selection.Select(kind, id);
        _renamingId = id;
        _renameBuffer = name ?? string.Empty;
        _renameFocusPending = true;
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
        DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Surface, surface.Id, surface.Name,
                      outputText,
                      onDelete: () => SetupActions.DeleteSurface(setup, surfaceId), leadingIcon: Icon.Grid, trailingIcon: outputIcon,
                      // Delete comes from the row itself, so it isn't repeated here.
                      drawExtraMenuItems: () => SetupActions.DrawSurfaceMenuItems(selection, setup, surface, includeDelete: false),
                      depth: depth,
                      isExpanded: hasChildren ? isExpanded : null,
                      onToggleExpanded: () => ToggleSurfaceExpanded(surfaceId),
                      reserveExpander: true,
                      // A surface that won't render reads as unused (dimmed) and is struck through its icon.
                      muted: !surface.Render,
                      strikeLeadingIcon: !surface.Render,
                      onRename: n => { surface.Name = n; OutputSetupHandling.SaveActive(); });

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
        DrawInlineIcon(Icon.ChevronDown, UiColors.TextMuted.Rgba);

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
    private static void DrawInlineIcon(Icon icon, Vector4 rgba)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, rgba);
        ImGui.PushFont(Icons.IconFont);
        ImGui.TextUnformatted(((char)icon).ToString());
        ImGui.PopFont();
        ImGui.PopStyleColor();
    }

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

    /// <param name="depth">Tree depth. The row's background stays full width — only its content indents, so
    /// the selection highlight still reads as one row. (ImGui.Indent can't do this: the row is positioned from
    /// the window edges, not the cursor.)</param>
    /// <param name="isExpanded">null when the row has no children, so no chevron is drawn.</param>
    private static void DrawEntityRow(SetupEntitySelection selection, Setup setup, SetupEntitySelection.EntityKind kind, Guid id, string name, string? status,
                                      Action? onDelete = null, Action? onRemoveFromOutput = null, Icon? leadingIcon = null, Icon? trailingIcon = null,
                                      Action? drawExtraMenuItems = null, int depth = 0, bool? isExpanded = null, Action? onToggleExpanded = null,
                                      bool reserveExpander = false, bool muted = false, bool strikeLeadingIcon = false,
                                      Action<string>? onRename = null)
    {
        var scale = T3Ui.UiScaleFactor;
        var rounding = 4 * scale;
        // Odd height so a 15px icon centers exactly ((23-15)/2 = 4).
        var height = (float)Math.Round(23 * scale);
        var indent = depth * 12 * scale;

        // Nothing shows this entity, so it recedes rather than competing with the rows that are in use.
        var fade = muted ? 0.45f : 1f;

        // Rows that consume something own a left in-gutter (surfaces take content, outputs take surfaces).
        // The column is reserved whether or not a toggle is currently shown, so nothing shifts sideways when
        // the selection changes.
        var hasInputGutter = kind is SetupEntitySelection.EntityKind.Surface or SetupEntitySelection.EntityKind.Output;
        var gutterWidth = hasInputGutter ? Icons.FontSize + 4 * scale : 0;

        ImGui.PushID(id.GetHashCode());

        // Rounded row inset 4px from the window edges (so the selection/outline never clips), pixel-snapped
        // to avoid a blurry sub-pixel edge.
        var entryPos = ImGui.GetCursorScreenPos();
        var windowPos = ImGui.GetWindowPos();
        var rowMin = new Vector2((float)Math.Round(windowPos.X + 4 * scale), (float)Math.Round(entryPos.Y));
        var rowMax = new Vector2((float)Math.Round(windowPos.X + ImGui.GetWindowWidth() - 4 * scale), rowMin.Y + height);
        var dl = ImGui.GetWindowDrawList();
        var isSelected = selection.IsSelected(kind, id);

        // Full-row hit test — a selectable spanning the padded row; its own header background is suppressed
        // so we can draw a rounded one instead.
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
        ImGui.SetCursorScreenPos(rowMin);
        var clicked = ImGui.Selectable("##row", isSelected, ImGuiSelectableFlags.None, new Vector2(rowMax.X - rowMin.X, height));
        ImGui.PopStyleColor(3);

        var isHovered = ImGui.IsItemHovered();

        var isRenaming = onRename != null && _renamingId == id;

        // Double-click a renamable row to edit its name inline. Suppress the click-select handling below so the
        // double-click doesn't also toggle/reselect while the field takes focus.
        if (onRename != null && !isRenaming && isHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            BeginRename(selection, kind, id, name);
            isRenaming = true;
            clicked = false;
        }

        if (isRenaming)
            clicked = false;

        // The chevron shares the row's selectable rather than overlapping it with its own button — a click in
        // its column toggles instead of selecting.
        // A source is selected, so every row that could take it offers a click-target to bind or unbind.
        var isBound = false;
        var canBind = hasInputGutter && SetupActions.TryDescribeInputToggle(setup, kind, id, _primaryKind, _primaryId, out isBound);
        var gutterMaxX = rowMin.X + gutterWidth;
        if (clicked && canBind && ImGui.GetMousePos().X < gutterMaxX)
        {
            SetupActions.ToggleInput(setup, kind, id, _primaryKind, _primaryId);
            clicked = false;
        }

        var chevronMaxX = rowMin.X + gutterWidth + indent + 20 * scale;
        if (clicked && isExpanded.HasValue && ImGui.GetMousePos().X < chevronMaxX)
        {
            onToggleExpanded?.Invoke();
        }
        else if (clicked)
        {
            var io = ImGui.GetIO();
            if (io.KeyCtrl)
                selection.Toggle(kind, id);
            else if (io.KeyShift)
                selection.Add(kind, id);
            else
            {
                selection.Select(kind, id);
                // A content row is a live op — a plain click selects it in the graph and brings it into view.
                if (kind == SetupEntitySelection.EntityKind.ContentSource)
                    SetupActions.RevealContentOpInGraph(id);
            }
        }

        if (isHovered)
        {
            _pendingHoveredKind = kind;
            _pendingHoveredId = id;
            FrameStats.PulseItemWithId(id);
            if (kind == SetupEntitySelection.EntityKind.ContentSource)
                FrameStats.AddHoveredId(id);
        }

        HandleRowDragDrop(setup, kind, id);

        if (onDelete != null || onRemoveFromOutput != null || drawExtraMenuItems != null || onRename != null)
        {
            // Right-clicking inside a multi-selection acts on the whole thing. The per-entity actions stay
            // visible but dimmed rather than vanishing, so the menu keeps its shape and it is obvious *why*
            // they can't be used.
            var multi = isSelected && selection.Count > 1;
            var deletable = multi ? SetupActions.CountDeletable(selection) : 0;
            CustomComponents.ContextMenuForItem(() =>
                                                {
                                                    // These row menus carry no toggles or icons, so their labels sit flush left.
                                                    CustomComponents.MenuItemsFlushLeft = true;
                                                    CustomComponents.MenuItemsDisabled = multi;
                                                    ImGui.BeginDisabled(multi);
                                                    drawExtraMenuItems?.Invoke();

                                                    if (onRemoveFromOutput != null && CustomComponents.DrawMenuItem(1, "Remove from output"))
                                                        onRemoveFromOutput();

                                                    ImGui.EndDisabled();
                                                    CustomComponents.MenuItemsDisabled = false;

                                                    if (onRename != null && !multi && CustomComponents.DrawMenuItem(3, "Rename"))
                                                        BeginRename(selection, kind, id, name);

                                                    // Deleting is the one action that reads the selection rather than the row, so it is
                                                    // offered even from a row that isn't itself deletable.
                                                    if (multi)
                                                    {
                                                        if (deletable > 0 && CustomComponents.DrawMenuItem(2, $"Delete {deletable}"))
                                                            SetupActions.DeleteSelection(selection, setup);
                                                    }
                                                    else if (onDelete != null && CustomComponents.DrawMenuItem(2, "Delete"))
                                                    {
                                                        onDelete();
                                                    }

                                                    CustomComponents.MenuItemsFlushLeft = false;
                                                },
                                                null);
        }

        // While this row's context menu is open the pointer sits on the popup, not the row, so keep the row
        // lit anyway — otherwise it's no longer obvious which entity the menu belongs to. The popup id is
        // scoped by the row's PushID, so this only matches our own menu.
        var menuOpen = ImGui.IsPopupOpen("context_menu");

        // Hovered from the canvas (its frame is under the cursor) but not here: pulse so the eye is drawn to
        // the row that answers "which item is that frame?".
        var canvasPulse = !isHovered && !isSelected && !menuOpen ? FrameStats.GetPulse(id) : 0;

        if (isSelected)
        {
            dl.AddRectFilled(rowMin, rowMax, UiColors.StatusActivated.Fade(0.3f), rounding);
        }
        else if (isHovered || menuOpen)
        {
            dl.AddRectFilled(rowMin, rowMax, UiColors.StatusActivated.Fade(0.2f), rounding);
            dl.AddRect(rowMin, rowMax, UiColors.StatusActivated.Fade(0.8f), rounding);
        }
        else if (canvasPulse > 0.001f)
        {
            // Match the mouse-hover look (light fill + outline) so a canvas-driven highlight reads the same.
            dl.AddRectFilled(rowMin, rowMax, UiColors.StatusActivated.Fade(0.2f), rounding);
            dl.AddRect(rowMin, rowMax, UiColors.StatusActivated.Fade(0.8f), rounding);
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
            DrawInlineIcon(Icon.ArrowRight, overGutter ? UiColors.ForegroundFull.Rgba : color.Rgba);
        }
        else if (hasInputGutter && IsHoverInputHighlighted(kind, id))
        {
            // This row consumes the hovered feed: point its input arrow back at it (read-only, no bind click).
            ImGui.SetCursorScreenPos(new Vector2(rowMin.X + 4 * scale, iconY));
            DrawInlineIcon(Icon.ArrowRight, UiColors.StatusActivated.Rgba);
        }

        var contentX = rowMin.X + 6 * scale + gutterWidth + indent;
        if (isExpanded.HasValue)
        {
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            DrawInlineIcon(isExpanded.Value ? Icon.ChevronDown : Icon.ChevronRight, UiColors.TextMuted.Fade(0.6f).Rgba);
            contentX = ImGui.GetItemRectMax().X + 3 * scale;
        }
        else if (depth > 0 || reserveExpander)
        {
            // Keep the chevron column even when this row has nothing to expand — otherwise a childless row
            // sits further left than its siblings and the tree reads as ragged. Drawing the same glyph fully
            // transparent reserves *exactly* the width the real one takes, rather than a guessed constant.
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            DrawInlineIcon(Icon.ChevronRight, new Vector4(0, 0, 0, 0));
            contentX = ImGui.GetItemRectMax().X + 3 * scale;
        }

        if (leadingIcon.HasValue)
        {
            ImGui.SetCursorScreenPos(new Vector2(contentX, iconY));
            DrawInlineIcon(leadingIcon.Value, UiColors.TextMuted.Fade(fade).Rgba);

            // A disabled (non-rendered) surface is struck through its icon — visible at a glance without
            // stealing the gutter or the name.
            if (strikeLeadingIcon)
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
                    onRename!(_renameBuffer.Trim());

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
            CustomComponents.StylizedText(string.IsNullOrEmpty(name) ? "untitled" : name,
                                          isSelected ? Fonts.FontBold : Fonts.FontNormal, UiColors.Text.Fade(fade));
        }

        // Right-aligned trailing gutter "→ [count] [target-icon]": arrow, then the ×N count (if any), then the
        // target type at the very edge. When this row feeds the selected item — or the hovered one — the whole
        // group is bright StatusActivated so the source reads at a glance; otherwise the gutter is dim.
        var isSource = IsSourceOfPrimary(setup, kind, id) || IsHoverTrailingHighlighted(kind, id);
        if (!isRenaming && (isSource || trailingIcon.HasValue || status != null))
        {
            ImGui.PushFont(Fonts.FontSmall);
            var smallHeight = ImGui.GetTextLineHeight();
            var statusWidth = status != null ? ImGui.CalcTextSize(status).X : 0;
            ImGui.PopFont();

            var trailWidth = Icons.FontSize; // the direction arrow
            if (status != null)
                trailWidth += statusWidth + 3 * scale;
            if (trailingIcon.HasValue)
                trailWidth += Icons.FontSize + 3 * scale;

            var arrowColor = isSource ? UiColors.StatusActivated : UiColors.TextMuted.Fade(0.3f * fade);
            var textColor = isSource ? UiColors.StatusActivated : UiColors.TextMuted.Fade(fade);

            var trailX = rowMax.X - 6 * scale - trailWidth;
            ImGui.SetCursorScreenPos(new Vector2(trailX, iconY));
            DrawInlineIcon(Icon.ArrowRight, arrowColor.Rgba);
            trailX = ImGui.GetItemRectMax().X + 3 * scale;

            if (status != null)
            {
                // FontSmall is shorter than the row's FontNormal baseline — center it on its own height.
                var statusY = (float)Math.Round(rowMin.Y + (height - smallHeight) * 0.5f - 1 * scale);
                ImGui.SetCursorScreenPos(new Vector2(trailX, statusY));
                CustomComponents.StylizedText(status, Fonts.FontSmall, textColor);
                trailX += statusWidth + 3 * scale;
            }

            if (trailingIcon.HasValue)
            {
                ImGui.SetCursorScreenPos(new Vector2(trailX, iconY));
                DrawInlineIcon(trailingIcon.Value, textColor.Rgba);
            }
        }

        // Next row starts a tight 2px below, independent of the content cursor above.
        ImGui.SetCursorScreenPos(new Vector2(entryPos.X, rowMax.Y + 2 * scale));
        ImGui.PopID();
    }

    private static void DrawOutputBindingSubMenu(OutputDefinition output, MachineConfig machineConfig)
    {
        if (CustomComponents.DrawSubMenu(3, "Bind to display"))
        {
            ResolutionHandling.DrawBindingMenuItems(output, machineConfig);
            ImGui.EndMenu();
        }
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
    private static Guid _renamingId;
    private static string _renameBuffer = string.Empty;
    private static bool _renameFocusPending;
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
