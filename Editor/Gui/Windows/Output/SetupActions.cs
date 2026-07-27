#nullable enable
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Entity mutations, graph glue, and shared labels/menus for output setups. These operations are shared
/// by the setup sidebar, the canvas views, and the flow view — they are model operations, not panel UI,
/// so they live here in a neutral class none of the views own.
/// </summary>
internal static class SetupActions
{
    internal static void ApplyDrop(Setup setup, SetupEntitySelection.EntityKind dragKind, Guid dragId,
                                   SetupEntitySelection.EntityKind targetKind, Guid targetId)
    {
        if (targetKind == SetupEntitySelection.EntityKind.Output && dragKind == SetupEntitySelection.EntityKind.Surface)
        {
            var surface = setup.FindSurface(dragId);
            var output = setup.FindOutput(targetId);
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
            var output = setup.FindOutput(targetId);
            if (output == null)
                return;

            if (dragKind == SetupEntitySelection.EntityKind.Slice && setup.FindSlice(dragId) != null)
            {
                output.SliceId = dragId;
                OutputSetupHandling.SaveActive();
            }
            else if (dragKind == SetupEntitySelection.EntityKind.ContentSource)
            {
                var source = setup.FindSourceByChildId(dragId);
                if (source != null)
                {
                    output.SliceId = EnsureSlice(setup, source).Id;
                    OutputSetupHandling.SaveActive();
                }
            }

            return;
        }

        // Dropping a slice on a free surface shows it there. A *different* slice dropped on an occupied surface
        // lands as a sub-region cut to its own aspect (the poster-slot case) rather than replacing — but
        // re-dropping the slice the surface already shows is a no-op, not a spurious duplicate sub-region.
        if (dragKind == SetupEntitySelection.EntityKind.Slice)
        {
            var slice = setup.FindSlice(dragId);
            var surface = setup.FindSurface(targetId);
            if (slice != null && surface != null && surface.SliceId != slice.Id)
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
            var source = setup.FindSourceByChildId(dragId);
            var surface = source == null ? null : setup.FindSurface(targetId);
            if (source != null && surface != null)
            {
                surface.SliceId = EnsureSlice(setup, source).Id;
                OutputSetupHandling.SaveActive();
            }
        }
    }

    internal static bool TryParseDrag(string data, out SetupEntitySelection.EntityKind kind, out Guid id)
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

    internal static void AddSlice(SetupEntitySelection selection, Setup setup, ContentSource source)
    {
        // Left unnamed: the label is derived from the source, so it stays right when the op is later renamed.
        var slice = new Slice { SourceId = source.Id };

        setup.Slices.Add(slice);
        selection.Select(SetupEntitySelection.EntityKind.Slice, slice.Id);
        OutputSetupHandling.SaveActive();
    }

    /// <summary>Deleting a slice clears it from anything showing it — the reference would mean nothing.</summary>
    internal static void DeleteSlice(Setup setup, Guid sliceId)
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

    /// <summary>
    /// Whether <paramref name="kind"/>/<paramref name="id"/> can take the primary selection as its input, and
    /// whether it already does. Clicking the in-gutter then binds or unbinds without any dragging: select a
    /// slice and the surfaces that could show it light up; select a surface and the outputs light up.
    /// </summary>
    internal static bool TryDescribeInputToggle(Setup setup, SetupEntitySelection.EntityKind kind, Guid id,
                                                SetupEntitySelection.EntityKind sourceKind, Guid sourceId, out bool isBound)
    {
        isBound = false;
        if (sourceKind == SetupEntitySelection.EntityKind.None)
            return false;

        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.FindSurface(id);
                if (surface == null)
                    return false;

                if (sourceKind == SetupEntitySelection.EntityKind.Slice)
                {
                    isBound = surface.SliceId == sourceId;
                    return setup.FindSlice(sourceId) != null;
                }

                if (sourceKind == SetupEntitySelection.EntityKind.ContentSource)
                {
                    var source = setup.FindSourceByChildId(sourceId);
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

                var surface = setup.FindSurface(sourceId);
                if (surface == null)
                    return false;

                isBound = surface.OutputMappings.Exists(m => m.OutputId == id);
                return true;
            }

            default:
                return false;
        }
    }

    internal static void ToggleInput(Setup setup, SetupEntitySelection.EntityKind kind, Guid id,
                                     SetupEntitySelection.EntityKind sourceKind, Guid sourceId)
    {
        if (!TryDescribeInputToggle(setup, kind, id, sourceKind, sourceId, out var isBound))
            return;

        if (kind == SetupEntitySelection.EntityKind.Surface)
        {
            var surface = setup.FindSurface(id);
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
                var source = setup.FindSourceByChildId(sourceId);
                if (source == null)
                    return;

                surface.SliceId = EnsureSlice(setup, source).Id;
            }
        }
        else
        {
            var surface = setup.FindSurface(sourceId);
            var output = setup.FindOutput(id);
            if (surface == null || output == null)
                return;

            if (isBound)
                surface.OutputMappings.RemoveAll(m => m.OutputId == id);
            else
                surface.OutputMappings.Add(CreateDefaultMapping(output));
        }

        OutputSetupHandling.SaveActive();
    }

    /// <summary>Whether a slice belongs to the given source.</summary>
    internal static bool IsSliceOf(Setup setup, Guid sliceId, Guid sourceId)
    {
        if (sliceId == Guid.Empty)
            return false;

        var slice = setup.FindSlice(sliceId);
        return slice != null && slice.SourceId == sourceId;
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

    internal static void AddSurface(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var surface = new Surface { Name = $"Surface {setup.Surfaces.Count + 1}" };
        setup.Surfaces.Add(surface);
        selection.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
    }

    internal static void AddProp(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var prop = new Prop();
        setup.Props.Add(prop);
        selection.Select(SetupEntitySelection.EntityKind.Prop, prop.Id);
    }

    internal static void AddOutput(SetupEntitySelection selection)
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

    internal static void AddReferenceImage(SetupEntitySelection selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var image = new ReferenceImage { Name = $"Image {setup.ReferenceImages.Count + 1}" };
        setup.ReferenceImages.Add(image);
        selection.Select(SetupEntitySelection.EntityKind.ReferenceImage, image.Id);
    }

    /// <summary>
    /// Drops a <c>SendToOutput</c> op into the focused composition, selects it, and frames the view on it. When a
    /// texture-outputting op is selected it lands to its right and is wired straight in, so the feed shows up in
    /// the setup at once (the CONTENT row appears next frame, once <see cref="ContentSourceSync"/> adopts it).
    /// </summary>
    internal static void AddContentSink(SetupEntitySelection selection)
    {
        var projectView = ProjectView.Focused;
        var composition = projectView?.CompositionInstance;
        if (projectView == null || composition == null)
            return;

        if (!composition.Symbol.TryGetSymbolUi(out var compositionUi)
            || !SymbolUiRegistry.TryGetSymbolUi(SendToOutputSymbolId, out var sinkSymbolUi))
            return;

        // A selected texture op becomes the feed: place the sink to its right and wire it up.
        var selected = projectView.NodeSelection.GetSelectedInstanceWithoutComposition();
        var sourceSlot = selected == null ? null : FindTextureOutput(selected);
        var selectedUi = selected?.GetChildUi();
        var pos = selectedUi != null
                      ? selectedUi.PosOnCanvas + new Vector2(selectedUi.Size.X + 40, 0)
                      : Vector2.Zero;

        var newChildUi = GraphOperations.AddSymbolChild(sinkSymbolUi.Symbol, compositionUi, pos);

        if (sourceSlot != null && selectedUi != null)
        {
            var connection = new Symbol.Connection(selectedUi.Id, sourceSlot.Id, newChildUi.Id, SendToOutputTextureInputId);
            UndoRedoStack.AddAndExecute(new AddConnectionCommand(compositionUi.Symbol, connection, 0));
        }

        projectView.NodeSelection.TrySelectCompositionChild(composition, newChildUi.Id, add: false);
        projectView.FocusViewToSelection();
    }

    /// <summary>
    /// Deletes everything deletable in the selection. Each kind keeps its own cascade (a surface re-parents
    /// its children, an output drops the mappings onto it), so deleting a set is just deleting each in turn —
    /// which is why the targets are copied first: those cascades mutate the setup underneath us.
    /// </summary>
    internal static void DeleteSelection(SetupEntitySelection selection, Setup setup)
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

    /// <summary>Renames an entity by kind. A content source has no name of its own — renaming it renames its
    /// op, which flows back through the sync.</summary>
    internal static void RenameEntity(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, string newName)
    {
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.ContentSource:
                RenameContentSourceOp(id, newName);
                return;

            case SetupEntitySelection.EntityKind.Output:
                var output = setup.FindOutput(id);
                if (output == null)
                    return;

                output.Name = newName;
                break;

            case SetupEntitySelection.EntityKind.ReferenceImage:
                var image = setup.FindReferenceImage(id);
                if (image == null)
                    return;

                image.Name = newName;
                break;

            case SetupEntitySelection.EntityKind.Surface:
                var surface = setup.FindSurface(id);
                if (surface == null)
                    return;

                surface.Name = newName;
                break;

            case SetupEntitySelection.EntityKind.Slice:
                var slice = setup.FindSlice(id);
                if (slice == null)
                    return;

                slice.Name = newName;
                break;

            default:
                return;
        }

        OutputSetupHandling.SaveActive();
    }

    /// <summary>Kinds whose row/item offers a direct Delete (the others delete via selection or not at all).</summary>
    internal static bool CanDeleteDirectly(SetupEntitySelection.EntityKind kind)
    {
        return kind is SetupEntitySelection.EntityKind.Surface
                    or SetupEntitySelection.EntityKind.Slice
                    or SetupEntitySelection.EntityKind.Output;
    }

    internal static void DeleteEntity(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntitySelection.EntityKind.Surface:
                DeleteSurface(setup, id);
                break;

            case SetupEntitySelection.EntityKind.Slice:
                DeleteSlice(setup, id);
                break;

            case SetupEntitySelection.EntityKind.Output:
                if (OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
                    DeleteOutput(setup, machineConfig, id);

                break;
        }
    }

    // Deleting a surface takes its sub-regions with it (they're cuts of the parent, meaningless on their own).
    internal static void DeleteSurface(Setup setup, Guid surfaceId)
    {
        var command = new DeleteSurfaceCommand(setup, surfaceId);
        if (command.HasSurfaces)
            UndoRedoStack.AddAndExecute(command);
    }

    // Deleting an output cascades: drop every surface's mapping onto it, unbind the display, and stop
    // presenting it. Surfaces left without a mapping simply have no output — not lost.
    internal static void DeleteOutput(Setup setup, MachineConfig machineConfig, Guid outputId)
    {
        setup.Outputs.RemoveAll(o => o.Id == outputId);
        foreach (var surface in setup.Surfaces)
            surface.OutputMappings.RemoveAll(m => m.OutputId == outputId);

        machineConfig.Unbind(outputId);
        if (OutputManager.PresentedOutputId == outputId)
            OutputManager.PresentedOutputId = Guid.Empty;

        OutputSetupHandling.SaveActive();
    }

    /// <summary>
    /// How many of the selected entities this panel can actually delete. A content source is a graph op and a
    /// slice's source may be gone, so the menu counts what will really go rather than how many rows are lit.
    /// </summary>
    internal static int CountDeletable(SetupEntitySelection selection)
    {
        var count = 0;
        for (var i = 0; i < selection.Targets.Count; i++)
        {
            if (IsDeletable(selection.Targets[i].Kind))
                count++;
        }

        return count;
    }

    internal static int CountSlicesOfSource(Setup setup, Guid sourceId)
    {
        var count = 0;
        for (var i = 0; i < setup.Slices.Count; i++)
        {
            if (setup.Slices[i].SourceId == sourceId)
                count++;
        }

        return count;
    }

    internal static int CountChildren(Setup setup, Guid parentId)
    {
        var count = 0;
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == parentId)
                count++;
        }

        return count;
    }

    internal static string SinkName(Instance instance)
    {
        var parent = instance.Parent;
        if (parent != null && parent.Symbol.Children.TryGetValue(instance.SymbolChildId, out var child))
            return child.ReadableName;

        return "content";
    }

    internal static Instance? FindSinkInstance(Guid childId)
    {
        var sinks = OutputSinkRegistry.Sinks;
        for (var i = 0; i < sinks.Count; i++)
        {
            if (sinks[i] is Instance instance && instance.SymbolChildId == childId)
                return instance;
        }

        return null;
    }

    /// <summary>
    /// Renames a content source by renaming its op — the source has no name of its own, it mirrors the
    /// SendToOutput op (see <see cref="ContentSourceSync"/>). Needs a live instance to reach the graph; an
    /// op that isn't instantiated can't be renamed from here.
    /// </summary>
    internal static void RenameContentSourceOp(Guid childId, string newName)
    {
        var parent = FindSinkInstance(childId)?.Parent;
        var parentSymbolUi = parent?.GetSymbolUi();
        if (parentSymbolUi == null || !parentSymbolUi.ChildUis.TryGetValue(childId, out var childUi))
            return;

        UndoRedoStack.AddAndExecute(new ChangeSymbolChildNameCommand(childUi, parentSymbolUi.Symbol) { NewName = newName });
    }

    // Select the content's SendToOutput op in the focused graph and frame it — the sidebar → graph half of
    // the sync (the graph → sidebar highlight is handled by the highlighted-content id).
    internal static void RevealContentOpInGraph(Guid childId)
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

    /// <summary>Display name of a content send, for labelling its slice on the source canvas.</summary>
    internal static string? TryGetContentName(Guid contentChildId)
    {
        return FindSinkInstance(contentChildId) is { } instance ? SinkName(instance) : null;
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

        var source = setup.FindSource(slice.SourceId);
        return source is { IsRenamed: true } && !string.IsNullOrEmpty(source.Name)
                   ? $"{source.Name}.{ordinal}"
                   : $"Slice {ordinal}";
    }

    internal static string SurfaceShortLabel(Surface surface)
    {
        return Abbreviate(surface.Name);
    }

    // Compact gutter form: uppercase letters + digits ("Surface 1" → "S1", "WallFront" → "WF"), falling back
    // to the full name when there's nothing to abbreviate (all-lowercase).
    internal static string Abbreviate(string name)
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

    /// <summary>
    /// Reshapes the surface so its real-world proportions match the pixels of the slice it shows — the inverse
    /// of the slice's "Match target aspect", for when the wall is what should give. Keeps the width and solves
    /// the height, so it reads as a nudge rather than a jump.
    /// </summary>
    private static void MatchSurfaceToSliceAspect(Setup setup, Surface surface)
    {
        var slice = setup.FindSlice(surface.SliceId);
        if (slice == null || !TryGetSliceAspect(setup, slice, out var aspect))
            return;

        var oldState = new ResizeSurfaceCommand.State(surface);
        var width = MathF.Max(surface.SizeInMeters.X, SurfaceGeometry.MinSize);
        SurfaceGeometry.ResizeAnchored(surface, new Vector2(width, width / MathF.Max(aspect, 0.0001f)));

        UndoRedoStack.Add(new ResizeSurfaceCommand(surface.Id, oldState, new ResizeSurfaceCommand.State(surface)));
        OutputSetupHandling.SaveActive();
    }

    /// <summary>Aspect of a slice's pixels — its uv extent against the source's resolution.</summary>
    private static bool TryGetSliceAspect(Setup setup, Slice slice, out float aspect)
    {
        aspect = 1f;
        var source = setup.FindSource(slice.SourceId);
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
                           Render = source.Render,
                           SizeInMeters = source.SizeInMeters,
                           LockAspect = source.LockAspect,
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

    /// <summary>
    /// Drops this surface from every send that targets it, so it stops receiving content. The surface itself
    /// and its calibration are untouched — this only edits the sends' target lists (op-side, like the drag).
    /// </summary>
    private static void ClearContentInputs(Guid surfaceId)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var surface = setup.FindSurface(surfaceId);
        if (surface == null || surface.SliceId == Guid.Empty)
            return;

        surface.SliceId = Guid.Empty;
        OutputSetupHandling.SaveActive();
    }

    private static ISlot? FindTextureOutput(Instance instance)
    {
        foreach (var slot in instance.Outputs)
        {
            if (slot.ValueType == typeof(Texture2D))
                return slot;
        }

        return null;
    }

    private static bool IsDeletable(SetupEntitySelection.EntityKind kind)
    {
        return kind is SetupEntitySelection.EntityKind.Surface
                    or SetupEntitySelection.EntityKind.Slice
                    or SetupEntitySelection.EntityKind.Output
                    or SetupEntitySelection.EntityKind.ReferenceImage
                    or SetupEntitySelection.EntityKind.Prop;
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

    private static readonly List<SelectionTarget> _deleteBuffer = [];

    // Lib SendToOutput op and its texture input — the CONTENT "+" instantiates this and wires a selected feed in.
    private static readonly Guid SendToOutputSymbolId = new("0b8f2d4e-6a1c-47d3-9f5e-8c2a1b7d4e60");
    private static readonly Guid SendToOutputTextureInputId = new("8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942");
}
