#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.InputUi.ListInputs;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.InputsAndTypes;
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

    public static void Draw(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
        {
            CustomComponents.EmptyWindowMessage("No project focused");
            return;
        }

        // Sources are 1:1 with the ops that supply them, so adopt new sends and cascade away deleted ones.
        ContentSourceSync.Update(setup);

        // Cross-highlight: what the row hovered last frame references (one-frame lag is imperceptible for hover).
        ComputeReferenced(setup);

        // Resolved once: TryResolve prunes the target list behind a closure, and every row asks for the
        // primary when deciding whether to offer an in-gutter toggle.
        if (!selection.TryResolve(setup, out _primaryKind, out _primaryId))
        {
            _primaryKind = SetupEntitySelection.EntityKind.None;
            _primaryId = Guid.Empty;
        }

        _pendingHoveredKind = SetupEntitySelection.EntityKind.None;
        _pendingHoveredId = Guid.Empty;

        DrawSetupSwitcher(setup, selection);
        FormInputs.AddVerticalSpace(4);

        // CONTENT — live SendToOutput sinks (their targeting lives on the op, so they aren't setup entities).
        if (DrawSectionLabel("CONTENT"))
            DrawContentSinks(selection, setup);

        if (DrawSection("SURFACES", "##addSurface", selection, AddSurface))
            DrawSurfaces(selection, setup);

        if (DrawSection("OUTPUTS", "##addOutput", selection, AddOutput))
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
                              onDelete: () => DeleteOutput(setup, machineConfig, outputId), leadingIcon: Icon.Projector,
                              drawExtraMenuItems: bindable ? () => DrawOutputBindingSubMenu(output, machineConfig) : null,
                              // A paused output (Send off) reads the same as a non-rendering surface.
                              muted: !output.Send,
                              strikeLeadingIcon: !output.Send,
                              onRename: n => { output.Name = n; OutputSetupHandling.SaveActive(); });
            }
        }

        if (DrawSection("REFERENCE IMAGES", "##addRefImage", selection, AddReferenceImage))
        {
            for (var i = 0; i < setup.ReferenceImages.Count; i++)
            {
                var image = setup.ReferenceImages[i];
                DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.ReferenceImage, image.Id, image.Name, null,
                              onRename: n => { image.Name = n; OutputSetupHandling.SaveActive(); });
            }
        }

        if (DrawSection("PROPS", "##addProp", selection, AddProp))
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
        // Divider above the card, matching the section top edge, plus a 2px gap before the first item.
        DrawPanelDivider();
        FormInputs.AddVerticalSpace(2);
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
        }

        ImGui.PopStyleColor();
        ImGui.Unindent(6 * T3Ui.UiScaleFactor);
    }

    private static void DrawSurfaceCard(Setup setup, Guid id)
    {
        var surface = setup.Surfaces.Find(s => s.Id == id);
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
        var shortName = surface.ShortName;
        if (FormInputsNarrow.DrawString("Short Name", ref shortName, "Auto", "Empty = auto-abbreviated (e.g. S1)."))
        {
            surface.ShortName = shortName;
            OutputSetupHandling.SaveActive();
        }

        FormInputsNarrow.DrawLabel("Sending to…", "Outputs this surface is mapped to.");
        for (var i = 0; i < surface.OutputMappings.Count; i++)
        {
            var output = setup.Outputs.Find(o => o.Id == surface.OutputMappings[i].OutputId);
            FormInputsNarrow.DrawListItem(output == null ? "?" : output.Name);
        }

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
                                                    reserveRight: 20);

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
            SurfaceGeometry.ResizeAnchored(surface, new Vector2(size[0], size[1]));

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
        var output = setup.Outputs.Find(o => o.Id == id);
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
        var instance = FindSinkInstance(childId);
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

    // Fills _referenced with the entities the currently-hovered row points at, along the
    // content → surface → output chain, so those rows can draw the Referenced state.
    private static void ComputeReferenced(Setup setup)
    {
        _referenced.Clear();
        if (_hoveredKind == SetupEntitySelection.EntityKind.None)
            return;

        _sinkContext ??= new EvaluationContext();
        _sinkContext.Reset();

        switch (_hoveredKind)
        {
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.Surfaces.Find(s => s.Id == _hoveredId);
                if (surface != null)
                {
                    foreach (var mapping in surface.OutputMappings)
                        _referenced.Add((SetupEntitySelection.EntityKind.Output, mapping.OutputId));
                }

                AddSinksTargeting(_hoveredId);
                break;
            }
            case SetupEntitySelection.EntityKind.Output:
            {
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.OutputMappings.Exists(m => m.OutputId == _hoveredId))
                        _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id));
                }

                AddSinksTargeting(_hoveredId);
                break;
            }
            case SetupEntitySelection.EntityKind.ContentSource:
            {
                // A source references whatever shows one of its slices.
                var hoveredSource = setup.ContentSources.Find(c => c.SymbolChildId == _hoveredId);
                if (hoveredSource != null)
                {
                    foreach (var surface in setup.Surfaces)
                    {
                        if (IsSliceOf(setup, surface.SliceId, hoveredSource.Id))
                            _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id));
                    }

                    foreach (var output in setup.Outputs)
                    {
                        if (IsSliceOf(setup, output.SliceId, hoveredSource.Id))
                            _referenced.Add((SetupEntitySelection.EntityKind.Output, output.Id));
                    }
                }

                break;
            }
            case SetupEntitySelection.EntityKind.ReferenceImage:
            {
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.Reference != null && surface.Reference.ImageId == _hoveredId)
                        _referenced.Add((SetupEntitySelection.EntityKind.Surface, surface.Id));
                }

                break;
            }
        }
    }

    /// <summary>Sources whose slice is shown by this surface or output — the reverse of the content gutter.</summary>
    private static void AddSinksTargeting(Guid targetId)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var surface = setup.Surfaces.Find(s => s.Id == targetId);
        var sliceId = surface?.SliceId ?? setup.Outputs.Find(o => o.Id == targetId)?.SliceId ?? Guid.Empty;
        if (sliceId == Guid.Empty)
            return;

        var slice = setup.Slices.Find(s => s.Id == sliceId);
        var source = slice == null ? null : setup.ContentSources.Find(c => c.Id == slice.SourceId);
        if (source != null)
            _referenced.Add((SetupEntitySelection.EntityKind.ContentSource, source.SymbolChildId));
    }

    /// <summary>Whether any surface mapped to this output shows one of the source's slices.</summary>
    private static bool IsReferenced(SetupEntitySelection.EntityKind kind, Guid id)
    {
        for (var i = 0; i < _referenced.Count; i++)
        {
            if (_referenced[i].kind == kind && _referenced[i].id == id)
                return true;
        }

        return false;
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
            || !TryParseDrag(dragData, out var dragKind, out var dragId))
            return;

        var accepts = kind == SetupEntitySelection.EntityKind.Output
                          ? dragKind is SetupEntitySelection.EntityKind.Surface or SetupEntitySelection.EntityKind.ContentSource
                                     or SetupEntitySelection.EntityKind.Slice
                          : dragKind is SetupEntitySelection.EntityKind.ContentSource or SetupEntitySelection.EntityKind.Slice;
        if (!accepts || dragId == id)
            return;

        if (DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.SetupEntity, out _) == DragAndDropHandling.DragInteractionResult.Dropped)
            ApplyDrop(setup, dragKind, dragId, kind, id);
    }

    private static bool TryParseDrag(string data, out SetupEntitySelection.EntityKind kind, out Guid id)
    {
        kind = SetupEntitySelection.EntityKind.None;
        id = Guid.Empty;
        var separator = data.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(data.AsSpan(0, separator), out var kindInt)
            || !Guid.TryParse(data.AsSpan(separator + 1), out id))
            return false;

        kind = (SetupEntitySelection.EntityKind)kindInt;
        return true;
    }

    private static void ApplyDrop(Setup setup, SetupEntitySelection.EntityKind dragKind, Guid dragId,
                                  SetupEntitySelection.EntityKind targetKind, Guid targetId)
    {
        if (targetKind == SetupEntitySelection.EntityKind.Output && dragKind == SetupEntitySelection.EntityKind.Surface)
        {
            var surface = setup.Surfaces.Find(s => s.Id == dragId);
            var output = setup.Outputs.Find(o => o.Id == targetId);
            if (surface != null && output != null && !surface.OutputMappings.Exists(m => m.OutputId == targetId))
            {
                surface.OutputMappings.Add(CreateDefaultMapping(output));
                OutputSetupHandling.SaveActive();
            }

            return;
        }

        // Dropping a source or slice straight onto an output shows it full-frame (the direct path, no surface
        // or corner-pin) — an output names a slice through OutputDefinition.SliceId.
        if (targetKind == SetupEntitySelection.EntityKind.Output
            && dragKind is SetupEntitySelection.EntityKind.Slice or SetupEntitySelection.EntityKind.ContentSource)
        {
            var output = setup.Outputs.Find(o => o.Id == targetId);
            if (output == null)
                return;

            if (dragKind == SetupEntitySelection.EntityKind.Slice && setup.Slices.Exists(s => s.Id == dragId))
            {
                output.SliceId = dragId;
                OutputSetupHandling.SaveActive();
            }
            else if (dragKind == SetupEntitySelection.EntityKind.ContentSource)
            {
                var source = setup.ContentSources.Find(c => c.SymbolChildId == dragId);
                if (source != null)
                {
                    output.SliceId = EnsureSlice(setup, source).Id;
                    OutputSetupHandling.SaveActive();
                }
            }

            return;
        }

        // Dropping a source onto a surface shows one of its slices there, creating a full-frame slice if the
        // source has none yet. Routing is setup data now, so this is a plain field write.
        // Dropping a slice on a free surface shows it there. If the surface already shows something, the drop
        // can't have meant "replace it", so it lands as a sub-region cut to the slice's own aspect instead —
        // which is the poster-slot case, and never destroys an existing assignment.
        if (dragKind == SetupEntitySelection.EntityKind.Slice)
        {
            var slice = setup.Slices.Find(s => s.Id == dragId);
            var surface = setup.Surfaces.Find(s => s.Id == targetId);
            if (slice != null && surface != null)
            {
                if (surface.SliceId == Guid.Empty)
                    surface.SliceId = slice.Id;
                else
                    AddRegionForSlice(setup, surface, slice);

                OutputSetupHandling.SaveActive();
            }
        }

        if (dragKind == SetupEntitySelection.EntityKind.ContentSource)
        {
            var source = setup.ContentSources.Find(c => c.SymbolChildId == dragId);
            var surface = source == null ? null : setup.Surfaces.Find(s => s.Id == targetId);
            if (source != null && surface != null)
            {
                surface.SliceId = EnsureSlice(setup, source).Id;
                OutputSetupHandling.SaveActive();
            }
        }
    }

    private static Surface.OutputMapping CreateDefaultMapping(OutputDefinition output)
    {
        float w = Math.Max(1, output.CanvasResolution.Width);
        float h = Math.Max(1, output.CanvasResolution.Height);
        float x0 = w * 0.2f, x1 = w * 0.8f, y0 = h * 0.2f, y1 = h * 0.8f;
        return new Surface.OutputMapping
                   {
                       OutputId = output.Id,
                       Quad = [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)],
                   };
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
            var source = setup.ContentSources.Find(c => c.SymbolChildId == childId);
            var sliceCount = source == null ? 0 : setup.Slices.FindAll(x => x.SourceId == source.Id).Count;
            var expanded = !_collapsedSources.Contains(childId);

            var (icon, text) = DescribeSourceGutter(setup, childId);
            DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.ContentSource, childId, SinkName(instance), text,
                          leadingIcon: Icon.FileImage, trailingIcon: icon,
                          drawExtraMenuItems: source == null ? null : () =>
                                                                      {
                                                                          if (CustomComponents.DrawMenuItem(8, "Add slice"))
                                                                              AddSlice(selection, setup, source);
                                                                      },
                          isExpanded: sliceCount > 0 ? expanded : null,
                          onToggleExpanded: () => ToggleSourceExpanded(childId),
                          reserveExpander: true,
                          // No surface shows this source, so it steps back visually.
                          muted: icon == null);

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
        var status = DescribeSliceStatus(setup, slice, out var mismatched);

        DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Slice, sliceId,
                      SliceLabel(setup, slice), status,
                      onDelete: () => DeleteSlice(setup, sliceId),
                      leadingIcon: Icon.Slice,
                      trailingIcon: mismatched ? Icon.Warning : null,
                      depth: 1,
                      muted: status is "unused" or "no source",
                      onRename: n => { slice.Name = n; OutputSetupHandling.SaveActive(); });
    }

    /// <summary>Aspect of the slice's pixels, plus whether that disagrees with what shows it.</summary>
    private static string DescribeSliceStatus(Setup setup, Slice slice, out bool mismatched)
    {
        mismatched = false;

        var source = setup.ContentSources.Find(c => c.Id == slice.SourceId);
        if (source == null || !OutputManager.TryGetSourceContent(source.SymbolChildId, out _, out var content)
            || content is not { IsDisposed: false })
            return "no source";

        var width = content.Description.Width * MathF.Max(slice.UvRect.Z - slice.UvRect.X, 0.0001f);
        var height = content.Description.Height * MathF.Max(slice.UvRect.W - slice.UvRect.Y, 0.0001f);
        if (height <= 0)
            return "no source";

        var aspect = width / height;

        Surface? shownBy = null;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId != slice.Id)
                continue;

            shownBy = surface;
            break;
        }

        if (shownBy == null)
            return "unused";

        var surfaceAspect = shownBy.SizeInMeters.X / MathF.Max(shownBy.SizeInMeters.Y, 0.0001f);
        mismatched = MathF.Abs(aspect - surfaceAspect) > surfaceAspect * 0.02f;
        return FormatAspect(aspect);
    }

    /// <summary>A ratio reads faster than a decimal, so prefer a small whole-number one when it's close.</summary>
    private static string FormatAspect(float aspect)
    {
        for (var denominator = 1; denominator <= 16; denominator++)
        {
            var numerator = aspect * denominator;
            if (MathF.Abs(numerator - MathF.Round(numerator)) < 0.02f)
                return $"{(int)MathF.Round(numerator)}:{denominator}";
        }

        return $"{aspect:0.00}:1";
    }

    private static void ToggleSourceExpanded(Guid childId)
    {
        if (!_collapsedSources.Add(childId))
            _collapsedSources.Remove(childId);
    }

    private static void AddSlice(SetupEntitySelection selection, Setup setup, ContentSource source)
    {
        // Left unnamed: the label is derived from the source, so it stays right when the op is later renamed.
        var slice = new Slice { SourceId = source.Id };

        setup.Slices.Add(slice);
        selection.Select(SetupEntitySelection.EntityKind.Slice, slice.Id);
        OutputSetupHandling.SaveActive();
    }

    /// <summary>
    /// A slice's display name: a name the user typed if there is one, otherwise a default derived from its
    /// source. Unnamed sources give "Slice N"; a renamed source gives "{name}.N", so naming the op renames
    /// every one of its auto-named slices at once. N is the slice's position among its source's slices.
    /// </summary>
    internal static string SliceLabel(Setup setup, Slice slice)
    {
        if (!string.IsNullOrEmpty(slice.Name))
            return slice.Name;

        var ordinal = 1;
        foreach (var other in setup.Slices)
        {
            if (other.SourceId != slice.SourceId)
                continue;

            if (other.Id == slice.Id)
                break;

            ordinal++;
        }

        var source = setup.ContentSources.Find(c => c.Id == slice.SourceId);
        return source is { IsRenamed: true } && !string.IsNullOrEmpty(source.Name)
                   ? $"{source.Name}.{ordinal}"
                   : $"Slice {ordinal}";
    }

    /// <summary>Deleting a slice clears it from anything showing it — the reference would mean nothing.</summary>
    /// <summary>Enters inline-rename mode for a row: selects it, seeds the buffer, and focuses the field next frame.</summary>
    private static void BeginRename(SetupEntitySelection selection, SetupEntitySelection.EntityKind kind, Guid id, string name)
    {
        selection.Select(kind, id);
        _renamingId = id;
        _renameBuffer = name ?? string.Empty;
        _renameFocusPending = true;
    }

    /// <summary>The slice's context menu, shared by its sidebar row and its frame label on the canvas.</summary>
    internal static void DrawSliceMenuItems(SetupEntitySelection selection, Setup setup, Slice slice)
    {
        CustomComponents.MenuItemsFlushLeft = true;
        if (CustomComponents.DrawMenuItem(2, "Delete"))
        {
            DeleteSlice(setup, slice.Id);
            selection.Clear();
        }

        CustomComponents.MenuItemsFlushLeft = false;
    }

    private static void DeleteSlice(Setup setup, Guid sliceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                surface.SliceId = Guid.Empty;
        }

        foreach (var output in setup.Outputs)
        {
            if (output.SliceId == sliceId)
                output.SliceId = Guid.Empty;
        }

        setup.Slices.RemoveAll(s => s.Id == sliceId);
        OutputSetupHandling.SaveActive();
    }

    private static string SinkName(Instance instance)
    {
        var parent = instance.Parent;
        if (parent != null && parent.Symbol.Children.TryGetValue(instance.SymbolChildId, out var child))
            return child.ReadableName;

        return "content";
    }

    private static Instance? FindSinkInstance(Guid childId)
    {
        var sinks = OutputSinkRegistry.Sinks;
        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is Instance instance && instance.SymbolChildId == childId)
                return instance;
        }

        return null;
    }

    // Select the content's SendToOutput op in the focused graph and frame it — the sidebar → graph half of
    // the sync (the graph → sidebar highlight is handled by the highlighted-content id).
    private static void RevealContentOpInGraph(Guid childId)
    {
        var instance = FindSinkInstance(childId);
        var parentSymbolUi = instance?.Parent?.GetSymbolUi();
        if (instance == null || parentSymbolUi == null || ProjectView.Focused == null)
            return;

        if (!parentSymbolUi.ChildUis.TryGetValue(instance.SymbolChildId, out var childUi))
            return;

        ProjectView.Focused.NodeSelection.SetSelection(childUi, instance);
        FitViewToSelectionHandling.FitViewToSelection();
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
        var surface = setup.Surfaces.Find(s => s.Id == targetId);
        if (surface != null)
            return (Icon.Grid, SurfaceShortLabel(surface));

        var output = setup.Outputs.Find(o => o.Id == targetId);
        if (output != null)
            return (Icon.Projector, string.IsNullOrEmpty(output.Name) ? "output" : Abbreviate(output.Name));

        return (Icon.Grid, "?");
    }

    /// <summary>
    /// Reshapes the surface so its real-world proportions match the pixels of the slice it shows — the inverse
    /// of the slice's "Match target aspect", for when the wall is what should give. Keeps the width and solves
    /// the height, so it reads as a nudge rather than a jump.
    /// </summary>
    private static void MatchSurfaceToSliceAspect(Setup setup, Surface surface)
    {
        var slice = setup.Slices.Find(s => s.Id == surface.SliceId);
        if (slice == null || !TryGetSliceAspect(setup, slice, out var aspect))
            return;

        var oldState = new ResizeSurfaceCommand.State(surface);
        var width = MathF.Max(surface.SizeInMeters.X, SurfaceGeometry.MinSize);
        SurfaceGeometry.ResizeAnchored(surface, new Vector2(width, width / MathF.Max(aspect, 0.0001f)));

        UndoRedoStack.Add(new ResizeSurfaceCommand(surface.Id, oldState, new ResizeSurfaceCommand.State(surface)));
        OutputSetupHandling.SaveActive();
    }

    /// <summary>
    /// A sub-region shaped to a slice: sized so its real-world proportions match the slice's pixels, so the
    /// content lands undistorted, and centred in the parent.
    /// </summary>
    private static void AddRegionForSlice(Setup setup, Surface parent, Slice slice)
    {
        var parentSize = parent.SizeInMeters;
        var aspect = TryGetSliceAspect(setup, slice, out var value) ? value : 1f;

        var width = parentSize.X * 0.5f;
        var height = width / MathF.Max(aspect, 0.0001f);
        if (height > parentSize.Y * 0.8f)
        {
            height = parentSize.Y * 0.5f;
            width = height * aspect;
        }

        // Centre it: surface space runs Y down, so the bottom edge is below the middle.
        var anchor = SurfaceGeometry.AnchorInSurface(parent);
        var bottomLeft = new Vector2(parentSize.X * 0.5f - width * 0.5f, parentSize.Y * 0.5f + height * 0.5f);

        var region = new Surface
                         {
                             Name = string.IsNullOrEmpty(slice.Name) ? $"Sub region {CountChildren(setup, parent.Id) + 1}" : slice.Name,
                             Kind = Surface.SurfaceKinds.Layout,
                             ParentId = parent.Id,
                             SizeInMeters = new Vector2(MathF.Max(width, SurfaceGeometry.MinSize),
                                                        MathF.Max(height, SurfaceGeometry.MinSize)),
                             LocalPosition = new Vector2(bottomLeft.X - anchor.X, anchor.Y - bottomLeft.Y),
                             PixelsPerMeter = parent.PixelsPerMeter,
                             SliceId = slice.Id,
                         };

        setup.Surfaces.Add(region);
    }

    /// <summary>Aspect of a slice's pixels — its uv extent against the source's resolution.</summary>
    private static bool TryGetSliceAspect(Setup setup, Slice slice, out float aspect)
    {
        aspect = 1f;
        var source = setup.ContentSources.Find(c => c.Id == slice.SourceId);
        if (source == null || !OutputManager.TryGetSourceContent(source.SymbolChildId, out _, out var content)
            || content is not { IsDisposed: false })
            return false;

        var width = content.Description.Width * MathF.Max(slice.UvRect.Z - slice.UvRect.X, 0.0001f);
        var height = content.Description.Height * MathF.Max(slice.UvRect.W - slice.UvRect.Y, 0.0001f);
        if (width <= 0 || height <= 0)
            return false;

        aspect = width / height;
        return true;
    }

    /// <summary>Whether a slice belongs to the given source.</summary>
    /// <summary>
    /// Whether <paramref name="kind"/>/<paramref name="id"/> can take the primary selection as its input, and
    /// whether it already does. Clicking the in-gutter then binds or unbinds without any dragging: select a
    /// slice and the surfaces that could show it light up; select a surface and the outputs light up.
    /// </summary>
    private static bool TryDescribeInputToggle(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, out bool isBound)
    {
        isBound = false;
        var sourceKind = _primaryKind;
        var sourceId = _primaryId;
        if (sourceKind == SetupEntitySelection.EntityKind.None)
            return false;

        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.Surfaces.Find(x => x.Id == id);
                if (surface == null)
                    return false;

                if (sourceKind == SetupEntitySelection.EntityKind.Slice)
                {
                    isBound = surface.SliceId == sourceId;
                    return setup.Slices.Exists(x => x.Id == sourceId);
                }

                if (sourceKind == SetupEntitySelection.EntityKind.ContentSource)
                {
                    var source = setup.ContentSources.Find(c => c.SymbolChildId == sourceId);
                    if (source == null)
                        return false;

                    isBound = IsSliceOf(setup, surface.SliceId, source.Id);
                    return true;
                }

                return false;
            }

            case SetupEntitySelection.EntityKind.Output:
            {
                if (sourceKind != SetupEntitySelection.EntityKind.Surface)
                    return false;

                var surface = setup.Surfaces.Find(x => x.Id == sourceId);
                if (surface == null)
                    return false;

                isBound = surface.OutputMappings.Exists(m => m.OutputId == id);
                return true;
            }

            default:
                return false;
        }
    }

    private static void ToggleInput(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (!TryDescribeInputToggle(setup, kind, id, out var isBound))
            return;

        var sourceKind = _primaryKind;
        var sourceId = _primaryId;

        if (kind == SetupEntitySelection.EntityKind.Surface)
        {
            var surface = setup.Surfaces.Find(x => x.Id == id);
            if (surface == null)
                return;

            if (isBound)
            {
                surface.SliceId = Guid.Empty;
            }
            else if (sourceKind == SetupEntitySelection.EntityKind.Slice)
            {
                surface.SliceId = sourceId;
            }
            else
            {
                var source = setup.ContentSources.Find(c => c.SymbolChildId == sourceId);
                if (source == null)
                    return;

                surface.SliceId = EnsureSlice(setup, source).Id;
            }
        }
        else
        {
            var surface = setup.Surfaces.Find(x => x.Id == sourceId);
            var output = setup.Outputs.Find(o => o.Id == id);
            if (surface == null || output == null)
                return;

            if (isBound)
                surface.OutputMappings.RemoveAll(m => m.OutputId == id);
            else
                surface.OutputMappings.Add(CreateDefaultMapping(output));
        }

        OutputSetupHandling.SaveActive();
    }

    private static bool IsSliceOf(Setup setup, Guid sliceId, Guid sourceId)
    {
        if (sliceId == Guid.Empty)
            return false;

        var slice = setup.Slices.Find(s => s.Id == sliceId);
        return slice != null && slice.SourceId == sourceId;
    }

    /// <summary>
    /// A source's first slice, creating a full-frame one if it has none — assigning content needs a slice to
    /// name, and "the whole image" is simply the identity rect.
    /// </summary>
    private static Slice EnsureSlice(Setup setup, ContentSource source)
    {
        var existing = setup.Slices.Find(s => s.SourceId == source.Id);
        if (existing != null)
            return existing;

        // Unnamed: its label is derived from the source (see SliceLabel), so renaming the op renames it too.
        var slice = new Slice { SourceId = source.Id };
        setup.Slices.Add(slice);
        return slice;
    }

    /// <summary>Out-gutter for a content row: what shows one of this source's slices.</summary>
    private static (Icon? icon, string? text) DescribeSourceGutter(Setup setup, Guid symbolChildId)
    {
        var source = setup.ContentSources.Find(c => c.SymbolChildId == symbolChildId);
        if (source == null)
            return (null, "unbound");

        Surface? first = null;
        var count = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (!IsSliceOf(setup, surface.SliceId, source.Id))
                continue;

            first = first ?? surface;
            count++;
        }

        if (first == null)
            return (null, "unused");

        var label = SurfaceShortLabel(first);
        return (Icon.Grid, count > 1 ? label + " +" + (count - 1) : label);
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
        var hasChildren = CountChildren(setup, surfaceId) > 0;
        var isExpanded = !_collapsedSurfaces.Contains(surfaceId);

        var (outputIcon, outputText) = DescribeSurfaceOutputGutter(setup, surface);
        DrawEntityRow(selection, setup, SetupEntitySelection.EntityKind.Surface, surface.Id, surface.Name,
                      outputText,
                      onDelete: () => DeleteSurface(setup, surfaceId), leadingIcon: Icon.Grid, trailingIcon: outputIcon,
                      // Delete comes from the row itself, so it isn't repeated here.
                      drawExtraMenuItems: () => DrawSurfaceMenuItems(selection, setup, surface, includeDelete: false),
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

    // Out-gutter for a surface: the projector icon + the mapped output name (+N for edge-blended extras).
    private static (Icon? icon, string? text) DescribeSurfaceOutputGutter(Setup setup, Surface surface)
    {
        if (surface.OutputMappings.Count == 0)
            return (null, null);

        var firstOutput = setup.Outputs.Find(o => o.Id == surface.OutputMappings[0].OutputId);
        var name = firstOutput == null ? "?" : Abbreviate(firstOutput.Name);
        var text = surface.OutputMappings.Count > 1 ? $"{name} +{surface.OutputMappings.Count - 1}" : name;
        return (Icon.Projector, text);
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
                var image = setup.ReferenceImages.Find(e => e.Id == id);
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
                var surface = setup.Surfaces.Find(e => e.Id == id);
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
                var prop = setup.Props.Find(e => e.Id == id);
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
                var output = setup.Outputs.Find(e => e.Id == id);
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
                var instance = FindSinkInstance(id);
                if (instance is IOutputSink sink)
                {
                    _sinkContext ??= new EvaluationContext();
                    _sinkContext.Reset();
                    CustomComponents.StylizedText("Content · SendToOutput", Fonts.FontSmall, UiColors.TextMuted);
                    CustomComponents.StylizedText(SinkName(instance), Fonts.FontLarge, UiColors.Text);
                    var (_, targetText) = DescribeSourceGutter(setup, instance.SymbolChildId);
                    CustomComponents.StylizedText(targetText ?? "unused", Fonts.FontNormal, UiColors.TextMuted);
                }

                break;
            }
        }

        ImGui.EndGroup();
    }

    private static void DrawSetupSwitcher(Setup setup, SetupEntitySelection selection)
    {
        var scale = T3Ui.UiScaleFactor;
        var pos = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        if (ImGui.InvisibleButton("##setupSwitcher", new Vector2(ImGui.GetContentRegionAvail().X, height)))
            ImGui.OpenPopup("##setupMenu");

        // Label + chevron drawn over the button so the chevron sits next to the name (not far-right like a combo).
        ImGui.SetCursorScreenPos(new Vector2(pos.X + 2 * scale, pos.Y));
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(setup.Name, Fonts.FontNormal, UiColors.Text);
        ImGui.SameLine(0, 4 * scale);
        DrawInlineIcon(Icon.ChevronDown, UiColors.TextMuted.Rgba);
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

    // A surface's compact label: its explicit ShortName, else the auto-abbreviation.
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

        var surface = setup.Surfaces.Find(s => s.Id == id);
        if (surface != null)
            return SurfaceShortLabel(surface);

        var output = setup.Outputs.Find(o => o.Id == id);
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

    /// <summary>Display name of a content send, for labelling its slice on the source canvas.</summary>
    internal static string? TryGetContentName(Guid contentChildId)
    {
        return FindSinkInstance(contentChildId) is { } instance ? SinkName(instance) : null;
    }

    private static string SurfaceShortLabel(Surface surface)
    {
        return string.IsNullOrEmpty(surface.ShortName) ? Abbreviate(surface.Name) : surface.ShortName;
    }

    // Compact gutter form: uppercase letters + digits ("Surface 1" → "S1", "WallFront" → "WF"), falling back
    // to the full name when there's nothing to abbreviate (all-lowercase).
    private static string Abbreviate(string name)
    {
        Span<char> buffer = stackalloc char[6];
        var length = 0;
        foreach (var c in name)
        {
            if ((char.IsUpper(c) || char.IsDigit(c)) && length < buffer.Length)
                buffer[length++] = c;
        }

        return length >= 1 ? new string(buffer[..length]) : name;
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
        var canBind = hasInputGutter && TryDescribeInputToggle(setup, kind, id, out isBound);
        var gutterMaxX = rowMin.X + gutterWidth;
        if (clicked && canBind && ImGui.GetMousePos().X < gutterMaxX)
        {
            ToggleInput(setup, kind, id);
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
                    RevealContentOpInGraph(id);
            }
        }

        if (isHovered)
        {
            _pendingHoveredKind = kind;
            _pendingHoveredId = id;
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
            var deletable = multi ? CountDeletable(selection) : 0;
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
                                                            DeleteSelection(selection, setup);
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

        if (isSelected)
            dl.AddRectFilled(rowMin, rowMax, UiColors.StatusActivated.Fade(0.3f), rounding);
        else if (isHovered || menuOpen)
            dl.AddRectFilled(rowMin, rowMax, UiColors.ForegroundFull.Fade(0.2f), rounding);

        if (!isSelected && IsReferenced(kind, id))
            dl.AddRect(rowMin, rowMax, UiColors.StatusAutomated.Fade(0.6f), rounding);

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
            CustomComponents.StylizedText(string.IsNullOrEmpty(name) ? "untitled" : name, Fonts.FontNormal, UiColors.Text.Fade(fade));
        }

        if (!isRenaming && (trailingIcon.HasValue || status != null))
        {
            // Right-aligned rather than parked at a fixed fraction of the row: a long name used to run
            // straight into the gutter. Measure the group first, then lay it out from the right edge.
            ImGui.PushFont(Fonts.FontSmall);
            var smallHeight = ImGui.GetTextLineHeight();
            var statusWidth = status != null ? ImGui.CalcTextSize(status).X : 0;
            ImGui.PopFont();

            var trailWidth = statusWidth;
            if (trailingIcon.HasValue)
                trailWidth += Icons.FontSize * 2 + 2 * scale + 4 * scale;

            var trailX = rowMax.X - 6 * scale - trailWidth;
            if (trailingIcon.HasValue)
            {
                ImGui.SetCursorScreenPos(new Vector2(trailX, iconY));
                DrawInlineIcon(Icon.ArrowRight, UiColors.TextMuted.Fade(0.3f * fade).Rgba);
                ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X + 2 * scale, iconY));
                DrawInlineIcon(trailingIcon.Value, UiColors.TextMuted.Fade(fade).Rgba);
                trailX = ImGui.GetItemRectMax().X + 4 * scale;
            }

            if (status != null)
            {
                // FontSmall is shorter than the row's FontNormal baseline — center it on its own height.
                var statusY = (float)Math.Round(rowMin.Y + (height - smallHeight) * 0.5f - 1 * scale);
                ImGui.SetCursorScreenPos(new Vector2(trailX, statusY));
                CustomComponents.StylizedText(status, Fonts.FontSmall, UiColors.TextMuted.Fade(fade));
            }
        }

        // Next row starts a tight 2px below, independent of the content cursor above.
        ImGui.SetCursorScreenPos(new Vector2(entryPos.X, rowMax.Y + 2 * scale));
        ImGui.PopID();
    }

    private static void AddReferenceImage(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var image = new ReferenceImage { Name = $"Image {setup.ReferenceImages.Count + 1}" };
        setup.ReferenceImages.Add(image);
        selection.Select(SetupEntitySelection.EntityKind.ReferenceImage, image.Id);
    }

    /// <summary>
    /// The surface actions, shared by the sidebar row and the canvas label so the two can't drift apart.
    /// </summary>
    internal static void DrawSurfaceMenuItems(SetupEntitySelection selection, Setup setup, Surface surface, bool includeDelete)
    {
        if (CustomComponents.DrawMenuItem(4, "Add sub-region"))
            AddSubRegion(selection, setup, surface);

        if (CustomComponents.DrawMenuItem(5, "Duplicate"))
            DuplicateSurface(selection, setup, surface);

        // Only meaningful once something is shown here — there's no aspect to match otherwise.
        if (surface.SliceId != Guid.Empty && CustomComponents.DrawMenuItem(9, "Adjust aspect to slice"))
            MatchSurfaceToSliceAspect(setup, surface);

        if (CustomComponents.DrawMenuItem(6, "Clear content inputs"))
            ClearContentInputs(surface.Id);

        if (includeDelete && CustomComponents.DrawMenuItem(7, "Delete"))
            DeleteSurface(setup, surface.Id);
    }

    /// <summary>
    /// Copies a surface — with its sub-regions — offset a little so it doesn't hide under the original. The
    /// copy gets fresh GUIDs, so content sends still point at the original; the duplicate starts unbound.
    /// </summary>
    internal static void DuplicateSurface(SetupEntitySelection selection, Setup setup, Surface surface)
    {
        var copy = CloneSurface(surface);
        var isChild = surface.ParentId != Guid.Empty;
        copy.Name = isChild ? $"Sub region {CountChildren(setup, surface.ParentId) + 1}" : surface.Name + " copy";

        if (isChild)
        {
            copy.LocalPosition = surface.LocalPosition + new Vector2(surface.SizeInMeters.X * 0.15f,
                                                                    -surface.SizeInMeters.Y * 0.15f);
        }
        else
        {
            // A root carries its own pins, so nudge those instead.
            foreach (var mapping in copy.OutputMappings)
            {
                for (var i = 0; i < mapping.Quad.Length; i++)
                    mapping.Quad[i] += new Vector2(24, 24);
            }
        }

        setup.Surfaces.Add(copy);
        DuplicateChildrenOf(setup, surface.Id, copy.Id);

        selection.Select(SetupEntitySelection.EntityKind.Surface, copy.Id);
        OutputSetupHandling.SaveActive();
    }

    private static void DuplicateChildrenOf(Setup setup, Guid sourceParentId, Guid newParentId)
    {
        // Snapshot first: the loop appends to the same list it walks.
        var originals = setup.Surfaces.FindAll(s => s.ParentId == sourceParentId);
        foreach (var original in originals)
        {
            var copy = CloneSurface(original);
            copy.ParentId = newParentId;
            setup.Surfaces.Add(copy);
            DuplicateChildrenOf(setup, original.Id, copy.Id);
        }
    }

    private static Surface CloneSurface(Surface source)
    {
        var copy = new Surface
                       {
                           Name = source.Name,
                           Type = source.Type,
                           Kind = source.Kind,
                           ParentId = source.ParentId,
                           ShortName = string.Empty, // auto-abbreviated, so two surfaces don't share a gutter label
                           Render = source.Render,
                           SizeInMeters = source.SizeInMeters,
                           LocalPosition = source.LocalPosition,
                           PixelsPerMeter = source.PixelsPerMeter,
                           ShowGrid = source.ShowGrid,
                           GridSubdivisions = source.GridSubdivisions,
                       };

        foreach (var mapping in source.OutputMappings)
        {
            copy.OutputMappings.Add(new Surface.OutputMapping
                                        {
                                            OutputId = mapping.OutputId,
                                            Mode = mapping.Mode,
                                            Quad = (Vector2[])mapping.Quad.Clone(),
                                        });
        }

        if (source.Placement != null)
            copy.Placement = new Surface.StagePlacement { Pose = source.Placement.Pose, Pivot = source.Placement.Pivot };

        return copy;
    }

    /// <summary>
    /// Drops this surface from every send that targets it, so it stops receiving content. The surface itself
    /// and its calibration are untouched — this only edits the sends' target lists (op-side, like the drag).
    /// </summary>
    private static void ClearContentInputs(Guid surfaceId)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var surface = setup.Surfaces.Find(s => s.Id == surfaceId);
        if (surface == null || surface.SliceId == Guid.Empty)
            return;

        surface.SliceId = Guid.Empty;
        OutputSetupHandling.SaveActive();
    }

    /// <summary>
    /// Adds a Layout child — a rectangle living inside its parent, riding the parent's corner pin rather than
    /// carrying one of its own. Its position is stored in meters from the parent's anchor, so it stays welded
    /// to the meter raster when the parent is cropped or stretched.
    /// </summary>
    private static void AddSubRegion(SetupEntitySelection selection, Setup setup, Surface parent)
    {
        var parentSize = parent.SizeInMeters;
        var size = new Vector2(MathF.Max(parentSize.X * 0.3f, SurfaceGeometry.MinSize),
                               MathF.Max(parentSize.Y * 0.3f, SurfaceGeometry.MinSize));

        // Land inside the parent rather than at its anchor: cropping an edge past the anchor legitimately
        // pushes the pivot outside [0..1], and a child sitting on it would then start outside the parent —
        // where extrapolating through a keystoned projection sends it a very long way off.
        var anchor = SurfaceGeometry.AnchorInSurface(parent);
        var bottomLeft = new Vector2(parentSize.X * 0.1f, parentSize.Y * 0.9f); // surface space runs Y down

        var child = new Surface
                        {
                            Name = $"Sub region {CountChildren(setup, parent.Id) + 1}",
                            Kind = Surface.SurfaceKinds.Layout,
                            ParentId = parent.Id,
                            SizeInMeters = size,
                            LocalPosition = new Vector2(bottomLeft.X - anchor.X, anchor.Y - bottomLeft.Y),
                            PixelsPerMeter = parent.PixelsPerMeter,
                        };

        setup.Surfaces.Add(child);
        selection.Select(SetupEntitySelection.EntityKind.Surface, child.Id);
        OutputSetupHandling.SaveActive();
    }

    private static int CountChildren(Setup setup, Guid parentId)
    {
        var count = 0;
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == parentId)
                count++;
        }

        return count;
    }

    private static void AddSurface(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var surface = new Surface { Name = $"Surface {setup.Surfaces.Count + 1}" };
        setup.Surfaces.Add(surface);
        selection.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
    }

    /// <summary>
    /// How many of the selected entities this panel can actually delete. A content source is a graph op and a
    /// slice's source may be gone, so the menu counts what will really go rather than how many rows are lit.
    /// </summary>
    private static int CountDeletable(SetupEntitySelection selection)
    {
        var count = 0;
        for (var i = 0; i < selection.Targets.Count; i++)
        {
            if (IsDeletable(selection.Targets[i].Kind))
                count++;
        }

        return count;
    }

    private static bool IsDeletable(SetupEntitySelection.EntityKind kind)
    {
        return kind is SetupEntitySelection.EntityKind.Surface
                    or SetupEntitySelection.EntityKind.Slice
                    or SetupEntitySelection.EntityKind.Output
                    or SetupEntitySelection.EntityKind.ReferenceImage
                    or SetupEntitySelection.EntityKind.Prop;
    }

    /// <summary>
    /// Deletes everything deletable in the selection. Each kind keeps its own cascade (a surface re-parents
    /// its children, an output drops the mappings onto it), so deleting a set is just deleting each in turn —
    /// which is why the targets are copied first: those cascades mutate the setup underneath us.
    /// </summary>
    private static void DeleteSelection(SetupEntitySelection selection, Setup setup)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
            return;

        _deleteBuffer.Clear();
        _deleteBuffer.AddRange(selection.Targets);

        foreach (var target in _deleteBuffer)
        {
            var id = target.EntityId;
            switch (target.Kind)
            {
                case SetupEntitySelection.EntityKind.Surface:
                    DeleteSurface(setup, id);
                    break;

                case SetupEntitySelection.EntityKind.Slice:
                    DeleteSlice(setup, id);
                    break;

                case SetupEntitySelection.EntityKind.Output:
                    DeleteOutput(setup, machineConfig, id);
                    break;

                case SetupEntitySelection.EntityKind.ReferenceImage:
                    setup.ReferenceImages.RemoveAll(r => r.Id == id);
                    break;

                case SetupEntitySelection.EntityKind.Prop:
                    setup.Props.RemoveAll(r => r.Id == id);
                    break;
            }
        }

        selection.Clear();
        OutputSetupHandling.SaveActive();
    }

    private static void DeleteSurface(Setup setup, Guid surfaceId)
    {
        // Re-parent orphaned children to the deleted surface's parent so the tree stays connected.
        var parentId = setup.Surfaces.Find(s => s.Id == surfaceId)?.ParentId ?? Guid.Empty;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.ParentId == surfaceId)
                surface.ParentId = parentId;
        }

        setup.Surfaces.RemoveAll(s => s.Id == surfaceId);

        OutputSetupHandling.SaveActive();
    }

    private static void DrawOutputBindingSubMenu(OutputDefinition output, MachineConfig machineConfig)
    {
        if (CustomComponents.DrawSubMenu(3, "Bind to display"))
        {
            ResolutionHandling.DrawBindingMenuItems(output, machineConfig);
            ImGui.EndMenu();
        }
    }

    // Deleting an output cascades: drop every surface's mapping onto it, unbind the display, and stop
    // presenting it. Surfaces left without a mapping simply have no output — not lost.
    private static void DeleteOutput(Setup setup, MachineConfig machineConfig, Guid outputId)
    {
        setup.Outputs.RemoveAll(o => o.Id == outputId);
        foreach (var surface in setup.Surfaces)
            surface.OutputMappings.RemoveAll(m => m.OutputId == outputId);

        machineConfig.Unbind(outputId);
        if (OutputManager.PresentedOutputId == outputId)
            OutputManager.PresentedOutputId = Guid.Empty;

        OutputSetupHandling.SaveActive();
    }

    private static void AddProp(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var prop = new Prop();
        setup.Props.Add(prop);
        selection.Select(SetupEntitySelection.EntityKind.Prop, prop.Id);
    }

    private static void AddOutput(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var output = new OutputDefinition
                         {
                             Name = $"P{CountProjectorOutputs(setup) + 1}",
                             Kind = OutputDefinition.Kinds.Projector,
                             CanvasResolution = new T3.Core.DataTypes.Vector.Int2(1920, 1200),
                         };
        setup.Outputs.Add(output);
        selection.Select(SetupEntitySelection.EntityKind.Output, output.Id);
    }

    private static int CountProjectorOutputs(Setup setup)
    {
        var count = 0;
        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Projector)
                count++;
        }

        return count;
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
    private static readonly List<SelectionTarget> _deleteBuffer = [];
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
    private static readonly List<(SetupEntitySelection.EntityKind kind, Guid id)> _referenced = [];
}
