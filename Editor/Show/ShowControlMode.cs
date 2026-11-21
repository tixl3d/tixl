#nullable enable
using T3.Core.Show;
using T3.Editor.Collab;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Show;

/// <summary>
/// The mutually exclusive show-control modes. A machine runs exactly one of the three features at
/// a time (or none): boygrouping distributes a show to render nodes, failover keeps a redundant
/// pair streaming, collaboration edits one graph together. <see cref="SwitchTo"/> enforces the
/// exclusivity — entering a mode tears the other two down — and mirrors the per-feature enable
/// switches in the settings panels. The active mode is derived from the live feature states, so
/// switches made in the panels, over the debug bridge and in the menu bar all agree.
/// </summary>
internal static class ShowControlMode
{
    public const string Off = "off";
    public const string Boygroup = "boygroup";
    public const string Failover = "failover";
    public const string Collab = "collab";

    public static readonly string[] All = [Off, Boygroup, Failover, Collab];

    /// <summary>The mode this machine currently runs, derived from the live feature states.</summary>
    public static string Current
    {
        get
        {
            // Failover first: it owns the machine when enabled (it may drive a collab session).
            if (FailoverArbiter.IsEnabled)
                return Failover;

            var boygroup = BoygroupManager.Instance;
            if (UserSettings.Config.BoygroupEnabled || boygroup.State != BoygroupManager.BoygroupState.Off)
                return Boygroup;

            var collab = CollabManager.Instance;
            if (UserSettings.Config.CollabEnabled || collab.IsSessionActive || collab.State == CollabManager.SessionState.Connecting)
                return Collab;

            return Off;
        }
    }

    /// <summary>Human-readable label of the machine's role in the current mode, or "" when off.</summary>
    public static string RoleLabel
    {
        get
        {
            switch (Current)
            {
                case Failover:
                    return FailoverArbiter.RoleText.Replace("ACTIVE (streaming)", "Active", StringComparison.Ordinal);
                case Boygroup:
                {
                    var boygroup = BoygroupManager.Instance;
                    return boygroup.State switch
                               {
                                   BoygroupManager.BoygroupState.Server => "Master",
                                   BoygroupManager.BoygroupState.Client => "Slave",
                                   _ => UserSettings.Config.BoygroupIsMaster ? "Master" : "Slave",
                               };
                }
                case Collab:
                {
                    var collab = CollabManager.Instance;
                    return collab.State switch
                               {
                                   CollabManager.SessionState.Hosting => "Host",
                                   CollabManager.SessionState.Connected => "Joined",
                                   _ => "Connecting",
                               };
                }
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// Claims a mode without starting anything: tears the other features down and flips the
    /// enable flags, so a session started manually from a settings panel (a direct LAN host, a
    /// specific room join) runs inside the right mode.
    /// </summary>
    public static void Activate(string mode)
    {
        if (mode != Boygroup)
            TearDownBoygroup();

        if (mode != Failover && FailoverArbiter.IsEnabled)
        {
            UserSettings.Config.FailoverEnabled = false;
            UserSettings.Save();
            FailoverArbiter.ApplyConfig(new FailoverConfig { Enabled = false });
        }

        if (mode != Collab)
        {
            var collab = CollabManager.Instance;
            if (collab.IsSessionActive || collab.State == CollabManager.SessionState.Connecting)
                collab.Leave();
            UserSettings.Config.CollabEnabled = false;
        }

        switch (mode)
        {
            case Boygroup:
                UserSettings.Config.BoygroupEnabled = true;
                break;
            case Failover:
                UserSettings.Config.FailoverEnabled = true;
                break;
            case Collab:
                UserSettings.Config.CollabEnabled = true;
                break;
        }

        UserSettings.Save();
    }

    /// <summary>
    /// Switches this machine to a mode: claims it (tearing the other features down) and starts
    /// the target one - boygrouping applies its persisted role, failover enables the arbiter,
    /// collaboration opens a relay room others can join. Returns an error message or null.
    /// </summary>
    public static string? SwitchTo(string mode)
    {
        if (mode != Off && mode != Boygroup && mode != Failover && mode != Collab)
            return $"unknown mode '{mode}'";

        Activate(mode);

        switch (mode)
        {
            case Boygroup:
                BoygroupManager.Instance.ApplyEnabledSetting();
                break;
            case Failover:
                // Like the debug bridge and the settings panel: enable through the persisted
                // settings, then push the config - the arbiter reconciles from settings each frame.
                FailoverArbiter.ApplyConfig(new FailoverConfig
                                                {
                                                    Enabled = true,
                                                    ShowId = UserSettings.Config.FailoverShowId,
                                                    Priority = UserSettings.Config.FailoverPriority,
                                                    HeartbeatMs = UserSettings.Config.FailoverHeartbeatMs,
                                                    TimeoutFactor = UserSettings.Config.FailoverTimeoutFactor,
                                                    Preempt = UserSettings.Config.FailoverPreempt,
                                                });
                break;
            case Collab:
            {
                var (host, port) = ParseRelay(UserSettings.Config.CollabRelayAddress);
                CollabManager.Instance.StartHostViaRelay(host, port);
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets the boygroup role (master = server / slave = render node) from the menu bar: persists
    /// it and re-applies the boygroup session when one is running, so a serving master becomes a
    /// searching slave and vice versa without touching the other modes.
    /// </summary>
    public static void SetBoygroupRole(bool isMaster)
    {
        if (UserSettings.Config.BoygroupIsMaster == isMaster)
            return;

        UserSettings.Config.BoygroupIsMaster = isMaster;
        UserSettings.Save();

        var boygroup = BoygroupManager.Instance;
        if (Current == Boygroup || boygroup.State != BoygroupManager.BoygroupState.Off)
        {
            boygroup.Leave();
            boygroup.ApplyEnabledSetting();
        }
    }

    private static void TearDownBoygroup()
    {
        var boygroup = BoygroupManager.Instance;
        if (boygroup.State != BoygroupManager.BoygroupState.Off)
            boygroup.Leave();

        if (UserSettings.Config.BoygroupEnabled)
        {
            UserSettings.Config.BoygroupEnabled = false;
            UserSettings.Save();
        }
    }

    private static (string Host, int Port) ParseRelay(string? configured)
    {
        var text = configured?.Trim();
        if (string.IsNullOrEmpty(text))
            return (CollabProtocol.DefaultRelayHost, CollabProtocol.DefaultRelayPort);

        var separator = text.LastIndexOf(':');
        if (separator > 0 && int.TryParse(text[(separator + 1)..], out var port))
            return (text[..separator], port);

        return (text, CollabProtocol.DefaultRelayPort);
    }
}
