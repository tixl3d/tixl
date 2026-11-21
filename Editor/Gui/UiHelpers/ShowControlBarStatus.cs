#nullable enable
using ImGuiNET;
using System.Net;
using System.Net.Sockets;
using T3.Core.DataTypes.Vector;
using T3.Core.Show;
using T3.Core.Utils;
using T3.Editor.Collab;
using T3.Editor.Gui.Styling;
using T3.Editor.Show;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui;

/// <summary>
/// The compact show-control indicator drawn centered in the menu bar: one short role word per
/// active feature (Master, Slave, Host, Joined, Active, Standby). Hovering a segment opens a
/// detail tooltip — mode, addresses, group/room, peers — in the style of the GPU memory monitor.
/// Nothing is drawn when no show-control feature is active.
/// </summary>
internal static class ShowControlBarStatus
{
    private readonly record struct Segment(string Label, Color Color, string[] DetailLines);

    private static readonly List<string> _peerAddresses = [];

    /// <summary>
    /// Draws the show-control status pill centered in the menu bar: the current mode and the
    /// machine's role in it. Pure status - modes are switched where they belong (Collab in the
    /// Collab window's Party tabs); hovering shows the live details.
    /// </summary>
    public static void DrawMenuBarIndicator()
    {
        var mode = ShowControlMode.Current;
        var role = ShowControlMode.RoleLabel;
        var label = mode == ShowControlMode.Off
                        ? "Editor mode"
                        : role.Length > 0
                            ? $"{ModeDisplayName(mode)} · {role}"
                            : ModeDisplayName(mode);

        var (color, detailLines) = DescribeCurrent(mode);

        var pad = new System.Numerics.Vector2(8, 3) * T3Ui.UiScaleFactor;
        var size = ImGui.CalcTextSize(label) + pad * 2;
        var totalWidth = size.X;
        ImGui.SameLine(0, 14 * T3Ui.UiScaleFactor);
        var startX = (ImGui.GetWindowWidth() - totalWidth) * 0.5f;
        if (startX > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(startX);

        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        // Subtle tinted pill behind the mode word; the word itself in the status color.
        drawList.AddRectFilled(pos, pos + size,
                               ImGui.ColorConvertFloat4ToU32(color.Fade(0.18f).Rgba),
                               4 * T3Ui.UiScaleFactor);
        drawList.AddText(pos + pad,
                         ImGui.ColorConvertFloat4ToU32(color.Rgba),
                         label);

        ImGui.InvisibleButton("##showMode", size);
        if (ImGui.IsItemHovered() && detailLines.Length > 0)
        {
            CustomComponents.BeginTooltip(360);
            foreach (var line in detailLines)
            {
                if (string.IsNullOrEmpty(line))
                    ImGui.TextUnformatted("");
                else
                    ImGui.TextUnformatted(line);
            }

            CustomComponents.EndTooltip();
        }
    }

    private static string ModeDisplayName(string mode) => mode switch
                                                             {
                                                                 ShowControlMode.Boygroup => "Boygroup",
                                                                 ShowControlMode.Failover => "Failover",
                                                                 ShowControlMode.Collab   => "Collab",
                                                                 _                        => "Editor mode",
                                                             };

    /// <summary>Status color and hover detail for the current mode; falls back to the pill word when nothing runs.</summary>
    private static (Color Color, string[] Lines) DescribeCurrent(string mode)
    {
        var segments = CollectSegments();
        if (segments.Count > 0)
            return (segments[0].Color, segments[0].DetailLines);

        return (UiColors.TextMuted, mode == ShowControlMode.Off
                                         ? []
                                         : [$"{ModeDisplayName(mode)} is enabled, but no session is running yet."]);
    }

    private static List<Segment> CollectSegments()
    {
        var segments = new List<Segment>(3);

        // Failover first: when enabled it owns the streaming role.
        if (FailoverArbiter.IsEnabled)
        {
            var role = FailoverArbiter.RoleText;
            _peerAddresses.Clear();
            FailoverArbiter.GetLivePeerAddresses(_peerAddresses);
            var peerAddress = _peerAddresses.Count > 0 ? _peerAddresses[0] : "—";

            // Green = this machine is live; blue = ready and following; magenta = a handover is happening.
            var (label, color) = role switch
                                      {
                                          "ACTIVE (streaming)" => ("Active", UiColors.StatusOkay),
                                          "Standby"            => ("Standby", UiColors.StatusActivated),
                                          "Handover"           => ("Handover", UiColors.StatusAttention),
                                          "Probing"            => ("Probing", UiColors.TextMuted),
                                          _                    => ("Failover", UiColors.TextMuted),
                                      };

            var config = UserSettings.Config;
            segments.Add(new Segment(label, color,
                                     [
                                         $"Show Failover — {role}",
                                         $"Show: {config.FailoverShowId} · Priority: {config.FailoverPriority}",
                                         $"Peer: {peerAddress}",
                                         FailoverArbiter.PeerSummary,
                                         $"Takeovers so far: {FailoverArbiter.TakeoverCount}",
                                     ]));
        }

        var boygroup = BoygroupManager.Instance;
        if (boygroup.State == BoygroupManager.BoygroupState.Server)
        {
            var ip = LocalAddress(FirstAddress(boygroup.Clients), UserSettings.Config.BoygroupInterface) ?? "any";
            var detail = new List<string>
                             {
                                 "Boygrouping — Master (server)",
                                 $"Address: {ip} · Port: {UserSettings.Config.BoygroupPort}",
                                 $"Group: {boygroup.GroupName}",
                                 $"Nodes: {boygroup.Clients.Count} connected",
                             };
            foreach (var client in boygroup.Clients)
                detail.Add($"  · {client.Name} ({client.Address}) — {client.StatusText}");

            if (!string.IsNullOrEmpty(boygroup.DeploySummary))
                detail.Add(boygroup.DeploySummary);
            if (boygroup.RestartPending)
                detail.Add("Restart pending on the nodes");

            var masterColor = boygroup.RestartPending
                                  ? UiColors.StatusAttention
                                  : UiColors.StatusActivated;
            segments.Add(new Segment("Master", masterColor, [.. detail]));
        }
        else if (boygroup.State == BoygroupManager.BoygroupState.Client)
        {
            var detail = new List<string>
                             {
                                 "Boygrouping — Slave (render node)",
                                 $"Master: {boygroup.ConnectedMasterAddress ?? "?"}",
                                 $"Group: {boygroup.GroupName}",
                                 boygroup.DeploySummary.Length > 0 ? $"Deploy: {boygroup.DeploySummary}" : "",
                                 boygroup.TimeFollowSummary,
                             };
            if (!string.IsNullOrEmpty(boygroup.ErrorText))
                detail.Add(boygroup.ErrorText);

            // Green once the show files are on the node; magenta while a restart is pending or
            // something went wrong.
            var slaveColor = !string.IsNullOrEmpty(boygroup.ErrorText) || boygroup.RestartPending
                                 ? UiColors.StatusAttention
                                 : boygroup.DeploySummary.Contains("complete", StringComparison.OrdinalIgnoreCase)
                                     ? UiColors.StatusOkay
                                     : UiColors.TextMuted;
            segments.Add(new Segment("Slave", slaveColor, [.. detail]));
        }

        var collab = CollabManager.Instance;
        if (collab.IsEnabled && collab.IsSessionActive)
        {
            if (collab.State == CollabManager.SessionState.Hosting)
            {
                var ip = LocalAddress(FirstAddress(collab.Peers), UserSettings.Config.CollabInterface) ?? "any";
                var detail = new List<string>
                                 {
                                     "Collaboration — Hosting",
                                     collab.RoomCode.Length > 0
                                         ? $"Session ID: {collab.RoomCode} · Relay: {collab.RelayEndpoint}"
                                         : $"Address: {ip} · Port: {UserSettings.Config.CollabPort}",
                                     $"Participants: {collab.Peers.Count}",
                                     $"Signed in as {collab.LocalIdentity.DisplayName}",
                                 };
                var hostColor = string.IsNullOrEmpty(collab.ErrorText)
                                    ? UiColors.StatusActivated
                                    : UiColors.StatusAttention;
                segments.Add(new Segment("Host", hostColor, [.. detail]));
            }
            else
            {
                var host = collab.ConnectedHostAddress;
                var detail = host != null
                                 ? new List<string>
                                       {
                                           "Collaboration — Connected",
                                           $"Host: {host}",
                                           $"Signed in as {collab.LocalIdentity.DisplayName}",
                                       }
                                 : new List<string>
                                       {
                                           "Collaboration — Connected via relay",
                                           $"Room: {UserSettings.Config.CollabLastRelayRoom} · Relay: {collab.RelayEndpoint}",
                                           $"Signed in as {collab.LocalIdentity.DisplayName}",
                                       };
                var joinedColor = string.IsNullOrEmpty(collab.ErrorText)
                                      ? UiColors.StatusOkay
                                      : UiColors.StatusAttention;
                segments.Add(new Segment("Joined", joinedColor, [.. detail]));
            }
        }

        return segments;
    }

    private static string? FirstAddress(IReadOnlyList<CollabPeerConnection> peers) =>
        peers.Count > 0 ? peers[0].RemoteAddress?.ToString() : null;

    private static string? FirstAddress(IReadOnlyList<BoygroupManager.ClientInfo> clients) =>
        clients.Count > 0 ? clients[0].Peer.RemoteAddress?.ToString() : null;

    /// <summary>
    /// The address the serving side is reached on: the adapter pinned for that service (Settings →
    /// Networking), otherwise the local interface a UDP probe toward the first connected peer would
    /// use — the same route the real show traffic takes.
    /// </summary>
    private static string? LocalAddress(string? towardAddress, string adapterName)
    {
        var pinned = LanInterfaces.ResolveAddress(adapterName)?.ToString();
        if (!string.IsNullOrEmpty(pinned))
            return pinned;

        if (!string.IsNullOrEmpty(towardAddress) && IPAddress.TryParse(towardAddress, out var target))
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(target, 9);
                return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
            }
            catch
            {
                // No route to that peer; the detail tooltip then shows "any".
            }
        }

        return null;
    }
}
