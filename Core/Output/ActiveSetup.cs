#nullable enable
using System;

namespace T3.Core.Output;

/// <summary>
/// The host-published active setup, readable by operators (which only reference Core).
/// The editor (and later the player) assigns <see cref="Current"/> when a project's setup
/// loads or switches; ops resolve entity GUIDs against it each evaluation — never cache
/// the resolved objects across frames.
/// </summary>
public static class ActiveSetup
{
    public static Setup? Current;
    public static MachineConfig? Machine;

    public static Surface? TryFindSurface(Guid id)
    {
        var setup = Current;
        if (setup == null || id == Guid.Empty)
            return null;

        foreach (var surface in setup.Surfaces)
        {
            if (surface.Id == id)
                return surface;
        }

        return null;
    }

    public static OutputDefinition? TryFindOutput(Guid id)
    {
        var setup = Current;
        if (setup == null || id == Guid.Empty)
            return null;

        foreach (var output in setup.Outputs)
        {
            if (output.Id == id)
                return output;
        }

        return null;
    }
}
