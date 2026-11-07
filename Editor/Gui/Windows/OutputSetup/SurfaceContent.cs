#nullable enable
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using System.Diagnostics.CodeAnalysis;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// "Connect..." for a surface or region that shows nothing yet: new content built around its reference photo,
/// existing content, or slices across its regions. Turns the separate steps of creating a send, sizing it,
/// connecting it to the surface and the surface to an output into one.
/// </summary>
internal static class SurfaceContent
{
    /// <summary>Whether a surface still waits for content, which is when "Connect..." is offered.</summary>
    public static bool NeedsContent(Surface surface) => surface.SliceId == Guid.Empty;

    /// <summary>
    /// The menu items, for the toolbar popup. Call between a popup's Begin and End. No row has an icon, so none
    /// reserves the icon column: a submenu header never does, and the labels would not line up.
    /// </summary>
    public static void DrawMenuItems(SetupEntitySelection selection, Setup setup, Surface surface)
    {
        if (CustomComponents.DrawMenuItem(0, "New 2D Content", reserveIconColumn: false))
            CreateContent(setup, surface);

        CustomComponents.TooltipForLastItem("Adds a scene to the graph that renders onto this surface, with its reference photo as the backdrop.",
                                            "Copies the chain of the _SurfaceContent2d template (SurfaceReferenceImage → Layer2d → Group → RenderTarget "
                                            + "→ SendToOutput), sized to the surface, and connects the surface to the output if there is only one. "
                                            + "Add your content to the Group, and mute the photo's Layer2d before the show.");

        Draw3dContentItems(setup, surface);

        if (CustomComponents.DrawSubMenu(2, "Use Existing Content"))
        {
            for (var i = 0; i < setup.ContentSources.Count; i++)
            {
                var source = setup.ContentSources[i];
                if (CustomComponents.DrawMenuItem(100 + i, string.IsNullOrEmpty(source.Name) ? "untitled" : source.Name, reserveIconColumn: false))
                    SetupRouting.ApplyDrop(setup, SetupEntityKinds.ContentSource, source.SymbolChildId, SetupEntityKinds.Surface, surface.Id);
            }

            if (setup.ContentSources.Count == 0)
                CustomComponents.DrawMenuItem(99, "No content yet", isEnabled: false, reserveIconColumn: false);

            ImGui.EndMenu();
        }

        RegionSlices.CollectRegions(setup, surface, _regions);
        if (_regions.Count > 0 && CustomComponents.DrawMenuItem(1, "Create Slices from Regions", reserveIconColumn: false))
            RegionSlices.Create(selection, setup, surface, _regions, RegionSlices.FindShownSource(setup, _regions));
    }

    /// <summary>
    /// Adds the 2D chain from its template to the open composition, routes the send to the surface, and maps the
    /// surface's root to the only output when nothing above it is on one yet. One undo step for the graph and the setup.
    /// </summary>
    private static void CreateContent(Setup setup, Surface surface)
    {
        var oldJson = setup.ToJsonString();
        if (!TryInsertTemplate(_surfaceContent2dTemplateId, "New 2D content for surface", surface, null, surface.Name, out var inserted))
            return;

        // Registered right away rather than left to the sync's adoption next frame: the slice needs a source to name.
        var source = new ContentSource { SymbolChildId = inserted.SendId, Name = surface.Name, IsRenamed = true };
        setup.ContentSources.Add(source);
        surface.SliceId = WallpaperLayout.CreateSlice(setup, source).Id;
        var mappedOutput = MapRootToOnlyOutput(setup, surface);
        PlaceCardAboveFrame(setup, source, surface);
        Finish(inserted, setup, oldJson);

        var next = mappedOutput != null ? $"connected to {SetupLabels.OutputLabel(mappedOutput)}; calibrate it next"
                   : setup.FindMappedAncestor(surface.Id) is { OutputMappings.Count: > 0 } ? "calibrate it next"
                   : "connect it to an output, then calibrate it";
        Log.Info($"New 2D content for '{surface.Name}': add your content to its Group; the surface is {next}.");
    }

    /// <summary>
    /// "New 3D Content": for each projector output the surface is corner-pinned onto whose camera is solved. One
    /// output is an item, several a submenu; none shows the item greyed out with what's missing.
    /// </summary>
    private static void Draw3dContentItems(Setup setup, Surface surface)
    {
        CollectProjectorOutputs(setup, surface, _projectorOutputs);
        if (_projectorOutputs.Count == 0)
        {
            CustomComponents.DrawMenuItem(3, "New 3D Content", isEnabled: false, reserveIconColumn: false);
            CustomComponents.TooltipForLastItem("Solve the projector camera first.",
                                                "Corner-pin this surface onto a projector output, calibrate it, and choose Solve from Calibrated Surface "
                                                + "in the output's Projector section.");
            return;
        }

        if (_projectorOutputs.Count == 1)
        {
            if (CustomComponents.DrawMenuItem(3, "New 3D Content", reserveIconColumn: false))
                Create3dContent(setup, surface, _projectorOutputs[0]);

            CustomComponents.TooltipForLastItem(New3dContentTooltip, New3dContentChain);
            return;
        }

        if (!CustomComponents.DrawSubMenu(3, "New 3D Content"))
            return;

        for (var i = 0; i < _projectorOutputs.Count; i++)
        {
            var output = _projectorOutputs[i];
            if (CustomComponents.DrawMenuItem(200 + i, SetupLabels.OutputLabel(output), reserveIconColumn: false))
                Create3dContent(setup, surface, output);

            CustomComponents.TooltipForLastItem(New3dContentTooltip, New3dContentChain);
        }

        ImGui.EndMenu();
    }

    /** Outputs the surface's pin lies on whose projector camera is solved: where its 3D content can be rendered. */
    private static void CollectProjectorOutputs(Setup setup, Surface surface, List<OutputDefinition> outputs)
    {
        outputs.Clear();
        if (setup.FindMappedAncestor(surface.Id) is not { } carrier)
            return;

        foreach (var mapping in carrier.OutputMappings)
        {
            if (!mapping.IsFilling && setup.FindOutput(mapping.OutputId) is { Camera.Pose: not null } output && !outputs.Contains(output))
                outputs.Add(output);
        }
    }

    /// <summary>
    /// Adds the 3D chain from its template to the open composition: the surface drawn with its photo through the
    /// output's projector camera, the send filling the output as its front layer. One undo step for both.
    /// </summary>
    private static void Create3dContent(Setup setup, Surface surface, OutputDefinition output)
    {
        var oldJson = setup.ToJsonString();
        var sendName = $"{SetupLabels.OutputLabel(output)} 3D";
        if (!TryInsertTemplate(_surfaceContent3dTemplateId, "New 3D content for surface", surface, output, sendName, out var inserted))
            return;

        AddFrontLayer(setup, output, inserted.SendId, sendName);
        Finish(inserted, setup, oldJson);
        Log.Info($"New 3D content for '{surface.Name}' on {SetupLabels.OutputLabel(output)}: add 3D objects to its Group, in the surface's metres; "
                 + "mute the photo before the show.");
    }

    /** What a template insert added: where, and the send among the new children. */
    private sealed record InsertedTemplate(ProjectView View, Instance Composition, MacroCommand Macro, List<Guid> ChildIds, Guid SendId);

    /// <summary>
    /// Copies a template symbol's children into the open composition, at a free spot in view. Every Guid input marked
    /// as picking a setup entity is pointed at the surface, every one picking an output at the output, so the template
    /// says which ops to add and how they connect, and the editor only knows what to point them at. The send, the one
    /// [SendToOutput] among them, gets the given name. Nothing is committed yet: <see cref="Finish"/> does, together
    /// with the setup changes.
    /// </summary>
    private static bool TryInsertTemplate(Guid templateId, string undoName, Surface surface, OutputDefinition? output, string sendName,
                                          [NotNullWhen(true)] out InsertedTemplate? inserted)
    {
        inserted = null;
        var projectView = ProjectView.Focused;
        var composition = projectView?.CompositionInstance;
        if (projectView == null || composition == null || !composition.Symbol.TryGetSymbolUi(out var compositionUi))
        {
            Log.Warning($"{undoName}: no composition is open to add it to.");
            return false;
        }

        if (!SymbolUiRegistry.TryGetSymbolUi(templateId, out var templateUi))
        {
            Log.Warning($"{undoName}: its template {templateId} is not loaded.");
            return false;
        }

        var parent = compositionUi.Symbol;
        var macro = new MacroCommand(undoName);
        var copy = new CopySymbolChildrenCommand(templateUi, null, null, compositionUi, GraphUtils.FindFreePositionInView(compositionUi));
        macro.AddAndExecCommand(copy);

        var childIds = new List<Guid>(copy.OldToNewChildIds.Values);
        var sendId = Guid.Empty;
        foreach (var childId in childIds)
        {
            if (!parent.Children.TryGetValue(childId, out var child) || !child.Symbol.TryGetSymbolUi(out var childSymbolUi))
                continue;

            if (sendId == Guid.Empty && child.Symbol.Id == ContentSourceSync.SendToOutputSymbolId)
                sendId = childId;

            foreach (var inputUi in childSymbolUi.InputUis.Values)
            {
                if (inputUi is not GuidInputUi guidInputUi)
                    continue;

                var target = guidInputUi.Usage switch
                                 {
                                     GuidInputUi.UsageType.OutputSetupEntities => surface.Id,
                                     GuidInputUi.UsageType.OutputSetupOutputs  => output?.Id ?? Guid.Empty,
                                     _                                         => Guid.Empty,
                                 };
                if (target != Guid.Empty)
                    SetInput(macro, parent, childId, guidInputUi.Id, new InputValue<Guid>(target));
            }
        }

        if (sendId == Guid.Empty)
        {
            Log.Warning($"{undoName}: its template has no SendToOutput, so nothing reaches the setup.");
            macro.Undo();
            return false;
        }

        if (compositionUi.ChildUis.TryGetValue(sendId, out var sendUi))
            macro.AddAndExecCommand(new ChangeSymbolChildNameCommand(sendUi, parent) { NewName = sendName });

        inserted = new InsertedTemplate(projectView, composition, macro, childIds, sendId);
        return true;
    }

    /** Commits the graph and setup changes as one undo step, saves, and selects what was added. */
    private static void Finish(InsertedTemplate inserted, Setup setup, string oldSetupJson)
    {
        inserted.Macro.AddExecutedCommandForUndo(new SetupSnapshotCommand(inserted.Macro.Name, setup.Id, oldSetupJson, setup.ToJsonString()));
        UndoRedoStack.Add(inserted.Macro);
        OutputSetupHandling.SaveActive();

        inserted.View.FlagChanges(ProjectView.ChangeTypes.Children | ProjectView.ChangeTypes.Connections);
        for (var i = 0; i < inserted.ChildIds.Count; i++)
            inserted.View.NodeSelection.TrySelectCompositionChild(inserted.Composition, inserted.ChildIds[i], add: i > 0);
    }

    /// <summary>
    /// Registers the send as content shown on the whole canvas of the output, as the front layer: what the projector
    /// camera renders stands in front of the wall, and its transparent background leaves the other layers visible.
    /// </summary>
    private static void AddFrontLayer(Setup setup, OutputDefinition output, Guid sendId, string name)
    {
        var source = new ContentSource { SymbolChildId = sendId, Name = name, IsRenamed = true };
        setup.ContentSources.Add(source);
        var patch = SetupActions.CreateFullCanvasPatch(WallpaperLayout.CreateSlice(setup, source).Id);
        output.Patches.Add(patch);

        var layers = new List<OutputLayers.Layer>();
        OutputLayers.Collect(setup, output, layers);
        for (var index = 0; index < layers.Count; index++)
        {
            if (layers[index].Id == patch.Id)
                OutputLayers.Move(output, layers, index, layers.Count - 1);
        }
    }

    /** The surface's root mapped to the output when there is exactly one to present on and nothing above the surface is on one. */
    private static OutputDefinition? MapRootToOnlyOutput(Setup setup, Surface surface)
    {
        if (setup.FindMappedAncestor(surface.Id) is { OutputMappings.Count: > 0 })
            return null;

        OutputDefinition? only = null;
        foreach (var output in setup.Outputs)
        {
            // The Default output is the editor's internal preview, not something presented.
            if (output.Kind == OutputDefinition.Kinds.Default)
                continue;

            if (only != null)
                return null;

            only = output;
        }

        if (only == null)
            return null;

        // A region follows its parent's pin, so the pin goes on the root: the region then shows only its part of it.
        SetupActions.AddMapping(RootOf(setup, surface), only);
        return only;
    }

    /// <summary>
    /// Puts the new content's Board card above the surface frame, as wide as the surface or region it feeds and in
    /// line with it, so it reads as belonging there without covering the frame. Left to the Board's own seeding
    /// when the frame has no place on the Board yet.
    /// </summary>
    private static void PlaceCardAboveFrame(Setup setup, ContentSource source, Surface surface)
    {
        var root = RootOf(setup, surface);
        if (root.BoardPlacement == null)
            return;

        var frameMin = root.BoardPlacement.Position - root.AnchorInMeters;
        var frameMax = frameMin + root.SizeInMeters;
        var left = frameMin.X;
        if (!ReferenceEquals(root, surface) && SurfaceGeometry.TryGetRegionRect(setup, root, surface, out var regionMin, out _, out _))
            left = root.BoardPlacement.Position.X + regionMin.X;

        var placement = new BoardPlacement
                            {
                                Position = new Vector2(left, frameMax.Y + (frameMax.Y - frameMin.Y) * CardGapOfFrameHeight),
                            };

        // Card pixels per metre: the card's fixed width over the surface's, so the card is the surface's size.
        if (surface.SizeInMeters.X > 0)
            placement.CardScale = BoardPlacement.NominalCardWidth / surface.SizeInMeters.X;

        source.BoardPlacement = placement;
    }

    /** The top-level surface a region belongs to, or the surface itself. */
    private static Surface RootOf(Setup setup, Surface surface)
    {
        var root = surface;
        for (var guard = 0; root.ParentId != Guid.Empty && guard < 16; guard++)
        {
            if (setup.FindSurface(root.ParentId) is not { } parentSurface)
                break;

            root = parentSurface;
        }

        return root;
    }

    private static void SetInput(MacroCommand macro, Symbol parent, Guid childId, Guid inputId, InputValue value)
    {
        if (!parent.Children.TryGetValue(childId, out var child) || !child.Inputs.TryGetValue(inputId, out var input))
        {
            Log.Warning($"New content for a surface: can't set input {inputId} on a template's op.");
            return;
        }

        macro.AddAndExecCommand(new ChangeInputValueCommand(parent, childId, input, value));
    }

    private static readonly List<Surface> _regions = [];
    private static readonly List<OutputDefinition> _projectorOutputs = [];

    private const string New3dContentTooltip = "Draws this surface in 3D through the projector's camera, with its reference photo on it.";

    private const string New3dContentChain =
        "Copies the chain of the _SurfaceContent3d template (SurfaceGeometry → GeometryToMesh → DrawMeshUnlit with SurfaceReferenceImage, "
        + "into a Group → UseProjectorCam → RenderTarget → SendToOutput) and shows the send on the whole output, in front. "
        + "Add 3D objects to the Group in the surface's metres, and mute the photo before the show.";

    /** The gap between the surface frame and the new content's card above it, as a share of the frame's height. */
    private const float CardGapOfFrameHeight = 0.3f;

    private static readonly Guid _surfaceContent2dTemplateId = new("58b6f67e-f933-4b10-aa85-63ec745f15bf");
    private static readonly Guid _surfaceContent3dTemplateId = new("bc4b2ed5-02ad-4b2a-aa3d-0d3d908f6c51");
}
