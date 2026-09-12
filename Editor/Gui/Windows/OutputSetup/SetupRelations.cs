#nullable enable
using T3.Core.Output;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The routing graph of a setup, as queries: for any entity, what feeds it (producers, upstream) and what
/// shows it (consumers, downstream), along the content → slice → surface/patch → output chain. The flow
/// outliner draws its connections and breadcrumb from it, and the canvas fading rules will read it.
/// Plain loops into caller-owned buffers — this runs inside per-frame draws.
/// </summary>
internal static class SetupRelations
{
    /// <summary>One neighbour of an entity: a consumer sits downstream, a producer upstream.</summary>
    internal readonly record struct Relation(SetupEntitySelection.EntityKinds Kind, Guid Id, bool IsConsumer);

    /// <summary>Every entity directly related to <paramref name="kind"/>/<paramref name="id"/>, both directions, into <paramref name="into"/>.</summary>
    public static void CollectRelated(Setup setup, SetupEntitySelection.EntityKinds kind, Guid id, List<Relation> into)
    {
        into.Clear();
        switch (kind)
        {
            case SetupEntitySelection.EntityKinds.Surface:
            {
                var surface = setup.FindSurface(id);
                if (surface == null)
                    break;

                AddOutputsOfSurface(setup, surface, into);
                AddSourceOfSlice(setup, surface.SliceId, into);
                break;
            }
            case SetupEntitySelection.EntityKinds.Output:
            {
                foreach (var surface in setup.Surfaces)
                {
                    if (!IsMappedTo(surface, id))
                        continue;

                    into.Add(new Relation(SetupEntitySelection.EntityKinds.Surface, surface.Id, false));
                    AddSourceOfSlice(setup, surface.SliceId, into); // the feed behind each mapped surface
                }

                var output = setup.FindOutput(id);
                if (output != null)
                {
                    foreach (var patch in output.Patches)
                        AddSourceOfSlice(setup, patch.SliceId, into); // the feeds on the direct pipe
                }

                break;
            }
            case SetupEntitySelection.EntityKinds.Patch:
            {
                var patch = setup.FindPatch(id, out _);
                if (patch != null)
                    AddSourceOfSlice(setup, patch.SliceId, into);

                break;
            }
            case SetupEntitySelection.EntityKinds.ContentSource:
            {
                var source = setup.FindSourceByChildId(id);
                if (source != null)
                    AddConsumersOfSource(setup, source.Id, into);

                break;
            }
            case SetupEntitySelection.EntityKinds.Slice:
                AddConsumersOfSlice(setup, id, into);
                break;

            case SetupEntitySelection.EntityKinds.ReferenceImage:
            {
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.Reference != null && surface.Reference.ImageId == id)
                        into.Add(new Relation(SetupEntitySelection.EntityKinds.Surface, surface.Id, true));
                }

                break;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="kind"/>/<paramref name="id"/> is the immediate source feeding
    /// <paramref name="targetKind"/>/<paramref name="targetId"/> — the slice a surface, patch or output shows,
    /// or the content source a slice belongs to.
    /// </summary>
    public static bool IsDirectSourceOf(Setup setup, SetupEntitySelection.EntityKinds targetKind, Guid targetId,
                                        SetupEntitySelection.EntityKinds kind, Guid id)
    {
        if (id == Guid.Empty)
            return false;

        switch (targetKind)
        {
            case SetupEntitySelection.EntityKinds.Surface:
                var surface = setup.FindSurface(targetId);
                return surface != null && kind == SetupEntitySelection.EntityKinds.Slice && surface.SliceId == id;

            case SetupEntitySelection.EntityKinds.Output:
                var output = setup.FindOutput(targetId);
                return output != null && kind == SetupEntitySelection.EntityKinds.Slice && OutputShowsSlice(output, id);

            case SetupEntitySelection.EntityKinds.Patch:
                var patch = setup.FindPatch(targetId, out _);
                return patch != null && kind == SetupEntitySelection.EntityKinds.Slice && patch.SliceId == id;

            case SetupEntitySelection.EntityKinds.Slice:
                var slice = setup.FindSlice(targetId);
                var source = slice == null ? null : setup.FindSource(slice.SourceId);
                return source != null && kind == SetupEntitySelection.EntityKinds.ContentSource && source.SymbolChildId == id;

            default:
                return false;
        }
    }

    /// <summary>
    /// The output a surface is shown on — its first mapping's output. A Layout child carries no mapping of
    /// its own; it's shown wherever its parent is mapped, so walk up to the surface that actually holds the
    /// corner pin rather than reporting the child as unmapped.
    /// </summary>
    public static bool TryGetSurfaceOutput(Setup setup, Guid surfaceId, out Guid outputId)
    {
        outputId = Guid.Empty;
        var surface = setup.FindSurface(surfaceId);
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.OutputMappings.Count > 0)
            {
                outputId = surface.OutputMappings[0].OutputId;
                return true;
            }

            if (surface.ParentId == Guid.Empty)
                break;

            var parentId = surface.ParentId;
            surface = setup.FindSurface(parentId);
        }

        return false;
    }

    /// <summary>
    /// The output any entity reaches, following the routing downstream: an output is itself, a patch and a
    /// surface name theirs, a slice and a send are traced through whatever shows them. Lets a view offer the
    /// projector a selection *leads to* — picking it is then a matter of selecting that output.
    /// </summary>
    public static bool TryGetOutputOf(Setup setup, SetupEntitySelection.EntityKinds kind, Guid id, out Guid outputId)
    {
        outputId = Guid.Empty;
        switch (kind)
        {
            case SetupEntitySelection.EntityKinds.Output:
                if (setup.FindOutput(id) == null)
                    return false;

                outputId = id;
                return true;

            case SetupEntitySelection.EntityKinds.Patch:
                return TryGetPatchOutput(setup, id, out outputId);

            case SetupEntitySelection.EntityKinds.Surface:
                return TryGetSurfaceOutput(setup, id, out outputId);

            case SetupEntitySelection.EntityKinds.Slice:
                return TryGetSliceOutput(setup, id, out outputId);

            case SetupEntitySelection.EntityKinds.ContentSource:
                return TryGetSendOutput(setup, id, out outputId);

            default:
                return false;
        }
    }

    /// <summary>
    /// The surface an entity reaches: itself, or the first one showing this slice (or any slice of this send).
    /// A patch has none — it is the surface-less pipe — and neither has an output, whose surfaces are picked
    /// on its own canvas.
    /// </summary>
    public static bool TryGetSurfaceOf(Setup setup, SetupEntitySelection.EntityKinds kind, Guid id, out Guid surfaceId)
    {
        surfaceId = Guid.Empty;
        switch (kind)
        {
            case SetupEntitySelection.EntityKinds.Surface:
                if (setup.FindSurface(id) == null)
                    return false;

                surfaceId = id;
                return true;

            case SetupEntitySelection.EntityKinds.Slice:
                return TryGetSurfaceShowing(setup, id, out surfaceId);

            case SetupEntitySelection.EntityKinds.ContentSource:
                var source = setup.FindSourceByChildId(id);
                if (source == null)
                    return false;

                foreach (var slice in setup.Slices)
                {
                    if (slice.SourceId == source.Id && TryGetSurfaceShowing(setup, slice.Id, out surfaceId))
                        return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryGetSurfaceShowing(Setup setup, Guid sliceId, out Guid surfaceId)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId != sliceId)
                continue;

            surfaceId = surface.Id;
            return true;
        }

        surfaceId = Guid.Empty;
        return false;
    }

    /// <summary>The output a slice reaches first: a patch showing it, else a surface showing it that is mapped.</summary>
    public static bool TryGetSliceOutput(Setup setup, Guid sliceId, out Guid outputId)
    {
        outputId = Guid.Empty;
        foreach (var output in setup.Outputs)
        {
            if (!OutputShowsSlice(output, sliceId))
                continue;

            outputId = output.Id;
            return true;
        }

        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId && TryGetSurfaceOutput(setup, surface.Id, out outputId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The output a send op's content reaches first: through a patch showing one of its slices, or a surface
    /// showing one that is mapped somewhere.
    /// </summary>
    public static bool TryGetSendOutput(Setup setup, Guid symbolChildId, out Guid outputId)
    {
        outputId = Guid.Empty;
        var source = setup.FindSourceByChildId(symbolChildId);
        if (source == null)
            return false;

        foreach (var slice in setup.Slices)
        {
            if (slice.SourceId != source.Id)
                continue;

            foreach (var output in setup.Outputs)
            {
                if (OutputShowsSlice(output, slice.Id))
                {
                    outputId = output.Id;
                    return true;
                }
            }

            foreach (var surface in setup.Surfaces)
            {
                if (surface.SliceId == slice.Id && TryGetSurfaceOutput(setup, surface.Id, out outputId))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The op supplying a slice's source, so selecting a slice can open the canvas it lives on.</summary>
    public static bool TryGetSliceSource(Setup setup, Guid sliceId, out Guid symbolChildId)
    {
        symbolChildId = Guid.Empty;
        var slice = setup.FindSlice(sliceId);
        var source = slice == null ? null : setup.FindSource(slice.SourceId);
        if (source == null)
            return false;

        symbolChildId = source.SymbolChildId;
        return true;
    }

    public static bool TryGetPatchOutput(Setup setup, Guid patchId, out Guid outputId)
    {
        outputId = Guid.Empty;
        if (setup.FindPatch(patchId, out var owner) == null)
            return false;

        outputId = owner!.Id;
        return true;
    }

    /// <summary>Whether a slice belongs to the given source.</summary>
    public static bool IsSliceOf(Setup setup, Guid sliceId, Guid sourceId)
    {
        if (sliceId == Guid.Empty)
            return false;

        var slice = setup.FindSlice(sliceId);
        return slice != null && slice.SourceId == sourceId;
    }

    public static bool IsMappedTo(Surface surface, Guid outputId)
    {
        foreach (var mapping in surface.OutputMappings)
        {
            if (mapping.OutputId == outputId)
                return true;
        }

        return false;
    }

    /// <summary>Whether any surface or patch shows this slice.</summary>
    public static bool IsSliceShown(Setup setup, Guid sliceId)
    {
        if (sliceId == Guid.Empty)
            return false;

        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                return true;
        }

        foreach (var output in setup.Outputs)
        {
            if (OutputShowsSlice(output, sliceId))
                return true;
        }

        return false;
    }

    /// <summary>How many surfaces and patches show this slice — above one, it is shared.</summary>
    public static int CountConsumersOfSlice(Setup setup, Guid sliceId)
    {
        if (sliceId == Guid.Empty)
            return 0;

        var count = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                count++;
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId == sliceId)
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// An output's implicit patch: its only patch, unnamed and covering the whole canvas — the full-canvas
    /// route a drop makes. It *is* the output as far as the user is concerned, so the views fold it into the
    /// output row/card instead of listing it, exactly as a source's full-frame slice folds into the source.
    /// It becomes a patch of its own the moment it is named, turned, moved off the full canvas, or joined by a second.
    /// </summary>
    public static bool TryGetImplicitPatch(OutputDefinition output, out OutputDefinition.Patch? implicitPatch)
    {
        implicitPatch = null;
        if (output.Patches.Count != 1)
            return false;

        // A turned patch is no longer "the output itself": its rotation is state worth seeing and undoing
        // on its own row, which a folded-away patch has none of.
        var patch = output.Patches[0];
        if (!string.IsNullOrEmpty(patch.Name) || !CoversFullCanvas(output, patch) || patch.QuarterTurns != 0)
            return false;

        implicitPatch = patch;
        return true;
    }

    public static bool IsImplicitPatch(OutputDefinition output, OutputDefinition.Patch patch)
    {
        return TryGetImplicitPatch(output, out var implicitPatch) && ReferenceEquals(implicitPatch, patch);
    }

    /// <summary>Patches the views list — the output's implicit full-canvas patch is folded into the output.</summary>
    public static int CountListedPatches(OutputDefinition output)
    {
        return TryGetImplicitPatch(output, out _) ? 0 : output.Patches.Count;
    }

    private static bool CoversFullCanvas(OutputDefinition output, OutputDefinition.Patch patch)
    {
        if (patch.Quad.Length < 4)
            return false;

        var full = OutputDefinition.FullCanvasQuad();
        // A thousandth of the canvas of slack: a quad round-tripped through JSON, or nudged by a drag, is
        // still "the whole canvas".
        for (var i = 0; i < 4; i++)
        {
            if (Vector2.Distance(patch.Quad[i], full[i]) > 0.001f)
                return false;
        }

        return true;
    }

    /// <summary>Whether any patch on the output shows this slice.</summary>
    public static bool OutputShowsSlice(OutputDefinition output, Guid sliceId)
    {
        if (sliceId == Guid.Empty)
            return false;

        foreach (var patch in output.Patches)
        {
            if (patch.SliceId == sliceId)
                return true;
        }

        return false;
    }

    /// <summary>Whether any patch on the output shows a slice of this source.</summary>
    public static bool OutputShowsSource(Setup setup, OutputDefinition output, Guid sourceId)
    {
        foreach (var patch in output.Patches)
        {
            if (IsSliceOf(setup, patch.SliceId, sourceId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A source's implicit slice: its only slice, unnamed, uncut and unrotated — the full frame a drop onto a
    /// surface or output creates. It is the source as far as the user is concerned, so the views fold it into
    /// the content row/card instead of listing it. It surfaces as a slice of its own the moment it is cut,
    /// named, or joined by a second slice.
    /// </summary>
    public static bool TryGetImplicitSlice(Setup setup, Guid sourceId, out Slice? implicitSlice)
    {
        implicitSlice = null;
        Slice? only = null;
        for (var i = 0; i < setup.Slices.Count; i++)
        {
            var slice = setup.Slices[i];
            if (slice.SourceId != sourceId)
                continue;

            if (only != null)
                return false;

            only = slice;
        }

        if (only == null || !IsFullFrame(only))
            return false;

        implicitSlice = only;
        return true;
    }

    public static bool IsImplicitSlice(Setup setup, Slice slice)
    {
        return TryGetImplicitSlice(setup, slice.SourceId, out var implicitSlice) && implicitSlice!.Id == slice.Id;
    }

    /// <summary>Slices the views list — the source's implicit full-frame slice is folded into the source.</summary>
    public static int CountListedSlicesOfSource(Setup setup, Guid sourceId)
    {
        return TryGetImplicitSlice(setup, sourceId, out _) ? 0 : CountSlicesOfSource(setup, sourceId);
    }

    private static bool IsFullFrame(Slice slice)
    {
        const float epsilon = 0.0005f;
        var uv = slice.UvRect;
        return string.IsNullOrEmpty(slice.Name)
               && MathF.Abs(slice.Rotation) < epsilon
               && MathF.Abs(uv.X) < epsilon && MathF.Abs(uv.Y) < epsilon
               && MathF.Abs(uv.Z - 1f) < epsilon && MathF.Abs(uv.W - 1f) < epsilon;
    }

    public static int CountSlicesOfSource(Setup setup, Guid sourceId)
    {
        var count = 0;
        for (var i = 0; i < setup.Slices.Count; i++)
        {
            if (setup.Slices[i].SourceId == sourceId)
                count++;
        }

        return count;
    }

    /// <summary>Surfaces and patches showing any slice of the source.</summary>
    public static int CountConsumersOfSource(Setup setup, Guid sourceId)
    {
        var count = 0;
        foreach (var surface in setup.Surfaces)
        {
            if (IsSliceOf(setup, surface.SliceId, sourceId))
                count++;
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (IsSliceOf(setup, patch.SliceId, sourceId))
                    count++;
            }
        }

        return count;
    }

    public static int CountChildren(Setup setup, Guid parentId)
    {
        var count = 0;
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].ParentId == parentId)
                count++;
        }

        return count;
    }

    /// <summary>The outputs a surface reaches — its own mappings, or a coplanar child's nearest mapped ancestor. Consumers.</summary>
    private static void AddOutputsOfSurface(Setup setup, Surface? surface, List<Relation> into)
    {
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.OutputMappings.Count > 0)
            {
                foreach (var mapping in surface.OutputMappings)
                    into.Add(new Relation(SetupEntitySelection.EntityKinds.Output, mapping.OutputId, true));

                return;
            }

            if (surface.ParentId == Guid.Empty)
                return;

            var parentId = surface.ParentId;
            surface = setup.FindSurface(parentId);
        }
    }

    /// <summary>The slice and its content source feeding a surface/patch — producers.</summary>
    private static void AddSourceOfSlice(Setup setup, Guid sliceId, List<Relation> into)
    {
        if (sliceId == Guid.Empty)
            return;

        into.Add(new Relation(SetupEntitySelection.EntityKinds.Slice, sliceId, false));
        var slice = setup.FindSlice(sliceId);
        var source = slice == null ? null : setup.FindSource(slice.SourceId);
        if (source != null)
            into.Add(new Relation(SetupEntitySelection.EntityKinds.ContentSource, source.SymbolChildId, false));
    }

    /// <summary>Surfaces and patches showing any slice of this source — consumers.</summary>
    private static void AddConsumersOfSource(Setup setup, Guid sourceId, List<Relation> into)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (IsSliceOf(setup, surface.SliceId, sourceId))
                into.Add(new Relation(SetupEntitySelection.EntityKinds.Surface, surface.Id, true));
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (IsSliceOf(setup, patch.SliceId, sourceId))
                    into.Add(new Relation(SetupEntitySelection.EntityKinds.Patch, patch.Id, true));
            }
        }
    }

    /// <summary>Surfaces and patches showing this exact slice — consumers.</summary>
    private static void AddConsumersOfSlice(Setup setup, Guid sliceId, List<Relation> into)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (surface.SliceId == sliceId)
                into.Add(new Relation(SetupEntitySelection.EntityKinds.Surface, surface.Id, true));
        }

        foreach (var output in setup.Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.SliceId == sliceId)
                    into.Add(new Relation(SetupEntitySelection.EntityKinds.Patch, patch.Id, true));
            }
        }
    }
}
