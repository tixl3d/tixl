#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.InputUi.ListInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Draws the selected setup entity's properties in the Parameter window — the successor of the setup
/// sidebar's properties footer. It draws only while the entity selection owns the inspection
/// (<see cref="GlobalSelectionHandling"/>); a graph pick takes the window back. Cards use the standard
/// <see cref="FormInputs"/> conventions; continuous fields commit gesture-scoped snapshot undo steps.
/// </summary>
internal static class SetupParameterView
{
    /// <summary>
    /// Draws the primary entity's card and returns true while the entity selection owns the inspection.
    /// Returns false — and lets the inspection go — when nothing resolves anymore (entity deleted, setup gone).
    /// </summary>
    public static bool TryDraw()
    {
        if (GlobalSelectionHandling.InspectionTarget != GlobalSelectionHandling.InspectionTargets.SetupEntity)
            return false;

        var selection = OutputSetupHandling.EntitySelection;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig)
            || !selection.TryResolve(setup, out var kind, out var id))
        {
            GlobalSelectionHandling.ReleaseInspection(GlobalSelectionHandling.InspectionTargets.SetupEntity);
            return false;
        }

        FormInputs.SetIndentToParameters();
        FormInputs.AddVerticalSpace(5);
        DrawHeader(setup, kind, id);

        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
                DrawSurfaceCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.Output:
                DrawOutputCard(setup, machineConfig, id);
                break;
            case SetupEntitySelection.EntityKind.ContentSource:
                DrawContentCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.Slice:
                DrawSliceCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.ReferenceImage:
                DrawReferenceImageCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.Prop:
                DrawPropCard(setup, id);
                break;
            case SetupEntitySelection.EntityKind.Patch:
                DrawPatchCard(setup, id);
                break;
        }

        if (selection.Count > 1)
        {
            FormInputs.AddVerticalSpace(8);
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText($"+{selection.Count - 1} more selected", Fonts.FontSmall, UiColors.TextMuted);
        }

        return true;
    }

    /// <summary>
    /// The setup side of a selected SendToOutput op, appended below its parameters: the content resolution
    /// and where its slices go. This is the one place op parameters and setup properties share a screen —
    /// deliberately, they describe the same thing.
    /// </summary>
    public static void DrawSendExtras(Instance instance)
    {
        if (instance is not IOutputSink sink)
            return;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        FormInputs.SetIndentToParameters();
        FormInputs.AddSectionSubHeader("Output Setup");

        _sendContext ??= new EvaluationContext();
        _sendContext.Reset();

        // Reset() leaves RequestedResolution at 0×0; pulling the content preview at that size makes the
        // graph's auto-sized RenderTargets bail ("invalid texture size") and stop updating. Preview at
        // the resolution the content would render at when bound.
        _sendContext.RequestedResolution = ContentPreviewResolution(setup);

        Span<int> resolution = [1, 1];
        var content = sink.GetContent(_sendContext);
        if (content is { IsDisposed: false })
        {
            resolution[0] = content.Description.Width;
            resolution[1] = content.Description.Height;
        }

        DrawIntsRow("Resolution (px)", resolution, "Comes from the source texture (read-only).", readOnly: true);

        var source = setup.FindSourceByChildId(instance.SymbolChildId);
        if (source != null)
        {
            var slices = SetupRelations.CountSlicesOfSource(setup, source.Id);
            var consumers = SetupRelations.CountConsumersOfSource(setup, source.Id);
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText(consumers == 0
                                              ? $"{slices} slice{(slices == 1 ? "" : "s")}, nothing shows them yet"
                                              : $"{slices} slice{(slices == 1 ? "" : "s")} → {consumers} target{(consumers == 1 ? "" : "s")}",
                                          Fonts.FontSmall, UiColors.TextMuted);
        }
    }

    /// <summary>Installs the Guid-list parameter hooks so SendToOutput.TargetIds shows target names and a
    /// surface/output picker in the op parameter window. Called from UI registration at startup.</summary>
    public static void RegisterGuidListHooks()
    {
        GuidListLabels.Resolver = ResolveTargetLabel;
        GuidListLabels.Picker = PickTarget;
    }

    private static void DrawHeader(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        var (icon, kindLabel) = kind switch
                                    {
                                        SetupEntitySelection.EntityKind.Surface when IsRegion(setup, id) => (Icon.Grid, "Region"),
                                        SetupEntitySelection.EntityKind.Surface => (Icon.Grid, "Surface"),
                                        SetupEntitySelection.EntityKind.Output => (Icon.Projector, "Output"),
                                        SetupEntitySelection.EntityKind.Slice => (Icon.Slice, "Slice"),
                                        SetupEntitySelection.EntityKind.ContentSource => (Icon.FileImage, "Content"),
                                        SetupEntitySelection.EntityKind.ReferenceImage => (Icon.FileImage, "Reference Image"),
                                        SetupEntitySelection.EntityKind.Prop => (Icon.Grid, "Prop"),
                                        SetupEntitySelection.EntityKind.Patch => (Icon.Patch, "Patch"),
                                        _ => (Icon.Grid, kind.ToString()),
                                    };

        FormInputs.ApplyIndent();
        Icons.DrawInlineGlyph(icon, UiColors.TextMuted.Rgba);
        ImGui.SameLine(0, 6 * T3Ui.UiScaleFactor);
        CustomComponents.StylizedText(kindLabel, Fonts.FontLarge, UiColors.Text);
        FormInputs.AddVerticalSpace(4);

        // Props carry no name; a content source's name is its op (rename cascades through the sync).
        if (kind != SetupEntitySelection.EntityKind.Prop)
            DrawNameField(setup, kind, id);
    }

    /// <summary>Editable name, committed as one undoable rename when the field loses focus.</summary>
    private static void DrawNameField(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        var currentName = SetupActions.NameForEntity(kind, id);
        if (_renameTargetId != id)
        {
            _renameTargetId = id;
            _renameBuffer = currentName;
        }
        else if (!_renameFieldActive)
        {
            _renameBuffer = currentName; // follow external renames while not editing
        }

        FormInputs.DrawInputLabel("Name");
        ImGui.SetNextItemWidth(FormInputs.GetAvailableInputSize(null, false, fillWidth: true, maxWidth: FormInputs.MaxNumberInputWidth).X);
        ImGui.InputText("##entityName", ref _renameBuffer, 256);
        _renameFieldActive = ImGui.IsItemActive();
        if (ImGui.IsItemDeactivatedAfterEdit() && !string.IsNullOrWhiteSpace(_renameBuffer) && _renameBuffer.Trim() != currentName)
            SetupActions.RenameEntity(setup, kind, id, _renameBuffer.Trim());
    }

    private static void DrawSurfaceCard(Setup setup, Guid id)
    {
        var surface = setup.FindSurface(id);
        if (surface == null)
            return;

        var render = surface.Render;
        if (FormInputs.AddCheckBox("Render", ref render, "Skip drawing this surface without removing it."))
            SetupActions.RunUndoable("Toggle render", setup, () => surface.Render = render);

        var position = surface.Placement?.Pose.Position ?? Vector3.Zero;
        Span<float> pos = [position.X, position.Y, position.Z];
        var posState = DrawFloatsRow("Position (m)", pos);
        BeginFieldUndo(setup, posState);
        if ((posState & InputEditStateFlags.Modified) != 0)
        {
            var placement = surface.Placement ??= new Surface.StagePlacement();
            placement.Pose = new Pose(new Vector3(pos[0], pos[1], pos[2]), placement.Pose.Orientation);
        }

        CommitFieldUndo(setup, "Move surface", posState);

        // A Layout child inherits its parent's plane, so it's placed in the parent's local space instead of the stage.
        if (surface.Kind == Surface.SurfaceKinds.Layout)
        {
            Span<float> local = [surface.LocalPosition.X, surface.LocalPosition.Y];
            var localState = DrawFloatsRow("Position in parent (m)", local,
                                           "Bottom-left corner, in metres from the parent's anchor (X right, Y up).");
            BeginFieldUndo(setup, localState);
            if ((localState & InputEditStateFlags.Modified) != 0)
                surface.LocalPosition = new Vector2(local[0], local[1]);

            CommitFieldUndo(setup, "Move region", localState);
        }

        Span<float> size = [surface.SizeInMeters.X, surface.SizeInMeters.Y];
        var sizeState = DrawFloatsRow("Size (m)", size,
                                      "Resizes the surface's footprint — the corner pin follows, so it covers a different area of the wall.",
                                      reserveRight: 44);

        // Locking keeps the current width/height ratio while resizing — the edited axis drives, the other follows.
        ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
        if (CustomComponents.IconButton(Icon.Link, Vector2.Zero,
                                        surface.LockAspect ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
        {
            SetupActions.RunUndoable("Lock aspect", setup, () => surface.LockAspect = !surface.LockAspect);
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
            OutputSetupHandling.SaveActive();
            _resizeOldState = null;
        }

        var showGrid = surface.ShowGrid;
        if (FormInputs.AddCheckBox("Show size raster", ref showGrid,
                                   "Projects a real-world grid (no content needed) so you can hand-align the corner-pin to physical wall features."))
        {
            SetupActions.RunUndoable("Toggle raster", setup, () => surface.ShowGrid = showGrid);
        }

        if (surface.ShowGrid)
        {
            Span<int> subdivisions = [surface.GridSubdivisions];
            var gridCellState = DrawIntsRow("Subdivisions / m", subdivisions,
                                            "Minor lines per metre; 1 draws metre lines only. They fade out once too dense to resolve.");
            BeginFieldUndo(setup, gridCellState);
            if ((gridCellState & InputEditStateFlags.Modified) != 0)
                surface.GridSubdivisions = Math.Clamp(subdivisions[0], 1, 100);

            CommitFieldUndo(setup, "Change raster", gridCellState);
        }

        Span<float> anchor = [surface.Anchor.X, surface.Anchor.Y];
        var anchorState = DrawFloatsRow("Anchor (-1..1)", anchor,
                                        "Origin of the metre raster and of child regions: (0,0) is the centre, (0,-1) the bottom-centre, (±1,±1) the corners.");
        BeginFieldUndo(setup, anchorState);
        if ((anchorState & InputEditStateFlags.Modified) != 0)
            surface.Anchor = new Vector2(anchor[0], anchor[1]);

        CommitFieldUndo(setup, "Move anchor", anchorState);
    }

    private static void DrawOutputCard(Setup setup, MachineConfig machineConfig, Guid id)
    {
        var output = setup.FindOutput(id);
        if (output == null)
            return;

        var send = output.Send;
        if (FormInputs.AddCheckBox("Send", ref send, "Pause presenting without dropping the display binding."))
            SetupActions.RunUndoable("Toggle send", setup, () => output.Send = send);

        var binding = machineConfig.TryGetBinding(output.Id);
        FormInputs.ApplyIndent();
        CustomComponents.StylizedText(binding == null
                                          ? $"{output.CanvasResolution.Width}×{output.CanvasResolution.Height} px · unbound"
                                          : $"{output.CanvasResolution.Width}×{output.CanvasResolution.Height} px · Display {binding.DisplayIndex + 1}",
                                      Fonts.FontSmall, UiColors.TextMuted);
    }

    private static void DrawContentCard(Setup setup, Guid childId)
    {
        var instance = SetupActions.FindSendInstance(childId);
        if (instance is not IOutputSink sink)
            return;

        _sendContext ??= new EvaluationContext();
        _sendContext.Reset();
        _sendContext.RequestedResolution = ContentPreviewResolution(setup);

        var update = sink.GetUpdateEnabled(_sendContext);
        if (FormInputs.AddCheckBox("Update", ref update, "When off, freezes this content at its last frame."))
            sink.SetUpdateEnabled(update);

        Span<int> resolution = [1, 1];
        var content = sink.GetContent(_sendContext);
        if (content is { IsDisposed: false })
        {
            resolution[0] = content.Description.Width;
            resolution[1] = content.Description.Height;
        }

        DrawIntsRow("Resolution (px)", resolution, "Comes from the source texture (read-only).", readOnly: true);
    }

    private static void DrawSliceCard(Setup setup, Guid id)
    {
        var slice = setup.FindSlice(id);
        if (slice == null)
            return;

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
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText("Connect the source op to edit in pixels.", Fonts.FontSmall, UiColors.TextMuted);
            Span<float> posUv = [uv.X, uv.Y];
            DrawFloatsRow("Position (uv)", posUv, readOnly: true);
            Span<float> sizeUv = [uv.Z - uv.X, uv.W - uv.Y];
            DrawFloatsRow("Size (uv)", sizeUv, readOnly: true);
            return;
        }

        var widthUv = MathF.Max(uv.Z - uv.X, MinSliceSize);
        var heightUv = MathF.Max(uv.W - uv.Y, MinSliceSize);

        Span<int> position = [(int)MathF.Round(uv.X * texW), (int)MathF.Round(uv.Y * texH)];
        var positionState = DrawIntsRow("Position (px)", position);
        BeginFieldUndo(setup, positionState);
        if ((positionState & InputEditStateFlags.Modified) != 0)
        {
            var nx = Math.Clamp(position[0] / (float)texW, 0f, 1f - widthUv);
            var ny = Math.Clamp(position[1] / (float)texH, 0f, 1f - heightUv);
            slice.UvRect = new Vector4(nx, ny, nx + widthUv, ny + heightUv);
        }

        CommitFieldUndo(setup, "Move slice", positionState);

        Span<int> size = [(int)MathF.Round(widthUv * texW), (int)MathF.Round(heightUv * texH)];
        var sizePxState = DrawIntsRow("Size (px)", size);
        BeginFieldUndo(setup, sizePxState);
        if ((sizePxState & InputEditStateFlags.Modified) != 0)
        {
            var nw = Math.Clamp(size[0] / (float)texW, MinSliceSize, 1f - uv.X);
            var nh = Math.Clamp(size[1] / (float)texH, MinSliceSize, 1f - uv.Y);
            slice.UvRect = new Vector4(uv.X, uv.Y, uv.X + nw, uv.Y + nh);
        }

        CommitFieldUndo(setup, "Resize slice", sizePxState);
    }

    /// <summary>
    /// A patch is a rectangle of output pixels fed by one slice. Axis-aligned patches edit as position + size;
    /// a warped quad (surface-less keystone) is shown by its corners until the canvas editor lands.
    /// </summary>
    private static void DrawPatchCard(Setup setup, Guid id)
    {
        var patch = setup.FindPatch(id, out var output);
        if (patch == null || output == null)
            return;

        var slice = setup.FindSlice(patch.SliceId);
        FormInputs.ApplyIndent();
        CustomComponents.StylizedText(slice == null
                                          ? "Nothing routed yet — drop a slice or content onto this patch."
                                          : $"Shows {SetupActions.SliceLabel(setup, slice)} on {output.Name}",
                                      Fonts.FontSmall, UiColors.TextMuted);

        if (patch.Quad.Length < 4)
            return;

        var quad = patch.Quad;
        var isAxisAligned = MathF.Abs(quad[0].Y - quad[1].Y) < 0.001f && MathF.Abs(quad[2].Y - quad[3].Y) < 0.001f
                            && MathF.Abs(quad[0].X - quad[3].X) < 0.001f && MathF.Abs(quad[1].X - quad[2].X) < 0.001f;
        if (!isAxisAligned)
        {
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText("Warped quad — edit its corners on the output canvas.", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        Span<int> position = [(int)MathF.Round(quad[0].X), (int)MathF.Round(quad[0].Y)];
        var positionState = DrawIntsRow("Position (px)", position, "Top-left corner on the output canvas.");
        BeginFieldUndo(setup, positionState);
        if ((positionState & InputEditStateFlags.Modified) != 0)
        {
            var delta = new Vector2(position[0], position[1]) - quad[0];
            for (var i = 0; i < 4; i++)
                quad[i] += delta;
        }

        CommitFieldUndo(setup, "Move patch", positionState);

        Span<int> size = [(int)MathF.Round(quad[1].X - quad[0].X), (int)MathF.Round(quad[3].Y - quad[0].Y)];
        var sizeState = DrawIntsRow("Size (px)", size);
        BeginFieldUndo(setup, sizeState);
        if ((sizeState & InputEditStateFlags.Modified) != 0)
        {
            var w = MathF.Max(size[0], 1);
            var h = MathF.Max(size[1], 1);
            quad[1] = new Vector2(quad[0].X + w, quad[0].Y);
            quad[2] = new Vector2(quad[0].X + w, quad[0].Y + h);
            quad[3] = new Vector2(quad[0].X, quad[0].Y + h);
        }

        CommitFieldUndo(setup, "Resize patch", sizeState);
    }

    private static void DrawReferenceImageCard(Setup setup, Guid id)
    {
        var image = setup.FindReferenceImage(id);
        if (image == null)
            return;

        FormInputs.ApplyIndent();
        CustomComponents.StylizedText(string.IsNullOrEmpty(image.FilePath)
                                          ? "Drop a photo onto the reference canvas, or pick an asset."
                                          : $"{image.FilePath}  ({image.Width}×{image.Height})",
                                      Fonts.FontSmall, UiColors.TextMuted);
    }

    private static void DrawPropCard(Setup setup, Guid id)
    {
        var prop = setup.FindProp(id);
        if (prop == null)
            return;

        Span<float> height = [prop.HeightInMeters];
        var heightState = DrawFloatsRow("Height (m)", height);
        BeginFieldUndo(setup, heightState);
        if ((heightState & InputEditStateFlags.Modified) != 0)
            prop.HeightInMeters = MathF.Max(height[0], 0.1f);

        CommitFieldUndo(setup, "Resize prop", heightState);
    }

    /// <summary>
    /// Row of drag-edit float fields sharing one label — hairline gaps and a rounded frame so the
    /// components read as a single control. Width and label column follow <see cref="FormInputs"/>.
    /// </summary>
    private static InputEditStateFlags DrawFloatsRow(string label, Span<float> values, string? tooltip = null,
                                                     float speed = 0.01f, bool readOnly = false,
                                                     string format = "{0:0.###}", float reserveRight = 0)
    {
        var size = BeginValuesRow(label, tooltip, values.Length, readOnly, reserveRight, out var gap);
        var result = InputEditStateFlags.Nothing;
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, gap);

            ImGui.PushID(i);
            var v = values[i];
            result |= SingleValueEdit.Draw(ref v, size, scale: speed, format: format);
            values[i] = v;
            ImGui.PopID();
        }

        EndValuesRow(tooltip, readOnly);
        return result;
    }

    /// <summary>Integer counterpart of <see cref="DrawFloatsRow"/>.</summary>
    private static InputEditStateFlags DrawIntsRow(string label, Span<int> values, string? tooltip = null, bool readOnly = false)
    {
        var size = BeginValuesRow(label, tooltip, values.Length, readOnly, 0, out var gap);
        var result = InputEditStateFlags.Nothing;
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, gap);

            ImGui.PushID(i);
            var v = values[i];
            result |= SingleValueEdit.Draw(ref v, size);
            values[i] = v;
            ImGui.PopID();
        }

        EndValuesRow(tooltip, readOnly);
        return result;
    }

    /// <summary>Label, ID scope, frame style and the per-component size shared by the value rows; pair with <see cref="EndValuesRow"/>.</summary>
    private static Vector2 BeginValuesRow(string label, string? tooltip, int count, bool readOnly, float reserveRight, out float gap)
    {
        FormInputs.DrawInputLabel(label);
        ImGui.PushID(label);
        var scale = T3Ui.UiScaleFactor;
        gap = 1 * scale;

        // Capped like FormInputs' own number fields so setup cards line up with op parameters in the same window.
        var total = FormInputs.GetAvailableInputSize(tooltip, false, fillWidth: true, maxWidth: FormInputs.MaxNumberInputWidth).X
                    - reserveRight * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3 * scale);
        if (readOnly)
            ImGui.BeginDisabled();

        return new Vector2((total - gap * (count - 1)) / count, ImGui.GetFrameHeight());
    }

    private static void EndValuesRow(string? tooltip, bool readOnly)
    {
        if (readOnly)
            ImGui.EndDisabled();

        ImGui.PopStyleVar();
        ImGui.PopID();
        FormInputs.AppendTooltip(tooltip);
    }

    /// <summary>
    /// Snapshot-based undo for the card's continuous drag-fields. Call Begin right after the widget with its
    /// state flags and BEFORE applying the Modified value; call Commit after applying. The pre-edit setup is
    /// captured on the gesture's first event and committed as one undo step + a single save when the edit
    /// finishes — a whole drag (or typed entry) is one step, with no file writes while dragging.
    /// </summary>
    private static void BeginFieldUndo(Setup setup, InputEditStateFlags state)
    {
        if ((state & InputEditStateFlags.Started) != 0)
            _fieldEditOldJson = setup.ToJsonString();
        else if ((state & InputEditStateFlags.Modified) != 0)
            _fieldEditOldJson ??= setup.ToJsonString();
    }

    private static void CommitFieldUndo(Setup setup, string name, InputEditStateFlags state)
    {
        if ((state & InputEditStateFlags.Finished) == 0 || _fieldEditOldJson == null)
            return;

        var newJson = setup.ToJsonString();
        if (newJson != _fieldEditOldJson)
        {
            UndoRedoStack.Add(new SetupSnapshotCommand(name, setup.Id, _fieldEditOldJson, newJson));
            OutputSetupHandling.SaveActive();
        }

        _fieldEditOldJson = null;
    }

    /// <summary>
    /// "Set measured dimensions": states the surface's real size without touching its corner pins. Used after
    /// the rect is already aligned on the wall — you're correcting the measurement, not moving the projection.
    /// The declared size drives the calibration raster's density and the straighten hypothesis.
    /// </summary>
    private static void DrawMeasuredSizePopup(Surface surface)
    {
        ImGui.SetNextWindowSize(new Vector2(260 * T3Ui.UiScaleFactor, 0));
        if (!ImGui.BeginPopup(MeasuredSizePopupId))
            return;

        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted("Set measured dimensions");
        ImGui.PopFont();

        CustomComponents.StylizedText("The projection stays put — this only records\nhow big the surface really is.",
                                      Fonts.FontSmall, UiColors.TextMuted);

        Span<float> measured = [_measuredEdit.X, _measuredEdit.Y];
        DrawFloatsRow("Width × Height (m)", measured);
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

    private static bool IsRegion(Setup setup, Guid id)
    {
        // The two roles must never read alike: a plane-root is a Surface, a coplanar child is a Region.
        var surface = setup.FindSurface(id);
        return surface is { Kind: Surface.SurfaceKinds.Layout } && surface.ParentId != Guid.Empty;
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

    /// <summary>Smallest slice fraction — mirrors <c>SetupOutputView.MinSliceSize</c>.</summary>
    private const float MinSliceSize = 0.01f;

    private const string MeasuredSizePopupId = "##measuredSize";
    private static Vector2 _measuredEdit;

    // Pre-edit rectangle snapshot while a Size (m) field is being dragged, so the resize undoes as one step.
    private static ResizeSurfaceCommand.State? _resizeOldState;

    // Pre-edit setup snapshot while a card drag-field gesture is live (see BeginFieldUndo/CommitFieldUndo).
    private static string? _fieldEditOldJson;

    private static EvaluationContext? _sendContext;

    // Name-field editing state: buffer follows the entity until the field takes focus.
    private static Guid _renameTargetId;
    private static string _renameBuffer = string.Empty;
    private static bool _renameFieldActive;
}
