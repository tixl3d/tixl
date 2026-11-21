#nullable enable
using T3.Editor.SystemUi;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
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
using T3.Editor.Show;
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
internal static partial class SetupParameterView
{
    /** How the projector's zoom is entered: as a spec sheet's throw ratio, or as a vertical field of view. */
    private enum ZoomUnits
    {
        Ratio,
        Zoom,
    }

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

        // Entity cards can be far taller than the panel (a laser output: frame preview + scanner
        // camera + optimization + safety) and the parameter window deliberately has no scrollbar,
        // so the card body scrolls here. The header stays fixed. The EndChild pair below belongs to
        // this child and to setupEntity: it had been left without its BeginChild, which made every
        // frame end one window too many (Missing PopStyleVar / End() too many times / ImGui-ID).
        ImGui.BeginChild("##setupEntityCard", Vector2.Zero, ImGuiChildFlags.None,
                         ImGuiWindowFlags.AlwaysVerticalScrollbar);

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

        // Scroll headroom: the section cards auto-resize a frame behind their content, and live
        // readouts (scan stats, pump times) change that height while the show runs - without this
        // spacer the card tail clips at the panel's max scroll.
        FormInputs.AddVerticalSpace(20);

        ImGui.EndChild(); // ##setupEntityCard
        ImGui.EndChild(); // setupEntity
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
                    "What this content actually rendered at: the canvas of the output it is routed to, unless the op's Resolution Override or Resolution From says otherwise.",
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
                                   "Projects the surface's measuring lines onto the wall, so a line can be nudged until it lies along the real feature it was drawn on. Calibrate shows them too while it is on."))
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

    /// <summary>
    /// The outputs an output-card edit fans out to: the primary plus every other selected output
    /// of the same kind (ctrl/shift-extend in the outliner or on the board). Edits across a multi
    /// selection apply to all of them as one undo step; per-device aim geometry (setup rect, warp
    /// corners, masks) and identity stay on the primary - those are aimed per projector.
    /// </summary>
    private static List<OutputDefinition> CollectEditTargets(Setup setup, OutputDefinition primary)
    {
        var selection = GlobalSelectionHandling.SetupEntities;
        if (selection.Count < 2)
            return [primary];

        List<OutputDefinition>? targets = null;
        foreach (var target in selection.Targets)
        {
            if (target.Kind != SetupEntityKinds.Output)
                continue;

            var output = setup.FindOutput(target.EntityId);
            if (output == null || output.Kind != primary.Kind)
                continue;

            targets ??= [];
            targets.Add(output);
        }

        if (targets == null)
            return [primary];

        // The primary leads: first in the list, and always present even when its selection row
        // was pruned mid-draw.
        targets.Remove(primary);
        targets.Insert(0, primary);
        return targets;
    }

    /// <summary>An undoable output edit applied to every selected output of the primary's kind as one undo step.</summary>
    private static void EditOutputs(Setup setup, string undoLabel, OutputDefinition primary, Action<OutputDefinition> edit)
    {
        SetupUndo.RunUndoable(undoLabel, setup, () =>
        {
            foreach (var target in CollectEditTargets(setup, primary))
                edit(target);
        });
    }

    /// <summary>Safety-settings fan-out: every target gets its own settings object created on demand.</summary>
    private static void EditSafety(Setup setup, string undoLabel, OutputDefinition primary,
                                   Action<LaserProcessingSettings.SafetySettings> edit)
        => EditOutputs(setup, undoLabel, primary,
                       output => edit((output.LaserProcessing ??= new LaserProcessingSettings()).Safety));

    /// <summary>Path-optimizer fan-out, same as <see cref="EditSafety"/>.</summary>
    private static void EditOptimizer(Setup setup, string undoLabel, OutputDefinition primary,
                                      Action<LaserProcessingSettings.OptimizerSettings> edit)
        => EditOutputs(setup, undoLabel, primary,
                       output => edit((output.LaserProcessing ??= new LaserProcessingSettings()).Optimizer));

    /// <summary>Scanner-motion fan-out, same as <see cref="EditSafety"/>.</summary>
    private static void EditMotion(Setup setup, string undoLabel, OutputDefinition primary,
                                   Action<LaserProcessingSettings.MotionSettings> edit)
        => EditOutputs(setup, undoLabel, primary,
                       output => edit((output.LaserProcessing ??= new LaserProcessingSettings()).Motion));

    private static void DrawOutputCard(Setup setup, MachineConfig machineConfig, Guid id)
    {
        var output = setup.FindOutput(id);
        if (output == null)
            return;

        // A wider label column than other cards: the zoom row's unit switch sits in it.
        FormInputs.Indent(OutputCardExtraIndent);

        var isLaser = output.Kind == OutputDefinition.Kinds.Laser;
        if (isLaser)
        {
            var editTargetCount = CollectEditTargets(setup, output).Count;
            if (editTargetCount > 1)
            {
                FormInputs.AddHint($"Editing {editTargetCount} laser outputs together - the shared settings below apply to all of them. "
                                   + "Per-device aim (setup rect, warp corners, masks) stays on this one.");
            }

            DrawLaserStatusStrip(machineConfig, output);
            PanelLayout.BeginSection("Output");
        }

        // Laser outputs stream vector content instead of compositing a canvas, but Send means the
        // same thing to them: pause this output without dropping its device binding.
        var send = output.IsSending;
        var sendTooltip = output.Kind == OutputDefinition.Kinds.Laser
                              ? "Streams this laser output. Unticked, its bound scanners get nothing while the device stays connected."
                              : "Pause presenting without dropping the display binding.";
        if (FormInputs.AddCheckBox("Send", ref send, sendTooltip))
            EditOutputs(setup, "Toggle send", output, o => o.IsSending = send);

        // Show distribution: which boygroup node presents this output. Empty = every machine (the
        // classic behavior), so single-machine setups need none of this.
        var labels = new List<string> { "(all machines)", Environment.MachineName };
        foreach (var client in Show.BoygroupManager.Instance.Clients)
        {
            if (!string.IsNullOrEmpty(client.Name) && !labels.Contains(client.Name))
                labels.Add(client.Name);
        }

        var selectedLabel = string.IsNullOrEmpty(output.MachineName) ? labels[0] : output.MachineName;
        if (FormInputs.AddDropdown(ref selectedLabel, labels, "Render on machine",
                                   "In a boygrouped show only the chosen node presents this output. '(all machines)' keeps the default behavior."))
        {
            var value = labels.IndexOf(selectedLabel) > 0 ? selectedLabel : string.Empty;
            SetupUndo.RunUndoable("Assign output machine", setup, () => output.MachineName = value);
        }

        if (isLaser)
        {
            PanelLayout.EndSection();
            DrawLaserOutputCard(setup, output);
            FormInputs.Unindent(OutputCardExtraIndent);
            return;
        }

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
        if (DrawLayerList(setup, output))
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
                                       + "A display shows the canvas as it is.",
                                       isFlushLeft: true))
            {
                SetupUndo.RunUndoable(isProjector ? "Make projector" : "Make display", setup,
                                      () => output.Kind = isProjector ? OutputDefinition.Kinds.Projector : OutputDefinition.Kinds.Display);
            }

            if (output.Kind == OutputDefinition.Kinds.Projector)
                DrawProjectorCameraRows(setup, output);
        }

        FormInputs.Unindent(OutputCardExtraIndent);
    }

    /// <summary>
    /// Where the projector stands and how it zooms, for [UseProjectorCam]: solved from the output's aligned surface
    /// with the lens axis given, since one surface can't tell where the axis passes through the picture.
    /// </summary>
    private static void DrawProjectorCameraRows(Setup setup, OutputDefinition output)
    {
        FormInputs.DrawInputLabel("Model");
        ProjectorPresets.DrawSelector(setup, output);
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

        FormInputs.AddVerticalSpace(6);
        DrawZoomRows(setup, output);

        FormInputs.AddVerticalSpace(4);
        ProjectorSideView.Draw(setup, output);
        FormInputs.AddVerticalSpace(16);

        var syncs = camera?.SyncsToSurface ?? true;
        if (FormInputs.AddCheckBox("Sync to Surface Calibration", ref syncs,
                                   "Solves where the projector stands and how it zooms from the largest surface corner-pinned to this output, "
                                   + "again whenever that surface is calibrated: while points are aimed only the position follows, the zoom is "
                                   + "measured once they are let go. Turn it off to keep the camera as it is. Set the lens shift first, and turn "
                                   + "the projector's own digital keystone correction off.",
                                   isFlushLeft: true))
        {
            SetupUndo.RunUndoable(syncs ? "Sync projector camera" : "Keep projector camera", setup,
                                  () => (output.Camera ??= new OutputDefinition.ProjectorCamera()).SyncsToSurface = syncs);
        }

        FormInputs.AddHint(ProjectorCalibration.DescribeCamera(setup, output));
    }

    /** Matches how FormInputs fades a field that holds its default value. */
    private const float DefaultValueAlpha = 0.7f;

    /** Pure UI state: whether the gamma row drives all three channels with one value. */
    private static bool _linkGammaChannels;

    private static void DrawCardDivider()
    {
        FormInputs.AddVerticalSpace(8);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(8);
    }

    /// <summary>
    /// What a laser output needs at a glance: where it streams, whether the stream is healthy, and the
    /// two safety states (calibration and beam) that decide what actually reaches the scanners.
    /// </summary>
    private static void DrawLaserStatusStrip(MachineConfig machineConfig, OutputDefinition output)
    {
        var processing = output.LaserProcessing ??= new LaserProcessingSettings();
        var safety = processing.Safety;

        // A laser output may bind several plugs - the same frame fans out to each device.
        _boundPlugNames.Clear();
        foreach (var binding in machineConfig.FindLaserBindings(output.Id))
        {
            var plug = machineConfig.FindLaserPlug(binding.PlugId);
            if (plug != null)
                _boundPlugNames.Add(plug.Name);
        }

        var hasStream = LaserDriver.TryGetStreamStatus(output.Id, out var streamStatus, out var streamLevel);
        var streamColor = !hasStream
                              ? UiColors.TextMuted
                              : streamLevel switch
                                    {
                                        IStatusProvider.StatusLevel.Success => UiColors.StatusOkay,
                                        IStatusProvider.StatusLevel.Warning or IStatusProvider.StatusLevel.Error => UiColors.StatusAttention,
                                        _ => UiColors.TextMuted,
                                    };
        var streamLabel = !hasStream
                              ? "No driver stream"
                              : streamLevel == IStatusProvider.StatusLevel.Success
                                  ? "Driver OK"
                                  : streamLevel is IStatusProvider.StatusLevel.Warning or IStatusProvider.StatusLevel.Error
                                      ? "Driver problem"
                                      : "Driver idle";

        PanelLayout.StatusPill(streamLabel, streamColor);
        CustomComponents.TooltipForLastItem("Live status of the driver streaming this output",
                                            hasStream ? streamStatus : "No laser stream yet - is the master laser on?");

        ImGui.SameLine(0, PanelLayout.SpaceS * T3Ui.UiScaleFactor);
        PanelLayout.StatusPill(safety.Calibrated ? "Calibrated" : "Uncalibrated",
                               safety.Calibrated ? UiColors.StatusOkay : UiColors.StatusAttention);
        CustomComponents.TooltipForLastItem(safety.Calibrated
                                                ? "Content streams, limited by the safety settings below."
                                                : "Only the alignment rectangle streams until this output is calibrated.");

        ImGui.SameLine(0, PanelLayout.SpaceS * T3Ui.UiScaleFactor);
        PanelLayout.StatusPill(safety.BeamEnable ? "Beam on" : "Beam off",
                               safety.BeamEnable ? UiColors.StatusOkay : UiColors.TextMuted);
        CustomComponents.TooltipForLastItem("The output's beam switch: off blanks every point.");

        PanelLayout.Space(PanelLayout.SpaceXS);
        var boundTo = _boundPlugNames.Count == 0
                          ? "Unbound - drop this output on a laser plug (it can bind several)"
                          : $"Streams to {string.Join(", ", _boundPlugNames)} (edit a plug under Local Bindings)";
        DrawWrappedSmallText(boundTo, UiColors.TextMuted);

        if (hasStream)
            DrawWrappedSmallTextStable(streamStatus, UiColors.TextMuted, 3);

        PanelLayout.Space(PanelLayout.SpaceM);
    }

    /// <summary>
    /// A laser output carries the show: the processing pipeline (optimization + safety — managed here
    /// rather than in graph ops). The projection itself is the routed [LaserCamera]'s own aim — its
    /// Position/Target/Use Rotation/FieldOfView on the op. The device — address, port, device settings
    /// — lives on the laser plug it is bound to; select the plug in LOCAL BINDINGS to edit it.
    /// Setting a [LaserCamera]'s OutputRef to this output routes the camera's frame here.
    /// </summary>
    private static void DrawLaserOutputCard(Setup setup, OutputDefinition output)
    {
        DrawLaserProcessing(setup, output);

        // The projector card's venue map, same rows: an image to place the safe zone, masks and
        // warp corners against while aiming. Lasers have no patches, so nothing follows it here.
        var routedContent = LaserContent.Cameras
                                        .OfType<Instance>()
                                        .Where(c => LaserContent.OutputRefOf(c) == output.Id)
                                        .Select(c => c.Symbol.Name)
                                        .ToList();
        FormInputs.AddHint(routedContent.Count > 0
                               ? $"Content: {string.Join(", ", routedContent)}"
                               : "Content: nothing routed yet - set the routed camera op's Output reference to this output.");

        DrawCardDivider();
        DrawReferenceLayoutRows(setup, output);
    }

    private static void DrawWrappedSmallText(string text, T3.Core.DataTypes.Vector.Color color)
    {
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    /// <summary>
    /// Wrapped small text inside a fixed line-count slot. Live timings (the pump stats in a
    /// stream status) change the message length every frame, and a strip that resizes with them
    /// shifts everything below one frame late - which clips the card tail at the panel's max
    /// scroll. The slot keeps the height constant; longer text clips at the last line.
    /// </summary>
    private static void DrawWrappedSmallTextStable(string text, T3.Core.DataTypes.Vector.Color color, int lines)
    {
        ImGui.PushFont(Fonts.FontSmall);
        var lineHeight = ImGui.GetTextLineHeight();
        ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();

        var renderedLines = Math.Max(1, (int)MathF.Round(ImGui.GetItemRectSize().Y / lineHeight));
        var missing = lines - renderedLines;
        if (missing > 0)
            ImGui.Dummy(new Vector2(1, missing * lineHeight));

        ImGui.PopFont();
    }

    /// <summary>The streamed frame, live - the same drawing the Board card shows, where the output is edited.</summary>
    private static void DrawLaserFramePreview(Setup setup, OutputDefinition output)
    {
        // A square frame reads like the scanner field; capped so it does not stretch across a wide panel.
        var previewSize = MathF.Min(ImGui.GetContentRegionAvail().X, 200f * T3Ui.UiScaleFactor);
        var previewMin = ImGui.GetCursorScreenPos();
        var previewMax = new Vector2(previewMin.X + previewSize, previewMin.Y + previewSize);
        var previewDl = ImGui.GetWindowDrawList();
        previewDl.AddRectFilled(previewMin, previewMax, UiColors.BackgroundPopup.Fade(0.6f));

        // The venue map (Reference Layout) under the points, the way the board draws it over the
        // cards - so the safe zone and masks are aimed against the room, not a black square.
        var reference = setup.FindReferenceImage(output.ReferenceImageId);
        if (reference != null && output.ReferenceOpacity > 0f
                               && SetupOutputView.TryGetReferenceSrv(reference) is { IsDisposed: false } mapSrv)
        {
            previewDl.AddImage((IntPtr)mapSrv.ImGuiTextureId, previewMin, previewMax, Vector2.Zero, Vector2.One,
                               UiColors.ForegroundFull.Fade(output.ReferenceOpacity));
        }

        LaserOutputPreview.DrawFrame(previewDl, previewMin, previewMax, LaserOutputPreview.GetFrame(output));

        // Safe-zone outline and, with frame warp on, the four draggable keystone corners.
        var processing = output.LaserProcessing ??= new LaserProcessingSettings();
        DrawLaserFrameOverlay(setup, processing.Safety, previewDl, previewMin, previewMax);

        ImGui.SetCursorScreenPos(new Vector2(previewMin.X, previewMax.Y));
        ImGui.Dummy(Vector2.Zero);
        PanelLayout.Space(PanelLayout.SpaceM);
    }

    /// <summary>
    /// Over the frame preview: the safe zone the corners must stay inside, and — with frame warp
    /// on — the warp quad with its four draggable corner handles. This is where the laser output's
    /// "basic screen" is trapezed: drag a corner and the quad follows, clamped to the safe zone,
    /// as one undo step per drag.
    /// </summary>
    private static void DrawLaserFrameOverlay(Setup setup, LaserProcessingSettings.SafetySettings safety,
                                              ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        var uiScale = T3Ui.UiScaleFactor;
        var inset = 3f * uiScale;
        var side = MathF.Min(max.X - min.X, max.Y - min.Y) - 2f * inset;
        if (side < 16f)
            return;

        var projection = new FieldProjection { Center = (min + max) * 0.5f, Half = side * 0.5f };

        var zoneHalfW = MathF.Max(0f, safety.SafeZoneWidth) * 0.5f;
        var zoneHalfH = MathF.Max(0f, safety.SafeZoneHeight) * 0.5f;
        var zoneMin = new Vector2(Math.Clamp(safety.SafeZoneCenterX - zoneHalfW, -1f, 1f),
                                  Math.Clamp(safety.SafeZoneCenterY - zoneHalfH, -1f, 1f));
        var zoneMax = new Vector2(Math.Clamp(safety.SafeZoneCenterX + zoneHalfW, -1f, 1f),
                                  Math.Clamp(safety.SafeZoneCenterY + zoneHalfH, -1f, 1f));
        dl.AddRect(projection.CanvasToScreen(zoneMin), projection.CanvasToScreen(zoneMax),
                   UiColors.TextMuted.Fade(0.3f), 0, ImDrawFlags.None, MathF.Max(1f, MathF.Round(uiScale)));

        // Safety masks: every quad outlined (allow masks green, blank masks orange, disabled
        // dimmed); the selected one gets the draggable corner handles, same gesture as the
        // keystone. Drawn whatever the warp state - masks apply after the warp, so they land
        // exactly where they are drawn.
        if (_laserMaskSelected >= safety.Masks.Count)
            _laserMaskSelected = -1;

        for (var m = 0; m < safety.Masks.Count; m++)
        {
            var mask = safety.Masks[m];
            Span<Vector2> maskCorners =
            [
                new Vector2(mask.X1, mask.Y1),
                new Vector2(mask.X2, mask.Y2),
                new Vector2(mask.X3, mask.Y3),
                new Vector2(mask.X4, mask.Y4),
            ];

            var maskColor = !mask.Enabled
                                ? UiColors.TextMuted.Fade(0.3f)
                                : mask.AllowOnlyInside
                                    ? UiColors.StatusOkay.Fade(0.8f)
                                    : UiColors.StatusAttention.Fade(0.8f);
            for (var i = 0; i < 4; i++)
            {
                dl.AddLine(projection.CanvasToScreen(maskCorners[i]), projection.CanvasToScreen(maskCorners[(i + 1) % 4]),
                           maskColor, 1.5f * uiScale);
            }

            if (m != _laserMaskSelected)
                continue;

            var handleStyle = CanvasPointHandle.Style.Default(mask.Enabled ? UiColors.StatusAttention : UiColors.TextMuted);
            handleStyle.Radius = 4;
            for (var i = 0; i < 4; i++)
            {
                ImGui.PushID(100 + m * 4 + i);
                var phase = CanvasPointHandle.Draw(ref maskCorners[i], projection, handleStyle);
                if (phase != CanvasPointHandle.DragPhases.None)
                {
                    maskCorners[i] = ClampToZone(maskCorners[i], zoneMin, zoneMax);
                    SetMaskCorner(safety, m, i, maskCorners[i]);

                    if (phase == CanvasPointHandle.DragPhases.Started)
                    {
                        _laserMaskDragOldJson = setup.ToJsonString();
                        OutputSetupHandling.NotifyLiveEdit();
                    }
                    else if (phase == CanvasPointHandle.DragPhases.Completed && _laserMaskDragOldJson != null)
                    {
                        SetupUndo.CommitGesture(setup, "Edit laser mask corners", _laserMaskDragOldJson);
                        _laserMaskDragOldJson = null;
                    }
                }

                ImGui.PopID();
            }
        }

        if (!safety.FrameWarp)
            return;

        Span<Vector2> corners =
        [
            new Vector2(safety.WarpCorner1X, safety.WarpCorner1Y),
            new Vector2(safety.WarpCorner2X, safety.WarpCorner2Y),
            new Vector2(safety.WarpCorner3X, safety.WarpCorner3Y),
            new Vector2(safety.WarpCorner4X, safety.WarpCorner4Y),
        ];

        var quadColor = UiColors.StatusAttention.Fade(0.8f);
        for (var i = 0; i < 4; i++)
        {
            dl.AddLine(projection.CanvasToScreen(corners[i]), projection.CanvasToScreen(corners[(i + 1) % 4]),
                       quadColor, 1.5f * uiScale);
        }

        var style = CanvasPointHandle.Style.Default(UiColors.StatusAttention);
        style.Radius = 4;
        for (var i = 0; i < 4; i++)
        {
            ImGui.PushID(i);
            var phase = CanvasPointHandle.Draw(ref corners[i], projection, style);
            if (phase != CanvasPointHandle.DragPhases.None)
            {
                corners[i] = ClampToZone(corners[i], zoneMin, zoneMax);
                SetWarpCorner(safety, i, corners[i]);

                if (phase == CanvasPointHandle.DragPhases.Started)
                {
                    _laserWarpDragOldJson = setup.ToJsonString();
                    OutputSetupHandling.NotifyLiveEdit();
                }
                else if (phase == CanvasPointHandle.DragPhases.Completed && _laserWarpDragOldJson != null)
                {
                    SetupUndo.CommitGesture(setup, "Warp laser frame corners", _laserWarpDragOldJson);
                    _laserWarpDragOldJson = null;
                }
            }

            ImGui.PopID();
        }
    }

    private static Vector2 ClampToZone(Vector2 corner, Vector2 zoneMin, Vector2 zoneMax)
    {
        return new Vector2(Math.Clamp(corner.X, zoneMin.X, zoneMax.X),
                           Math.Clamp(corner.Y, zoneMin.Y, zoneMax.Y));
    }

    private static void SetMaskCorner(LaserProcessingSettings.SafetySettings safety, int maskIndex, int cornerIndex, Vector2 value)
    {
        var mask = safety.Masks[maskIndex];
        switch (cornerIndex)
        {
            case 0:
                mask.X1 = value.X;
                mask.Y1 = value.Y;
                break;
            case 1:
                mask.X2 = value.X;
                mask.Y2 = value.Y;
                break;
            case 2:
                mask.X3 = value.X;
                mask.Y3 = value.Y;
                break;
            case 3:
                mask.X4 = value.X;
                mask.Y4 = value.Y;
                break;
        }

        safety.MasksChanged();
    }

    private static void SetWarpCorner(LaserProcessingSettings.SafetySettings safety, int corner, Vector2 value)
    {
        switch (corner)
        {
            case 0:
                safety.WarpCorner1X = value.X;
                safety.WarpCorner1Y = value.Y;
                break;
            case 1:
                safety.WarpCorner2X = value.X;
                safety.WarpCorner2Y = value.Y;
                break;
            case 2:
                safety.WarpCorner3X = value.X;
                safety.WarpCorner3Y = value.Y;
                break;
            case 3:
                safety.WarpCorner4X = value.X;
                safety.WarpCorner4Y = value.Y;
                break;
        }
    }

    /// <summary>Maps normalized field units (-1..1, y up) onto the square preview rect (y down) —
    /// the same inscribed square <see cref="LaserOutputPreview"/> draws the frame into, so the
    /// handles sit exactly on the quad they warp.</summary>
    private sealed class FieldProjection : ICanvasProjection
    {
        public Vector2 Center;
        public float Half;

        public Vector2 CanvasToScreen(Vector2 posInCanvas)
        {
            return new Vector2(Center.X + posInCanvas.X * Half, Center.Y - posInCanvas.Y * Half);
        }

        public Vector2 ScreenToCanvas(Vector2 posOnScreen)
        {
            return new Vector2((posOnScreen.X - Center.X) / Half, -(posOnScreen.Y - Center.Y) / Half);
        }
    }

    private static string? _laserWarpDragOldJson;

    /** The laser output card's mask currently showing its draggable corner handles; -1 = none. */
    private static int _laserMaskSelected = -1;
    private static string? _laserMaskDragOldJson;
    private static readonly List<string> _boundPlugNames = [];

    /// <summary>
    /// A laser plug's own card: the endpoint (address, port, network interface) and the device settings
    /// (scan rate, brightness ceiling, channel order, protocol identity) live HERE, at the plug in local
    /// bindings — the output bound here carries the show (camera, pipeline), never the device address.
    /// </summary>
    private static void DrawLaserPlugCard(MachineConfig machineConfig, LaserPlug plug, OutputDefinition? boundOutput)
    {
        // One control for the endpoint: type an address, or open the chevron to pick a discovered
        // device (Ether Dream DACs announce themselves by UDP broadcast).
        if (plug.Kind == LaserPlug.Kinds.EtherDream)
            LaserPlugDiscovery.EnsureEtherDreamDiscovery();
        else if (plug.Kind == LaserPlug.Kinds.Citp)
            CitpPeerDiscovery.EnsureStarted();
        else if (plug.Kind == LaserPlug.Kinds.Ponk)
            PonkSenderDiscovery.EnsureStarted();
        else if (plug.Kind == LaserPlug.Kinds.ShowNet)
            LaserPlugDiscovery.EnsureShowNetDiscovery();

        // An address left empty gets a working basic one as soon as something is known - the
        // first active CITP peer (instantly, while it announces), the first discovered Ether
        // Dream DAC, or the PONK multicast group. Typing always wins afterwards.
        AutoPreselectLaserEndpoint(machineConfig, plug);

        DrawLaserPlugStatus(plug, boundOutput);

        PanelLayout.BeginSection("Connection");
        FormInputs.DrawInputLabel("Address");
        if (_laserAddressPlug != plug.Id)
        {
            _laserAddressPlug = plug.Id;
            _laserAddressBuffer = plug.Address;
        }

        var addressArrow = DrawEditableComboField("##laserAddress", ref _laserAddressBuffer, 64,
                                                  $"##laserAddressPick{plug.Id:N}");
        if (addressArrow)
            Plugs.UpdateLaser(machineConfig, plug.Id, null, _laserAddressBuffer.Trim(), null);

        CustomComponents.TooltipForLastItem(plug.Kind switch
                                                {
                                                    LaserPlug.Kinds.Ponk => "The PONK host: the 239.255.10.24 multicast group, or a receiver's unicast IP.",
                                                    LaserPlug.Kinds.Citp => "The CITP capture host (used with a Port above 0; 0 keeps PLoc multicast discovery).",
                                                    LaserPlug.Kinds.ShowNet => "The showNET laser's IP address on the LAN — type it, or pick a discovered laser from the chevron.",
                                                    _ => "The Ether Dream DAC's IP address on the LAN — type it, or pick a discovered device from the chevron.",
                                                });

        if (plug.Kind == LaserPlug.Kinds.EtherDream && ImGui.BeginPopup($"##laserAddressPick{plug.Id:N}"))
        {
            var dacs = LaserPlugDiscovery.EtherDreamDacs;
            var connected = LaserPlugDiscovery.ConnectedDevice;

            if (dacs.Count == 0 && connected == null)
                CustomComponents.StylizedText("discovering…", Fonts.FontSmall, UiColors.TextMuted);

            foreach (var dac in dacs)
            {
                if (ImGui.Selectable($"{dac.Name} ({dac.Ip})") && dac.Ip != plug.Address)
                {
                    _laserAddressBuffer = dac.Ip;
                    Plugs.UpdateLaser(machineConfig, plug.Id, null, dac.Ip, null);
                    ImGui.CloseCurrentPopup();
                }
            }

            // A DAC being streamed to stops its discovery broadcasts — offer what an output op
            // is connected to, so the picker works during a show too.
            if (connected is { } device && dacs.All(d => !string.Equals(d.Ip, device.Ip, StringComparison.OrdinalIgnoreCase)))
            {
                if (ImGui.Selectable($"● connected: {device.Name} ({device.Ip})") && device.Ip != plug.Address)
                {
                    _laserAddressBuffer = device.Ip;
                    Plugs.UpdateLaser(machineConfig, plug.Id, null, device.Ip, null);
                    ImGui.CloseCurrentPopup();
                }
            }

            CustomComponents.StylizedText("DACs stop broadcasting while a show streams to them — the connected device is listed above.",
                                          Fonts.FontSmall, UiColors.TextMuted);
            ImGui.EndPopup();
        }
        else if (plug.Kind == LaserPlug.Kinds.Citp && ImGui.BeginPopup($"##laserAddressPick{plug.Id:N}"))
        {
            // CITP peers announce themselves (CITP PLoc on 224.0.0.180:4809) - Capture, Depence,
            // media servers. Picking one sets host and port for a direct connection.
            CitpPeerDiscovery.EnsureStarted();
            var peers = CitpPeerDiscovery.Peers;
            if (peers.Count == 0)
                CustomComponents.StylizedText("listening for CITP peers announcing themselves…", Fonts.FontSmall, UiColors.TextMuted);

            foreach (var peer in peers)
            {
                if (ImGui.Selectable($"{peer.Name} ({peer.Type}) — {peer.Ip}:{peer.TcpPort}")
                    && (peer.Ip != plug.Address || peer.TcpPort != plug.Port))
                {
                    _laserAddressBuffer = peer.Ip;
                    Plugs.UpdateLaser(machineConfig, plug.Id, null, peer.Ip, peer.TcpPort);
                    ImGui.CloseCurrentPopup();
                }
            }

            CustomComponents.StylizedText("Peers appear as they announce themselves on 224.0.0.180:4809.",
                                          Fonts.FontSmall, UiColors.TextMuted);
            ImGui.EndPopup();
        }
        else if (plug.Kind == LaserPlug.Kinds.Ponk && ImGui.BeginPopup($"##laserAddressPick{plug.Id:N}"))
        {
            PonkSenderDiscovery.EnsureStarted();

            // Multicast first - it reaches every receiver and needs no address at all.
            const string multicastAddress = "239.255.10.24";
            if (ImGui.Selectable($"Multicast group — {multicastAddress} (all receivers)")
                && plug.Address != multicastAddress)
            {
                _laserAddressBuffer = multicastAddress;
                Plugs.UpdateLaser(machineConfig, plug.Id, null, multicastAddress, null);
                ImGui.CloseCurrentPopup();
            }

            var senders = PonkSenderDiscovery.Senders;
            foreach (var sender in senders)
            {
                if (ImGui.Selectable($"{sender.Name} — {sender.Ip}") && sender.Ip != plug.Address)
                {
                    _laserAddressBuffer = sender.Ip;
                    Plugs.UpdateLaser(machineConfig, plug.Id, null, sender.Ip, null);
                    ImGui.CloseCurrentPopup();
                }
            }

            if (senders.Count == 0)
                CustomComponents.StylizedText("listening for PONK senders on 239.255.10.24:5583…", Fonts.FontSmall, UiColors.TextMuted);

            CustomComponents.StylizedText("Multicast reaches every receiver without an address; a receive-only MadMapper cannot announce itself, so only senders on the group are listed.",
                                          Fonts.FontSmall, UiColors.TextMuted);
            ImGui.EndPopup();
        }
        else if (plug.Kind == LaserPlug.Kinds.ShowNet && ImGui.BeginPopup($"##laserAddressPick{plug.Id:N}"))
        {
            LaserPlugDiscovery.EnsureShowNetDiscovery();
            var lasers = LaserPlugDiscovery.ShowNetLasers;
            var connected = LaserPlugDiscovery.ConnectedDevice;

            if (lasers.Count == 0 && connected == null)
                CustomComponents.StylizedText("probing for showNET lasers...", Fonts.FontSmall, UiColors.TextMuted);

            foreach (var laser in lasers)
            {
                if (ImGui.Selectable($"{laser.Name} ({laser.Ip})") && laser.Ip != plug.Address)
                {
                    _laserAddressBuffer = laser.Ip;
                    Plugs.UpdateLaser(machineConfig, plug.Id, null, laser.Ip, null);
                    ImGui.CloseCurrentPopup();
                }
            }

            CustomComponents.StylizedText("Lasers answer the broadcast probe while streaming; entries age out after ~12 s of silence.",
                                          Fonts.FontSmall, UiColors.TextMuted);
            ImGui.EndPopup();
        }

        // Ether Dream DACs listen on the protocol-fixed port, so editing would be a no-op — shown read-only
        // at its effective value; PONK and CITP endpoints are configurable.
        if (plug.Kind is LaserPlug.Kinds.EtherDream or LaserPlug.Kinds.ShowNet)
        {
            var defaultPort = plug.Kind == LaserPlug.Kinds.EtherDream ? 7765 : 41855;
            Span<int> port = [plug.Port > 0 ? plug.Port : defaultPort];
            var portNote = plug.Kind == LaserPlug.Kinds.EtherDream
                               ? "The Ether Dream protocol port is fixed at 7765."
                               : "The showNET protocol port is fixed at 41855.";
            DrawIntsRow("Port", port, portNote, readOnly: true);
        }
        else
        {
            Span<int> port = [plug.Port];
            var portState = DrawIntsRow("Port", port, "Remote port; 0 = the protocol's default (PONK 5583, CITP discovery).");
            if ((portState & InputEditStateFlags.Modified) != 0)
                Plugs.UpdateLaser(machineConfig, plug.Id, null, null, port[0]);
        }

        FormInputs.DrawInputLabel("Interface");
        if (_laserTextKey != plug.Id + ":if")
        {
            _laserTextKey = plug.Id + ":if";
            _laserTextBuffer = plug.LocalAddress;
        }

        var interfaceArrow = DrawEditableComboField("##laserLocal", ref _laserTextBuffer, 40,
                                                    $"##laserInterfacePick{plug.Id:N}");
        if (interfaceArrow)
        {
            plug.LocalAddress = _laserTextBuffer.Trim();
            OutputSetupHandling.SaveActive();
        }

        CustomComponents.TooltipForLastItem(plug.Kind == LaserPlug.Kinds.EtherDream
                                                ? "Not used by Ether Dream: the DAC is reached over the operating system's route."
                                                : "Network interface packets leave on (this machine's NIC IP); empty = any. Type it, or pick from the chevron.");

        if (ImGui.BeginPopup($"##laserInterfacePick{plug.Id:N}"))
        {
            foreach (var nic in LaserPlugDiscovery.InterfaceAddresses)
            {
                if (ImGui.Selectable($"{nic.Name}: {nic.Ip}") && nic.Ip != plug.LocalAddress)
                {
                    _laserTextBuffer = nic.Ip;
                    plug.LocalAddress = nic.Ip;
                    OutputSetupHandling.SaveActive();
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndPopup();
        }

        PanelLayout.EndSection();

        DrawLaserDeviceSettings(machineConfig, plug);
    }

    /// <summary>The plug's protocol, what it streams for, and the live status of the driver behind it.</summary>
    private static void DrawLaserPlugStatus(LaserPlug plug, OutputDefinition? boundOutput)
    {
        PanelLayout.StatusPill(plug.Kind.ToString(), UiColors.TextMuted);

        var hasStream = boundOutput != null
                        && LaserDriver.TryGetStreamStatus(boundOutput.Id, out _, out _);
        ImGui.SameLine(0, PanelLayout.SpaceS * T3Ui.UiScaleFactor);
        if (boundOutput == null)
        {
            PanelLayout.StatusPill("Nothing bound", UiColors.TextMuted);
            PanelLayout.Space(PanelLayout.SpaceM);
            return;
        }

        LaserDriver.TryGetStreamStatus(boundOutput.Id, out var opStatus, out var opLevel);
        var statusColor = !hasStream
                              ? UiColors.TextMuted
                              : opLevel switch
                                    {
                                        IStatusProvider.StatusLevel.Success => UiColors.StatusOkay,
                                        IStatusProvider.StatusLevel.Warning or IStatusProvider.StatusLevel.Error => UiColors.StatusAttention,
                                        _ => UiColors.TextMuted,
                                    };
        PanelLayout.StatusPill(hasStream ? "Driver connected" : "No driver stream", statusColor);
        CustomComponents.TooltipForLastItem("Live status of the driver streaming to this plug's device.",
                                            hasStream ? opStatus : "No laser stream for this plug yet - is the master laser on?");

        PanelLayout.Space(PanelLayout.SpaceXS);
        DrawWrappedSmallText($"Streaming for {boundOutput.Name}", UiColors.TextMuted);
        if (hasStream)
            DrawWrappedSmallTextStable(opStatus, statusColor, 3); // pump timings change every frame

        PanelLayout.Space(PanelLayout.SpaceM);
    }

    /// <summary>
    /// Fills a laser plug's empty address with a working basic endpoint as soon as one is known:
    /// the first announcing CITP peer (direct host+port), the first discovered Ether Dream DAC,
    /// or the PONK multicast group. Runs once per plug — as soon as the address is non-empty
    /// (by this write or by typing) it never touches it again.
    /// </summary>
    private static void AutoPreselectLaserEndpoint(MachineConfig machineConfig, LaserPlug plug)
    {
        if (!string.IsNullOrWhiteSpace(plug.Address))
            return;

        switch (plug.Kind)
        {
            case LaserPlug.Kinds.Citp:
            {
                // "Select the first active instantly": the plug starts talking to the first peer
                // that announced itself instead of waiting for a manual pick.
                var peer = CitpPeerDiscovery.Peers.FirstOrDefault();
                if (peer == null)
                    return;

                _laserAddressBuffer = peer.Ip;
                Plugs.UpdateLaser(machineConfig, plug.Id, null, peer.Ip, peer.TcpPort);
                break;
            }
            case LaserPlug.Kinds.EtherDream:
            {
                var dacs = LaserPlugDiscovery.EtherDreamDacs;
                if (dacs.Count == 0)
                    return;

                _laserAddressBuffer = dacs[0].Ip;
                Plugs.UpdateLaser(machineConfig, plug.Id, null, dacs[0].Ip, null);
                break;
            }
            case LaserPlug.Kinds.Ponk:
            {
                // Multicast group = the PONK basic: reaches every receiver without an address.
                const string multicast = "239.255.10.24";
                _laserAddressBuffer = multicast;
                Plugs.UpdateLaser(machineConfig, plug.Id, null, multicast, null);
                break;
            }
            case LaserPlug.Kinds.ShowNet:
            {
                // The first discovered laser = the showNET basic: the probe listener starts with
                // the plug card, so the first announcement arrives within a probe interval.
                var lasers = LaserPlugDiscovery.ShowNetLasers;
                if (lasers.Count == 0)
                    return;

                _laserAddressBuffer = lasers[0].Ip;
                Plugs.UpdateLaser(machineConfig, plug.Id, null, lasers[0].Ip, null);
                break;
            }
        }
    }

    /// <summary>
    /// An editable combo: an input line with a chevron button at its right that opens popupId (the
    /// device/NIC list drawn by the caller). Typing commits on deactivate; returns true then, with
    /// the buffer holding the committed text.
    /// </summary>
    private static bool DrawEditableComboField(string fieldId, ref string buffer, uint maxLength, string? popupId)
    {
        var scale = T3Ui.UiScaleFactor;
        var gap = 4 * scale;
        var arrowWidth = popupId != null ? ImGui.GetFrameHeight() : 0f;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - arrowWidth - gap);
        ImGui.InputText(fieldId, ref buffer, maxLength);
        var committed = ImGui.IsItemDeactivatedAfterEdit();

        if (popupId == null)
            return committed;

        ImGui.SameLine(0, gap);
        // IconButton pushes the icon's id internally — two chevrons on one card (address +
        // interface) would collide, so scope it under the unique popup id.
        ImGui.PushID(popupId);
        var chevronClicked = CustomComponents.IconButton(Icon.ChevronDown, Vector2.Zero);
        ImGui.PopID();
        if (chevronClicked)
            ImGui.OpenPopup(popupId);

        return committed;
    }

    /// <summary>Device settings of the plug — machine state, saved on change like the address.</summary>
    private static void DrawLaserDeviceSettings(MachineConfig machineConfig, LaserPlug plug)
    {
        PanelLayout.BeginSection("Device");

        if (plug.Kind == LaserPlug.Kinds.EtherDream)
        {
            var device = plug.EtherDreamSettings;
            Span<int> scanRate = [device.ScanRate];
            var scanState = DrawIntsRow("Scan rate (pps)", scanRate,
                                        "Points per second requested from the DAC; 0 = the output op's own input. Stream more points per second than this or the DAC buffer drains and playback halts.",
                                        readOnly: false, reserveRight: 22);
            if ((scanState & InputEditStateFlags.Modified) != 0)
            {
                device.ScanRate = Math.Clamp(scanRate[0], 0, 100000);
                OutputSetupHandling.SaveActive();
            }

            DrawRowReset("ResetScanRate", "scan rate",
                         () =>
                             {
                                 device.ScanRate = 30000;
                                 OutputSetupHandling.SaveActive();
                             }, null);

            DrawLaserCeilingAndSwap(machineConfig, plug, ref device.MaxBrightness, ref device.SwapColorChannels);
        }
        else if (plug.Kind == LaserPlug.Kinds.Ponk)
        {
            var device = plug.PonkSettings;

            var multicast = device.UseMulticast;
            if (FormInputs.AddCheckBox("Multicast", ref multicast, "Send to the multicast group (how MadMapper discovers PONK senders); off sends unicast."))
            {
                device.UseMulticast = multicast;
                OutputSetupHandling.SaveActive();
            }

            DrawRowReset("ResetMulticast", "multicast",
                         () =>
                             {
                                 device.UseMulticast = true;
                                 OutputSetupHandling.SaveActive();
                             }, null);

            FormInputs.DrawInputLabel("Sender name");
            if (_laserTextKey != plug.Id + ":sender")
            {
                _laserTextKey = plug.Id + ":sender";
                _laserTextBuffer = device.SenderName;
            }

            if (ImGui.InputText("##laserSender", ref _laserTextBuffer, 32))
            {
                // committed on deactivate
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                device.SenderName = _laserTextBuffer.Trim();
                OutputSetupHandling.SaveActive();
            }

            ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
            ImGui.PushID("ResetSenderName");
            if (CustomComponents.IconButton(Icon.Reset, Vector2.Zero))
            {
                device.SenderName = "TiXL";
                _laserTextBuffer = device.SenderName;
                OutputSetupHandling.SaveActive();
            }

            ImGui.PopID();
            CustomComponents.TooltipForLastItem("Reset sender name", "Restores the default value.");

            Span<float> maxSpeed = [device.MaxScanSpeed];
            var speedState = DrawFloatsRow("Scan speed (x)", maxSpeed, "Sent as MAXSPEED path metadata - the requested scanner-speed multiplier. 1.0 = neutral.",
                                           reserveRight: 22);
            if ((speedState & InputEditStateFlags.Modified) != 0)
            {
                device.MaxScanSpeed = MathF.Max(0.01f, maxSpeed[0]);
                OutputSetupHandling.SaveActive();
            }

            DrawRowReset("ResetScanSpeed", "scan speed",
                         () =>
                             {
                                 device.MaxScanSpeed = 1f;
                                 OutputSetupHandling.SaveActive();
                             }, null);

            var ponkKeepalive = device.Keepalive;
            if (FormInputs.AddCheckBox("Keepalive", ref ponkKeepalive,
                                       "Keeps streaming while the LASER master is off. A preview override for receivers that only visualize - PONK receivers can drive real scanners, so while this is off the master stays a hard kill."))
            {
                device.Keepalive = ponkKeepalive;
                OutputSetupHandling.SaveActive();
            }

            DrawRowReset("ResetPonkKeepalive", "keepalive",
                         () =>
                             {
                                 device.Keepalive = false;
                                 OutputSetupHandling.SaveActive();
                             }, null);

            DrawLaserCeilingAndSwap(machineConfig, plug, ref device.MaxBrightness, ref device.SwapColorChannels);
        }
        else if (plug.Kind == LaserPlug.Kinds.ShowNet)
        {
            var device = plug.ShowNetSettings;
            Span<int> scanRate = [device.ScanRate];
            var scanState = DrawIntsRow("Scan rate (pps)", scanRate, "Points per second requested from the laser; 0 = the 30 kpps default — the device's ticks-per-point divisor.",
                                        reserveRight: 22);
            if ((scanState & InputEditStateFlags.Modified) != 0)
            {
                device.ScanRate = Math.Clamp(scanRate[0], 0, 100000);
                OutputSetupHandling.SaveActive();
            }

            DrawRowReset("ResetShowNetScanRate", "scan rate",
                         () =>
                             {
                                 device.ScanRate = 30000;
                                 OutputSetupHandling.SaveActive();
                             }, null);

            DrawLaserCeilingAndSwap(machineConfig, plug, ref device.MaxBrightness, ref device.SwapColorChannels);
        }
        else if (plug.Kind == LaserPlug.Kinds.Citp)
        {
            var device = plug.CitpSettings;

            FormInputs.DrawInputLabel("Feed name");
            if (_laserTextKey != plug.Id + ":feed")
            {
                _laserTextKey = plug.Id + ":feed";
                _laserTextBuffer = device.FeedName;
            }

            if (ImGui.InputText("##laserFeed", ref _laserTextBuffer, 32))
            {
                // committed on deactivate
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                device.FeedName = _laserTextBuffer.Trim();
                OutputSetupHandling.SaveActive();
            }

            ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
            ImGui.PushID("ResetFeedName");
            if (CustomComponents.IconButton(Icon.Reset, Vector2.Zero))
            {
                device.FeedName = string.Empty;
                _laserTextBuffer = device.FeedName;
                OutputSetupHandling.SaveActive();
            }

            ImGui.PopID();
            CustomComponents.TooltipForLastItem("Reset feed name", "Empty - the feed follows the plug's name.");

            // What the peer actually lists: every plug is one feed of the machine's single "TiXL"
            // source, numbered by feed order ("Physical Output" in Capture).
            FormInputs.AddHint($"\"{device.ResolveFeedName(plug.Name)}\" as a feed of the TiXL source");

            var citpKeepalive = device.Keepalive;
            if (FormInputs.AddCheckBox("Keepalive", ref citpKeepalive,
                                       "Keeps the feed streaming while the LASER master is off. CITP is a previz path that never drives projectors, so this defaults to on - Capture keeps showing the show while the real beam is dark."))
            {
                device.Keepalive = citpKeepalive;
                OutputSetupHandling.SaveActive();
            }

            DrawRowReset("ResetCitpKeepalive", "keepalive",
                         () =>
                             {
                                 device.Keepalive = true;
                                 OutputSetupHandling.SaveActive();
                             }, null);

            DrawLaserCeilingAndSwap(machineConfig, plug, ref device.MaxBrightness, ref device.SwapColorChannels);
        }

        PanelLayout.EndSection();
    }

    /// <summary>The two device settings every laser protocol shares: the final brightness ceiling
    /// applied at encode (the non-bypassable audit) and the RGB/BGR channel order.</summary>
    private static void DrawLaserCeilingAndSwap(MachineConfig machineConfig, LaserPlug plug, ref float maxBrightness, ref bool swap)
    {
        Span<float> ceiling = [maxBrightness];
        var ceilingState = DrawFloatsRow("Brightness ceiling", ceiling, "Final intensity ceiling (0..1) applied to every point right before streaming - runs inside the device, so no upstream bug can exceed it.",
                                         reserveRight: 22);
        if ((ceilingState & InputEditStateFlags.Modified) != 0)
        {
            maxBrightness = Math.Clamp(ceiling[0], 0f, 1f);
            OutputSetupHandling.SaveActive();
        }

        // The resets flag through DrawRowReset and apply here: the values are ref parameters,
        // which lambdas must not capture.
        var ceilingReset = false;
        DrawRowReset("ResetCeiling", "brightness ceiling", () => ceilingReset = true, null);
        if (ceilingReset)
        {
            maxBrightness = 1f;
            OutputSetupHandling.SaveActive();
        }

        var swapChannels = swap;
        if (FormInputs.AddCheckBox("Swap color channels", ref swapChannels, "Some projectors expect the color channels as BGR - swaps R and B when on."))
        {
            swap = swapChannels;
            OutputSetupHandling.SaveActive();
        }

        var swapReset = false;
        DrawRowReset("ResetSwapChannels", "color channels", () => swapReset = true, null);
        if (swapReset)
        {
            swap = false;
            OutputSetupHandling.SaveActive();
        }
    }

    /// <summary>
    /// The setup-driven pipeline: optimization and safety for this laser output, applied by the output
    /// operator before streaming. Safety starts uncalibrated - only the alignment rectangle streams -
    /// and the app-bar laser button stays orange until every laser output is calibrated.
    /// </summary>
    private static void DrawLaserProcessing(Setup setup, OutputDefinition output)
    {
        var processing = output.LaserProcessing ??= new LaserProcessingSettings();
        var optimizer = processing.Optimizer;
        var safety = processing.Safety;
        var motion = processing.Motion;

        PanelLayout.BeginSection("Frame",
                                 "The projection is the aim of the routed [LaserCamera] (edit it on the op). A new camera attaches to a free laser output; drop a camera onto this output to route a specific one. The frame streams to the bound plug while the master laser is on.");
        DrawLaserFramePreview(setup, output);

        // How the camera frame lands in the aimed zone. The frame is the scanner field itself (the
        // camera's picture), so this is a fixed transform of that field - content keeps its size
        // while it animates.
        var frameMapping = output.FrameMapping;
        if (FormInputs.AddEnumDropdown(ref frameMapping, "Frame mapping",
                                       "How the camera frame lands in the aimed zone (the setup rectangle). The frame is the scanner field, so sizes stay as the camera framed them: Fit keeps the aspect and fits the field into the rect (a 1.0 x 1.0 rect is a pass-through); Stretch fills both axes and may distort; Crop keeps the aspect, fills the rect and blanks what falls outside the aimed zone; Off streams the field as-is. Applies in calibrated mode.",
                                       LaserFrameMappingMode.Fit,
                                       m => m switch
                                               {
                                                   LaserFrameMappingMode.Stretch => "Stretch",
                                                   LaserFrameMappingMode.Fit => "Fit (keep aspect)",
                                                   LaserFrameMappingMode.Crop => "Crop (fill, cut overflow)",
                                                   _ => "Off",
                                               }))
        {
            EditOutputs(setup, "Set laser frame mapping", output, o => o.FrameMapping = frameMapping);
        }

        // Keystone: four draggable corners in the frame preview above (clamped to the safe zone).
        // Uncalibrated, the green alignment shape IS the quad - trapeze it against a skewed screen.
        // Calibrated, the frame is mapped onto the quad with a homography.
        var frameWarp = safety.FrameWarp;
        if (FormInputs.AddCheckBox("Trapeze frame (4 corners)", ref frameWarp,
                                   "Warps the frame onto the four corners instead of the setup rectangle: drag the corners in the frame preview above (they stay inside the safe zone). While uncalibrated, the green alignment shape becomes the corner quad itself — trapeze it against a skewed screen. In calibrated mode the camera frame is mapped onto the quad (keystone), replacing the Frame mapping. Switching on adopts the setup rectangle's corners, so nothing moves until a corner is dragged."))
        {
            EditSafety(setup, "Toggle laser frame warp", output, s =>
                       {
                           s.FrameWarp = frameWarp;
                           if (frameWarp)
                           {
                               // Start from each output's own setup rect's corners: enabling changes
                               // nothing until a corner is dragged.
                               var halfW = s.SetupWidth * 0.5f;
                               var halfH = s.SetupHeight * 0.5f;
                               s.WarpCorner1X = s.SetupCenterX - halfW;
                               s.WarpCorner1Y = s.SetupCenterY + halfH;
                               s.WarpCorner2X = s.SetupCenterX + halfW;
                               s.WarpCorner2Y = s.SetupCenterY + halfH;
                               s.WarpCorner3X = s.SetupCenterX + halfW;
                               s.WarpCorner3Y = s.SetupCenterY - halfH;
                               s.WarpCorner4X = s.SetupCenterX - halfW;
                               s.WarpCorner4Y = s.SetupCenterY - halfH;
                           }
                       });
        }

        DrawRowReset("ResetFrameWarp", "frame corners",
                     () => EditSafety(setup, "Reset frame corners", output, s => s.FrameWarp = false), null);

        PanelLayout.EndSection();

        PanelLayout.BeginSection("Safety", "What reaches the scanners. Calibrate first: until then only the alignment rectangle streams.");

        var calibrated = safety.Calibrated;
        if (FormInputs.AddCheckBox("Calibrated", ref calibrated,
                                   "While off, upstream content is ignored and only the alignment rectangle below streams - aim it, then switch on for content. The app-bar laser button stays orange until every laser output is calibrated."))
        {
            EditSafety(setup, "Toggle laser calibration", output, s => s.Calibrated = calibrated);
        }

        // Test screen: the aiming aid that works BEFORE calibration - rectangle + output name, so
        // the projector on the wall can be told apart from its siblings. Transient show state.
        var testScreen = LaserDriver.IsTestScreen(output.Id);
        if (FormInputs.AddCheckBox("Test screen", ref testScreen,
                                   "Streams a test pattern - regardless of Calibrated - so you can aim and calibrate the projector. The frame preview shows it too. Master enable still gates the beam."))
        {
            foreach (var target in CollectEditTargets(setup, output))
                LaserDriver.SetTestScreen(target.Id, testScreen);
        }

        if (testScreen)
        {
            var patternNames = new[]
                                   {
                                       "Name frame", "Color shift", "Diode threshold", "White balance",
                                       "Gamma ramp", "Scan area grid", "Circle", "ILDA test pattern",
                                   };
            var selectedPattern = patternNames[(int)LaserDriver.TestScreenPattern];
            if (FormInputs.AddDropdown(ref selectedPattern, patternNames, "Test pattern",
                                       "The calibration pattern to stream: color shift tunes the per-channel color timing, diode threshold and white balance the per-channel voltages, gamma ramp the brightness curve, the grid shows the scan area, the circle checks motion smoothness, and the ILDA pattern verifies the whole field."))
            {
                var index = Array.IndexOf(patternNames, selectedPattern);
                if (index >= 0)
                    LaserDriver.TestScreenPattern = (LaserTestFrame.Pattern)index;
            }
        }

        if (!calibrated)
        {
            Span<float> setupRect =
            [
                safety.SetupCenterX, safety.SetupCenterY, safety.SetupWidth, safety.SetupHeight,
            ];
            var rectState = DrawFloatsRow("Setup rect (cx, cy, w, h)", setupRect,
                                          "The alignment rectangle in normalized field units: center -1..1, width/height 0..1 where 1 = the full scanner field. Start tiny at center and extend step by step until the rectangle fills the projection, then switch Calibrated on.",
                                          speed: 0.001f, format: "{0:0.000}", reserveRight: 22);
            BeginFieldUndo(setup, rectState);
            if ((rectState & InputEditStateFlags.Modified) != 0)
            {
                safety.SetupCenterX = Math.Clamp(setupRect[0], -1f, 1f);
                safety.SetupCenterY = Math.Clamp(setupRect[1], -1f, 1f);
                safety.SetupWidth = Math.Clamp(setupRect[2], 0.01f, 1f);
                safety.SetupHeight = Math.Clamp(setupRect[3], 0.01f, 1f);
            }

            CommitFieldUndo(setup, "Aim setup rectangle", rectState);
            DrawRowReset("ResetSetupRect", "setup rectangle",
                         () =>
                             {
                                 safety.SetupCenterX = 0f;
                                 safety.SetupCenterY = 0f;
                                 safety.SetupWidth = 0.04f;
                                 safety.SetupHeight = 0.04f;
                             }, setup);
        }

        var beamEnable = safety.BeamEnable;
        if (FormInputs.AddCheckBox("Beam enable", ref beamEnable, "Master beam switch for this output; off blanks every point but keeps the geometry."))
        {
            EditSafety(setup, "Toggle laser beam", output, s => s.BeamEnable = beamEnable);
        }

        var desiredFps = safety.DesiredFps;
        if (FormInputs.AddInt("Desired FPS", ref desiredFps, 1, 120, 1,
                              "Frames per second this output recomputes content at. Between updates the scanners loop the last frame at full scan rate, so the visible ILDA frame rate stays scan-rate / point count. Match a synced video's FPS; text stops blinking above ~45 FPS and blinking shows under ~35. Scanning faster than the hardware likes heats the scanners - lower is safer.", 60))
        {
            EditSafety(setup, "Set laser desired FPS", output, s => s.DesiredFps = Math.Clamp(desiredFps, 1, 120));
        }

        var (pointCount, scanRate, effectiveFps) = LaserDriver.GetScanStats(output);
        FormInputs.AddHintSingleLine(pointCount > 0
                                         ? $"{pointCount} points · {scanRate} pps · {effectiveFps} fps scanned"
                                         : "No frame computed yet - the readout fills in once the pump ran.");

        Span<float> zone =
        [
            safety.SafeZoneCenterX, safety.SafeZoneCenterY, safety.SafeZoneWidth, safety.SafeZoneHeight,
        ];
        var zoneState = DrawFloatsRow("Safe zone (cx, cy, w, h)", zone,
                                      "The region the beam may enter, in ILDA units (full field 65534 wide). Points outside are clamped in.",
                                      reserveRight: 22);
        BeginFieldUndo(setup, zoneState);
        if ((zoneState & InputEditStateFlags.Modified) != 0)
        {
            foreach (var target in CollectEditTargets(setup, output))
            {
                var s = (target.LaserProcessing ??= new LaserProcessingSettings()).Safety;
                s.SafeZoneCenterX = zone[0];
                s.SafeZoneCenterY = zone[1];
                // MathF.Max propagates NaN, which would persist as "no safe zone" - treat a NaN
                // edit as the smallest zone instead.
                s.SafeZoneWidth = float.IsNaN(zone[2]) ? 0f : MathF.Max(0f, zone[2]);
                s.SafeZoneHeight = float.IsNaN(zone[3]) ? 0f : MathF.Max(0f, zone[3]);
            }
        }

        CommitFieldUndo(setup, "Laser safe zone", zoneState);
        DrawRowReset("ResetSafeZone", "safe zone",
                     () => EditSafety(setup, "Reset safe zone", output,
                                      s =>
                                          {
                                              s.SafeZoneCenterX = 0f;
                                              s.SafeZoneCenterY = 0f;
                                              s.SafeZoneWidth = 65534f;
                                              s.SafeZoneHeight = 65534f;
                                          }), null);

        Span<float> limits =
        [
            safety.MaxStepPerPoint, safety.MaxStaticPoints, safety.MaxBrightness,
        ];
        var limitsState = DrawFloatsRow("Jump / dwell / bright", limits,
                                        "Max jump per point (ILDA units, subdivided), max points dwelling in one place before blanking, and the show-level brightness ceiling (the plug's device ceiling stays the final audit).",
                                        reserveRight: 22);
        BeginFieldUndo(setup, limitsState);
        if ((limitsState & InputEditStateFlags.Modified) != 0)
        {
            foreach (var target in CollectEditTargets(setup, output))
            {
                var s = (target.LaserProcessing ??= new LaserProcessingSettings()).Safety;
                // Same NaN hole as the zone: MathF.Max/Math.Clamp pass NaN through, and a NaN step
                // limit disables the jump limiter downstream.
                s.MaxStepPerPoint = float.IsNaN(limits[0]) ? 4000f : MathF.Max(1f, limits[0]);
                s.MaxStaticPoints = Math.Clamp((int)limits[1], 1, 100000);
                s.MaxBrightness = float.IsNaN(limits[2]) ? 0f : Math.Clamp(limits[2], 0f, 1f);
            }
        }

        CommitFieldUndo(setup, "Laser safety limits", limitsState);
        DrawRowReset("ResetSafetyLimits", "safety limits",
                     () => EditSafety(setup, "Reset safety limits", output,
                                      s =>
                                          {
                                              s.MaxStepPerPoint = 4000f;
                                              s.MaxStaticPoints = 64;
                                              s.MaxBrightness = 1f;
                                          }), null);

        Span<float> gains =
        [
            safety.ColorGainR, safety.ColorGainG, safety.ColorGainB,
        ];
        var gainsState = DrawFloatsRow("Color gains (R G B)", gains,
                                       "Per-channel projector calibration: multiplies each diode's level (0..2) so the projectors' whites match or a weak diode is lifted.",
                                       reserveRight: 22);
        BeginFieldUndo(setup, gainsState);
        if ((gainsState & InputEditStateFlags.Modified) != 0)
        {
            foreach (var target in CollectEditTargets(setup, output))
            {
                var s = (target.LaserProcessing ??= new LaserProcessingSettings()).Safety;
                // NaN through MathF.Max would persist as an unusable gain - fold it to neutral.
                s.ColorGainR = float.IsNaN(gains[0]) ? 1f : Math.Clamp(gains[0], 0f, 2f);
                s.ColorGainG = float.IsNaN(gains[1]) ? 1f : Math.Clamp(gains[1], 0f, 2f);
                s.ColorGainB = float.IsNaN(gains[2]) ? 1f : Math.Clamp(gains[2], 0f, 2f);
            }
        }

        CommitFieldUndo(setup, "Laser color gains", gainsState);
        DrawRowReset("ResetColorGains", "color gains",
                     () => EditSafety(setup, "Reset color gains", output,
                                      s =>
                                          {
                                              s.ColorGainR = 1f;
                                              s.ColorGainG = 1f;
                                              s.ColorGainB = 1f;
                                          }), null);

        Span<float> minimums =
        [
            safety.ColorMinR, safety.ColorMinG, safety.ColorMinB,
        ];
        var minState = DrawFloatsRow("Color min (R G B)", minimums,
                                     "Per-channel emission threshold (0..1): a diode below this fraction of the field stays dark. Laser diodes start emitting at slightly different voltages - without a minimum, near-black content shows a faint unwanted color.",
                                     speed: 0.002f, format: "{0:0.000}", reserveRight: 22);
        BeginFieldUndo(setup, minState);
        if ((minState & InputEditStateFlags.Modified) != 0)
        {
            foreach (var target in CollectEditTargets(setup, output))
            {
                var s = (target.LaserProcessing ??= new LaserProcessingSettings()).Safety;
                s.ColorMinR = float.IsNaN(minimums[0]) ? 0f : Math.Clamp(minimums[0], 0f, 1f);
                s.ColorMinG = float.IsNaN(minimums[1]) ? 0f : Math.Clamp(minimums[1], 0f, 1f);
                s.ColorMinB = float.IsNaN(minimums[2]) ? 0f : Math.Clamp(minimums[2], 0f, 1f);
            }
        }

        CommitFieldUndo(setup, "Laser color minimums", minState);
        DrawRowReset("ResetColorMinimums", "color minimums",
                     () => EditSafety(setup, "Reset color minimums", output,
                                      s =>
                                          {
                                              s.ColorMinR = 0f;
                                              s.ColorMinG = 0f;
                                              s.ColorMinB = 0f;
                                          }), null);

        var ttlMode = safety.TtlMode;
        if (FormInputs.AddCheckBox("TTL laser", ref ttlMode,
                                   "Binary on/off diodes: at or above 50% the channel outputs its maximum, below it turns off. Color gains, minimums and gamma are skipped - only for lasers without analog modulation."))
        {
            EditSafety(setup, "Toggle laser TTL mode", output, s => s.TtlMode = ttlMode);
        }

        if (!safety.TtlMode)
        {
            var linkGamma = _linkGammaChannels;
            if (FormInputs.AddCheckBox("Link gamma channels", ref linkGamma,
                                       "Drive all three channels' gamma with one value - the usual setup; untick to tune a channel separately."))
                _linkGammaChannels = linkGamma;

            Span<float> gammas =
            [
                safety.ColorGammaR, safety.ColorGammaG, safety.ColorGammaB,
            ];
            var gammaState = DrawFloatsRow(_linkGammaChannels ? "Gamma (linked)" : "Gamma (R G B)", gammas,
                                           "Per-channel gamma exponent: out = in^(1/gamma). 1 = linear, 2.2 = the sRGB-style perceptual default (mid-tones compressed), higher compresses more - tune on the Gamma ramp test pattern so brightness rows look evenly spaced. Per-channel values fix mixed direct-diode/DPSS systems.",
                                           speed: 0.01f, format: "{0:0.00}", reserveRight: 22,
                                           min: 0.1f, max: 8f, clampMin: true, clampMax: true);
            BeginFieldUndo(setup, gammaState);
            if ((gammaState & InputEditStateFlags.Modified) != 0)
            {
                // Linked mode: any channel edit drives all three with its value.
                var linked = _linkGammaChannels
                                 && !float.IsNaN(gammas[0])
                                 && (float.IsNaN(gammas[1]) || gammas[1].Equals(gammas[0]))
                                 && (float.IsNaN(gammas[2]) || gammas[2].Equals(gammas[0]));
                var gamma = float.IsNaN(gammas[0]) ? 1f : Math.Clamp(gammas[0], 0.1f, 8f);
                var gammaG = linked ? gamma : float.IsNaN(gammas[1]) ? 1f : Math.Clamp(gammas[1], 0.1f, 8f);
                var gammaB = linked ? gamma : float.IsNaN(gammas[2]) ? 1f : Math.Clamp(gammas[2], 0.1f, 8f);
                foreach (var target in CollectEditTargets(setup, output))
                {
                    var s = (target.LaserProcessing ??= new LaserProcessingSettings()).Safety;
                    s.ColorGammaR = gamma;
                    s.ColorGammaG = gammaG;
                    s.ColorGammaB = gammaB;
                }
            }

            CommitFieldUndo(setup, "Laser gamma", gammaState);
            DrawRowReset("ResetLaserGamma", "gamma",
                         () => EditSafety(setup, "Reset laser gamma", output,
                                          s =>
                                              {
                                                  s.ColorGammaR = 1f;
                                                  s.ColorGammaG = 1f;
                                                  s.ColorGammaB = 1f;
                                              }), null);
        }

        var staticPercent = safety.MaxStaticIntensity * 100f;
        if (FormInputs.AddFloat("Max static beam %", ref staticPercent, 0f, 100f, 0.5f,
                                clampMin: true, clampMax: true,
                                tooltip: "Intensity fraction a stationary beam keeps after the dwell limit, instead of blanking - the venue-controlled equivalent of the safety guide's 'max non-moving beam intensity'. 0 = blank (scan-fail default). WARNING: raising this lets a holding beam output real power - be sure nobody and nothing could be burnt by the beam before changing it."))
        {
            foreach (var target in CollectEditTargets(setup, output))
                (target.LaserProcessing ??= new LaserProcessingSettings()).Safety.MaxStaticIntensity =
                    Math.Clamp(staticPercent / 100f, 0f, 1f);
        }

        Span<float> flips =
        [
            safety.FlipX ? 1f : 0f, safety.FlipY ? 1f : 0f,
        ];
        var flipState = DrawFloatsRow("Flip (X Y)", flips,
                                      "Mirror the output horizontally / vertically on the projector (0/1). Applied inside the safe-zone clamp so masks and the bounds audit keep their guarantees.",
                                      format: "{0:0}", reserveRight: 22);
        BeginFieldUndo(setup, flipState);
        if ((flipState & InputEditStateFlags.Modified) != 0)
        {
            foreach (var target in CollectEditTargets(setup, output))
            {
                var s = (target.LaserProcessing ??= new LaserProcessingSettings()).Safety;
                s.FlipX = flips[0] >= 0.5f;
                s.FlipY = flips[1] >= 0.5f;
            }
        }

        CommitFieldUndo(setup, "Laser flip", flipState);

        Span<float> shifts =
        [
            safety.ColorShiftR, safety.ColorShiftG, safety.ColorShiftB,
        ];
        var shiftState = DrawFloatsRow("Color shift (R G B)", shifts,
                                       "Per-channel time shift in points: how far a diode's color trails the beam position (negative = leads). Diodes react at different speeds, so without it a color edge fringes along the scan direction - tune it on a test pattern with fast moving colored lines. A shift never lights a blanked point.",
                                       speed: 0.1f, format: "{0:0}", reserveRight: 22,
                                       min: -LaserSafetyTransforms.MaxColorShiftPoints, max: LaserSafetyTransforms.MaxColorShiftPoints,
                                       clampMin: true, clampMax: true);
        BeginFieldUndo(setup, shiftState);
        if ((shiftState & InputEditStateFlags.Modified) != 0)
        {
            var shiftR = ToColorShiftPoints(shifts[0]);
            var shiftG = ToColorShiftPoints(shifts[1]);
            var shiftB = ToColorShiftPoints(shifts[2]);
            foreach (var target in CollectEditTargets(setup, output))
            {
                var s = (target.LaserProcessing ??= new LaserProcessingSettings()).Safety;
                s.ColorShiftR = shiftR;
                s.ColorShiftG = shiftG;
                s.ColorShiftB = shiftB;
            }
        }

        CommitFieldUndo(setup, "Laser color shift", shiftState);
        DrawRowReset("ResetColorShift", "color shift",
                     () => EditSafety(setup, "Reset color shift", output,
                                      s =>
                                          {
                                              s.ColorShiftR = 0;
                                              s.ColorShiftG = 0;
                                              s.ColorShiftB = 0;
                                          }), null);

        PanelLayout.EndSection();

        DrawLaserMasks(setup, safety);

        if (PanelLayout.BeginCollapsibleSection("Optimization", "Path tour, blanking and corner dwell applied before the safety stage."))
        {

            var enableOptimization = optimizer.EnableOptimization;
            if (FormInputs.AddCheckBox("Reorder path", ref enableOptimization,
                                       "Greedy nearest-neighbour tour over the lit segments (reversing segments when closer). Off keeps the drawn order; blanking and corner dwelling always apply."))
            {
                EditOptimizer(setup, "Toggle laser optimization", output, o => o.EnableOptimization = enableOptimization);
            }

            DrawRowReset("ResetReorderPath", "reorder path",
                         () => EditOptimizer(setup, "Reset reorder path", output, o => o.EnableOptimization = true), null);

            Span<float> jump = [optimizer.MaxJumpDistance];
            var jumpState = DrawFloatsRow("Max jump (units)", jump, "ILDA units per blanked-jump segment; the blank count between shapes scales with jump length.",
                                          reserveRight: 22);
            BeginFieldUndo(setup, jumpState);
            if ((jumpState & InputEditStateFlags.Modified) != 0)
            {
                foreach (var target in CollectEditTargets(setup, output))
                    (target.LaserProcessing ??= new LaserProcessingSettings()).Optimizer.MaxJumpDistance = MathF.Max(1f, jump[0]);
            }

            CommitFieldUndo(setup, "Laser optimization", jumpState);
            DrawRowReset("ResetMaxJump", "max jump",
                         () => EditOptimizer(setup, "Reset max jump", output, o => o.MaxJumpDistance = 5000f), null);

            Span<int> blanking = [optimizer.BlankingDelayPoints];
            var blankingState = DrawIntsRow("Blanking points", blanking, "Maximum blanked points inserted before a shape start (0..100).",
                                            reserveRight: 22);
            BeginFieldUndo(setup, blankingState);
            if ((blankingState & InputEditStateFlags.Modified) != 0)
            {
                foreach (var target in CollectEditTargets(setup, output))
                    (target.LaserProcessing ??= new LaserProcessingSettings()).Optimizer.BlankingDelayPoints = Math.Clamp(blanking[0], 0, 100);
            }

            CommitFieldUndo(setup, "Laser optimization", blankingState);
            DrawRowReset("ResetBlanking", "blanking points",
                         () => EditOptimizer(setup, "Reset blanking points", output, o => o.BlankingDelayPoints = 5), null);

            Span<float> corner = [optimizer.CornerAngleThreshold];
            var cornerState = DrawFloatsRow("Corner angle (°)", corner, "Corners sharper than this get dwell anchors.",
                                            reserveRight: 22);
            BeginFieldUndo(setup, cornerState);
            if ((cornerState & InputEditStateFlags.Modified) != 0)
            {
                foreach (var target in CollectEditTargets(setup, output))
                    (target.LaserProcessing ??= new LaserProcessingSettings()).Optimizer.CornerAngleThreshold = MathF.Max(0f, corner[0]);
            }

            CommitFieldUndo(setup, "Laser optimization", cornerState);
            DrawRowReset("ResetCornerAngle", "corner angle",
                         () => EditOptimizer(setup, "Reset corner angle", output, o => o.CornerAngleThreshold = 30f), null);

            Span<int> anchors = [optimizer.CornerAnchorCount];
            var anchorsState = DrawIntsRow("Corner anchors", anchors, "Repeated points at a corner (1..10) - brighter, steadier corners.",
                                           reserveRight: 22);
            BeginFieldUndo(setup, anchorsState);
            if ((anchorsState & InputEditStateFlags.Modified) != 0)
            {
                foreach (var target in CollectEditTargets(setup, output))
                    (target.LaserProcessing ??= new LaserProcessingSettings()).Optimizer.CornerAnchorCount = Math.Clamp(anchors[0], 1, 10);
            }

            CommitFieldUndo(setup, "Laser optimization", anchorsState);
            DrawRowReset("ResetCornerAnchors", "corner anchors",
                         () => EditOptimizer(setup, "Reset corner anchors", output, o => o.CornerAnchorCount = 3), null);

            PanelLayout.EndSection();
        }

        if (PanelLayout.BeginCollapsibleSection("Scanner Motion",
                                                "The scanners' time budgets in µs, converted to dwell and sample counts at the bound plug's scan rate. All zero keeps the content's own point spacing in charge."))
        {
            Span<float> drawingSpeed = [motion.DrawingSpeedUs];
            var speedState = DrawFloatsRow("Drawing speed (µs)", drawingSpeed,
                                           "Slowest the beam may draw, in µs per 1000 ILDA units - drawn segments are stretched with interpolated samples so the scanners can track them (0 = the content's point spacing decides; typical galvos 6-12).",
                                           reserveRight: 22);
            BeginFieldUndo(setup, speedState);
            if ((speedState & InputEditStateFlags.Modified) != 0)
            {
                foreach (var target in CollectEditTargets(setup, output))
                    (target.LaserProcessing ??= new LaserProcessingSettings()).Motion.DrawingSpeedUs = Math.Clamp(drawingSpeed[0], 0f, 100f);
            }

            CommitFieldUndo(setup, "Laser scanner motion", speedState);
            DrawRowReset("ResetDrawingSpeed", "drawing speed",
                         () => EditMotion(setup, "Reset drawing speed", output, m => m.DrawingSpeedUs = 0f), null);

            Span<float> motionResponse = [motion.MotionResponseUs];
            var responseState = DrawFloatsRow("Motion response (µs)", motionResponse,
                                              "Dwell at every shape's first sample - the settle time the scanners need after a jump before the beam opens (0 = off; typical 50-300).",
                                              reserveRight: 22);
            BeginFieldUndo(setup, responseState);
            if ((responseState & InputEditStateFlags.Modified) != 0)
            {
                foreach (var target in CollectEditTargets(setup, output))
                    (target.LaserProcessing ??= new LaserProcessingSettings()).Motion.MotionResponseUs = Math.Clamp(motionResponse[0], 0f, 2000f);
            }

            CommitFieldUndo(setup, "Laser scanner motion", responseState);
            DrawRowReset("ResetMotionResponse", "motion response",
                         () => EditMotion(setup, "Reset motion response", output, m => m.MotionResponseUs = 0f), null);

            Span<float> cornerSettle = [motion.CornerSettleUs];
            var settleState = DrawFloatsRow("Corner settle (µs)", cornerSettle,
                                            "Extra dwell at sharp corners, on top of the corner anchors above, so corners render sharp instead of rounded off (0 = off; typical 50-200).",
                                            reserveRight: 22);
            BeginFieldUndo(setup, settleState);
            if ((settleState & InputEditStateFlags.Modified) != 0)
            {
                foreach (var target in CollectEditTargets(setup, output))
                    (target.LaserProcessing ??= new LaserProcessingSettings()).Motion.CornerSettleUs = Math.Clamp(cornerSettle[0], 0f, 2000f);
            }

            CommitFieldUndo(setup, "Laser scanner motion", settleState);
            DrawRowReset("ResetCornerSettle", "corner settle",
                         () => EditMotion(setup, "Reset corner settle", output, m => m.CornerSettleUs = 0f), null);

            Span<float> transition = [motion.TransitionUs];
            var transitionState = DrawFloatsRow("Transitions (µs)", transition,
                                                "Extra blanked hold at each shape entry - the time between shapes the scanners get before the beam opens (0 = off; typical 100-500).",
                                                reserveRight: 22);
            BeginFieldUndo(setup, transitionState);
            if ((transitionState & InputEditStateFlags.Modified) != 0)
            {
                foreach (var target in CollectEditTargets(setup, output))
                    (target.LaserProcessing ??= new LaserProcessingSettings()).Motion.TransitionUs = Math.Clamp(transition[0], 0f, 2000f);
            }

            CommitFieldUndo(setup, "Laser scanner motion", transitionState);
            DrawRowReset("ResetTransitionUs", "transitions",
                         () => EditMotion(setup, "Reset transitions", output, m => m.TransitionUs = 0f), null);

            PanelLayout.EndSection();
        }
    }

    private static int ToColorShiftPoints(float value)
    {
        return float.IsNaN(value)
                   ? 0
                   : (int)Math.Clamp(MathF.Round(value), -LaserSafetyTransforms.MaxColorShiftPoints, LaserSafetyTransforms.MaxColorShiftPoints);
    }

    /// <summary>
    /// Safety masks: quads in the frame preview where the beam is blanked or dimmed - or, in
    /// allow-only mode, the only place it may emit. Edited like the keystone corners, but they
    /// apply in the safety stage after the warp, so a mask lands exactly where it is drawn
    /// regardless of keystone or content - no graph operator can bypass it.
    /// </summary>
    private static void DrawLaserMasks(Setup setup, LaserProcessingSettings.SafetySettings safety)
    {
        PanelLayout.BeginSection("Masks", "Quads where the beam is blanked or dimmed, applied after the warp so no graph operator can bypass them.");

        var renderShapes = safety.RenderSafetyShapes;
        if (FormInputs.AddCheckBox("Render shapes on the laser", ref renderShapes,
                                   "Streams the safe-zone rectangle and the enabled masks as faint outlines on top of the calibrated frame, so you can verify the protected regions against the real venue while aiming."))
        {
            SetupUndo.RunUndoable("Toggle laser safety shapes", setup, () => safety.RenderSafetyShapes = renderShapes);
        }

        if (renderShapes)
        {
            Span<float> shapeLevel = [safety.RenderShapesLevel];
            var levelState = DrawFloatsRow("Shape level", shapeLevel,
                                           "Brightness of the rendered safety outlines (0..1). Kept low on purpose: the outline is an aiming aid, not content.",
                                           reserveRight: 22);
            BeginFieldUndo(setup, levelState);
            if ((levelState & InputEditStateFlags.Modified) != 0)
                safety.RenderShapesLevel = float.IsNaN(shapeLevel[0]) ? 0.1f : Math.Clamp(shapeLevel[0], 0f, 1f);
            CommitFieldUndo(setup, "Laser safety shape level", levelState);

            var customColor = safety.RenderShapesUseCustomColor;
            if (FormInputs.AddCheckBox("Custom shape color", ref customColor,
                                       "Draw the outlines in the color below instead of the semantic defaults (green = safe zone, orange = masks) - for matching a show's look or a dim red in dark venues."))
            {
                SetupUndo.RunUndoable("Toggle laser shape color", setup,
                                      () => safety.RenderShapesUseCustomColor = customColor);
            }

            if (customColor)
            {
                Span<float> shapeColor = [safety.RenderShapesColorR, safety.RenderShapesColorG, safety.RenderShapesColorB];
                var colorState = DrawFloatsRow("Shape color (R G B)", shapeColor,
                                               "Outline color for the safe zone and masks, 0..1 per channel.",
                                               reserveRight: 22);
                BeginFieldUndo(setup, colorState);
                if ((colorState & InputEditStateFlags.Modified) != 0)
                {
                    safety.RenderShapesColorR = float.IsNaN(shapeColor[0]) ? 1f : Math.Clamp(shapeColor[0], 0f, 1f);
                    safety.RenderShapesColorG = float.IsNaN(shapeColor[1]) ? 0f : Math.Clamp(shapeColor[1], 0f, 1f);
                    safety.RenderShapesColorB = float.IsNaN(shapeColor[2]) ? 0f : Math.Clamp(shapeColor[2], 0f, 1f);
                }

                CommitFieldUndo(setup, "Laser safety shape color", colorState);
            }
        }

        if (safety.Masks.Count == 0)
        {
            CustomComponents.StylizedText("None - add quads to blank or dim the beam where drawn, e.g. over people or structures.",
                                          Fonts.FontSmall, UiColors.TextMuted);
        }

        for (var i = 0; i < safety.Masks.Count; i++)
        {
            var mask = safety.Masks[i];
            ImGui.PushID(i);

            var enabled = mask.Enabled;
            if (FormInputs.AddCheckBox("Enabled", ref enabled,
                                       mask.AllowOnlyInside
                                           ? "Allow-only mask: everything OUTSIDE this quad is blanked - the beam may only emit inside it. Off disables the mask."
                                           : "Blank mask: the beam is off inside this quad (the scanners keep moving the path). Off disables the mask."))
            {
                var captured = i;
                SetupUndo.RunUndoable("Toggle laser mask", setup, () =>
                                                                     {
                                                                         safety.Masks[captured].Enabled = enabled;
                                                                         safety.MasksChanged();
                                                                     });
            }

            ImGui.SameLine();
            if (ImGui.Button(mask.AllowOnlyInside ? "Allow inside" : "Blank inside"))
            {
                var captured = i;
                SetupUndo.RunUndoable("Switch laser mask mode", setup, () =>
                                                                           {
                                                                               safety.Masks[captured].AllowOnlyInside = !safety.Masks[captured].AllowOnlyInside;
                                                                               safety.MasksChanged();
                                                                           });
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Blank inside = protect this area. Allow inside = the beam may only emit here.");

            if (!mask.AllowOnlyInside)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60f * T3Ui.UiScaleFactor);
                var level = mask.Level;
                if (ImGui.DragFloat("##masklevel", ref level, 0.005f, 0f, 1f, "{0:0.00}"))
                {
                    var capturedValue = Math.Clamp(level, 0f, 1f);
                    var captured = i;
                    SetupUndo.RunUndoable("Set laser mask level", setup, () =>
                                                                             {
                                                                                 safety.Masks[captured].Level = capturedValue;
                                                                                 safety.MasksChanged();
                                                                             });
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Beam level kept inside the mask: 0 blanks it, 0.05 keeps a faint 5% beam (reduced-level audience scanning), 1 would change nothing.");
            }

            ImGui.SameLine();
            var selected = _laserMaskSelected == i;
            if (ImGui.Button(selected ? "Editing" : "Edit"))
                _laserMaskSelected = selected ? -1 : i;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Drag this mask's four corners in the frame preview above.");

            ImGui.SameLine();
            if (PanelLayout.DestructiveButton("Remove"))
            {
                if (_laserMaskSelected == i)
                    _laserMaskSelected = -1;
                SetupUndo.RunUndoable("Remove laser mask", setup, () =>
                                                                      {
                                                                          safety.Masks.RemoveAt(i);
                                                                          safety.MasksChanged();
                                                                      });
                ImGui.PopID();
                break; // the list shortened under this row
            }

            ImGui.PopID();
        }

        if (ImGui.Button("Add mask"))
        {
            SetupUndo.RunUndoable("Add laser mask", setup, () =>
                                                               {
                                                                   safety.Masks.Add(LaserProcessingSettings.SafetySettings.MaskQuad.Centered());
                                                                   safety.MasksChanged();
                                                                   _laserMaskSelected = safety.Masks.Count - 1;
                                                               });
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Adds a centered quad; drag its corners in the frame preview above (select Edit first).");

        PanelLayout.EndSection();
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
        // The output's implicit full-canvas patch is the output itself, so it gets no row; with only that one, the
        // section is its title and Add.
        var hasListedPatches = SetupRelations.CountListedPatches(output) > 0;
        DrawPatchListHeader(setup, output, selection, listScreenX, listWidth, hasListedPatches);
        if (hasListedPatches)
            DrawPatchListRows(setup, output, selection, highlightedPatchId, listScreenX, listWidth);

        ImGui.PopID();
    }

    /** The section title, the unit switch in the parameter column, and the add button at the list's right end. */
    private static void DrawPatchListHeader(Setup setup, OutputDefinition output, SetupEntitySelection selection, float listScreenX, float listWidth,
                                            bool hasListedPatches)
    {
        var padding = PatchHeaderPadding * T3Ui.UiScaleFactor;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText("Patches", Fonts.FontBold, UiColors.TextMuted);
        ImGui.SameLine();
        if (hasListedPatches)
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

        // Dragging a grip past a neighbour swaps the two, here and in the output's layer order, so the later patch
        // still draws over the earlier one. One undo step for the whole drag.
        if (moveFromIndex >= 0)
        {
            _patchReorderOldJson ??= setup.ToJsonString();

            // A patch still labelled by its position would change its name under the cursor.
            foreach (var patch in output.Patches)
            {
                if (string.IsNullOrEmpty(patch.Name) && !SetupRelations.IsImplicitPatch(output, patch))
                    patch.Name = SetupLabels.PatchLabel(output, patch);
            }

            SwapInLayerOrder(output, output.Patches[moveFromIndex].Id, output.Patches[moveToIndex].Id);
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
    /// <summary>
    /// Properties of a render node's display: its reported mode, what the node presents there, and who
    /// the node is. Read-only - the display and its bindings belong to that machine; the binding is
    /// changed by dropping an output on the row and released from its context menu.
    /// </summary>
    private static void DrawRemoteDisplayCard(Setup setup, Show.BoygroupManager.ClientInfo client, int displayIndex)
    {
        Show.BoygroupManager.RemoteScreen? screen = null;
        foreach (var candidate in client.Screens)
        {
            if (candidate.Index != displayIndex)
                continue;

            screen = candidate;
            break;
        }

        Span<int> mode = [screen?.Width ?? 0, screen?.Height ?? 0];
        DrawIntsRow("Resolution (px)", mode, "The render node display's current mode (read-only).", readOnly: true);

        var boundOutput = client.MachineConfig == null
                              ? null
                              : Plugs.TryGetBoundOutput(setup, client.MachineConfig, Plugs.DisplayPlugId(displayIndex));
        FormInputs.AddHint(boundOutput == null ? "nothing bound" : $"presenting {SetupLabels.OutputLabel(boundOutput)}");
        FormInputs.AddHint($"Display {displayIndex + 1}{(screen == null ? "" : $" · {screen.Name}")}");

        FormInputs.AddVerticalSpace(6);
        FormInputs.AddSectionSubHeader("Render node");
        DrawInfoRow("Name", client.Name);
        DrawInfoRow("Address", client.Address);
        DrawInfoRow("Status", client.IsDeployed ? $"{client.StatusText} · project deployed" : client.StatusText);
        if (!string.IsNullOrEmpty(client.Group))
            DrawInfoRow("Group", client.Group);
    }

    /// <summary>A read-only label and value, aligned with the editable rows above and below it.</summary>
    private static void DrawInfoRow(string label, string value)
    {
        FormInputs.DrawInputLabel(label);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(value);
    }

    private static void DrawPlugCard(Setup setup, MachineConfig machineConfig, Guid id)
    {
        // A render node's display is not this machine's plug: it gets its own card, and without this
        // branch the local plug lookups below find nothing and the card stays empty.
        if (Show.BoygroupManager.Instance.TryGetRemoteDisplayPlug(id, out var remoteClient, out var remoteDisplayIndex))
        {
            DrawRemoteDisplayCard(setup, remoteClient, remoteDisplayIndex);
            return;
        }

        var boundOutput = Plugs.TryGetBoundOutput(setup, machineConfig, id);
        var boundLabel = boundOutput == null ? "nothing bound" : $"presenting {SetupLabels.OutputLabel(boundOutput)}";

        if (Plugs.TryGetDisplayIndex(id, out var displayIndex))
        {
            var screens = EditorUi.Instance.AllScreens;
            Span<int> mode = [0, 0];
            if (displayIndex < screens.Count)
            {
                mode[0] = screens[displayIndex].Bounds.Width;
                mode[1] = screens[displayIndex].Bounds.Height;
            }

            DrawIntsRow("Resolution (px)", mode, "The display's current mode (read-only).", readOnly: true);
            FormInputs.AddHint(boundLabel);
            return;
        }

        var laser = machineConfig.FindLaserPlug(id);
        if (laser != null)
        {
            DrawLaserPlugCard(machineConfig, laser, boundOutput);
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
        if (EditorUiContent.IsEditorUi(childId))
        {
            OutputContentResolver.TryGetPreviewContent(childId, out var frame);
            FormInputs.ApplyIndent();
            CustomComponents.StylizedText(frame is { IsDisposed: false }
                                              ? $"mirrors the editor window, {frame.Description.Width}×{frame.Description.Height}"
                                              : "mirrors the editor window",
                                          Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        var instance = ContentSourceSync.FindSendInstance(childId);
        if (instance == null)
        {
            DrawLaserCameraCard(setup, childId);
            return;
        }

        if (instance is not IContentSupplier supplier)
            return;

        OutputContentResolver.TryGetPreviewContent(childId, out var content);
        var context = OutputContentResolver.Context;

        var update = supplier.GetUpdateEnabled(context);
        if (FormInputs.AddCheckBox("Update", ref update, "When off, freezes this content at its last frame.")
            && supplier.UpdateInput != null)
        {
            SetInputUndoable(instance, supplier.UpdateInput, update);
        }

        // 0×0 means "whatever the output asks for", so the content follows the projector or display it is routed
        // to; a set value pins it. The line underneath says what that resolves to right now.
        var requested = supplier.GetResolution(context);
        Span<int> resolution = [requested.Width, requested.Height];
        var state = DrawIntsRow("Render at (px)", resolution,
                                "0 follows the output this content is routed to. Set a size to render at it regardless.");
        if ((state & InputEditStateFlags.Modified) != 0 && supplier.ResolutionInput != null)
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

    /// <summary>
    /// The laser counterpart of the content card: a [LaserCamera] has no pixels, so the card carries what
    /// the send card does — freezing the content — plus where its frame is routed and how big it is.
    /// </summary>
    private static void DrawLaserCameraCard(Setup setup, Guid childId)
    {
        if (LaserContent.FindCamera(childId) is not { } instance || instance is not ILaserContentSupplier supplier)
            return;

        var update = supplier.GetUpdateEnabled(OutputContentResolver.Context);
        var output = setup.FindOutput(LaserContent.OutputRefOf(instance));

        PanelLayout.StatusPill(output == null ? "Not routed" : "Routed",
                               output == null ? UiColors.StatusAttention : UiColors.StatusOkay);
        ImGui.SameLine(0, PanelLayout.SpaceS * T3Ui.UiScaleFactor);
        PanelLayout.StatusPill(update ? "Updating" : "Frozen", update ? UiColors.TextMuted : UiColors.StatusAttention);

        PanelLayout.Space(PanelLayout.SpaceXS);
        // The point count changes every frame: a wrapped line would resize its card a frame late.
        DrawWrappedSmallTextStable(output == null
                                       ? "Drop it on a laser output to route it."
                                       : $"Streams to {output.Name} · {LaserDriver.GetFrame(output).Count} points",
                                   UiColors.TextMuted, 1);

        if (instance is IStatusProvider { } status && status.GetStatusLevel() >= IStatusProvider.StatusLevel.Warning
                                                    && status.GetStatusMessage() is { } message)
        {
            DrawWrappedSmallTextStable(message, UiColors.StatusAttention, 2);
        }

        PanelLayout.Space(PanelLayout.SpaceM);
        PanelLayout.BeginSection("Frame");
        if (FormInputs.AddCheckBox("Update", ref update, "When off, freezes this laser frame and stops evaluating the scene."))
            SetInputUndoable(instance, supplier.UpdateInput, update);

        PanelLayout.EndSection();

        DrawLaserRenderSection(instance);
    }

    /// <summary>
    /// The content side of the MadLaser split: the output card owns device and safety, the routed
    /// content op owns how the content becomes points. Surfaces the render knobs the routed op
    /// declares - by input name, so [ImageToLaserPoints] and [LaserCamera] both work without type
    /// coupling. Missing knobs are simply absent (each op lists what it has).
    /// </summary>
    private static void DrawLaserRenderSection(Instance instance)
    {
        PanelLayout.Space(PanelLayout.SpaceM);
        PanelLayout.BeginSection("Laser Render",
                                 "How this content turns into laser points - the per-source render settings. "
                                 + "Everything device-side (scan rate, blanking, color calibration, safety) lives on the output's card.");

        var drewAny = false;
        drewAny |= DrawRenderFloat(instance, "PointSpacing", "Point spacing", 0.5f, 64f, 0.25f,
                                   "Working pixels between lit samples along a path - the density dial. Lower = more points (denser, brighter, heavier); higher = fewer points (lighter on the scanner budget).");
        drewAny |= DrawRenderInt(instance, "DotDwellPoints", "Dot dwell points", 0, 64,
                                 "Samples held on isolated dots - their point intensity. More = brighter beams that stay put.");
        drewAny |= DrawRenderInt(instance, "EndRepeat", "End repeat", 0, 16,
                                 "Extra samples held at each shape's end position, so the scanner settles on the real endpoint instead of rounding it off. A dot's dwell grows with it too.");
        drewAny |= DrawRenderFloat(instance, "InFade", "In fade", 0f, 1f, 0.01f,
                                   "Ramps each shape's first samples up from this dim level to full: the scanner is still accelerating at a path's start, which otherwise blooms the start point. 0 = off.");
        drewAny |= DrawRenderFloat(instance, "OutFade", "Out fade", 0f, 1f, 0.01f,
                                   "Ramps the end-repeat samples down, so finishing a shape doesn't leave a bright pause dot of its own. 0 = repeats hold at full brightness.");
        if (DrawRenderFloat(instance, "PathMinLength", "Path min length %", 0f, 100f, 0.5f,
                            "Drops shapes shorter than this percentage of the working image's larger dimension - noise specks that would cost a blank jump each. 0 keeps everything."))
        {
            DrawRenderFloat(instance, "PathMaxLength", "Path max length %", 0f, 2000f, 5f,
                            "Drops shapes longer than this percentage of the working image's larger dimension - runaway background strokes. 0 keeps everything.");
            drewAny = true;
        }

        if (!drewAny)
            FormInputs.AddHint("This content op has no render knobs - its parameters live on the op in the graph.");

        PanelLayout.EndSection();
    }

    private static bool DrawRenderFloat(Instance instance, string inputName, string label,
                                        float min, float max, float speed, string tooltip)
    {
        if (!TryGetInstanceInput(instance, inputName, out var slot)
            || slot.Input.Value is not InputValue<float> value)
            return false;

        var v = value.Value;
        if (FormInputs.AddFloat(label, ref v, min, max, speed, clampMin: true, clampMax: true, tooltip: tooltip))
            SetInputUndoable(instance, slot, v);

        return true;
    }

    private static bool DrawRenderInt(Instance instance, string inputName, string label,
                                      int min, int max, string tooltip)
    {
        if (!TryGetInstanceInput(instance, inputName, out var slot)
            || slot.Input.Value is not InputValue<int> value)
            return false;

        var v = value.Value;
        if (FormInputs.AddInt(label, ref v, min, max, 1, tooltip))
            SetInputUndoable(instance, slot, v);

        return true;
    }

    /// <summary>Finds an instance input slot by its definition name (the parameter-panel name).</summary>
    private static bool TryGetInstanceInput(Instance instance, string inputName, out IInputSlot slot)
    {
        foreach (var def in instance.Symbol.InputDefinitions)
        {
            if (!string.Equals(def.Name, inputName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var candidate in instance.Inputs)
            {
                if (candidate.Id == def.Id)
                {
                    slot = candidate;
                    return true;
                }
            }

            break;
        }

        slot = null;
        return false;
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
            FormInputs.AddHint("No scale yet — type the size above, or Set Scale in the card's menu draws a line over a known length.");
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
    /// <param name="fadedValueIndex">A value drawn faded, like a field at its default: one that merely repeats another.</param>
    /// <param name="min">Where the drag's slider range starts; a hard limit only with <paramref name="clampMin"/>.</param>
    /// <param name="max">Where the drag's slider range ends; a hard limit only with <paramref name="clampMax"/>.</param>
    private static InputEditStateFlags DrawFloatsRow(string label, Span<float> values, string? tooltip = null,
                                                     float speed = 0.01f, bool readOnly = false,
                                                     string format = "{0:0.###}", float reserveRight = 0, int fadedValueIndex = -1,
                                                     float min = float.NegativeInfinity, float max = float.PositiveInfinity,
                                                     bool clampMin = false, bool clampMax = false)
    {
        var size = BeginValuesRow(label, tooltip, values.Length, readOnly, reserveRight, out var gap);
        var result = InputEditStateFlags.Nothing;
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, gap);

            var isFaded = i == fadedValueIndex;
            if (isFaded)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultValueAlpha * ImGui.GetStyle().Alpha);

            ImGui.PushID(i);
            var v = values[i];
            result |= SingleValueEdit.Draw(ref v, size, min, max, clampMin, clampMax, scale: speed, format: format);
            values[i] = v;
            ImGui.PopID();
            if (isFaded)
                ImGui.PopStyleVar();
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
    private static InputEditStateFlags DrawIntsRow(string label, Span<int> values, string? tooltip = null, bool readOnly = false,
                                                   float reserveRight = 0)
    {
        var size = BeginValuesRow(label, tooltip, values.Length, readOnly, reserveRight, out var gap);
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
    internal static void BeginFieldUndo(Setup setup, InputEditStateFlags state)
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

    internal static void CommitFieldUndo(Setup setup, string name, InputEditStateFlags state)
    {
        if ((state & InputEditStateFlags.Finished) == 0 || _fieldEditOldJson == null)
            return;

        SetupUndo.CommitGesture(setup, name, _fieldEditOldJson);
        _fieldEditOldJson = null;
    }

    /// <summary>
    /// A reset-to-default button appended to the row just drawn (give the row reserveRight: 22 so the
    /// icon fits). Undoable on setup cards; machine-side rows (plugs) apply and save immediately.
    /// </summary>
    private static void DrawRowReset(string rowId, string what, Action reset, Setup? undoSetup)
    {
        ImGui.SameLine(0, 4 * T3Ui.UiScaleFactor);
        ImGui.PushID(rowId);
        var clicked = CustomComponents.IconButton(Icon.Reset, Vector2.Zero);
        ImGui.PopID();
        CustomComponents.TooltipForLastItem($"Reset {what}", "Restores the default value.");
        if (!clicked)
            return;

        if (undoSetup != null)
            SetupUndo.RunUndoable($"Reset {what}", undoSetup, reset);
        else
            reset();
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

    // Laser-plug address editing: the buffer reloads when a different plug's field is drawn.
    private static Guid _laserAddressPlug;
    private static string _laserAddressBuffer = string.Empty;

    // Laser-plug text rows (interface, PONK sender, CITP feed): one buffer keyed by plug + purpose.
    private static string? _laserTextKey;
    private static string _laserTextBuffer = string.Empty;

    private static SurfaceFacings FacingOf(float pitchDegrees)
    {
        const float tolerance = 0.5f;
        return MathF.Abs(pitchDegrees) < tolerance ? SurfaceFacings.Wall
               : MathF.Abs(pitchDegrees + 90) < tolerance ? SurfaceFacings.Floor
               : MathF.Abs(pitchDegrees - 90) < tolerance ? SurfaceFacings.Ceiling
               : SurfaceFacings.Tilted;
    }

    /// <summary>
    /// The projector's zoom: its range from the wide to the tele end, read as throw ratios (the way spec sheets give
    /// them) or as vertical fields of view, picked by a switch where the label would be; and a slider for where the
    /// zoom currently sits in that range.
    /// </summary>
    private static void DrawZoomRows(Setup setup, OutputDefinition output)
    {
        var scale = T3Ui.UiScaleFactor;
        var switchWidth = 0f;
        foreach (var unit in Enum.GetValues<ZoomUnits>())
            switchWidth += ImGui.CalcTextSize(CustomComponents.HumanReadablePascalCase(unit.ToString())).X + ZoomSwitchSegmentPadding * 2 * scale;

        FormInputs.SetCursorToLabelColumn(switchWidth);
        CustomComponents.SegmentedButton(ref _zoomUnit);
        ImGui.SameLine();

        // The values start in the parameter column, or after the switch where it reaches into it.
        var afterSwitchX = ImGui.GetCursorPosX();
        FormInputs.SetCursorToParameterEdit();
        if (ImGui.GetCursorPosX() < afterSwitchX)
            ImGui.SetCursorPosX(afterSwitchX);

        var camera = output.Camera ?? _defaultCamera;
        var aspect = ProjectorPresets.AspectOf(output);
        var isThrowRatio = _zoomUnit == ZoomUnits.Ratio;
        var range = camera.ZoomRangeDegrees;
        Span<float> ends = isThrowRatio
                               ? [ProjectorPresets.ThrowRatio(range.X, aspect), ProjectorPresets.ThrowRatio(range.Y, aspect)]
                               : [range.X, range.Y];
        var isFixedLens = MathF.Abs(range.X - range.Y) < 0.01f;
        var rangeState = DrawFloatsRow("##zoomRange", ends,
                                       isThrowRatio
                                           ? "The zoom's range as throw ratios, wide and tele: throw distance over picture width, as spec sheets give it. 1.5 throws a 2 m wide picture from 3 m. Equal values for a fixed lens."
                                           : "The zoom's range as vertical fields of view in degrees, wide and tele. Equal values for a fixed lens.",
                                       speed: isThrowRatio ? 0.005f : 0.1f, format: isThrowRatio ? "{0:0.00}" : "{0:0.#}°",
                                       fadedValueIndex: isFixedLens ? 1 : -1,
                                       min: isThrowRatio ? 0.1f : 1f, max: isThrowRatio ? 3f : 60f, clampMin: true);
        BeginFieldUndo(setup, rangeState);
        if ((rangeState & InputEditStateFlags.Modified) != 0)
        {
            var edited = output.Camera ??= new OutputDefinition.ProjectorCamera();
            var position = ProjectorPresets.ZoomPosition(edited, aspect);
            edited.ZoomRangeDegrees = isThrowRatio
                                          ? new Vector2(ToFieldOfView(ends[0], aspect), ToFieldOfView(ends[1], aspect))
                                          : new Vector2(Math.Clamp(ends[0], 1, 170), Math.Clamp(ends[1], 1, 170));
            ProjectorPresets.SetZoomPosition(edited, position, aspect);
        }

        // Sorted once the edit is done, never while typing: swapping the fields under the cursor would lose the entry.
        if ((rangeState & InputEditStateFlags.Finished) != 0 && output.Camera is { } finished)
            ProjectorPresets.SortZoomRange(finished);

        CommitFieldUndo(setup, isThrowRatio ? "Change throw ratio range" : "Change zoom range", rangeState);

        // Where the zoom ring sits; with both ends equal it has nowhere to move, and the faded second value says so.
        Span<float> zoom = [ProjectorPresets.ZoomPosition(camera, aspect) * 100];
        var zoomState = DrawFloatsRow("Zoom", zoom,
                                      "Where the zoom currently sits, from the wide (0) to the tele (100) end. A solve sets it to the zoom it measured.",
                                      speed: 0.5f, format: "{0:0}%", min: 0, max: 100, clampMin: true, clampMax: true);
        BeginFieldUndo(setup, zoomState);
        if ((zoomState & InputEditStateFlags.Modified) != 0)
            ProjectorPresets.SetZoomPosition(output.Camera ??= new OutputDefinition.ProjectorCamera(), zoom[0] / 100, aspect);

        CommitFieldUndo(setup, "Change zoom", zoomState);
    }

    private static float ToFieldOfView(float throwRatio, float aspect)
    {
        return Math.Clamp(ProjectorPresets.VerticalFieldOfViewDegrees(MathF.Max(throwRatio, 0.05f), aspect), 1, 170);
    }

    /** Which way the zoom field reads; the editor's choice for this session, not part of the setup. */
    private static ZoomUnits _zoomUnit = ZoomUnits.Ratio;

    /** Unscaled extra width of the output card's label column. */
    private const float OutputCardExtraIndent = 20;

    /** Matches the horizontal padding CustomComponents.SegmentedButton gives each segment. */
    private const float ZoomSwitchSegmentPadding = 14;

    /** What a projector with no camera data yet shows: the defaults, edited into a real camera on the first change. */
    private static readonly OutputDefinition.ProjectorCamera _defaultCamera = new();

}
