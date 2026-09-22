#nullable enable
using T3.Editor.SystemUi;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Resolves a kind + id against a setup — the one place that knows which list each <see cref="SetupEntityKinds"/>
/// lives in. Plugs are machine state and resolve against the machine config; a content source is addressed by
/// its op's SymbolChildId and counts as existing while a live send op has that child, even before the sync
/// adopts it into the setup.
/// </summary>
internal static class SetupEntities
{
    /// <summary>Whether the entity is still there. Allocation-free: the selection prunes with it every frame.</summary>
    public static bool Exists(Setup setup, SetupEntityKinds kind, Guid id)
    {
        switch (kind)
        {
            case SetupEntityKinds.ReferenceImage:
                return setup.FindReferenceImage(id) != null;

            case SetupEntityKinds.Surface:
                return setup.FindSurface(id) != null;

            case SetupEntityKinds.Prop:
                return setup.FindProp(id) != null;

            case SetupEntityKinds.FloorPlan:
                return setup.FindFloorPlan(id) != null;

            case SetupEntityKinds.Output:
                return setup.FindOutput(id) != null;

            case SetupEntityKinds.Slice:
                return setup.FindSlice(id) != null;

            case SetupEntityKinds.Patch:
                return setup.FindPatch(id, out _) != null;

            case SetupEntityKinds.ContentSource:
                return setup.FindSourceByChildId(id) != null || ContentSourceSync.FindSendInstance(id) != null;

            case SetupEntityKinds.Plug:
                return OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig)
                       && Plugs.Exists(machineConfig, id);

            default:
                return false;
        }
    }

    /// <summary>
    /// The entity's display name — pin labels, tooltips, the rename field. Derived where the entity carries none
    /// (a slice or patch by its position, a prop by its kind, a content source by its op), and never empty: an
    /// unnamed entity reads as its kind. False when the entity is gone.
    /// </summary>
    public static bool TryFindName(Setup setup, SetupEntityKinds kind, Guid id, out string name)
    {
        var label = SetupEntityKindInfo.Of(kind).Label;
        switch (kind)
        {
            case SetupEntityKinds.Surface:
            {
                var surface = setup.FindSurface(id);
                return TryName(surface?.Name, label, surface != null, out name);
            }

            case SetupEntityKinds.Output:
            {
                var output = setup.FindOutput(id);
                return TryName(output?.Name, label, output != null, out name);
            }

            case SetupEntityKinds.ReferenceImage:
            {
                var image = setup.FindReferenceImage(id);
                return TryName(image?.Name, label, image != null, out name);
            }

            case SetupEntityKinds.Prop:
            {
                var prop = setup.FindProp(id);
                return TryName(prop?.Kind, label, prop != null, out name);
            }

            case SetupEntityKinds.FloorPlan:
            {
                var plan = setup.FindFloorPlan(id);
                return TryName(plan?.Name, label, plan != null, out name);
            }

            case SetupEntityKinds.Slice:
            {
                var slice = setup.FindSlice(id);
                name = slice == null ? label : SetupLabels.SliceLabel(setup, slice);
                return slice != null;
            }

            case SetupEntityKinds.Patch:
            {
                var patch = setup.FindPatch(id, out var owner);
                name = patch == null || owner == null ? label : SetupLabels.PatchLabel(owner, patch);
                return patch != null && owner != null;
            }

            case SetupEntityKinds.ContentSource:
            {
                // The live op names it; the setup's mirror stands in while nothing is instantiated.
                var instance = ContentSourceSync.FindSendInstance(id);
                if (instance != null)
                {
                    name = ContentSourceSync.SendName(instance);
                    return true;
                }

                var source = setup.FindSourceByChildId(id);
                return TryName(source?.Name, label, source != null, out name);
            }

            case SetupEntityKinds.Plug:
            {
                var exists = OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig) && Plugs.Exists(machineConfig, id);
                name = exists ? Plugs.PlugName(machineConfig!, id) : label;
                return exists;
            }

            default:
                name = label;
                return false;
        }
    }

    /// <summary>Writes a setup-owned entity's name. Content sources (named by their op) and plugs (machine
    /// state) are not setup state and rename through their own channels; false for those and for a missing entity.</summary>
    public static bool TrySetName(Setup setup, SetupEntityKinds kind, Guid id, string newName)
    {
        switch (kind)
        {
            case SetupEntityKinds.Output:
            {
                var output = setup.FindOutput(id);
                if (output == null)
                    return false;

                output.Name = newName;
                return true;
            }

            case SetupEntityKinds.ReferenceImage:
            {
                var image = setup.FindReferenceImage(id);
                if (image == null)
                    return false;

                image.Name = newName;
                return true;
            }

            case SetupEntityKinds.Surface:
            {
                var surface = setup.FindSurface(id);
                if (surface == null)
                    return false;

                surface.Name = newName;
                return true;
            }

            case SetupEntityKinds.FloorPlan:
            {
                var plan = setup.FindFloorPlan(id);
                if (plan == null)
                    return false;

                plan.Name = newName;
                return true;
            }

            case SetupEntityKinds.Slice:
            {
                var slice = setup.FindSlice(id);
                if (slice == null)
                    return false;

                slice.Name = newName;
                return true;
            }

            case SetupEntityKinds.Patch:
            {
                var patch = setup.FindPatch(id, out _);
                if (patch == null)
                    return false;

                patch.Name = newName;
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// The first entity of any kind whose display name matches — how the debug bridge addresses items. Plugs
    /// are machine state rather than setup entities, but they are items like any other and are found too.
    /// </summary>
    public static bool TryFindByName(Setup setup, string name, out SetupEntityKinds kind, out Guid id)
    {
        foreach (var surface in setup.Surfaces)
        {
            if (Matches(setup, SetupEntityKinds.Surface, surface.Id, name, out kind, out id))
                return true;
        }

        foreach (var output in setup.Outputs)
        {
            if (Matches(setup, SetupEntityKinds.Output, output.Id, name, out kind, out id))
                return true;
        }

        foreach (var image in setup.ReferenceImages)
        {
            if (Matches(setup, SetupEntityKinds.ReferenceImage, image.Id, name, out kind, out id))
                return true;
        }

        foreach (var plan in setup.FloorPlans)
        {
            if (Matches(setup, SetupEntityKinds.FloorPlan, plan.Id, name, out kind, out id))
                return true;
        }

        if (OutputSetupHandling.TryGetActiveSetup(out _, out var machineConfig))
        {
            foreach (var stream in machineConfig.StreamPlugs)
            {
                if (Matches(setup, SetupEntityKinds.Plug, stream.Id, name, out kind, out id))
                    return true;
            }

            var screens = EditorUi.Instance.AllScreens;
            for (var i = 0; i < screens.Count; i++)
            {
                if (Matches(setup, SetupEntityKinds.Plug, Plugs.DisplayPlugId(i), name, out kind, out id))
                    return true;
            }
        }

        foreach (var source in setup.ContentSources)
        {
            if (Matches(setup, SetupEntityKinds.ContentSource, source.SymbolChildId, name, out kind, out id))
                return true;
        }

        foreach (var slice in setup.Slices)
        {
            if (Matches(setup, SetupEntityKinds.Slice, slice.Id, name, out kind, out id))
                return true;
        }

        kind = SetupEntityKinds.None;
        id = Guid.Empty;
        return false;
    }

    private static bool Matches(Setup setup, SetupEntityKinds candidateKind, Guid candidateId, string name,
                                out SetupEntityKinds kind, out Guid id)
    {
        kind = candidateKind;
        id = candidateId;
        return TryFindName(setup, candidateKind, candidateId, out var candidateName) && candidateName == name;
    }

    private static bool TryName(string? storedName, string fallback, bool exists, out string name)
    {
        name = string.IsNullOrEmpty(storedName) ? fallback : storedName;
        return exists;
    }
}
