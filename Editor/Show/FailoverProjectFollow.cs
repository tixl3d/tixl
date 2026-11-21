#nullable enable
using T3.Core.Show;

namespace T3.Editor.Show;

/// <summary>
/// Failover counterpart of the boygroup deploy: the standby opens the project the active node
/// advertises in its heartbeat, so a takeover renders the same show. Both machines are expected to
/// have the show installed — the heartbeat carries only the name, not the files.
/// </summary>
internal static class FailoverProjectFollow
{
    /// <summary>Called once per frame from the editor update.</summary>
    public static void Update()
    {
        if (!FailoverArbiter.IsEnabled || FailoverArbiter.IsActiveNode)
        {
            _lastAttemptName = null;
            return;
        }

        var name = FailoverArbiter.ActivePeerProjectName;
        if (string.IsNullOrEmpty(name))
            return;

        if (ProjectFollower.IsOpenName(name))
        {
            _lastAttemptName = null;
            return;
        }

        // Switching loads a project (and its GPU resources) — retrying every frame while the
        // project is missing or still coming up would stall the editor. A few seconds apart is
        // enough to pick up a project that finishes installing while failover runs.
        var now = Environment.TickCount64;
        if (now < _nextAttemptTicks)
            return;

        _nextAttemptTicks = now + RetryIntervalMs;

        if (ProjectFollower.TrySwitchToName(name))
        {
            _lastAttemptName = name;
            Log.Info($"Failover: following the active node onto project '{name}'");
            return;
        }

        if (_lastAttemptName != name)
        {
            _lastAttemptName = name;
            Log.Warning($"Failover: the active node runs project '{name}', which is not installed on this machine");
        }
    }

    private const long RetryIntervalMs = 5_000;
    private static long _nextAttemptTicks;
    private static string? _lastAttemptName;
}
