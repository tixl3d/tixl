#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Logging;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Collab;

/// <summary>
/// Remote management, the editor side. One machine is the <b>controller</b> (its owner is logged in
/// with Discord) and pairs <b>installations</b> with a 6-digit code; the installation keeps only a
/// device secret afterwards and beats a heartbeat. A connect command makes the installation host a
/// party, which the controller then joins - so an installation at another site can be worked on
/// from here like a normal session.
/// </summary>
internal static class CollabRemote
{
    /// <summary>How often an installation reports in; 35 s offline threshold on the backend.</summary>
    private const int HeartbeatIntervalMs = 12_000;

    // --- installation state (persisted) ------------------------------------------------

    public static bool IsPaired => UserSettings.Config.CollabRemoteDeviceId.Length > 0
                                   && UserSettings.Config.CollabRemoteDeviceKey.Length > 0;

    public static string PairedOwner => UserSettings.Config.CollabRemoteOwner;

    /// <summary>What an installation last heard from its heartbeat.</summary>
    public static string InstallationStatus { get; private set; } = "";

    private static long _nextHeartbeatTicks;
    private static int _heartbeatInFlight;
    private static string _pendingAck = "";
    private static string _pendingClaim = "";
    private static DateTime _pendingConnectAt = DateTime.MinValue;

    /// <summary>Called once per frame from the manager; cheap when idle.</summary>
    public static void Tick()
    {
        // The consent switch stops the heartbeat even on a paired machine: turning it off makes
        // this installation look offline to the owner.
        if (!IsPaired || !UserSettings.Config.CollabRemoteAllow || !CollabWebLobby.IsConfigured)
            return;

        if (Environment.TickCount64 < _nextHeartbeatTicks)
            return;

        _nextHeartbeatTicks = Environment.TickCount64 + HeartbeatIntervalMs;
        if (Interlocked.Exchange(ref _heartbeatInFlight, 1) != 0)
            return;

        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 await HeartbeatOnceAsync(CancellationToken.None);
                             }
                             catch (Exception e)
                             {
                                 InstallationStatus = $"Could not reach the server: {e.Message}";
                             }
                             finally
                             {
                                 Interlocked.Exchange(ref _heartbeatInFlight, 0);
                             }
                         });
    }

    private static async Task HeartbeatOnceAsync(CancellationToken cancellation)
    {
        var manager = CollabManager.Instance;
        var project = ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "";
        var state = manager.State switch
                        {
                            CollabManager.SessionState.Hosting => "busy",
                            CollabManager.SessionState.Connecting => "busy",
                            _ => "idle",
                        };
        var room = manager.State is CollabManager.SessionState.Hosting or CollabManager.SessionState.Connecting
                       ? manager.RoomCode
                       : "";

        var result = await CollabRemoteApi.HeartbeatAsync(
            UserSettings.Config.CollabRemoteDeviceId, UserSettings.Config.CollabRemoteDeviceKey,
            DeviceName, project, _version,
            state, _pendingAck, cancellation);

        _pendingAck = "";
        if (!result.Enabled)
        {
            InstallationStatus = "This installation was disabled by its owner";
            return;
        }

        InstallationStatus = $"Managed by {result.Owner}";
        Log.Debug($"Collab remote heartbeat: state={state}, command='{result.CommandType}', ack={_pendingAck.Length > 0}");

        if (result.CommandType.Length == 0)
            return;

        // A command is acknowledged with the next heartbeat, so a dropped reply just repeats it.
        _pendingAck = result.CommandId;

        var actedOnConnect = false;
        switch (result.CommandType)
        {
            case "connect":
                if (manager.State == CollabManager.SessionState.Off)
                {
                    InstallationStatus = "The owner asked to connect - starting a party...";
                    _pendingConnectAt = DateTime.UtcNow;
                    // Hosting with the claim lets the owner's controller join without an
                    // approval round-trip. StartHostViaBackend mutates the manager's UI-thread
                    // state, so it is enqueued there instead of running on this pool thread.
                    manager.RunOnUi(() => manager.StartHostViaBackend(result.Claim));
                    actedOnConnect = true;
                }

                break;

            case "disconnect":
                InstallationStatus = "The owner disconnected";
                manager.RunOnUi(() =>
                                    {
                                        if (manager.State != CollabManager.SessionState.Off)
                                            manager.Leave(manual: true);
                                    });

                break;
        }

        // Only a connect command that was actually acted on may report its room: reporting the
        // room of a session the owner did not start through this command would send the
        // controller into a stale party.
        _pendingClaim = actedOnConnect ? result.Claim : "";

        await Task.Delay(800, cancellation);
        if (manager.State is CollabManager.SessionState.Hosting or CollabManager.SessionState.Connecting
            && manager.RoomCode.Length > 0)
        {
            await CollabRemoteApi.ReportAsync(UserSettings.Config.CollabRemoteDeviceId,
                                              UserSettings.Config.CollabRemoteDeviceKey,
                                              _pendingClaim, "hosting", manager.RoomCode, cancellation);
        }
    }

    /// <summary>Report function failures (e.g. the party could not start) to the owner.</summary>
    public static void ReportFailure(string reason)
    {
        if (!IsPaired)
            return;

        InstallationStatus = reason;
        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 await CollabRemoteApi.ReportAsync(UserSettings.Config.CollabRemoteDeviceId,
                                                                   UserSettings.Config.CollabRemoteDeviceKey,
                                                                   _pendingClaim, "failed", "", CancellationToken.None);
                             }
                             catch
                             {
                                 // Best effort; the next heartbeat still carries the state.
                             }
                         });
    }

    /// <summary>Installation: redeems a pairing code and keeps the issued secret.</summary>
    public static async Task<string?> PairInstallationAsync(string code, CancellationToken cancellation)
    {
        try
        {
            var project = ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "";
            var result = await CollabRemoteApi.RedeemAsync(code, DeviceName, project, _version, cancellation);
            UserSettings.Config.CollabRemoteDeviceId = result.DeviceId;
            UserSettings.Config.CollabRemoteDeviceKey = result.Secret;
            UserSettings.Config.CollabRemoteOwner = result.Owner;
            UserSettings.Config.CollabRemoteAllow = true;
            UserSettings.Save();
            _nextHeartbeatTicks = 0;
            InstallationStatus = $"Paired with {result.Owner}";
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Installation: forgets the pairing on this machine.</summary>
    public static void ForgetInstallation()
    {
        var id = UserSettings.Config.CollabRemoteDeviceId;
        var secret = UserSettings.Config.CollabRemoteDeviceKey;
        UserSettings.Config.CollabRemoteDeviceId = "";
        UserSettings.Config.CollabRemoteDeviceKey = "";
        UserSettings.Config.CollabRemoteOwner = "";
        UserSettings.Save();
        InstallationStatus = "";
        _pendingAck = "";
        _pendingClaim = "";

        if (id.Length == 0 || secret.Length == 0)
            return;

        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 await CollabRemoteApi.UnpairAsync(id, secret, CancellationToken.None);
                             }
                             catch
                             {
                                 // The local pairing is gone either way.
                             }
                         });
    }

    // --- controller state -------------------------------------------------------------

    /// <summary>The owner's paired installations, newest list from <see cref="RefreshDevicesAsync"/>.</summary>
    public static IReadOnlyList<CollabRemoteApi.Device> Devices => _devices;

    public static string ListError { get; private set; } = "";
    public static bool IsLoading { get; private set; }

    private static CollabRemoteApi.Device[] _devices = [];
    private static int _listInFlight;

    public static async Task RefreshDevicesAsync(CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref _listInFlight, 1) != 0)
            return;

        IsLoading = true;
        try
        {
            var token = CollabDiscordAuth.GetValidAccessToken();
            if (token.Length == 0)
            {
                ListError = "Log in with Discord to manage remote computers";
                _devices = [];
                return;
            }

            _devices = [.. await CollabRemoteApi.ListAsync(token, cancellation)];
            ListError = "";
        }
        catch (Exception e)
        {
            ListError = e.Message;
        }
        finally
        {
            IsLoading = false;
            Interlocked.Exchange(ref _listInFlight, 0);
        }
    }

    /// <summary>The name this installation reports: the configured one, else the machine name.</summary>
    public static string DeviceName => string.IsNullOrWhiteSpace(UserSettings.Config.CollabRemoteName)
                                           ? Environment.MachineName
                                           : UserSettings.Config.CollabRemoteName.Trim();

    private static string _version
    {
        get
        {
            var value = typeof(CollabRemote).Assembly.GetName().Version;
            return value == null ? "" : $"{value.Major}.{value.Minor}.{value.Build}";
        }
    }
}
