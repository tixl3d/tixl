#nullable enable
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The drop matrix: which entity kinds connect, and what a drop of one onto another does. Connections are
/// direction-agnostic — dragging an output onto a surface is the same link as dragging the surface onto the
/// output — so every pair is normalized to its content-flow order before it is applied.
/// </summary>
internal static class SetupRouting
{
    /// <summary>
    /// Whether the two kinds form a routing connection at all. Connectable pairs: surface↔output, slice↔output,
    /// source↔output, slice↔surface, source↔surface, slice↔patch, source↔patch, surface↔patch, output↔plug, and
    /// surface/slice/source↔plug (which route into the output the plug presents).
    /// </summary>
    public static bool CanConnect(SetupEntityKinds a, SetupEntityKinds b)
    {
        // Normalize so `a` is the content-flow upstream side.
        if (SetupEntityKindInfo.Of(a).RoutingRank > SetupEntityKindInfo.Of(b).RoutingRank)
            (a, b) = (b, a);

        return b switch
                   {
                       SetupEntityKinds.Output => a is SetupEntityKinds.Surface
                                                       or SetupEntityKinds.Slice
                                                       or SetupEntityKinds.ContentSource,
                       SetupEntityKinds.Surface => a is SetupEntityKinds.Slice
                                                        or SetupEntityKinds.ContentSource,
                       SetupEntityKinds.Patch => a is SetupEntityKinds.Slice
                                                      or SetupEntityKinds.ContentSource
                                                      or SetupEntityKinds.Surface,
                       SetupEntityKinds.Plug => a is SetupEntityKinds.Output
                                                     or SetupEntityKinds.Surface
                                                     or SetupEntityKinds.Slice
                                                     or SetupEntityKinds.ContentSource,
                       _ => false,
                   };
    }

    /// <summary>The drag payload for an entity — the inverse of <see cref="TryParseDrag"/>.</summary>
    public static string DragPayload(SetupEntityKinds kind, Guid id) => $"{(int)kind}:{id}";

    public static bool TryParseDrag(string data, out SetupEntityKinds kind, out Guid id)
    {
        kind = SetupEntityKinds.None;
        id = Guid.Empty;
        var separator = data.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(data.AsSpan(0, separator), out var kindInt)
            || !Guid.TryParse(data.AsSpan(separator + 1), out id))
            return false;

        kind = (SetupEntityKinds)kindInt;
        return true;
    }

    public static void ApplyDrop(Setup setup, SetupEntityKinds dragKind, Guid dragId,
                                 SetupEntityKinds targetKind, Guid targetId)
    {
        // A plug binding is machine state, not setup state: it saves on its own and sits outside the setup's undo.
        if (dragKind == SetupEntityKinds.Plug || targetKind == SetupEntityKinds.Plug)
        {
            if (!OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
                return;

            var plugId = dragKind == SetupEntityKinds.Plug ? dragId : targetId;
            var otherKind = dragKind == SetupEntityKinds.Plug ? targetKind : dragKind;
            var otherId = dragKind == SetupEntityKinds.Plug ? targetId : dragId;

            if (otherKind == SetupEntityKinds.Output)
            {
                if (setup.FindOutput(otherId) != null)
                    Plugs.BindOutput(machineConfig, otherId, plugId);

                return;
            }

            // A plug stands for what it presents: dropping content or a surface on it routes into that output,
            // creating and binding one when the plug is still free — the three steps that shortcut asks for.
            var target = Plugs.TryGetBoundOutput(setup, machineConfig, plugId);
            if (target == null)
            {
                var created = CreateOutputForPlug(setup, machineConfig, plugId);
                if (created == null)
                    return;

                target = created;
            }

            var outputTargetId = target.Id;
            SetupUndo.RunUndoable("Connect", setup, () => ApplyDropInternal(setup, otherKind, otherId,
                                                                            SetupEntityKinds.Output, outputTargetId));
            return;
        }

        SetupUndo.RunUndoable("Connect", setup, () => ApplyDropInternal(setup, dragKind, dragId, targetKind, targetId));
    }

    /// <summary>
    /// One rule for every pair: a drop connects the two. Dropping what the target already takes changes nothing,
    /// and otherwise the target's input is *replaced* — a drop never stacks a second route onto the same place,
    /// which would read as "re-fed" while quietly keeping the old one alive underneath. Sub-regions and extra
    /// patches are made deliberately (their "Add …" actions), never as a side effect of a drop.
    /// </summary>
    private static void ApplyDropInternal(Setup setup, SetupEntityKinds dragKind, Guid dragId,
                                          SetupEntityKinds targetKind, Guid targetId)
    {
        // Normalize so the upstream side is always the drag and the cases below only handle one direction each.
        if (SetupEntityKindInfo.Of(dragKind).RoutingRank > SetupEntityKindInfo.Of(targetKind).RoutingRank)
        {
            (dragKind, targetKind) = (targetKind, dragKind);
            (dragId, targetId) = (targetId, dragId);
        }

        if (targetKind == SetupEntityKinds.Output && dragKind == SetupEntityKinds.Surface)
        {
            var surface = setup.FindSurface(dragId);
            var output = setup.FindOutput(targetId);

            // A region normally rides its parent's pin; a mapping of its own overrides that for this output
            // (see Surface.OutputMappings). It stays a child everywhere else — same plane, same rectangle.
            if (surface != null && output != null && !surface.HasMapping(targetId))
                surface.OutputMappings.Add(CreateDefaultMapping(output));

            return;
        }

        // A surface (or region) dropped on a patch is pinned to the patch's quad on that output. A patch that fed
        // content hands it over and goes, or both would draw the same pixels; a patch without content is layout
        // (a venue's pixel map traced as patches) and stays. The inverse of "Use on Surface", and for a region the way to give it a
        // pin of its own on one output while it keeps riding its parent everywhere else.
        if (targetKind == SetupEntityKinds.Patch && dragKind == SetupEntityKinds.Surface)
        {
            var surface = setup.FindSurface(dragId);
            var patch = setup.FindPatch(targetId, out var patchOutput);
            if (surface == null || patch == null || patchOutput == null || patch.Quad.Length < 4)
                return;

            // In the turned corner order, so a wall that the patch showed on its side keeps standing that way.
            var quad = new Vector2[4];
            patch.CopyTurnedCorners(quad);
            var mapping = surface.FindMapping(patchOutput.Id);
            if (mapping != null)
            {
                mapping.Quad = quad;
            }
            else
            {
                surface.OutputMappings.Add(new Surface.OutputMapping { OutputId = patchOutput.Id, Quad = quad });
            }

            // Nothing shown here yet: adopt what the patch fed, so the drop doesn't silently drop its content.
            if (surface.SliceId == Guid.Empty)
                surface.SliceId = patch.SliceId;

            if (patch.SliceId != Guid.Empty)
                patchOutput.Patches.RemoveAll(p => p.Id == targetId);

            return;
        }

        // Dropping a source or slice straight onto an output shows it full-frame: the direct pipe, as a new
        // full-canvas patch (no surface, no corner pin). Dropped onto a patch, it re-feeds that patch.
        if (dragKind is SetupEntityKinds.Slice or SetupEntityKinds.ContentSource
            && targetKind is SetupEntityKinds.Output or SetupEntityKinds.Patch)
        {
            var sliceId = Guid.Empty;
            if (dragKind == SetupEntityKinds.Slice && setup.FindSlice(dragId) != null)
            {
                sliceId = dragId;
            }
            else if (dragKind == SetupEntityKinds.ContentSource)
            {
                var source = setup.FindSourceByChildId(dragId);
                if (source != null)
                    sliceId = EnsureSlice(setup, source).Id;
            }

            if (sliceId == Guid.Empty)
                return;

            if (targetKind == SetupEntityKinds.Patch)
            {
                var patch = setup.FindPatch(targetId, out _);
                if (patch != null)
                    patch.SliceId = sliceId;
            }
            else
            {
                var output = setup.FindOutput(targetId);
                if (output != null)
                    FeedOutputDirectly(output, sliceId);
            }

            return;
        }

        // A surface shows one slice: the drop sets it, replacing whatever it showed. (To show a second thing on
        // the same wall, add a region and feed that — a drop is a connection, not a layout decision.)
        if (dragKind == SetupEntityKinds.Slice)
        {
            var slice = setup.FindSlice(dragId);
            var surface = setup.FindSurface(targetId);
            if (slice != null && surface != null)
                surface.SliceId = slice.Id;
        }

        if (dragKind == SetupEntityKinds.ContentSource)
        {
            var source = setup.FindSourceByChildId(dragId);
            var surface = source == null ? null : setup.FindSurface(targetId);
            if (source != null && surface != null)
                surface.SliceId = EnsureSlice(setup, source).Id;
        }
    }

    /// <summary>
    /// The canvas a free plug needs before anything can be routed to it: named and sized after the plug, and
    /// bound to it right away. Its creation is undoable; the binding is machine state and saves on its own, so
    /// undoing the drop leaves the binding pointing at a canvas that is gone — harmless (the plug reads as free
    /// again) and re-done by redo.
    /// </summary>
    private static OutputDefinition? CreateOutputForPlug(Setup setup, MachineConfig machineConfig, Guid plugId)
    {
        OutputDefinition? created = null;
        SetupUndo.RunUndoable("Add output", setup, () =>
                                                   {
                                                       created = new OutputDefinition
                                                                     {
                                                                         Name = Plugs.PlugName(machineConfig, plugId),
                                                                         Kind = OutputDefinition.Kinds.Display,
                                                                         CanvasResolution = Plugs.PlugResolution(plugId),
                                                                     };
                                                       setup.Outputs.Add(created);
                                                   });

        if (created != null)
            Plugs.BindOutput(machineConfig, created.Id, plugId);

        return created;
    }

    /// <summary>
    /// Feeds a slice straight onto an output's canvas. The direct pipe is one full-canvas patch, so a repeat
    /// drop re-feeds it instead of stacking a second one exactly over it. An output already split into tiles
    /// keeps them: the drop adds the full-canvas layer it asked for, which the tiles then sit under.
    /// </summary>
    private static void FeedOutputDirectly(OutputDefinition output, Guid sliceId)
    {
        foreach (var existing in output.Patches)
        {
            // Already showing it: the drop asked for a connection that is already there.
            if (existing.SliceId == sliceId)
                return;
        }

        if (output.Patches.Count == 1)
        {
            output.Patches[0].SliceId = sliceId;
            return;
        }

        SetupActions.AddPatchInternal(output, sliceId);
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

        // Unnamed: its label is derived from the source (see SetupLabels.SliceLabel), so renaming the op renames it too.
        var slice = new Slice { SourceId = source.Id };
        setup.Slices.Add(slice);
        return slice;
    }

    /// <summary>The middle 60% of the canvas, as fractions of it like every mapping quad.</summary>
    private static Surface.OutputMapping CreateDefaultMapping(OutputDefinition output)
    {
        const float x0 = 0.2f, x1 = 0.8f, y0 = 0.2f, y1 = 0.8f;
        return new Surface.OutputMapping
                   {
                       OutputId = output.Id,
                       Quad = [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)],
                   };
    }
}
