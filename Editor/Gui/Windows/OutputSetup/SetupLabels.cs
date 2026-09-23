#nullable enable
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>Display names derived for entities that carry none of their own, and the short forms the gutters use.</summary>
internal static class SetupLabels
{
    /// <summary>A display name for any entity kind — pin labels, tooltips. Resolves against the active
    /// setup; falls back to the kind's label when the entity (or its name) is gone.</summary>
    public static string NameForEntity(SetupEntityKinds kind, Guid id)
    {
        if (OutputSetupHandling.TryGetActiveSetup(out var setup, out _)
            && SetupEntities.TryFindName(setup, kind, id, out var name))
            return name;

        return SetupEntityKindInfo.Of(kind).Label;
    }

    /// <summary>
    /// A slice's display name: a name the user typed if there is one, otherwise a default derived from its
    /// source. Unnamed sources give "Slice N"; a renamed source gives "{name}.N", so naming the op renames
    /// every one of its auto-named slices at once. N is the slice's position among its source's slices.
    /// </summary>
    public static string SliceLabel(Setup setup, Slice slice)
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

    /// <summary>A patch's display name: the typed name, else "Patch N" by its position among the listed
    /// patches — the implicit full-canvas one reads as the output, so it takes no number.</summary>
    public static string PatchLabel(OutputDefinition output, OutputDefinition.Patch patch)
    {
        if (!string.IsNullOrEmpty(patch.Name))
            return patch.Name;

        SetupRelations.TryGetImplicitPatch(output, out var implicitPatch);
        var ordinal = 1;
        foreach (var other in output.Patches)
        {
            if (other.Id == patch.Id)
                break;

            if (!ReferenceEquals(other, implicitPatch))
                ordinal++;
        }

        return $"Patch {ordinal}";
    }

    public static string SurfaceShortLabel(Surface surface)
    {
        return Abbreviate(surface.Name);
    }

    // Compact gutter form: uppercase letters + digits ("Surface 1" → "S1", "WallFront" → "WF"), falling back
    // to the full name when there's nothing to abbreviate (all-lowercase).
    private static string Abbreviate(string name)
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
}
