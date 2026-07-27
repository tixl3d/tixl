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

    public static Surface? TryFindSurface(Guid id) => Current?.FindSurface(id);

    public static OutputDefinition? TryFindOutput(Guid id) => Current?.FindOutput(id);
}
