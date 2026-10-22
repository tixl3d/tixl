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

    /// <summary>
    /// Raised by the host on the main thread whenever the active setup may have changed: an edit (every frame of a
    /// live drag), an undo, a switch to another setup. Operators deriving data from the setup invalidate their
    /// outputs on it instead of re-evaluating, and re-invalidating everything downstream, every frame.
    /// </summary>
    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    public static Surface? FindSurface(Guid id) => Current?.FindSurface(id);

    public static OutputDefinition? FindOutput(Guid id) => Current?.FindOutput(id);
}
