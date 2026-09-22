namespace T3.Graphics;

/// <summary>
/// The graphics layer sits below Core, so it cannot use Core's logger. Core points these at it during
/// startup; until then messages go nowhere, which is the right behaviour for a unit test.
/// </summary>
public static class GraphicsLog
{
    public static Action<string>? Warning;
    public static Action<string>? Error;
    public static Action<string>? Debug;

    /// <summary>
    /// Warns once per message. Used where a warning would otherwise repeat every frame — a shader binding
    /// past the slot limit, an unsupported format — and drowning the log is worse than missing a repeat.
    /// </summary>
    public static void WarnOnce(string message)
    {
        lock (_seen)
        {
            if (!_seen.Add(message))
                return;
        }

        Warning?.Invoke(message);
    }

    private static readonly HashSet<string> _seen = [];
}
