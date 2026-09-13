#nullable enable
using T3.Core.Output;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The routing graph of a setup, as queries: for any entity, what feeds it (producers, upstream) and what
/// shows it (consumers, downstream), along the content → slice → surface/patch → output chain. The flow
/// outliner and the output views resolve their routing through it.
/// Plain loops, no buffers — this runs inside per-frame draws.
/// </summary>
internal static class SetupRelations
{
    /// <summary>
    /// The output a surface is shown on — the first mapping's output of its mapped ancestor
    /// (<see cref="Setup.FindMappedAncestor"/>), so a Layout child reports where its parent is pinned.
    /// </summary>
    public static bool TryGetSurfaceOutput(Setup setup, Guid surfaceId, out Guid outputId)
    {
        var carrier = setup.FindMappedAncestor(surfaceId);
        if (carrier == null)
        {
            outputId = Guid.Empty;
            return false;
        }

        outputId = carrier.OutputMappings[0].OutputId;
        return true;
    }

    /// <summary>
    /// The output any entity reaches, following the routing downstream: an output is itself, a patch and a
    /// surface name theirs, a slice and a send are traced through whatever shows them. Lets a view offer the
    /// projector a selection *leads to* — picking it is then a matter of selecting that output.
    /// </summary>
    public static bool TryGetOutputOf(Setup setup, SetupEntityKinds kind, Guid id, out Guid outputId)
    {
        outputId = Guid.Empty;
        switch (kind)
        {
            case SetupEntityKinds.Output:
                if (setup.FindOutput(id) == null)
                    return false;

                outputId = id;
                return true;

            case SetupEntityKinds.Patch:
                return TryGetPatchOutput(setup, id, out outputId);

            case SetupEntityKinds.Surface:
                return TryGetSurfaceOutput(setup, id, out outputId);

            case SetupEntityKinds.Slice:
                return TryGetSliceOutput(setup, id, out outputId);

            case SetupEntityKinds.ContentSource:
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
    public static bool TryGetSurfaceOf(Setup setup, SetupEntityKinds kind, Guid id, out Guid surfaceId)
    {
        surfaceId = Guid.Empty;
        switch (kind)
        {
            case SetupEntityKinds.Surface:
                if (setup.FindSurface(id) == null)
                    return false;

                surfaceId = id;
                return true;

            case SetupEntityKinds.Slice:
                return TryGetSurfaceShowing(setup, id, out surfaceId);

            case SetupEntityKinds.ContentSource:
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
            if (slice.SourceId == source.Id && TryGetSliceOutput(setup, slice.Id, out outputId))
                return true;
        }

        return false;
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

        // A turned or fitted patch is no longer "the output itself": its rotation or fit is state worth seeing
        // and undoing on its own item, which a folded-away patch has none of — and a fitted one only covers the
        // canvas while the display happens to match its aspect.
        var patch = output.Patches[0];
        if (!string.IsNullOrEmpty(patch.Name) || !CoversFullCanvas(output, patch) || patch.QuarterTurns != 0 || patch.IsFitted)
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
}
