#nullable enable
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Core.Output.Rendering;
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
        if (CustomComponents.DrawMenuItem(0, "New Content for This Surface", reserveIconColumn: false))
            CreateContent(setup, surface);

        CustomComponents.TooltipForLastItem("Adds a scene to the graph that renders onto this surface, with its reference photo as the backdrop.",
                                            "Creates SurfaceReferenceImage → Layer2d → Group → RenderTarget → SendToOutput, sized to the surface, "
                                            + "and connects the surface to the output if there is only one. Add your content to the Group, "
                                            + "and mute the photo's Layer2d before the show.");

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
    /// Builds SurfaceReferenceImage → Layer2d → Group → RenderTarget → SendToOutput in the open composition, routes
    /// the send to the surface sized from it, and maps the surface's root to the only output when nothing above it is
    /// on one yet. One undo step for the graph and the setup.
    /// </summary>
    private static void CreateContent(Setup setup, Surface surface)
    {
        var projectView = ProjectView.Focused;
        var composition = projectView?.CompositionInstance;
        if (projectView == null || composition == null || !composition.Symbol.TryGetSymbolUi(out var compositionUi))
        {
            Log.Warning("New content for a surface: no composition is open to add it to.");
            return;
        }

        var oldJson = setup.ToJsonString();
        var macro = new MacroCommand("New content for surface");
        var parent = compositionUi.Symbol;

        // One row, left to right in the order the picture flows.
        var start = GraphUtils.FindFreePositionInView(compositionUi);
        var step = new Vector2(SymbolUi.Child.DefaultOpSize.X + 30, 0);
        var referenceId = AddOp(macro, parent, _surfaceReferenceImageSymbolId, start, string.Empty);
        var layerId = AddOp(macro, parent, _layer2dSymbolId, start + step, string.Empty);
        var groupId = AddOp(macro, parent, _groupSymbolId, start + step * 2, string.Empty);
        var renderTargetId = AddOp(macro, parent, _renderTargetSymbolId, start + step * 3, string.Empty);
        var sendId = AddOp(macro, parent, ContentSourceSync.SendToOutputSymbolId, start + step * 4, surface.Name);

        Connect(macro, parent, referenceId, _surfaceReferenceImageTextureOutputId, layerId, _layer2dTextureInputId);
        Connect(macro, parent, layerId, _layer2dOutputId, groupId, _groupCommandsInputId);
        Connect(macro, parent, groupId, _groupOutputId, renderTargetId, _renderTargetCommandInputId);
        Connect(macro, parent, renderTargetId, _renderTargetColorBufferOutputId, sendId, ContentSourceSync.SendToOutputTextureInputId);

        SetInput(macro, parent, referenceId, _surfaceReferenceImageSurfaceInputId, new InputValue<Guid>(surface.Id));
        SetInput(macro, parent, sendId, _sendToOutputSizeFromInputId, new InputValue<int>(SendSizeFromSurface));

        // Registered right away rather than left to the sync's adoption next frame: the slice needs a source to name.
        var source = new ContentSource { SymbolChildId = sendId, Name = surface.Name, IsRenamed = true };
        setup.ContentSources.Add(source);
        surface.SliceId = WallpaperLayout.CreateSlice(setup, source).Id;
        var mappedOutput = MapRootToOnlyOutput(setup, surface);
        PlaceCardAboveFrame(setup, source, surface);

        macro.AddExecutedCommandForUndo(new SetupSnapshotCommand("New content for surface", setup.Id, oldJson, setup.ToJsonString()));
        UndoRedoStack.Add(macro);
        OutputSetupHandling.SaveActive();

        projectView.FlagChanges(ProjectView.ChangeTypes.Children | ProjectView.ChangeTypes.Connections);
        projectView.NodeSelection.TrySelectCompositionChild(composition, groupId, add: false);

        var next = mappedOutput != null ? $"connected to {SetupLabels.OutputLabel(mappedOutput)}; calibrate it next"
                   : setup.FindMappedAncestor(surface.Id) is { OutputMappings.Count: > 0 } ? "calibrate it next"
                   : "connect it to an output, then calibrate it";
        Log.Info($"New content for '{surface.Name}': add your content to the selected Group; the surface is {next}.");
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

        // Card pixels per metre: the render's width over the surface's, so the card is the surface's size.
        if (OutputContentResolver.TryGetSurfaceRequest(setup, source.SymbolChildId, out var resolution, out _) && surface.SizeInMeters.X > 0)
            placement.CardScale = resolution.Width / surface.SizeInMeters.X;

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

    private static Guid AddOp(MacroCommand macro, Symbol parent, Guid symbolId, Vector2 position, string name)
    {
        var command = new AddSymbolChildCommand(parent, symbolId) { PosOnCanvas = position, ChildName = name };
        macro.AddAndExecCommand(command);
        return command.AddedChildId;
    }

    private static void Connect(MacroCommand macro, Symbol parent, Guid sourceChildId, Guid sourceSlotId, Guid targetChildId, Guid targetSlotId)
    {
        var connection = new Symbol.Connection(sourceChildId, sourceSlotId, targetChildId, targetSlotId);
        macro.AddAndExecCommand(new AddConnectionCommand(parent, connection, 0));
    }

    private static void SetInput(MacroCommand macro, Symbol parent, Guid childId, Guid inputId, InputValue value)
    {
        if (!parent.Children.TryGetValue(childId, out var child) || !child.Inputs.TryGetValue(inputId, out var input))
        {
            Log.Warning($"New content for a surface: can't set input {inputId} on the new op.");
            return;
        }

        macro.AddAndExecCommand(new ChangeInputValueCommand(parent, childId, input, value));
    }

    private static readonly List<Surface> _regions = [];

    /** The gap between the surface frame and the new content's card above it, as a share of the frame's height. */
    private const float CardGapOfFrameHeight = 0.3f;

    /** SendToOutput's SizeFrom value for the surface's aspect: stable while the new surface is still being aligned. */
    private const int SendSizeFromSurface = 2;

    private static readonly Guid _surfaceReferenceImageSymbolId = new("6435a961-ef0a-4e1d-bbcd-76e665bd58b6");
    private static readonly Guid _surfaceReferenceImageTextureOutputId = new("8ab20f20-5e19-4b4b-b846-4b8abbd82817");
    private static readonly Guid _surfaceReferenceImageSurfaceInputId = new("1a5d05c4-b3e3-4b99-90cb-7c3da6920703");
    private static readonly Guid _layer2dSymbolId = new("d8c5330f-59b5-4907-b845-a02def3042fa");
    private static readonly Guid _layer2dTextureInputId = new("2a95ac54-5ef7-4d3c-a90b-ecd5b422bddc");
    private static readonly Guid _layer2dOutputId = new("e4a8d926-7abd-4d2a-82a1-b7d140cb457f");
    private static readonly Guid _groupSymbolId = new("a3f64d34-1fab-4230-86b3-1c3deba3f90b");
    private static readonly Guid _groupCommandsInputId = new("9E961F73-1EE7-4369-9AC7-5C653E570B6F");
    private static readonly Guid _groupOutputId = new("977ca2f4-cddb-4b9a-82b2-ff66453bbf9b");
    private static readonly Guid _renderTargetSymbolId = new("f9fe78c5-43a6-48ae-8e8c-6cdbbc330dd1");
    private static readonly Guid _renderTargetCommandInputId = new("4da253b7-4953-439a-b03f-1d515a78bddf");
    private static readonly Guid _renderTargetColorBufferOutputId = new("7A4C4FEB-BE2F-463E-96C6-CD9A6BAD77A2");
    private static readonly Guid _sendToOutputSizeFromInputId = new("3125e84e-8644-46a9-8fae-e88ed06731f4");
}
