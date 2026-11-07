#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Core.Output.Streaming;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.InputUi.ListInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
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
    /** How a surface lies in the stage, as the Facing preset offers it. */
    private enum SurfaceFacings
    {
        Wall,
        Floor,
        Ceiling,
        Tilted,
    }

    /// <summary>
    /// Draws the primary entity's card and returns true while the entity selection owns the inspection.
    /// Returns false — and lets the inspection go — when nothing resolves anymore (entity deleted, setup gone).
    /// </summary>
    public static bool TryDraw()
    {
        if (GlobalSelectionHandling.InspectionTarget != GlobalSelectionHandling.InspectionTargets.SetupEntity)
            return false;

        var selection = GlobalSelectionHandling.SetupEntities;
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig)
            || !selection.TryResolve(setup, out var kind, out var id))
        {
            GlobalSelectionHandling.ReleaseInspection(GlobalSelectionHandling.InspectionTargets.SetupEntity);
            return false;
        }

        // The Parameter window disables its own scrolling, so long cards (a floor plan with many walls) need a child.
        ImGui.BeginChild("setupEntity", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        FormInputs.SetIndentToParameters();
        FormInputs.AddVerticalSpace(5);
        DrawHeader(setup, kind, id);

        switch (kind)
        {
            case SetupEntityKinds.Surface:
                DrawSurfaceCard(setup, id);
                break;
            case SetupEntityKinds.Output:
                DrawOutputCard(setup, machineConfig, id);
                break;
            case SetupEntityKinds.ContentSource:
                DrawContentCard(setup, id);
                break;
            case SetupEntityKinds.Slice:
                DrawSliceCard(setup, id);
                break;
            case SetupEntityKinds.ReferenceImage:
                DrawReferenceImageCard(setup, id);
                break;
            case SetupEntityKinds.Prop:
                DrawPropCard(setup, id);
                break;
            case SetupEntityKinds.FloorPlan:
                DrawFloorPlanCard(setup, id);
                break;
            case SetupEntityKinds.Patch:
                DrawPatchCard(setup, id);
                break;
            case SetupEntityKinds.Plug:
                DrawPlugCard(setup, machineConfig, id);
                break;
        }

        if (selection.Count > 1)
        {
            FormInputs.AddVerticalSpace(8);
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText($"+{selection.Count - 1} more selected", Fonts.FontSmall, UiColors.TextMuted);
        }

        ImGui.EndChild();
        return true;
    }

    /// <summary>
    /// The setup side of a selected SendToOutput op, appended below its parameters: the content resolution
    /// and where its slices go. This is the one place op parameters and setup properties share a screen —
    /// deliberately, they describe the same thing.
    /// </summary>
    public static void DrawSendExtras(Instance instance)
    {
        if (instance is not IContentSupplier)
            return;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        FormInputs.SetIndentToParameters();
        FormInputs.AddSectionSubHeader("Output Setup");

        // Through the shared resolver: the same context and size the composite pulls with, so this shows the
        // frame the output renders rather than evaluating the graph a second time at a size of its own.
        OutputContentResolver.TryGetPreviewContent(instance.SymbolChildId, out var content);

        Span<int> resolution = [0, 0]; // nothing connected: no size, rather than a made-up one
        if (content is { IsDisposed: false })
        {
            resolution[0] = content.Description.Width;
            resolution[1] = content.Description.Height;
        }

        DrawIntsRow("Rendered (px)", resolution,
                    "What this content actually rendered at: the canvas of the output it is routed to, unless the op's Resolution is set.",
                    readOnly: true);

        // Two outputs of different sizes fed by one send: it renders once, so the second shows the first's size.
        if (OutputContentStats.TryGetSizeConflict(instance.SymbolChildId, out var conflictRendered, out var conflictOther))
        {
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText($"⚠ also routed to an output of {conflictOther.Width}×{conflictOther.Height} — it shows {conflictRendered.Width}×{conflictRendered.Height}",
                                          Fonts.FontSmall, UiColors.StatusAttention);
        }

        var source = setup.FindSourceByChildId(instance.SymbolChildId);
        if (source != null)
        {
            var slices = SetupRelations.CountListedSlicesOfSource(setup, source.Id);
            var consumers = SetupRelations.CountConsumersOfSource(setup, source.Id);
            FormInputs.ApplyIndent();
            // The implicit full-frame slice reads as the source itself: "full frame", not "1 slice".
            var what = slices == 0 ? "full frame" : $"{slices} slice{(slices == 1 ? "" : "s")}";
            CustomComponents.StylizedText(consumers == 0
                                              ? $"{what}, nothing shows it yet"
                                              : $"{what} → {consumers} target{(consumers == 1 ? "" : "s")}",
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

    private static void DrawHeader(Setup setup, SetupEntityKinds kind, Guid id)
    {
        var kindInfo = SetupEntityKindInfo.Of(kind);
        // The two roles must never read alike: a plane-root is a Surface, a coplanar child is a Region.
        var kindLabel = kind == SetupEntityKinds.Surface && IsRegion(setup, id) ? "Region" : kindInfo.Label;

        CustomComponents.StylizedText(kindLabel, Fonts.FontLarge, UiColors.TextMuted);
        if (kind == SetupEntityKinds.Output)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - Icons.FontSize - InputArea.ValueEditRightMargin);
            FormInputs.DrawHelpMarker("A canvas that projectors, displays and streams receive. Surfaces and patches are placed on it; a local binding decides where it goes on this machine.");
        }

        FormInputs.AddVerticalSpace(4);

        // A display's name comes from the OS; a content source's is its op (rename cascades through the sync).
        var namedByOs = kind == SetupEntityKinds.Plug && !SetupActions.CanRenamePlug(id);
        if (kindInfo.CanRename && !namedByOs)
            DrawNameField(setup, kind, id);
    }

    /// <summary>Editable name, committed as one undoable rename when the field loses focus.</summary>
    private static void DrawNameField(Setup setup, SetupEntityKinds kind, Guid id)
    {
        var currentName = SetupLabels.NameForEntity(kind, id);

        // An output or patch without a name of its own shows its derived one as a placeholder; clearing the field returns to it.
        var hasDerivedName = TryGetStoredName(setup, kind, id, out var storedName);
        if (hasDerivedName)
            currentName = storedName;

        if (_renameTargetId != id)
        {
            // The card moved on while a name was being typed: that name belongs to the entity it was typed for.
            if (_renameFieldActive && _renameTargetId != Guid.Empty && SetupEntities.Exists(setup, _renameTargetKind, _renameTargetId))
            {
                var typed = _renameBuffer.Trim();
                if (!string.IsNullOrWhiteSpace(typed) && typed != SetupLabels.NameForEntity(_renameTargetKind, _renameTargetId))
                    SetupActions.RenameEntity(setup, _renameTargetKind, _renameTargetId, typed);
            }

            _renameTargetId = id;
            _renameTargetKind = kind;
            _renameBuffer = currentName;
            _renameFieldActive = false;
        }
        else if (!_renameFieldActive)
        {
            _renameBuffer = currentName; // follow external renames while not editing
        }

        // The field is the entity's own item, so switching entities can never hand one's text to another.
        FormInputs.DrawInputLabel("Name");
        ImGui.PushID(id.GetHashCode());
        ImGui.SetNextItemWidth(FormInputs.GetAvailableInputSize(null, false, fillWidth: true, maxWidth: FormInputs.MaxNumberInputWidth).X);
        if (hasDerivedName)
        {
            ImGui.InputTextWithHint("##entityName", SetupLabels.NameForEntity(kind, id), ref _renameBuffer, 256);
        }
        else
        {
            ImGui.InputText("##entityName", ref _renameBuffer, 256);
        }

        _renameFieldActive = ImGui.IsItemActive();
        if (ImGui.IsItemDeactivatedAfterEdit() && (hasDerivedName || !string.IsNullOrWhiteSpace(_renameBuffer)) && _renameBuffer.Trim() != currentName)
            SetupActions.RenameEntity(setup, kind, id, _renameBuffer.Trim());

        ImGui.PopID();
    }

    /** The name as stored, possibly empty, for the kinds that derive a label when they have none. */
    private static bool TryGetStoredName(Setup setup, SetupEntityKinds kind, Guid id, out string storedName)
    {
        storedName = kind switch
                         {
                             SetupEntityKinds.Output => setup.FindOutput(id)?.Name,
                             SetupEntityKinds.Patch  => setup.FindPatch(id, out _)?.Name,
                             _                       => null,
                         } ?? string.Empty;
        return kind is SetupEntityKinds.Output or SetupEntityKinds.Patch;
    }

    private static void DrawSurfaceCard(Setup setup, Guid id)
    {
        var surface = setup.FindSurface(id);
        if (surface == null)
            return;

        var render = surface.IsRendered;
        if (FormInputs.AddCheckBox("Render", ref render, "Skip drawing this surface without removing it."))
            SetupUndo.RunUndoable("Toggle render", setup, () => surface.IsRendered = render);

        var plan = setup.FindFloorPlanOf(surface.Id, out var planSegment);
        if (plan != null)
        {
            FormInputs.AddHint(planSegment < 0
                                   ? $"Floor of {plan.Name} — its size and place follow the plan's floor rectangle."
                                   : $"Wall on edge {planSegment + 1} of {plan.Name} — its place follows the plan, and its width and the edge's length follow each other.");
        }

        var position = surface.Placement?.Pose.Position ?? Vector3.Zero;
        Span<float> pos = [position.X, position.Y, position.Z];
        var posState = DrawFloatsRow("Position (m)", pos, readOnly: plan != null);
        BeginFieldUndo(setup, posState);
        if ((posState & InputEditStateFlags.Modified) != 0)
        {
            var placement = surface.Placement ??= new Surface.StagePlacement();
            placement.Pose = new Pose(new Vector3(pos[0], pos[1], pos[2]), placement.Pose.Orientation);
        }

        CommitFieldUndo(setup, "Move surface", posState);

        if (plan != null && planSegment >= 0)
        {
            Span<float> elevation = [plan.ElevationOf(surface.Id)];
            var elevationState = DrawFloatsRow("Elevation (m)", elevation,
                                               "How far the wall's bottom edge is lifted above the ground, for a wall the projection only covers from some height up. The wall keeps its height and rises as a whole.");
            BeginFieldUndo(setup, elevationState);
            if ((elevationState & InputEditStateFlags.Modified) != 0)
                FloorPlanSync.SetWallElevation(setup, plan, surface.Id, MathF.Max(elevation[0], 0));

            CommitFieldUndo(setup, "Change wall elevation", elevationState);
        }

        if (surface.Kind == Surface.Kinds.Physical)
            DrawStageOrientationRows(setup, surface, readOnly: plan != null);

        // A Layout child inherits its parent's plane, so it's placed in the parent's local space instead of the stage.
        if (surface.Kind == Surface.Kinds.Layout)
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
                                      "How big the surface really is. Lines and regions scale with it; the projection and the trace stay where they are.",
                                      reserveRight: 44);

        // Locking keeps the current width/height ratio while resizing — the edited axis drives, the other follows.
        ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
        if (CustomComponents.IconButton(Icon.Link, Vector2.Zero,
                                        surface.IsAspectLocked ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
        {
            SetupUndo.RunUndoable("Lock aspect", setup, () => surface.IsAspectLocked = !surface.IsAspectLocked);
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
        DrawMeasuredSizePopup(setup, surface);

        // A re-metering: lines and regions scale along, nothing moves on the wall. One undo step per gesture.
        BeginFieldUndo(setup, sizeState);
        if ((sizeState & InputEditStateFlags.Modified) != 0)
        {
            var typed = ConstrainSize(surface.SizeInMeters, new Vector2(size[0], size[1]), surface.IsAspectLocked);
            if (plan != null && planSegment >= 0)
            {
                // A wall's width is its segment: the typed number goes to the plan, which hands it back.
                SurfaceMetrics.RemeterSurface(setup, surface, new Vector2(surface.SizeInMeters.X, typed.Y));
                FloorPlanSync.SetSegmentLength(setup, plan, planSegment, typed.X);
            }
            else if (plan != null)
            {
                // The floor's size is its plan's floor rectangle.
                FloorPlanSync.SetFloorSize(setup, plan, typed);
            }
            else
            {
                SurfaceMetrics.RemeterSurface(setup, surface, typed);
            }
        }

        CommitFieldUndo(setup, "Resize surface", sizeState);

        var showGrid = surface.ShowGrid;
        if (FormInputs.AddCheckBox("Show size raster", ref showGrid,
                                   "Projects a real-world grid (no content needed) so you can hand-align the corner-pin to physical wall features. Only while the editor runs: an exported show never projects it."))
        {
            SetupUndo.RunUndoable("Toggle raster", setup, () => surface.ShowGrid = showGrid);
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

        var showMeasuringLines = surface.ShowMeasuringLines;
        if (FormInputs.AddCheckBox("Show measuring lines", ref showMeasuringLines,
                                   "Projects the surface's measuring lines onto the wall, so a line can be nudged until it lies along the real feature it was drawn on. Align Points shows them too while it is on."))
        {
            SetupUndo.RunUndoable("Toggle measuring lines", setup, () => surface.ShowMeasuringLines = showMeasuringLines);
        }

        DrawMappingModeRows(setup, surface);

        Span<float> anchor = [surface.Anchor.X, surface.Anchor.Y];
        var anchorState = DrawFloatsRow("Anchor (-1..1)", anchor,
                                        "Origin of the metre raster and of child regions: (0,0) is the centre, (0,-1) the bottom-centre, (±1,±1) the corners.");
        BeginFieldUndo(setup, anchorState);
        if ((anchorState & InputEditStateFlags.Modified) != 0)
            surface.Anchor = new Vector2(anchor[0], anchor[1]);

        CommitFieldUndo(setup, "Move anchor", anchorState);
    }

    /// <summary>
    /// One row per output this surface is shown on: whether it takes the whole canvas or sits on corners you
    /// place. A fill ignores every later resize of the surface, which is the point of it on a display; switching
    /// to a corner pin hands over the quad it filled, so nothing moves until a corner is dragged.
    /// </summary>
    private static void DrawMappingModeRows(Setup setup, Surface surface)
    {
        for (var i = 0; i < surface.OutputMappings.Count; i++)
        {
            var mapping = surface.OutputMappings[i];
            var output = setup.FindOutput(mapping.OutputId);
            if (output == null)
                continue;

            var mode = mapping.Mode;
            ImGui.PushID(mapping.OutputId.GetHashCode());
            if (FormInputs.AddSegmentedButtonWithLabel(ref mode, $"On {SetupLabels.OutputLabel(output)}",
                                                       "Fill takes the whole canvas and stays there however the surface is resized. "
                                                       + "Corner pin is aimed: you drag its corners onto the wall, and reshaping the surface on the wall moves it along."))
            {
                SetupUndo.RunUndoable("Change mapping mode", setup, () =>
                                                                    {
                                                                        if (mode == MappingModes.Fill)
                                                                            mapping.FillCanvas();
                                                                        else
                                                                            mapping.PromoteToCornerPin();
                                                                    });
            }

            ImGui.PopID();
        }
    }

    /// <summary>How a physical surface is turned in the stage: yaw, pitch and roll, for reading and fine-tuning a free surface.</summary>
    private static void DrawStageOrientationRows(Setup setup, Surface surface, bool readOnly)
    {
        var orientation = surface.Placement?.Pose.Orientation ?? System.Numerics.Quaternion.Identity;
        var degrees = StagePlacing.ToYawPitchRollDegrees(orientation);

        // The common cases as one click: only the pitch changes, so a wall keeps the way it was turned.
        if (!readOnly)
        {
            var facing = FacingOf(degrees.Y);
            if (FormInputs.AddSegmentedButtonWithLabel(ref facing, "Facing",
                                                       "Whether the surface stands upright, lies on the ground or hangs from the ceiling. "
                                                       + "Picking one sets the pitch and keeps the turn and position; Tilted is any other pitch.")
                && facing != SurfaceFacings.Tilted)
            {
                var pitch = facing switch
                                {
                                    SurfaceFacings.Floor => -90f,
                                    SurfaceFacings.Ceiling => 90f,
                                    _ => 0f,
                                };
                SetupUndo.RunUndoable($"Face {facing}", setup, () =>
                                                              {
                                                                  var placement = surface.Placement ??= new Surface.StagePlacement();
                                                                  placement.Pose = new Pose(placement.Pose.Position,
                                                                                            StagePlacing.FromYawPitchRollDegrees(new System.Numerics.Vector3(degrees.X, pitch, degrees.Z)));
                                                              });
                degrees = StagePlacing.ToYawPitchRollDegrees(surface.Placement!.Pose.Orientation);
            }
        }
        Span<float> rotation = [degrees.X, degrees.Y, degrees.Z];
        var rotationState = DrawFloatsRow("Rotation (°)", rotation,
                                          "Yaw, pitch and roll in the stage. An unturned surface stands upright facing the viewer; a floor is pitched -90.",
                                          speed: 0.5f, readOnly: readOnly, format: "{0:0.#}");
        BeginFieldUndo(setup, rotationState);
        if ((rotationState & InputEditStateFlags.Modified) != 0)
        {
            var placement = surface.Placement ??= new Surface.StagePlacement();
            placement.Pose = new Pose(placement.Pose.Position,
                                      StagePlacing.FromYawPitchRollDegrees(new System.Numerics.Vector3(rotation[0], rotation[1], rotation[2])));
        }

        CommitFieldUndo(setup, "Turn surface", rotationState);
    }

    /// <summary>
    /// A floor plan's card: whether it closes into a room and carries a floor, the height new walls get, and one
    /// row per edge with its length and a wall toggle — the venue's spec sheet, typed in as printed.
    /// </summary>
    private static void DrawFloorPlanCard(Setup setup, Guid id)
    {
        var plan = setup.FindFloorPlan(id);
        if (plan == null)
            return;

        plan.TryGetBounds(out var min, out var max);
        FormInputs.AddHint($"{max.X - min.X:0.##}×{max.Y - min.Y:0.##} m · drag its corners on the Board");

        var closed = plan.IsClosed;
        if (FormInputs.AddCheckBox("Closed", ref closed, "The last corner joins the first: a room, which can carry a floor."))
        {
            SetupUndo.RunUndoable("Toggle plan closed", setup, () =>
                                                              {
                                                                  plan.IsClosed = closed;
                                                                  plan.EnsureWallSlots();
                                                                  FloorPlanSync.Apply(setup, plan);
                                                              });
        }

        if (plan.IsClosed)
        {
            var hasFloor = setup.FindSurface(plan.RaisedFloorId) != null;
            if (FormInputs.AddCheckBox("Floor surface", ref hasFloor,
                                       "A surface lying on the footprint. Unticking removes it, unless content, a projection, regions or a patch of its name were put on it — then it is kept, free of the plan, and ticking again brings it back."))
                SetupUndo.RunUndoable(hasFloor ? "Add floor" : "Take up floor", setup, () => FloorPlanSync.SetFloor(setup, plan, hasFloor));

            if (hasFloor)
                DrawFloorRectRows(setup, plan);
        }

        Span<float> height = [plan.WallHeight];
        var heightState = DrawFloatsRow("Wall height (m)", height,
                                        "The height of this plan's walls. A wall given a height of its own keeps it, and a newly raised wall starts here.");
        BeginFieldUndo(setup, heightState);
        if ((heightState & InputEditStateFlags.Modified) != 0)
        {
            var previous = plan.WallHeight;
            plan.WallHeight = MathF.Max(height[0], 0.1f);
            FloorPlanSync.ApplyWallHeight(setup, plan, previous);
        }

        CommitFieldUndo(setup, "Change wall height", heightState);

        DrawCardDivider();
        FormInputs.AddSectionSubHeader("Edges");
        for (var segment = 0; segment < plan.SegmentCount; segment++)
        {
            ImGui.PushID(segment);
            plan.GetSegment(segment, out var start, out var end);
            var wall = setup.FindSurface(plan.WallOf(segment));
            var lowered = wall == null ? setup.FindSurface(plan.SlotOf(segment)) : null;
            var hasWall = wall != null;
            var label = wall != null ? $"{wall.Name} · {(end - start).Length():0.##} m"
                        : lowered != null ? $"Edge {segment + 1} · {(end - start).Length():0.##} m · {lowered.Name} lowered"
                        : $"Edge {segment + 1} · {(end - start).Length():0.##} m";
            if (FormInputs.AddCheckBox(label, ref hasWall,
                                       "A wall standing on this edge, facing in. Unticking removes it, unless content, a projection, a trace, regions or a patch of its name were put on it — then it is kept, free of the plan, and ticking again brings it back."))
            {
                var index = segment;
                SetupUndo.RunUndoable(hasWall ? "Raise wall" : "Take down wall", setup, () => FloorPlanSync.SetWall(setup, plan, index, hasWall));
            }

            ImGui.PopID();
        }
    }

    /// <summary>
    /// The rectangle the floor surface and its patch stand for. It follows the footprint's bounding box until one of
    /// its fields is edited; a concave room usually wants it on the main hall, with a niche reaching past it.
    /// </summary>
    private static void DrawFloorRectRows(Setup setup, FloorPlan plan)
    {
        if (!plan.TryGetFloorRect(out var rect))
            return;

        var fitsBounds = plan.FloorRect == null;
        if (FormInputs.AddCheckBox("Floor fits bounds", ref fitsBounds,
                                   "The floor surface covers the footprint's bounding box. Editing the floor rectangle below, or its edges on the Board while the floor is selected, sets one of its own."))
        {
            SetupUndo.RunUndoable(fitsBounds ? "Fit floor to bounds" : "Keep floor rectangle", setup, () =>
                                                                                                    {
                                                                                                        plan.FloorRect = fitsBounds ? null : rect;
                                                                                                        FloorPlanSync.Apply(setup, plan);
                                                                                                    });
        }

        Span<float> centre = [rect.Centre.X, rect.Centre.Y];
        var centreState = DrawFloatsRow("Floor centre (m)", centre, "In plan metres from the stage origin, Y towards the front.");
        BeginFieldUndo(setup, centreState);
        if ((centreState & InputEditStateFlags.Modified) != 0)
        {
            rect.Centre = new Vector2(centre[0], centre[1]);
            plan.FloorRect = rect;
            FloorPlanSync.Apply(setup, plan);
        }

        CommitFieldUndo(setup, "Move floor", centreState);

        Span<float> size = [rect.Size.X, rect.Size.Y];
        var sizeState = DrawFloatsRow("Floor size (m)", size, "Width and depth of what the floor's patch shows, e.g. as printed on the pixel-map sheet.");
        BeginFieldUndo(setup, sizeState);
        if ((sizeState & InputEditStateFlags.Modified) != 0)
        {
            rect.Size = Vector2.Max(new Vector2(size[0], size[1]), new Vector2(SurfaceGeometry.MinSize));
            plan.FloorRect = rect;
            FloorPlanSync.Apply(setup, plan);
        }

        CommitFieldUndo(setup, "Resize floor", sizeState);

        Span<float> rotation = [rect.RotationDegrees];
        var rotationState = DrawFloatsRow("Floor rotation (°)", rotation, "Counter-clockwise seen from above, from the plan's X axis.", speed: 0.1f);
        BeginFieldUndo(setup, rotationState);
        if ((rotationState & InputEditStateFlags.Modified) != 0)
        {
            rect.RotationDegrees = rotation[0];
            plan.FloorRect = rect;
            FloorPlanSync.Apply(setup, plan);
        }

        CommitFieldUndo(setup, "Turn floor", rotationState);
    }

    private static void DrawOutputCard(Setup setup, MachineConfig machineConfig, Guid id)
    {
        var output = setup.FindOutput(id);
        if (output == null)
            return;

        var send = output.IsSending;
        if (FormInputs.AddCheckBox("Send", ref send, "Pause presenting without dropping the display binding."))
            SetupUndo.RunUndoable("Toggle send", setup, () => output.IsSending = send);

        // The canvas every route onto this output is measured in, and what its content is asked to render at:
        // an unset resolution upstream (a RenderTarget at 0×0) resolves to this, so it is the one place the
        // pixel size of a projector or a stream is decided.
        var isDefaultResolution = output.FollowsPlug;
        Span<int> canvas = [output.ResolvedResolution.Width, output.ResolvedResolution.Height];
        if (isDefaultResolution)
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultValueAlpha * ImGui.GetStyle().Alpha);

        var canvasState = DrawIntsRow("Resolution (px)", canvas,
                                      "The output's pixel size. It follows whatever is plugged in until set here. Content rendering at 'Fill' follows it, and a stream sends at it.");
        if (isDefaultResolution)
            ImGui.PopStyleVar();

        if (FormInputs.AppendResetButton(!isDefaultResolution, "resetResolution"))
            SetupUndo.RunUndoable("Reset resolution", setup, () => output.CanvasResolution = default);

        BeginFieldUndo(setup, canvasState);
        if ((canvasState & InputEditStateFlags.Modified) != 0)
        {
            // 0 means "follow the plug". Only a render size either way: every quad on this canvas is stored as
            // a fraction of it, so changing it re-renders without moving a single mapping.
            output.CanvasResolution = OutputDefinition.ClampResolution(new T3.Core.DataTypes.Vector.Int2(canvas[0], canvas[1]));
        }

        CommitFieldUndo(setup, "Resize canvas", canvasState);

        var binding = machineConfig.FindBinding(output.Id);
        var boundTo = binding == null ? "unbound" : Plugs.BindingLabel(machineConfig, binding);
        FormInputs.AddHint(binding == null ? "Not bound to a display or stream yet."
                           : output.FollowsPlug ? $"Using {boundTo}"
                           : $"Shown on {boundTo}");

        DrawCardDivider();
        DrawPatchTable(setup, output, Guid.Empty);
        DrawCardDivider();
        DrawReferenceLayoutRows(setup, output);

        if (output.Kind is OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display)
        {
            DrawCardDivider();
            var isProjector = output.Kind == OutputDefinition.Kinds.Projector;
            if (FormInputs.AddCheckBox("Is Projector", ref isProjector,
                                       "A projector throws its picture into the room, so its position and zoom can be solved for [UseProjectorCam]. "
                                       + "A display shows the canvas as it is."))
            {
                SetupUndo.RunUndoable(isProjector ? "Make projector" : "Make display", setup,
                                      () => output.Kind = isProjector ? OutputDefinition.Kinds.Projector : OutputDefinition.Kinds.Display);
            }

            if (output.Kind == OutputDefinition.Kinds.Projector)
                DrawProjectorCameraRows(setup, output);
        }
    }

    /// <summary>
    /// Where the projector stands and how it zooms, for [UseProjectorCam]: solved from the output's aligned surface
    /// with the lens axis given, since one surface can't tell where the axis passes through the picture.
    /// </summary>
    private static void DrawProjectorCameraRows(Setup setup, OutputDefinition output)
    {
        var camera = output.Camera;

        // Shown as spec sheets give it, the picture's shift off the lens axis in percent of its size; stored as where the
        // axis lands in the picture (NDC), which is the opposite direction with half the picture as 1.
        var lensAxis = camera?.LensAxis ?? Vector2.Zero;
        Span<float> shift = [-lensAxis.X * 50, -lensAxis.Y * 50];
        var shiftState = DrawFloatsRow("Lens shift (%)", shift,
                                       "How far the picture is moved off the lens axis, in percent of its width and height, right and up. "
                                       + "0 centres the picture on the lens. A projector on a table throwing up at a wall is usually about +50 up: "
                                       + "the picture's bottom edge sits on the lens axis, which some spec sheets call 100% offset. A ceiling mount is about -50.",
                                       speed: 0.5f, format: "{0:0.#}");
        BeginFieldUndo(setup, shiftState);
        if ((shiftState & InputEditStateFlags.Modified) != 0)
            (output.Camera ??= new OutputDefinition.ProjectorCamera()).LensAxis = new Vector2(shift[0], shift[1]) / -50;

        CommitFieldUndo(setup, "Change lens shift", shiftState);

        Span<float> zoom = [camera?.ManualFovYDegrees ?? 45];
        var zoomState = DrawFloatsRow("Zoom (°)", zoom,
                                      "The vertical field of view. Used when the surface is seen too squarely for its keystone to tell the zoom, and by the manual camera.",
                                      speed: 0.1f, format: "{0:0.#}");
        BeginFieldUndo(setup, zoomState);
        if ((zoomState & InputEditStateFlags.Modified) != 0)
            (output.Camera ??= new OutputDefinition.ProjectorCamera()).ManualFovYDegrees = Math.Clamp(zoom[0], 1, 170);

        CommitFieldUndo(setup, "Change zoom", zoomState);

        FormInputs.AddVerticalSpace(4);
        ProjectorSideView.Draw(setup, output.Camera ?? _unsolvedCamera);
        FormInputs.AddVerticalSpace(4);

        FormInputs.DrawInputLabel(string.Empty);
        if (CustomComponents.StateButton("Solve from Aligned Surface", CustomComponents.ButtonStates.Default))
        {
            _projectorSolveFailure = ProjectorCalibration.TrySolveFromAlignedSurface(setup, output, out var message)
                                         ? default
                                         : (output.Id, message);
        }

        CustomComponents.TooltipForLastItem("Solves where the projector stands and how it zooms from the largest surface aligned on this output.",
                                            "Align the surface first and set the lens shift. Turn the projector's own digital keystone correction off.");

        var isSolved = camera?.Pose != null;
        ImGui.SameLine();
        ImGui.BeginDisabled(!isSolved);
        if (CustomComponents.StateButton("Use Manual Camera", CustomComponents.ButtonStates.Default) && isSolved)
            ProjectorCalibration.UseManualCamera(setup, output);

        ImGui.EndDisabled();

        FormInputs.AddHint(_projectorSolveFailure.OutputId == output.Id ? _projectorSolveFailure.Message
                           : camera != null ? ProjectorCalibration.DescribeSolve(setup, camera)
                           : "Manual camera: not solved yet.");
    }

    /** Matches how FormInputs fades a field that holds its default value. */
    private const float DefaultValueAlpha = 0.7f;

    private static void DrawCardDivider()
    {
        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(8);
    }

    /// <summary>
    /// The image drawn over the canvas while placing patches — a venue's pixel map at the output's pixel size.
    /// Picking a file creates the setup's reference image for it, or repoints the one already linked.
    /// </summary>
    private static void DrawReferenceLayoutRows(Setup setup, OutputDefinition output)
    {
        var image = setup.FindReferenceImage(output.ReferenceImageId);

        // An empty string, not null: the picker only draws its search field for a string it can edit.
        FormInputs.DrawInputLabel("Reference Layout");
        string? path = image?.FilePath ?? string.Empty;
        var pathState = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.File, SetupActions.ImageFileFilter, ref path);
        if ((pathState & InputEditStateFlags.Modified) != 0 && !string.IsNullOrEmpty(path) && path != image?.FilePath)
        {
            SetupUndo.RunUndoable("Pick pixel map", setup, () =>
                                                           {
                                                               if (image == null)
                                                               {
                                                                   image = new ReferenceImage
                                                                               {
                                                                                   Name = $"{SetupLabels.OutputLabel(output)} map",
                                                                                   Kind = ReferenceImage.Kinds.PixelMap,
                                                                                   FilePath = path,
                                                                               };
                                                                   setup.ReferenceImages.Add(image);
                                                                   output.ReferenceImageId = image.Id;
                                                               }
                                                               else
                                                               {
                                                                   image.FilePath = path;
                                                               }
                                                           });
        }

        if (image == null)
        {
            FormInputs.AppendTooltip("The venue's pixel map: an image at the output's resolution, drawn over its canvas to place patches against.");
            return;
        }

        ImGui.SameLine();
        if (CustomComponents.IconButton(Icon.Close, Vector2.Zero, CustomComponents.ButtonStates.Default))
            SetupUndo.RunUndoable("Remove pixel map", setup, () => output.ReferenceImageId = Guid.Empty);

        CustomComponents.TooltipForLastItem("Remove the pixel map from this output", "The image stays in the setup.");

        var opacity = output.ReferenceOpacity;
        var opacityState = FormInputs.AddFloatWithEditState("Opacity", ref opacity, 0, 1, 0.005f, clampMin: true, clampMax: true,
                                                            "How strongly the map shows over the output's content.", defaultValue: 0.5f);
        BeginFieldUndo(setup, opacityState);
        if ((opacityState & InputEditStateFlags.Modified) != 0)
            output.ReferenceOpacity = opacity;

        CommitFieldUndo(setup, "Change pixel map opacity", opacityState);

        // The map is laid out for one canvas size; a mismatch means every patch typed from it lands off.
        if (image.Width <= 0)
        {
            FormInputs.AddHint("Not loaded yet — it shows once this output is open.");
        }
        else if (image.Width == output.ResolvedResolution.Width && image.Height == output.ResolvedResolution.Height)
        {
            FormInputs.AddHint($"{image.Width}×{image.Height} px · matches the resolution");
        }
        else
        {
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText($"{image.Width}×{image.Height} px · resolution is {output.ResolvedResolution.Width}×{output.ResolvedResolution.Height}",
                                          Fonts.FontSmall, UiColors.StatusAttention);
            ImGui.SameLine(0, 6 * T3Ui.UiScaleFactor);
            if (ImGui.SmallButton("Use as Resolution"))
            {
                SetupUndo.RunUndoable("Resize canvas to pixel map", setup,
                                      () => output.CanvasResolution =
                                                OutputDefinition.ClampResolution(new T3.Core.DataTypes.Vector.Int2(image.Width, image.Height)));
            }
        }
    }

    /// <summary>
    /// Every patch on the canvas as one row — name, top-left, size, rotation — so a venue's spec sheet can be
    /// typed in as it is printed. Clicking a name selects that patch; <paramref name="highlightedPatchId"/>
    /// is the row shown as current. Fitted and warped patches show their rect read-only, since their shape is
    /// decided elsewhere (the aspect rows, the corners on the canvas).
    /// </summary>
    private static void DrawPatchTable(Setup setup, OutputDefinition output, Guid highlightedPatchId)
    {
        var selection = GlobalSelectionHandling.SetupEntities;

        // A patch card draws the same switch for its own rows above, so this one needs its own id scope.
        ImGui.PushID("patchTable");
        var listWidth = ImGui.GetContentRegionAvail().X - InputArea.ValueEditRightMargin;
        var listScreenX = ImGui.GetCursorScreenPos().X;
        DrawPatchListHeader(setup, output, selection, listScreenX, listWidth);
        if (output.Patches.Count > 0)
            DrawPatchListRows(setup, output, selection, highlightedPatchId, listScreenX, listWidth);

        ImGui.PopID();
    }

    /** The section title, the unit switch in the parameter column, and the add button at the list's right end. */
    private static void DrawPatchListHeader(Setup setup, OutputDefinition output, SetupEntitySelection selection, float listScreenX, float listWidth)
    {
        var padding = PatchHeaderPadding * T3Ui.UiScaleFactor;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText("Patches", Fonts.FontBold, UiColors.TextMuted);
        ImGui.SameLine();
        if (output.Patches.Count > 0)
        {
            FormInputs.DrawInputLabel("in");
            var units = UserSettings.Config.OutputSetupEditUnits;
            if (CustomComponents.SegmentedButton(ref units, tooltipForItem: static _ => UnitSwitchTooltip))
                UserSettings.Config.OutputSetupEditUnits = units;
        }

        var addWidth = ImGui.CalcTextSize("Add").X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SameLine(listScreenX + listWidth - addWidth - padding - ImGui.GetWindowPos().X);
        if (ImGui.Button("Add"))
            SetupActions.AddPatch(selection, setup, output);

        CustomComponents.TooltipForLastItem("Adds a full-canvas patch to place by dragging on the output, or by typing its rect here.");
        FormInputs.AddVerticalSpace(4);
    }

    private static void DrawPatchListRows(Setup setup, OutputDefinition output, SetupEntitySelection selection, Guid highlightedPatchId,
                                          float listScreenX, float listWidth)
    {
        var scale = T3Ui.UiScaleFactor;
        var rowHeight = FormInputs.ListRowHeight;
        var gripWidth = FormInputs.ListRowGripWidth;
        var drawList = ImGui.GetWindowDrawList();

        var cellUnit = MathF.Max(listWidth - gripWidth - rowHeight * PatchRowIconCount, 1) / (PatchNameColumnUnits + _patchCellX.Length);
        var nameWidth = cellUnit * PatchNameColumnUnits;
        for (var cell = 0; cell < _patchCellX.Length; cell++)
        {
            _patchCellX[cell] = listScreenX + gripWidth + nameWidth + cell * cellUnit;
        }

        _patchCellWidth = cellUnit;

        var labelY = ImGui.GetCursorScreenPos().Y;
        ImGui.PushFont(Fonts.FontSmall);
        drawList.AddText(new Vector2(listScreenX + gripWidth, labelY), UiColors.TextMuted, _patchColumnLabels[0]);
        for (var cell = 0; cell < _patchCellX.Length; cell++)
        {
            drawList.AddText(new Vector2(_patchCellX[cell] + PatchCellTextInset * scale, labelY), UiColors.TextMuted, _patchColumnLabels[cell + 1]);
        }

        // The grips read this spacing back for their row stride, so it has to hold for the whole list.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, MathF.Round(PatchRowGap * scale)));
        ImGui.Dummy(new Vector2(listWidth, ImGui.GetTextLineHeight()));
        ImGui.PopFont();

        var canvas = output.CanvasSize;
        var moveFromIndex = -1;
        var moveToIndex = -1;

        // Placed explicitly: a line grows with its tallest item, which would turn the gap into more than the spacing.
        var rowsTop = MathF.Floor(ImGui.GetCursorScreenPos().Y);
        var rowStride = rowHeight + ImGui.GetStyle().ItemSpacing.Y;
        for (var index = 0; index < output.Patches.Count; index++)
        {
            var patch = output.Patches[index];

            // By patch, not by index: the grip being dragged has to stay active when its row changes places.
            ImGui.PushID(patch.Id.GetHashCode());
            ImGui.SetCursorScreenPos(new Vector2(listScreenX, rowsTop + index * rowStride));
            var isCurrent = patch.Id == highlightedPatchId || selection.IsSelected(SetupEntityKinds.Patch, patch.Id);

            var rowMin = ImGui.GetCursorScreenPos().Floor();
            var rowMax = rowMin + new Vector2(listWidth, rowHeight).Floor();
            var isDragging = FormInputs.DrawRowDragGrip(index, output.Patches.Count, rowHeight, ref moveFromIndex, ref moveToIndex);
            FormInputs.DrawListRowBackground(drawList, rowMin, rowMax, isDragging, drawGrip: true);
            if (isCurrent)
                drawList.AddRect(rowMin, rowMax, UiColors.Selection.Fade(0.6f), 4 * scale);

            ImGui.SameLine(0, 0);
            ImGui.InvisibleButton("##name", new Vector2(MathF.Max(nameWidth, 1), rowHeight));
            if (ImGui.IsItemClicked())
                selection.Select(SetupEntityKinds.Patch, patch.Id);

            // A double-click renames, in the Flow Outliner's row, where names are edited.
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                OutlinerItem.BeginRename(selection, SetupEntityKinds.Patch, patch.Id, patch.Name);

            var nameMin = ImGui.GetItemRectMin();
            var nameFont = isCurrent ? Fonts.FontBold : Fonts.FontNormal;
            drawList.PushClipRect(nameMin, ImGui.GetItemRectMax(), true);
            drawList.AddText(nameFont, nameFont.FontSize, new Vector2(nameMin.X, nameMin.Y + (rowHeight - nameFont.FontSize) * 0.5f).Floor(),
                             isCurrent ? UiColors.Text : UiColors.TextMuted, SetupLabels.PatchLabel(output, patch));
            drawList.PopClipRect();

            // The values read as plain text on the row until hovered.
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            DrawPatchRectCells(setup, output, patch, canvas, rowHeight);
            ImGui.PopStyleColor();

            // Quarter turns only: anything else isn't a pure reordering of the quad's corners.
            var iconSize = new Vector2(rowHeight);
            ImGui.SameLine(rowMax.X - rowHeight * PatchRowIconCount - ImGui.GetWindowPos().X);
            if (CustomComponents.IconButton(Icon.RotateCounterClockwise, iconSize))
                SetupUndo.RunUndoable("Rotate patch", setup, () => SetupActions.TurnPatch(output, patch, -1));

            CustomComponents.TooltipForLastItem("Turn a quarter counter-clockwise", PatchTurnTooltipNotes);

            ImGui.SameLine(0, 0);
            if (CustomComponents.IconButton(Icon.RotateClockwise, iconSize))
                SetupUndo.RunUndoable("Rotate patch", setup, () => SetupActions.TurnPatch(output, patch, 1));

            CustomComponents.TooltipForLastItem("Turn a quarter clockwise", PatchTurnTooltipNotes);

            ImGui.SameLine(0, 0);
            if (CustomComponents.IconButton(Icon.Settings2, iconSize))
                selection.Select(SetupEntityKinds.Patch, patch.Id);

            CustomComponents.TooltipForLastItem("Show this patch's settings", "Its content, fit and corners.");
            ImGui.PopID();
        }

        // An ImGui window's extent must not rest on a bare cursor set, so the end of the list is claimed with an item.
        ImGui.SetCursorScreenPos(new Vector2(listScreenX, rowsTop + output.Patches.Count * rowStride));
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopStyleVar();

        // Dragging a grip past a neighbour swaps the two: the list is the composite order (later patches draw over
        // earlier ones). One undo step for the whole drag.
        if (moveFromIndex >= 0)
        {
            _patchReorderOldJson ??= setup.ToJsonString();

            // A patch still labelled by its position would change its name under the cursor.
            foreach (var patch in output.Patches)
            {
                if (string.IsNullOrEmpty(patch.Name) && !SetupRelations.IsImplicitPatch(output, patch))
                    patch.Name = SetupLabels.PatchLabel(output, patch);
            }

            (output.Patches[moveFromIndex], output.Patches[moveToIndex]) = (output.Patches[moveToIndex], output.Patches[moveFromIndex]);
        }

        if (_patchReorderOldJson != null && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            SetupUndo.CommitGesture(setup, "Reorder patches", _patchReorderOldJson);
            _patchReorderOldJson = null;
        }
    }

    /// <summary>The X, Y, W, H and turn cells of one row, editable for a freely placed axis-aligned patch.</summary>
    private static void DrawPatchRectCells(Setup setup, OutputDefinition output, OutputDefinition.Patch patch, Vector2 canvas, float rowHeight)
    {
        var quad = patch.Quad;
        if (quad.Length < 4)
            return;

        CanvasDraw.Bounds(quad, out var min, out var max);
        var cells = _patchCellScratch;
        cells[0] = ToUnit(min.X, canvas.X);
        cells[1] = ToUnit(min.Y, canvas.Y);
        cells[2] = ToUnit(max.X - min.X, canvas.X);
        cells[3] = ToUnit(max.Y - min.Y, canvas.Y);

        const float aligned = 0.0001f; // of the canvas
        var isAxisAligned = MathF.Abs(quad[0].Y - quad[1].Y) < aligned && MathF.Abs(quad[2].Y - quad[3].Y) < aligned
                            && MathF.Abs(quad[0].X - quad[3].X) < aligned && MathF.Abs(quad[1].X - quad[2].X) < aligned;
        var editable = !patch.IsFitted;
        var format = EditsInPixels ? "{0:0}" : "{0:0.000}";
        var cellSize = new Vector2(_patchCellWidth, rowHeight);
        var windowX = ImGui.GetWindowPos().X;

        var state = InputEditStateFlags.Nothing;
        for (var cell = 0; cell < 4; cell++)
        {
            ImGui.SameLine(_patchCellX[cell] - windowX);
            if (!editable)
            {
                ImGui.AlignTextToFramePadding();
                CustomComponents.StylizedText(string.Format(format, cells[cell]), Fonts.FontSmall, UiColors.TextMuted);
                continue;
            }

            ImGui.PushID(cell);
            var value = cells[cell];
            var cellState = SingleValueEdit.Draw(ref value, cellSize,
                                                 float.NegativeInfinity, float.PositiveInfinity, clampMin: false, clampMax: false,
                                                 scale: EditsInPixels ? 1f : 0.001f, format: format);
            cells[cell] = EditsInPixels ? MathF.Round(value) : value;
            state |= cellState;
            ImGui.PopID();
        }

        if (editable)
        {
            BeginFieldUndo(setup, state);
            if ((state & InputEditStateFlags.Modified) != 0)
            {
                var topLeft = new Vector2(FromUnit(cells[0], canvas.X), FromUnit(cells[1], canvas.Y));
                var size = new Vector2(MathF.Max(FromUnit(cells[2], canvas.X), 0.0001f), MathF.Max(FromUnit(cells[3], canvas.Y), 0.0001f));
                ApplyBoundsToQuad(quad, isAxisAligned, min, max, topLeft, size);
            }

            CommitFieldUndo(setup, "Edit patch rect", state);
        }

        ImGui.SameLine(_patchCellX[4] - windowX);

        var degrees = DegreesOfTurns(patch.QuarterTurns);
        var turnState = SingleValueEdit.Draw(ref degrees, cellSize,
                                             -180, 180, clampMin: false, clampMax: false, scale: 1f, format: "{0:0}°");
        BeginFieldUndo(setup, turnState);
        if ((turnState & InputEditStateFlags.Modified) != 0)
            SetupActions.TurnPatch(output, patch, TurnsOfDegrees(degrees) - patch.QuarterTurns);

        CommitFieldUndo(setup, "Rotate patch", turnState);
    }

    /// <summary>
    /// Gives a quad new bounds: an axis-aligned one is rewritten as that exact rectangle (which also squares a
    /// sub-pixel skew), a warped one is moved and scaled about its old top-left, so its shape rides along.
    /// </summary>
    private static void ApplyBoundsToQuad(Vector2[] quad, bool isAxisAligned, Vector2 oldMin, Vector2 oldMax, Vector2 newMin, Vector2 newSize)
    {
        if (isAxisAligned)
        {
            quad[0] = newMin;
            quad[1] = new Vector2(newMin.X + newSize.X, newMin.Y);
            quad[2] = newMin + newSize;
            quad[3] = new Vector2(newMin.X, newMin.Y + newSize.Y);
            return;
        }

        var oldSize = Vector2.Max(oldMax - oldMin, new Vector2(0.0001f));
        var factor = newSize / oldSize;
        for (var c = 0; c < 4; c++)
            quad[c] = newMin + (quad[c] - oldMin) * factor;
    }

    /** How many value cells wide the name column is. */
    private const float PatchNameColumnUnits = 1.6f;
    private const float PatchCellTextInset = 4f;
    private const float PatchHeaderPadding = 6f;
    private const float PatchRowGap = 2f;
    private const int PatchRowIconCount = 3;
    private const string PatchTurnTooltipNotes = "Turns the patch around its centre, its shape and its picture together — for a display or LED panel mounted on its side.";
    private static readonly string[] _patchColumnLabels = ["Name", "X", "Y", "Width", "Height", "Rotation"];
    private static readonly float[] _patchCellX = new float[5]; // screen x of the value cells in the list drawn now
    private static float _patchCellWidth;
    private static string? _patchReorderOldJson; // the setup before a row drag started reordering, until the button is released
    private static readonly float[] _patchCellScratch = new float[4];

    /// <summary>
    /// A plug: what this machine presents through. A display is read-only (the OS owns its name and mode); a
    /// stream carries the settings its kind honours — the host asks the provider which those are, so a kind
    /// that has no notion of frame rate or alpha simply doesn't offer them.
    /// </summary>
    private static void DrawPlugCard(Setup setup, MachineConfig machineConfig, Guid id)
    {
        var boundOutput = Plugs.TryGetBoundOutput(setup, machineConfig, id);
        var boundLabel = boundOutput == null ? "nothing bound" : $"presenting {SetupLabels.OutputLabel(boundOutput)}";

        if (Plugs.TryGetDisplayIndex(id, out var displayIndex))
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            Span<int> mode = [0, 0];
            if (displayIndex < screens.Length)
            {
                mode[0] = screens[displayIndex].Bounds.Width;
                mode[1] = screens[displayIndex].Bounds.Height;
            }

            DrawIntsRow("Resolution (px)", mode, "The display's current mode (read-only).", readOnly: true);
            FormInputs.AddHint(boundLabel);
            return;
        }

        var stream = machineConfig.FindStreamPlug(id);
        if (stream == null)
            return;

        var provider = OutputStreamRegistry.TryGetProvider(stream.Kind);
        var supported = provider?.Supported ?? OutputStreamOptions.None;

        // What the receivers get: the canvas of the output bound here. A stream has no size of its own, but this is
        // where one looks for it, so an edit writes through to that output instead of pointing elsewhere.
        Span<int> resolution = [0, 0];
        if (boundOutput == null)
        {
            DrawIntsRow("Sends at (px)", resolution, "Nothing is bound to this stream, so it sends nothing.", readOnly: true);
        }
        else
        {
            resolution[0] = boundOutput.ResolvedResolution.Width;
            resolution[1] = boundOutput.ResolvedResolution.Height;
            var resolutionState = DrawIntsRow("Sends at (px)", resolution,
                                              $"The canvas of \"{SetupLabels.OutputLabel(boundOutput)}\", the output bound here — changing it resizes that output.");
            BeginFieldUndo(setup, resolutionState);
            if ((resolutionState & InputEditStateFlags.Modified) != 0)
            {
                // A bound output sends at its canvas, so 0 ("follow the plug") would be circular here.
                boundOutput.CanvasResolution = OutputDefinition.ClampResolution(new T3.Core.DataTypes.Vector.Int2(Math.Max(resolution[0], 1),
                                                                                                                 Math.Max(resolution[1], 1)));
            }

            CommitFieldUndo(setup, "Resize canvas", resolutionState);
        }

        if ((supported & OutputStreamOptions.FrameRate) != 0)
        {
            var frameRate = stream.FrameRate;
            if (FormInputs.AddInt("Frame rate", ref frameRate, 1, 240, 1,
                                  "Frames per second sent to receivers. Rendered frames in between are skipped, so a rate the render loop divides evenly into (30 from 60) is sent evenly.", 60))
            {
                stream.FrameRate = Math.Clamp(frameRate, 1, 240);
                OutputSetupHandling.SaveActive();
            }
        }

        if ((supported & OutputStreamOptions.Alpha) != 0)
        {
            var alpha = stream.EnableAlpha;
            if (FormInputs.AddCheckBox("Send alpha", ref alpha, "Carry the alpha channel; off sends an opaque frame."))
            {
                stream.EnableAlpha = alpha;
                OutputSetupHandling.SaveActive();
            }
        }

        FormInputs.AddHint(provider == null ? $"{stream.Kind} · package not loaded" : $"{stream.Kind} · {boundLabel}");

        // A refused frame is otherwise invisible: the output looks bound and sending while receivers get nothing.
        if (OutputPresentation.TryGetStreamError(id, out var streamError))
        {
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText(streamError, Fonts.FontSmall, UiColors.StatusAttention);
        }
    }

    /// <summary>Edits one of the send op's inputs as a regular parameter change: undoable, and it dirties the symbol.</summary>
    private static void SetInputUndoable<T>(Instance instance, IInputSlot slot, T value)
    {
        var parent = instance.Parent;
        if (parent == null || slot.Input.Value.Clone() is not InputValue<T> newValue)
            return;

        newValue.Value = value;
        UndoRedoStack.AddAndExecute(new ChangeInputValueCommand(parent.Symbol, instance.SymbolChildId, slot.Input, newValue, instance));
    }

    private static void DrawContentCard(Setup setup, Guid childId)
    {
        var instance = ContentSourceSync.FindSendInstance(childId);
        if (instance is not IContentSupplier supplier)
            return;

        OutputContentResolver.TryGetPreviewContent(childId, out var content);
        var context = OutputContentResolver.Context;

        var update = supplier.GetUpdateEnabled(context);
        if (FormInputs.AddCheckBox("Update", ref update, "When off, freezes this content at its last frame."))
            SetInputUndoable(instance, supplier.UpdateInput, update);

        // 0×0 means "whatever the output asks for", so the content follows the projector or display it is routed
        // to; a set value pins it. The line underneath says what that resolves to right now.
        var requested = supplier.GetResolution(context);
        Span<int> resolution = [requested.Width, requested.Height];
        var state = DrawIntsRow("Render at (px)", resolution,
                                "0 follows the output this content is routed to. Set a size to render at it regardless.");
        if ((state & InputEditStateFlags.Modified) != 0)
        {
            SetInputUndoable(instance, supplier.ResolutionInput,
                             new T3.Core.DataTypes.Vector.Int2(Math.Clamp(resolution[0], 0, 16384),
                                                               Math.Clamp(resolution[1], 0, 16384)));
        }

        FormInputs.AddHint(content is { IsDisposed: false }
                               ? $"rendering {content.Description.Width}×{content.Description.Height}"
                               : "nothing rendered yet");

        // Two outputs of different sizes fed by one send: it renders once, so the second shows the first's size.
        if (OutputContentStats.TryGetSizeConflict(instance.SymbolChildId, out var rendered, out var other))
        {
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText($"⚠ also routed to an output of {other.Width}×{other.Height} — it shows {rendered.Width}×{rendered.Height}",
                                          Fonts.FontSmall, UiColors.StatusAttention);
        }
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
        if (source != null && OutputContentResolver.TryGetPreviewContent(source.SymbolChildId, out var content)
            && content is { IsDisposed: false })
        {
            texW = content.Description.Width;
            texH = content.Description.Height;
        }

        var uv = slice.UvRect;
        if (texW <= 0 || texH <= 0)
        {
            FormInputs.AddHint("Connect the source op to edit in pixels.");
            Span<float> posUv = [uv.X, uv.Y];
            DrawFloatsRow("Position (uv)", posUv, readOnly: true);
            Span<float> sizeUv = [uv.Z - uv.X, uv.W - uv.Y];
            DrawFloatsRow("Size (uv)", sizeUv, readOnly: true);
            return;
        }

        var widthUv = MathF.Max(uv.Z - uv.X, SurfaceGeometry.MinSliceSize);
        var heightUv = MathF.Max(uv.W - uv.Y, SurfaceGeometry.MinSliceSize);

        // A slice is a fraction of its source, shown in whichever unit is selected — pixels of that source
        // while cutting an atlas, ratios when the source's size is not the point.
        DrawUnitSwitch();

        Span<float> position = [ToUnit(uv.X, texW), ToUnit(uv.Y, texH)];
        var positionState = DrawRectRow("Position", position);
        BeginFieldUndo(setup, positionState);
        if ((positionState & InputEditStateFlags.Modified) != 0)
        {
            var nx = Math.Clamp(FromUnit(position[0], texW), 0f, 1f - widthUv);
            var ny = Math.Clamp(FromUnit(position[1], texH), 0f, 1f - heightUv);
            slice.SetUvRect(new Vector4(nx, ny, nx + widthUv, ny + heightUv));
        }

        CommitFieldUndo(setup, "Move slice", positionState);

        Span<float> size = [ToUnit(widthUv, texW), ToUnit(heightUv, texH)];
        var sizePxState = DrawRectRow("Size", size);
        BeginFieldUndo(setup, sizePxState);
        if ((sizePxState & InputEditStateFlags.Modified) != 0)
        {
            var nw = Math.Clamp(FromUnit(size[0], texW), SurfaceGeometry.MinSliceSize, 1f - uv.X);
            var nh = Math.Clamp(FromUnit(size[1], texH), SurfaceGeometry.MinSliceSize, 1f - uv.Y);
            slice.SetUvRect(new Vector4(uv.X, uv.Y, uv.X + nw, uv.Y + nh));
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

        DrawPatchRows(setup, output, patch);
        DrawCardDivider();

        // Its siblings on the same canvas, with this one current — a venue's patches are placed as a set.
        DrawPatchTable(setup, output, patch.Id);
    }

    private static void DrawPatchRows(Setup setup, OutputDefinition output, OutputDefinition.Patch patch)
    {
        var slice = setup.FindSlice(patch.SliceId);
        FormInputs.AddHint(slice == null
                               ? "Nothing routed yet — drop a slice or content onto this patch."
                               : $"Shows {SetupLabels.SliceLabel(setup, slice)} on {SetupLabels.OutputLabel(output)}");

        DrawPatchScaleRows(setup, output, patch);
        if (patch.IsFitted || patch.Quad.Length < 4)
            return;

        var quad = patch.Quad;
        const float aligned = 0.0001f; // of the canvas
        var isAxisAligned = MathF.Abs(quad[0].Y - quad[1].Y) < aligned && MathF.Abs(quad[2].Y - quad[3].Y) < aligned
                            && MathF.Abs(quad[0].X - quad[3].X) < aligned && MathF.Abs(quad[1].X - quad[2].X) < aligned;
        if (!isAxisAligned)
            FormInputs.AddHint("Warped quad — its corners are on the output canvas; its row in the list below moves and scales its bounds.");
    }

    /// <summary>
    /// The scale mode and, for a fitted patch, its aspect pair and scale, from which its place and size derive.
    /// Every change re-fits at once, inside its undo step, so the snapshot holds the quad that is drawn.
    /// </summary>
    private static void DrawPatchScaleRows(Setup setup, OutputDefinition output, OutputDefinition.Patch patch)
    {
        var mode = patch.ScaleMode;
        if (FormInputs.AddSegmentedButtonWithLabel(ref mode, "Scale mode",
                                                   "Stretch places the patch freely, as a share of the canvas, so it stretches when the canvas changes aspect. "
                                                   + "Fit keeps the aspect ratio below and fits it into the canvas, centred."))
        {
            // Switching back to Stretch keeps the fitted rectangle as the starting point for free editing.
            SetupUndo.RunUndoable("Change patch scale mode", setup, () =>
                                                                    {
                                                                        patch.ScaleMode = mode;
                                                                        patch.TryFitQuad(output.CanvasSize);
                                                                    });
        }

        if (!patch.IsFitted)
            return;

        Span<float> aspect = [patch.AspectRatio.X, patch.AspectRatio.Y];
        var aspectState = DrawFloatsRow("Aspect ratio", aspect,
                                        "Width : height of the picture, such as 16 : 9 or 2.39 : 1. Measured along the picture, so a quarter turn fits it sideways.");
        BeginFieldUndo(setup, aspectState);
        if ((aspectState & InputEditStateFlags.Modified) != 0)
        {
            patch.AspectRatio = new Vector2(MathF.Max(aspect[0], MinAspectComponent), MathF.Max(aspect[1], MinAspectComponent));
            patch.TryFitQuad(output.CanvasSize);
        }

        CommitFieldUndo(setup, "Change patch aspect", aspectState);

        Span<float> scalePercent = [patch.Scale * 100f];
        var scaleState = DrawFloatsRow("Scale", scalePercent,
                                       "Size as a share of the largest rectangle of this aspect that fits the canvas. 100% touches the canvas edges.",
                                       speed: 0.5f, format: "{0:0.#}%");
        BeginFieldUndo(setup, scaleState);
        if ((scaleState & InputEditStateFlags.Modified) != 0)
        {
            patch.Scale = Math.Clamp(scalePercent[0] / 100f, OutputDefinition.Patch.MinScale, OutputDefinition.Patch.MaxScale);
            patch.TryFitQuad(output.CanvasSize);
        }

        CommitFieldUndo(setup, "Scale patch", scaleState);

        if (patch.Quad.Length == 4)
        {
            var pixels = (patch.Quad[2] - patch.Quad[0]) * output.CanvasSize;
            FormInputs.AddHint(FittedSizeLabel(pixels));
        }
    }

    /// <summary>The fitted size line, rebuilt only when the pixel size changes: the card draws every frame.</summary>
    private static string FittedSizeLabel(Vector2 pixels)
    {
        var width = (int)MathF.Round(pixels.X);
        var height = (int)MathF.Round(pixels.Y);
        if (width != _fittedLabelWidth || height != _fittedLabelHeight)
        {
            _fittedLabelWidth = width;
            _fittedLabelHeight = height;
            _fittedLabel = $"{width} × {height} px on the canvas, centred";
        }

        return _fittedLabel;
    }

    /// <summary>Clockwise quarter turns → the counter-clockwise angle the field shows, folded into -90..180.</summary>
    private static float DegreesOfTurns(int quarterTurns) => OutputDefinition.Patch.NormalizeTurns(quarterTurns) switch
                                                                 {
                                                                     1 => -90f,
                                                                     2 => 180f,
                                                                     3 => 90f,
                                                                     _ => 0f,
                                                                 };

    /// <summary>Any angle → the nearest clockwise quarter-turn count.</summary>
    private static int TurnsOfDegrees(float degrees)
    {
        var counterClockwiseQuarters = (int)MathF.Round(degrees / 90f);
        return OutputDefinition.Patch.NormalizeTurns(-counterClockwiseQuarters);
    }

    private const float MinAspectComponent = 0.01f;
    private static int _fittedLabelWidth = -1;
    private static int _fittedLabelHeight = -1;
    private static string _fittedLabel = string.Empty;

    private static void DrawReferenceImageCard(Setup setup, Guid id)
    {
        var image = setup.FindReferenceImage(id);
        if (image == null)
            return;

        // The project's image assets, through the same type-ahead picker the LoadImage op uses.
        FormInputs.DrawInputLabel("Image");
        string? path = image.FilePath;
        var pathState = FilePickingUi.DrawTypeAheadSearch(FileOperations.FilePickerTypes.File, SetupActions.ImageFileFilter, ref path);
        if ((pathState & InputEditStateFlags.Modified) != 0 && path != null && path != image.FilePath)
            SetupUndo.RunUndoable("Pick reference image", setup, () => image.FilePath = path);

        FormInputs.AddHint(image.Width > 0
                               ? $"{image.Width}×{image.Height} px · double-click its card on the Board to trace and straighten"
                               : "No image loaded yet — pick one above, or drop a photo onto the Board.");

        var locked = image.IsLocked;
        if (FormInputs.AddCheckBox("Locked", ref locked, "A backdrop: drawn beneath every card, never picked, grabbed or fenced."))
            SetupUndo.RunUndoable(locked ? "Lock image" : "Unlock image", setup, () => image.IsLocked = locked);

        var opacity = image.Opacity;
        var opacityState = FormInputs.AddFloatWithEditState("Opacity", ref opacity, 0, 1, 0.005f, clampMin: true, clampMax: true,
                                                            "How strongly the card shows the image on the Board.", defaultValue: 1f);
        BeginFieldUndo(setup, opacityState);
        if ((opacityState & InputEditStateFlags.Modified) != 0)
            image.Opacity = opacity;

        CommitFieldUndo(setup, "Change image opacity", opacityState);

        // The real size of what the card shows. Typing it states the image's scale; the aspect stays, nothing stretches.
        if (image.Width > 0 && image.Kind != ReferenceImage.Kinds.PixelMap)
        {
            image.GetVisiblePixelRect(out var visibleMin, out var visibleMax);
            var visiblePixels = visibleMax - visibleMin;
            var oldSize = visiblePixels * image.MetersPerPixel;
            Span<float> size = [oldSize.X, oldSize.Y];
            var sizeState = DrawFloatsRow("Size (m)", size,
                                          "The real size of what the card shows (its crop). Typing either side sets the image's scale; the aspect is kept.");
            BeginFieldUndo(setup, sizeState);
            if ((sizeState & InputEditStateFlags.Modified) != 0)
            {
                var metersPerPixel = MathF.Abs(size[0] - oldSize.X) >= MathF.Abs(size[1] - oldSize.Y)
                                         ? size[0] / MathF.Max(visiblePixels.X, 1)
                                         : size[1] / MathF.Max(visiblePixels.Y, 1);
                if (metersPerPixel > 0)
                    SetupOutputView.ApplyMetersPerPixel(image, metersPerPixel);
            }

            CommitFieldUndo(setup, "Change image size", sizeState);

            if (image.HasCrop)
            {
                FormInputs.ApplyIndent();
                CustomComponents.StylizedText($"Cropped to {visiblePixels.X:0}×{visiblePixels.Y:0} of {image.Width}×{image.Height} px",
                                              Fonts.FontSmall, UiColors.TextMuted);
                ImGui.SameLine();
                if (ImGui.SmallButton("Reset Crop"))
                    SetupUndo.RunUndoable("Reset image crop", setup,
                                          () => SetupOutputView.SetImageCrop(image, Vector2.Zero, new Vector2(image.Width, image.Height)));
            }
        }

        // The scale line's length re-derives the image's scale; the line itself is drawn with Set Scale on the Board.
        if (image.HasScaleLine)
        {
            Span<float> length = [image.ScaleLineMeters];
            var lengthState = DrawFloatsRow("Scale line (m)", length, "The real length of the line drawn on the image. The card's size follows it.");
            BeginFieldUndo(setup, lengthState);
            if ((lengthState & InputEditStateFlags.Modified) != 0)
            {
                image.ScaleLineMeters = MathF.Max(length[0], 0.001f);
                SetupOutputView.ApplyScaleLine(image);
            }

            CommitFieldUndo(setup, "Change scale line length", lengthState);
            FormInputs.AddHint($"{image.MetersPerPixel * 1000:0.##} mm per pixel");
        }
        else
        {
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText("No scale yet — type the size above, or Set Scale in the card's menu draws a line over a known length.", Fonts.FontSmall, UiColors.TextMuted);
        }
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

        CommitFieldUndo(setup, "Resize person", heightState);

        var opacity = prop.Opacity;
        var opacityState = FormInputs.AddFloatWithEditState("Opacity", ref opacity, 0, 1, 0.005f, clampMin: true, clampMax: true,
                                                            "How strongly the figure shows on the Board.", defaultValue: Prop.DefaultOpacity);
        BeginFieldUndo(setup, opacityState);
        if ((opacityState & InputEditStateFlags.Modified) != 0)
            prop.Opacity = opacity;

        CommitFieldUndo(setup, "Change person opacity", opacityState);
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

    /// <summary>
    /// The unit the rect fields below read in. Quads and slice rects are stored as ratios of what they sit on,
    /// so pixels are a presentation: handy while aiming at a projector's raster, wrong once the canvas changes
    /// size. Offered wherever such a rect is edited, and shared by all of them.
    /// </summary>
    private static void DrawUnitSwitch()
    {
        var units = UserSettings.Config.OutputSetupEditUnits;
        if (FormInputs.AddSegmentedButtonWithLabel(ref units, "Edit in", UnitSwitchTooltip))
        {
            UserSettings.Config.OutputSetupEditUnits = units;
        }
    }

    private const string UnitSwitchTooltip = "Pixels of the canvas or source this rect sits on, or ratios of it. Stored as ratios either way.";

    private static bool EditsInPixels => UserSettings.Config.OutputSetupEditUnits == OutputSetupEditUnits.Pixels;

    /// <summary>A ratio shown in the current unit, against the size of what it sits on. Pixels are rounded: a
    /// typed 1200 stored as a ratio comes back as 1199.9999, which must read as 1200 again.</summary>
    private static float ToUnit(float ratio, float extent) => EditsInPixels ? MathF.Round(ratio * extent) : ratio;

    /// <summary>The inverse of <see cref="ToUnit"/>, back to the stored ratio.</summary>
    private static float FromUnit(float value, float extent) => EditsInPixels ? value / MathF.Max(extent, 1) : value;

    /// <summary>
    /// A rect field in the selected unit: whole pixels when editing in pixels, ratios with decimals otherwise.
    /// Kept apart from <see cref="DrawIntsRow"/> because a ratio has no meaningful integer form.
    /// </summary>
    private static InputEditStateFlags DrawRectRow(string label, Span<float> values, string? tooltip = null)
    {
        var suffix = EditsInPixels ? " (px)" : " (ratio)";
        var size = BeginValuesRow(label + suffix, tooltip, values.Length, false, 0, out var gap);
        var result = InputEditStateFlags.Nothing;
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, gap);

            ImGui.PushID(i);
            var v = values[i];
            result |= SingleValueEdit.Draw(ref v, size, float.NegativeInfinity, float.PositiveInfinity,
                                           clampMin: false, clampMax: false,
                                           scale: EditsInPixels ? 1f : 0.001f,
                                           format: EditsInPixels ? "{0:0}" : "{0:0.0000}");
            values[i] = EditsInPixels ? MathF.Round(v) : v;
            ImGui.PopID();
        }

        EndValuesRow(tooltip, false);
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
        {
            _fieldEditOldJson = setup.ToJsonString();
        }
        else if ((state & InputEditStateFlags.Modified) != 0)
        {
            _fieldEditOldJson ??= setup.ToJsonString();
        }

        // The edit lands right after this and is saved only when the field is let go.
        if ((state & InputEditStateFlags.Modified) != 0)
            OutputSetupHandling.NotifyLiveEdit();
    }

    private static void CommitFieldUndo(Setup setup, string name, InputEditStateFlags state)
    {
        if ((state & InputEditStateFlags.Finished) == 0 || _fieldEditOldJson == null)
            return;

        SetupUndo.CommitGesture(setup, name, _fieldEditOldJson);
        _fieldEditOldJson = null;
    }

    /// <summary>
    /// "Set measured dimensions": states the surface's real size without touching its corner pins. Used after
    /// the rect is already aligned on the wall — you're correcting the measurement, not moving the projection.
    /// The declared size drives the calibration raster's density and the straighten hypothesis.
    /// </summary>
    private static void DrawMeasuredSizePopup(Setup setup, Surface surface)
    {
        ImGui.SetNextWindowSize(new Vector2(260 * T3Ui.UiScaleFactor, 0));
        if (!SetupPopup.Begin(MeasuredSizePopupId))
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
            SetupUndo.RunUndoable("Set measured size", setup, () => SurfaceMetrics.RemeterSurface(setup, surface, _measuredEdit));
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        SetupPopup.End();
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

    private static bool IsRegion(Setup setup, Guid id)
    {
        var surface = setup.FindSurface(id);
        return surface is { Kind: Surface.Kinds.Layout } && surface.ParentId != Guid.Empty;
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
        if (SetupPopup.Begin("##pickTargetPopup"))
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
                if (ImGui.Selectable($"{SetupLabels.OutputLabel(output)}##o{i}", output.Id == current))
                {
                    picked = output.Id;
                    changed = true;
                }
            }

            SetupPopup.End();
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
            return SetupLabels.SurfaceShortLabel(surface);

        var output = setup.FindOutput(id);
        if (output != null)
            return SetupLabels.OutputLabel(output);

        return "(missing)";
    }

    private const string MeasuredSizePopupId = "##measuredSize";
    private static Vector2 _measuredEdit;

    // Pre-edit setup snapshot while a card drag-field gesture is live (see BeginFieldUndo/CommitFieldUndo).
    private static string? _fieldEditOldJson;


    // Name-field editing state: buffer follows the entity until the field takes focus.
    private static Guid _renameTargetId;
    private static SetupEntityKinds _renameTargetKind;
    private static string _renameBuffer = string.Empty;
    private static bool _renameFieldActive;

    private static SurfaceFacings FacingOf(float pitchDegrees)
    {
        const float tolerance = 0.5f;
        return MathF.Abs(pitchDegrees) < tolerance ? SurfaceFacings.Wall
               : MathF.Abs(pitchDegrees + 90) < tolerance ? SurfaceFacings.Floor
               : MathF.Abs(pitchDegrees - 90) < tolerance ? SurfaceFacings.Ceiling
               : SurfaceFacings.Tilted;
    }

    /** Drawn for a projector that has no camera data yet: centred lens, the default zoom. */
    private static readonly OutputDefinition.ProjectorCamera _unsolvedCamera = new();

    /** Why the last solve on an output failed, shown until the next try. */
    private static (Guid OutputId, string Message) _projectorSolveFailure;
}
