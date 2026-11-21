#nullable enable
using ImGuiNET;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes.Vector;
using T3.Editor.Collab;
using T3.Editor.Show;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// The "Collaboration" panel of the settings window: identity (Discord login), session controls
/// (start a session and share the session ID, or join with an ID) and the participant list.
/// Direct/LAN sessions and the relay choice live under Advanced.
/// </summary>
internal sealed partial class SettingsWindow
{
    private static readonly LanDiscoveryScanner _discoveryScanner = new();

    private static void DrawCollabPanel(ref bool changed)
    {
        var manager = CollabManager.Instance;
        var config = UserSettings.Config;

        // Collaboration is switched through the menu-bar mode selector; this panel only
        // configures and runs sessions. Session actions below claim the collab mode, which ends
        // a boygroup/failover session on this machine (the modes are mutually exclusive).

        // -------------------------------------------------------------------------------------
        // Identity
        // -------------------------------------------------------------------------------------
        PanelLayout.BeginSection("Identity", "How other participants see you in sessions and in the session browser.");

        DrawDiscordLoginButton();

        if (FormInputs.AddCheckBox("Stay logged in",
                                   ref config.CollabDiscordStayLoggedIn,
                                   """
                                   Keeps the Discord login in the settings file, so you stay verified for other
                                   participants after restarting TiXL. Off: the login is forgotten when TiXL closes.
                                   """,
                                   false))
        {
            changed = true;
            CollabDiscordAuth.ApplyStayLoggedIn();
        }

        changed |= FormInputs.AddStringInput("Nickname",
                                             ref config.CollabDisplayName,
                                             null,
                                             null,
                                             """
                                             Local fallback when not logged in to Discord.
                                             Falls back to your OS user name when empty.
                                             """);

        PanelLayout.EndSection();

        // What sessions you host look like before one actually runs. While a session runs, the
        // "Session browser entry" section in DrawActiveSession owns the listing.
        PanelLayout.BeginSection("Hosting defaults", "Applied to the next session you host.");

        changed |= DrawSecretInput("Session password", ref config.CollabPassword,
                                   "Empty = anyone with the session ID can join. Recommended when the ID is shared widely.");

        if (!manager.IsSessionActive)
        {
            changed |= FormInputs.AddCheckBox("List in session browser",
                                              ref config.CollabListSession,
                                              """
                                              Shows a session you host in the relay's list of open sessions, with your name,
                                              the title and how many people are in it. Off: only people who have the
                                              session ID can find it. Applies to the next session you host.
                                              """,
                                              false);
            if (config.CollabListSession)
            {
                changed |= FormInputs.AddStringInput("Session title",
                                                     ref config.CollabSessionTitle,
                                                     $"{manager.LocalIdentity.DisplayName}'s session",
                                                     null,
                                                     "What people see in the session browser.");
                changed |= DrawPurposeInput(ref config.CollabSessionPurpose);
            }
        }

        PanelLayout.EndSection();

        // Sessions run from the Collab window; this panel only points there and reflects the state.
        PanelLayout.BeginSection("Current session");
        if (manager.IsSessionActive)
        {
            PanelLayout.ValueRow("State", manager.StatusText, UiColors.StatusActivated);
            if (!string.IsNullOrEmpty(manager.ErrorText))
            {
                PanelLayout.Space(PanelLayout.SpaceXS);
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
                ImGui.TextWrapped(manager.ErrorText);
                ImGui.PopStyleColor();
            }

            PanelLayout.Space(PanelLayout.SpaceS);
            FormInputs.SetCursorToParameterEdit();
            if (ImGui.Button("Open the Collab Window"))
                WindowManager.CollabWindow.Config.Visible = true;

            CustomComponents.TooltipForLastItem("Participants, whispers, listing and chat live there now.");
        }
        else
        {
            if (manager.IsReconnecting)
            {
                PanelLayout.ValueRow("State", manager.StatusText, UiColors.StatusWarning);
                FormInputs.SetCursorToParameterEdit();
                if (PanelLayout.DestructiveButton("Stop Reconnecting"))
                    manager.Leave(manual: true);
            }
            else
            {
                CustomComponents.HelpText("""
                                          No session is running. Host or join from the Collab window
                                          (menu: Windows > Collab) - its Party matching tab has the session
                                          browser and the hosting controls.
                                          """);
            }

            PanelLayout.Space(PanelLayout.SpaceS);
            if (CustomComponents.DrawCtaButton("Open the Collab Window", Icon.None, UiColors.ForegroundFull, UiColors.StatusActivated, Color.Transparent))
                WindowManager.CollabWindow.Config.Visible = true;
        }

        PanelLayout.EndSection();

        DrawAdvancedConnectionSettings(manager, ref changed);
    }

    // ---------------------------------------------------------------------------------------
    // Hosting / joining
    // ---------------------------------------------------------------------------------------

    /// <summary>One clipboard string carrying both things a joiner needs.</summary>
    private static string BuildInvite(CollabManager manager)
    {
        var relay = string.IsNullOrEmpty(manager.RelayEndpoint) ? RelayAddressOrDefault() : manager.RelayEndpoint;
        return $"""
                TiXL collab invite
                Relay: {relay}
                Room: {manager.RoomCode}
                """;
    }

    /// <summary>The configured relay, or the default relay when the field is empty.</summary>
    private static string RelayAddressOrDefault()
    {
        var relay = UserSettings.Config.CollabRelayAddress;
        if (!string.IsNullOrWhiteSpace(relay))
            return relay.Trim();

        var host = string.IsNullOrEmpty(CollabProtocol.DefaultRelayHost) ? "127.0.0.1" : CollabProtocol.DefaultRelayHost;
        return $"{host}:{CollabProtocol.DefaultRelayPort}";
    }

    private static void ParseRelayAddress(string text, out string host, out int port)
    {
        host = string.IsNullOrWhiteSpace(text) ? CollabProtocol.DefaultRelayHost : text.Trim();
        port = CollabProtocol.DefaultRelayPort;
        var separator = host.LastIndexOf(':');
        if (separator > 0 && int.TryParse(host[(separator + 1)..], out var parsed))
        {
            port = parsed;
            host = host[..separator];
        }
    }

    private static void ParseJoinAddress(string text, int defaultPort, out string host, out int port)
    {
        host = text?.Trim() ?? "";
        port = defaultPort;
        var separator = host.LastIndexOf(':');
        if (separator > 0 && int.TryParse(host[(separator + 1)..], out var parsed))
        {
            port = parsed;
            host = host[..separator];
        }
    }

    private static void DrawDiscoveredSessions(CollabManager manager)
    {
        var config = UserSettings.Config;
        PanelLayout.Space(PanelLayout.SpaceM);
        FormInputs.DrawInputLabel("Sessions on LAN");

        if (!_discoveryScannerActive)
        {
            if (ImGui.Button("Scan"))
            {
                _discovered.Clear();
                _discoveryScanner.Start(config.CollabPort + LanDiscoveryResponder.DiscoveryPortOffset, null, config.NetworkInterface);
                _discoveryScannerActive = true;
                _discoveryScanUntil = DateTime.UtcNow + TimeSpan.FromSeconds(6);
            }
        }
        else
        {
            ImGui.TextDisabled("scanning...");
        }

        DrainDiscoveryResults();

        DrawDiscoveredSessionRows(manager);
    }

    private static bool _discoveryScannerActive;
    private static DateTime _discoveryScanUntil;
    private static readonly List<CollabManager.DiscoveredSession> _discovered = [];
    private static string _joinAddress = "";
    private static string _whisperReplyInput = "";
    private static string _replyToName = "";
    private static string _lobbyWhisperInput = "";

    private static void DrainDiscoveryResults()
    {
        while (_discoveryScanner.Results.TryDequeue(out var result))
        {
            var announcement = result.Announcement;
            // Read type-tolerantly: the announcement comes from an untrusted UDP sender, and a
            // malformed field must be a miss, not an exception out of the frame loop.
            CollabProtocol.TryGetInt(announcement, "port", out var port);
            CollabProtocol.TryGetBool(announcement, "pwd", out var passwordProtected);
            var session = new CollabManager.DiscoveredSession
                              {
                                  Name = CollabProtocol.GetString(announcement, "name", "TiXL session"),
                                  HostName = CollabProtocol.GetString(announcement, "host", ""),
                                  Address = result.Source.Address.ToString(),
                                  Port = port > 0 ? port : CollabProtocol.DefaultTcpPort,
                                  PasswordProtected = passwordProtected,
                              };

            if (_discovered.Any(d => d.Address == session.Address && d.Port == session.Port))
                continue;

            _discovered.Add(session);
        }

        if (_discoveryScannerActive && DateTime.UtcNow > _discoveryScanUntil)
        {
            _discoveryScanner.Stop();
            _discoveryScannerActive = false;
        }

        _discovered.RemoveAll(d => d.Seen < DateTime.UtcNow - TimeSpan.FromSeconds(15));
    }

    private static void DrawDiscoveredSessionRows(CollabManager manager)
    {
        if (_discovered.Count == 0)
            return;

        FormInputs.AddVerticalSpace(5);
        foreach (var session in _discovered)
        {
            FormInputs.SetCursorToParameterEdit();
            ImGui.Bullet();
            ImGui.TextUnformatted($"{session.Name}  ({session.Address}:{session.Port})");
            if (session.PasswordProtected)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("password");
            }

            ImGui.SameLine();
            ImGui.PushID(session.Address + session.Port);
            if (ImGui.Button("Join"))
            {
                ShowControlMode.Activate(ShowControlMode.Collab);
                manager.JoinDirect(session.Address, session.Port, UserSettings.Config.CollabPassword);
            }

            ImGui.PopID();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Active session
    // ---------------------------------------------------------------------------------------

    private static void DrawActiveSession(CollabManager manager)
    {
        FormInputs.DrawInputLabel("Status");
        CustomComponents.StylizedText(manager.StatusText, Fonts.FontNormal, UiColors.StatusActivated);

        if (!string.IsNullOrEmpty(manager.RoomCode))
        {
            FormInputs.DrawInputLabel("Session ID");
            ImGui.TextUnformatted(manager.RoomCode);
            CustomComponents.TooltipForLastItem("Share this ID with the people you want to collaborate with.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Copy ID"))
                ImGui.SetClipboardText(manager.RoomCode);
            CustomComponents.TooltipForLastItem("Copies the session ID.");

            ImGui.SameLine();
            if (ImGui.SmallButton("Copy Invite"))
                ImGui.SetClipboardText(BuildInvite(manager));
            CustomComponents.TooltipForLastItem("Copies the relay address and room code as a single invite. The other person can use \"Paste Invite and Join\" under Join.");
        }

        // The listing is editable while the session runs, like a party-matching entry: title,
        // purpose and visibility go to the relay with the room key, without restarting the session.
        if (manager.State == CollabManager.SessionState.Hosting && manager.RelayEndpoint.Length > 0)
        {
            var config = UserSettings.Config;
            FormInputs.AddVerticalSpace();
            FormInputs.AddSectionSubHeader("Session browser entry");
            FormInputs.AddVerticalSpace(4);
            FormInputs.AddCheckBox("Listed in session browser", ref config.CollabListSession,
                                   """
                                   On: the session shows up in other people's session browser with the
                                   title and purpose below. Off: only people with the session ID can
                                   find it. Applies right away; the session keeps running either way.
                                   """,
                                   false);
            FormInputs.AddStringInput("Title", ref config.CollabSessionTitle,
                                      $"{manager.LocalIdentity.DisplayName}'s session",
                                      null, "What the session browser shows.");
            DrawPurposeInput(ref config.CollabSessionPurpose);
            if (ImGui.SmallButton("Update Entry"))
            {
                manager.UpdateSessionListing(config.CollabSessionTitle, config.CollabSessionPurpose,
                                             ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "",
                                             config.CollabListSession);
            }

            CustomComponents.TooltipForLastItem("Sends the title, purpose and visibility above to the relay so other people's session browsers see them.");
        }

        FormInputs.AddVerticalSpace();
        FormInputs.AddSectionSubHeader("Participants");
        FormInputs.AddVerticalSpace(4);

        DrawCursorColor(manager);

        var roster = manager.Roster;
        if (roster.Count == 0)
        {
            ImGui.TextDisabled("Waiting for participants...");
        }
        else
        {
            foreach (var (peerId, name) in roster)
            {
                var presence = manager.GetPresence(peerId);
                var color = presence?.AccentColor ?? UiColors.Gray.Rgba;
                var activity = presence == null
                                   ? ""
                                   : presence.CompositionId == Guid.Empty
                                       ? ""
                                       : presence.IsHoveringCanvas ? "editing" : "in session";

                ImGui.PushStyleColor(ImGuiCol.Text, CollabColors.ToUint(color));
                ImGui.Bullet();
                ImGui.TextUnformatted(string.IsNullOrEmpty(name) ? "unnamed participant" : name);
                ImGui.PopStyleColor();

                var info = manager.GetParticipantInfo(peerId);
                ImGui.SameLine();
                ImGui.TextDisabled(CollabUserIdentity.FormatLang(info.Lang));
                if (!info.Verified)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("unverified");
                    CustomComponents.TooltipForLastItem("This name is what the participant typed in. Names confirmed by a Discord login through the relay do not carry this mark.");
                }

                if (peerId == 0)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("(host)");
                }

                if (!string.IsNullOrEmpty(activity))
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled(activity);
                }

                // Round trip of our own heartbeats; relay hops included, so it reads on the internet
                // path too. -1 until the first echo returns.
                var rtt = manager.GetPeerRttMs(peerId);
                if (rtt >= 0)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"{rtt} ms");
                }

                if (manager.State == CollabManager.SessionState.Hosting && peerId != 0)
                {
                    ImGui.SameLine();
                    ImGui.PushID("kick" + peerId);
                    if (ImGui.SmallButton("Remove"))
                        _pendingKickPeerId = peerId;

                    ImGui.PopID();
                    CustomComponents.TooltipForLastItem("Removes this participant from the session. They can join again unless the session has a password.");

                    ImGui.SameLine();
                    ImGui.PushID("makehost" + peerId);
                    if (ImGui.SmallButton("Make Host"))
                        _pendingHostTransferPeerId = peerId;

                    ImGui.PopID();
                    CustomComponents.TooltipForLastItem("Hands the session over to this participant. It keeps running on their machine; you stay in it as a participant.");
                }
            }

            DrawKickConfirmation(manager);
            DrawHostTransferConfirmation(manager);
        }

        // Lobby whispers: visitors asking about the session before joining, with the reply box.
        // Replies go to whoever whispered last; a second host input is not worth the clutter.
        if (manager.State == CollabManager.SessionState.Hosting && manager.RelayEndpoint.Length > 0)
        {
            FormInputs.AddVerticalSpace();
            FormInputs.AddSectionSubHeader("Whispers");
            FormInputs.AddVerticalSpace(4);

            var whispers = manager.LobbyWhispers;
            if (whispers.Count == 0)
            {
                ImGui.TextDisabled("People can whisper you from the session browser before they join.");
            }
            else
            {
                foreach (var whisper in whispers)
                {
                    if (whisper.FromHost)
                    {
                        ImGui.TextColored(UiColors.WhisperOwn.Rgba, "You");
                    }
                    else
                    {
                        ImGui.TextUnformatted(whisper.From);
                    }

                    ImGui.SameLine();
                    ImGui.TextWrapped(whisper.Text);
                }

                // Reply to the most recent whisper that is not ours; when every entry is ours,
                // keep the previous name.
                for (var i = whispers.Count - 1; i >= 0; i--)
                {
                    if (!whispers[i].FromHost)
                    {
                        _replyToName = whispers[i].From;
                        break;
                    }
                }

                FormInputs.AddVerticalSpace(4);
                ImGui.SetNextItemWidth(-72 * T3Ui.UiScaleFactor);
                ImGui.InputTextWithHint("##whisperReply", $"Reply to {_replyToName}...", ref _whisperReplyInput, 256);
                ImGui.SameLine();
                ImGui.BeginDisabled(_whisperReplyInput.Length == 0);
                if (ImGui.Button("Send##whisper"))
                {
                    manager.ReplyToLobbyWhisper(_replyToName, _whisperReplyInput);
                    _whisperReplyInput = "";
                }

                ImGui.EndDisabled();
            }
        }

        FormInputs.AddVerticalSpace();
        FormInputs.SetIndentToLeft();
        if (ImGui.Button("Leave Session"))
            manager.Leave(manual: true);

        FormInputs.SetIndentToParameters();

        FormInputs.AddVerticalSpace();
        CustomComponents.HelpText("""
                                  How it works:
                                  - Late joiners receive full snapshots of the compositions edited in the session - their local changes to those compositions are replaced by the host's state.
                                  - Graph edits are synchronized: adding and deleting operators, connections, parameter changes, renames and moving nodes.
                                  - Undo/Redo work per person while connected: Ctrl+Z reverts YOUR last edit. An edit someone else has changed since is skipped.
                                  - If the connection drops, TiXL reconnects by itself and sends your unconfirmed edits again.
                                  - If the host disappears, the participant who joined first takes over after about 20 seconds; the host can also hand over with "Make Host".
                                  - Copies that drift apart are noticed within seconds and repaired from the host's state.
                                  - Parameters someone is editing are soft-locked: they appear disabled with a "(locked)" marker until released.
                                  - For sessions over the internet, the relay session ID works without port forwarding; a LAN host needs an open/forwarded port or a VPN like Tailscale.
                                  """);

        if (!string.IsNullOrEmpty(manager.UndoNotice))
        {
            FormInputs.AddVerticalSpace();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
            ImGui.TextUnformatted(manager.UndoNotice);
            ImGui.PopStyleColor();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Advanced
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Everything the session ID flow does not need: a custom relay, and direct or LAN connections
    /// for setups without a relay.
    /// </summary>
    private static void DrawAdvancedConnectionSettings(CollabManager manager, ref bool changed)
    {
        if (!ImGui.CollapsingHeader("Advanced"))
            return;

        var config = UserSettings.Config;

        PanelLayout.Space(PanelLayout.SpaceS);
        PanelLayout.BeginSection("Relay");
        changed |= FormInputs.AddStringInput("Relay server",
                                             ref config.CollabRelayAddress,
                                             $"{CollabProtocol.DefaultRelayHost}:{CollabProtocol.DefaultRelayPort}",
                                             null,
                                             """
                                             Address of a TiXLCollabRelay server, e.g. relay.example.com:44710.
                                             Hosts and joiners must use the same relay. Empty uses the default relay.
                                             """);

        PanelLayout.EndSection();

        PanelLayout.BeginSection("Peer-to-Peer");
        changed |= FormInputs.AddCheckBox("Use peer-to-peer connections",
                                          ref config.CollabWebRtcEnabled,
                                          """
                                          Relay sessions first authenticate over the relay, then try a direct WebRTC path
                                          between the machines. The session falls back to the relay when a direct path is
                                          not possible, so this can stay on everywhere.
                                          """,
                                          true);

        changed |= FormInputs.AddCheckBox("Share preview with participants",
                                          ref config.CollabSharePreview,
                                          """
                                          Streams the host's rendered output to the participants over the
                                          peer-to-peer video track. Applies to peers that join afterwards.
                                          """,
                                          true);

        changed |= FormInputs.AddStringInput("STUN/TURN servers",
                                             ref config.CollabWebRtcIceServers,
                                             "stun:stun.l.google.com:19302",
                                             null,
                                             """
                                             Comma-separated STUN/TURN URLs used to establish the direct path.
                                             Empty uses a public STUN server. A TURN server is only needed
                                             between peers that cannot reach each other directly.
                                             """);

        PanelLayout.EndSection();

        PanelLayout.BeginSection("Direct and LAN");
        changed |= FormInputs.AddInt("Port",
                                     ref config.CollabPort,
                                     1024, 65535, 1,
                                     "TCP port used by direct and LAN sessions. Pick something above 1024 that is not used by other software.",
                                     CollabProtocol.DefaultTcpPort);

        changed |= FormInputs.AddCheckBox("Announce session on LAN",
                                          ref config.CollabLanDiscovery,
                                          """
                                          Lets other TiXL instances on the local network discover a LAN session
                                          automatically. Turn off when hosting on a shared network.
                                          """,
                                          true);

        PanelLayout.EndSection();

        DrawNetworkAdapterHint("Collaboration", ref UserSettings.Config.CollabInterface, ref changed);

        if (manager.IsSessionActive)
            return;

        PanelLayout.BeginSection("Join by address");
        _joinAddress ??= "";
        FormInputs.DrawInputLabel("Address");
        ImGui.PushID("##collabjoin");
        var connectWidth = ImGui.CalcTextSize("Connect").X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetNextItemWidth(FormInputs.GetAvailableInputSize(null, false, true).X - connectWidth - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.InputText("##collabJoinAddress", ref _joinAddress, 256, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ShowControlMode.Activate(ShowControlMode.Collab);
            ParseJoinAddress(_joinAddress, config.CollabPort, out var host, out var port);
            manager.JoinDirect(host, port, config.CollabPassword);
        }

        ImGui.PopID();
        ImGui.SameLine();
        if (ImGui.Button("Connect"))
        {
            ParseJoinAddress(_joinAddress, config.CollabPort, out var host, out var port);
            manager.JoinDirect(host, port, config.CollabPassword);
        }

        CustomComponents.TooltipForLastItem("Direct address of the host, e.g. 192.168.1.42 for LAN sessions or a public address / VPN IP for online sessions.");

        PanelLayout.Space(PanelLayout.SpaceM);
        FormInputs.SetCursorToParameterEdit();
        if (CustomComponents.DrawCtaButton("Host on LAN", Icon.None, UiColors.ForegroundFull, UiColors.StatusActivated, Color.Transparent))
        {
            ShowControlMode.Activate(ShowControlMode.Collab);
            manager.StartHost(config.CollabPort, config.CollabPassword, config.CollabLanDiscovery);
        }

        CustomComponents.TooltipForLastItem("Start a session on this machine without a relay. Others join by LAN discovery or by your address.");

        DrawDiscoveredSessions(manager);
        PanelLayout.EndSection();
    }

    private static int _pendingKickPeerId = -1;

    /// <summary>Two-step removal: the button asks, this confirms.</summary>
    private static void DrawKickConfirmation(CollabManager manager)
    {
        if (_pendingKickPeerId < 0)
            return;

        var name = manager.Roster.TryGetValue(_pendingKickPeerId, out var found) ? found : $"#{_pendingKickPeerId}";
        FormInputs.AddVerticalSpace(4);
        ImGui.TextUnformatted($"Remove {name} from the session?");

        if (ImGui.Button("Remove"))
        {
            manager.KickParticipant(_pendingKickPeerId);
            _pendingKickPeerId = -1;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            _pendingKickPeerId = -1;
    }

    /// <summary>
    /// Everyone picks their own cursor color; other participants see it within a moment because it
    /// rides the presence packets. Reset returns to the color derived from the user id.
    /// </summary>
    private static void DrawCursorColor(CollabManager manager)
    {
        FormInputs.DrawInputLabel("Cursor color");

        var color = manager.LocalIdentity.AccentColor;
        var edited = ColorEditButton.Draw(ref color,
                                          new Vector2(ImGui.GetFrameHeight() * 1.6f, ImGui.GetFrameHeight()));
        if ((edited & InputEditStateFlags.Modified) != 0)
            UserSettings.Config.CollabCursorColor = CollabColors.ToPacked(color);

        // Saved when the edit ends, not on every frame of a drag.
        if ((edited & InputEditStateFlags.Finished) != 0)
            UserSettings.Save();

        CustomComponents.TooltipForLastItem("The color other participants see for your cursor, selections and edit badges.");

        if (UserSettings.Config.CollabCursorColor != 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Reset"))
            {
                UserSettings.Config.CollabCursorColor = 0;
                UserSettings.Save();
            }
        }

        FormInputs.AddVerticalSpace();
    }

    // ---------------------------------------------------------------------------------------
    // Secret inputs
    // ---------------------------------------------------------------------------------------

    private static bool DrawSecretInput(string label, ref string secret, string tooltip)
    {
        FormInputs.DrawInputLabel(label);

        ImGui.PushID(label);
        var input = secret ?? "";
        ImGui.SetNextItemWidth(FormInputs.GetAvailableInputSize(tooltip, false, true).X);
        var changed = ImGui.InputText("##secret", ref input, 256, ImGuiInputTextFlags.Password);
        ImGui.PopID();
        CustomComponents.TooltipForLastItem(tooltip);

        if (changed)
            secret = input;

        return changed;
    }

    // ---------------------------------------------------------------------------------------
    // Discord
    // ---------------------------------------------------------------------------------------

    private static void DrawDiscordLoginButton()
    {
        var loginState = CollabDiscordAuth.State;
        var loginBusy = loginState is CollabDiscordAuth.LoginState.WaitingForBrowser or CollabDiscordAuth.LoginState.ExchangingCode;

        FormInputs.SetCursorToParameterEdit();
        ImGui.BeginDisabled(loginBusy);
        if (ImGui.Button("Log In with Discord"))
            CollabDiscordAuth.StartLogin();

        ImGui.EndDisabled();
        CustomComponents.TooltipForLastItem("Opens the Discord login in your browser. The account must be a member of the TiXL Discord server.");

        if (loginBusy)
        {
            FormInputs.SetCursorToParameterEdit();
            ImGui.TextDisabled(CollabDiscordAuth.StatusText);
        }
        else if (loginState == CollabDiscordAuth.LoginState.Failed)
        {
            FormInputs.SetCursorToParameterEdit();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
            ImGui.TextWrapped(CollabDiscordAuth.StatusText);
            ImGui.PopStyleColor();

            // Some desktops (notably Linux ones) swallow the browser-open request; always leave
            // a way in by hand. Clicking copies the link to the clipboard.
            var authorizeUrl = CollabDiscordAuth.LastAuthorizeUrl;
            if (!string.IsNullOrEmpty(authorizeUrl))
            {
                FormInputs.SetCursorToParameterEdit();
                ImGui.TextDisabled("If no browser opened, open this link yourself (click to copy):");
                ImGui.PushFont(Fonts.FontSmall);
                if (ImGui.Selectable(authorizeUrl))
                    ImGui.SetClipboardText(authorizeUrl);

                ImGui.PopFont();
                CustomComponents.TooltipForLastItem("Copies the Discord login link to the clipboard");
            }
        }
        else if (!string.IsNullOrEmpty(UserSettings.Config.CollabDiscordUserId))
        {
            FormInputs.SetCursorToParameterEdit();
            CustomComponents.StylizedText(CollabManager.Instance.LocalIdentity.DisplayName, Fonts.FontNormal, UiColors.StatusActivated);
            ImGui.SameLine();
            ImGui.TextDisabled(CollabUserIdentity.FormatLang(UserSettings.Config.CollabDiscordLocale));
            CustomComponents.TooltipForLastItem("Language of your Discord profile. Shown next to your name in sessions and in the session browser.");
            ImGui.SameLine();
            if (PanelLayout.DestructiveButton("Log Out"))
                CollabDiscordAuth.LogOut();

            FormInputs.SetCursorToParameterEdit();
            if (CollabDiscordAuth.HasValidToken)
            {
                ImGui.TextDisabled("Relay sessions show this account as verified.");
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
                ImGui.TextWrapped("Log in again to appear as verified - the login of an earlier run was not kept.");
                ImGui.PopStyleColor();
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Session browser
    // ---------------------------------------------------------------------------------------

    private static readonly string[] _sessionPurposes = ["Jam", "Show", "Teaching", "Help", "Other"];
    private static string _lobbyFilter = "";
    private static string _lobbyPurposeFilter = "All";
    private static string _lobbySelectedCode = "";
    private static bool _lobbyLoadedOnce;

    private static bool DrawPurposeInput(ref string purpose)
    {
        var changed = false;
        FormInputs.DrawInputLabel("Purpose");
        ImGui.SetNextItemWidth(FormInputs.GetAvailableInputSize(null, false, true).X);
        if (ImGui.BeginCombo("##collabPurpose", string.IsNullOrEmpty(purpose) ? "Jam" : purpose))
        {
            foreach (var option in _sessionPurposes)
            {
                if (!ImGui.Selectable(option, option == purpose))
                    continue;

                purpose = option;
                changed = true;
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    /// <summary>
    /// The sessions the relay lists: who hosts, what it is about, how full it is. Pick a row and
    /// join. Only sessions whose host turned on "List in session browser" show up here.
    /// </summary>
    private static void DrawSessionBrowser(CollabManager manager)
    {
        FormInputs.AddVerticalSpace();
        FormInputs.AddSectionSubHeader("Open sessions");
        FormInputs.AddVerticalSpace(4);

        ParseRelayAddress(UserSettings.Config.CollabRelayAddress, out var relayHost, out var relayPort);
        var useBackend = CollabWebLobby.IsConfigured;
        if (useBackend)
        {
            if (!_lobbyLoadedOnce)
            {
                _lobbyLoadedOnce = true;
                CollabWebLobby.Refresh();
            }
            else if (!CollabWebLobby.IsRefreshing && DateTime.UtcNow - CollabWebLobby.LastRefreshUtc > TimeSpan.FromSeconds(15))
            {
                CollabWebLobby.Refresh();
            }
        }
        else if (!_lobbyLoadedOnce)
        {
            _lobbyLoadedOnce = true;
            CollabLobby.Refresh(relayHost, relayPort);
        }
        else if (!CollabLobby.IsRefreshing && DateTime.UtcNow - CollabLobby.LastRefreshUtc > TimeSpan.FromSeconds(15))
        {
            // Live entries: a host's Change entry appears here within seconds, not on manual refresh.
            CollabLobby.Refresh(relayHost, relayPort);
        }

        var rooms = useBackend ? CollabWebLobby.Rooms : CollabLobby.Rooms;

        var fontSize = ImGui.GetFontSize();
        FormInputs.SetIndentToLeft();
        ImGui.SetNextItemWidth(fontSize * 11);
        ImGui.InputTextWithHint("##lobbyFilter", "Name or title", ref _lobbyFilter, 64);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(fontSize * 7);
        if (ImGui.BeginCombo("##lobbyPurpose", _lobbyPurposeFilter))
        {
            if (ImGui.Selectable("All", _lobbyPurposeFilter == "All"))
                _lobbyPurposeFilter = "All";

            foreach (var option in _sessionPurposes)
            {
                if (ImGui.Selectable(option, option == _lobbyPurposeFilter))
                    _lobbyPurposeFilter = option;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(useBackend ? CollabWebLobby.IsRefreshing : CollabLobby.IsRefreshing);
        if (ImGui.Button("Refresh"))
        {
            if (useBackend)
                CollabWebLobby.Refresh();
            else
                CollabLobby.Refresh(relayHost, relayPort);
        }

        ImGui.EndDisabled();

        // Auto match: the Silkroad button. Joins the first listed session that passes the filters.
        var autoMatch = FindAutoMatch(rooms);
        ImGui.SameLine();
        ImGui.BeginDisabled(autoMatch == null);
        if (ImGui.Button("Auto Match") && autoMatch != null)
        {
            ShowControlMode.Activate(ShowControlMode.Collab);
            CollabLobbyWhisper.Reset();
            JoinListedRoom(manager, autoMatch);
        }

        ImGui.EndDisabled();
        if (autoMatch != null)
            CustomComponents.TooltipForLastItem($"Joins \"{autoMatch.Title}\" by {autoMatch.HostName} right away.");
        else
            CustomComponents.TooltipForLastItem("No listed session passes the current filters.");

        if (CollabLobby.IsRefreshing)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("loading...");
        }

        if (!string.IsNullOrEmpty(CollabLobby.ErrorText))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
            ImGui.TextWrapped(CollabLobby.ErrorText);
            ImGui.PopStyleColor();
        }

        LobbyRoom? selected = null;
        var joinNow = false;
        const ImGuiTableFlags tableFlags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        var tableHeight = ImGui.GetFrameHeightWithSpacing() * 8;
        if (ImGui.BeginTable("##collabLobby", 7, tableFlags, new Vector2(0, tableHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, fontSize * 2f);
            ImGui.TableSetupColumn("Lang", ImGuiTableColumnFlags.WidthFixed, fontSize * 3.5f);
            ImGui.TableSetupColumn("Host", ImGuiTableColumnFlags.WidthStretch, 0.24f);
            ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch, 0.42f);
            ImGui.TableSetupColumn("Purpose", ImGuiTableColumnFlags.WidthStretch, 0.16f);
            ImGui.TableSetupColumn("Members", ImGuiTableColumnFlags.WidthFixed, fontSize * 4.5f);
            ImGui.TableSetupColumn("Access", ImGuiTableColumnFlags.WidthFixed, fontSize * 5f);
            ImGui.TableHeadersRow();

            var rowNumber = 0;
            foreach (var room in rooms)
            {
                if (_lobbyPurposeFilter != "All" && !string.Equals(room.Purpose, _lobbyPurposeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_lobbyFilter.Length > 0
                    && !room.HostName.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase)
                    && !room.Title.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase)
                    && !room.Project.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                rowNumber++;
                var isSelected = room.Code == _lobbySelectedCode;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(rowNumber.ToString());
                ImGui.TableNextColumn();
                ImGui.PushID(room.Code);
                if (ImGui.Selectable(CollabUserIdentity.FormatLang(room.Lang), isSelected,
                                     ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
                {
                    _lobbySelectedCode = room.Code;
                    isSelected = true;
                    joinNow = ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
                }

                ImGui.PopID();
                if (isSelected)
                    selected = room;

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.IsNullOrEmpty(room.HostName) ? "unnamed" : room.HostName);
                if (!room.HostVerified)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    CustomComponents.TooltipForLastItem("This name is not confirmed by a Discord login.");
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(room.Title);
                if (!string.IsNullOrEmpty(room.Project))
                    CustomComponents.TooltipForLastItem($"Project: {room.Project}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(room.Purpose);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{room.Members}/{room.MaxMembers}");

                ImGui.TableNextColumn();
                ImGui.TextDisabled(room.PasswordProtected ? "password" : "open");
            }

            ImGui.EndTable();
        }

        if (rooms.Count == 0 && !(useBackend ? CollabWebLobby.IsRefreshing : CollabLobby.IsRefreshing)
            && string.IsNullOrEmpty(useBackend ? CollabWebLobby.ErrorText : CollabLobby.ErrorText))
            ImGui.TextDisabled(useBackend
                                   ? "No listed sessions on the backend right now."
                                   : "No listed sessions on this relay right now.");

        ImGui.BeginDisabled(selected == null);
        if (ImGui.Button("Join Session") || (joinNow && selected != null))
        {
            if (selected != null)
            {
                ShowControlMode.Activate(ShowControlMode.Collab);
                CollabLobbyWhisper.Reset();
                JoinListedRoom(manager, selected);
            }
        }

        ImGui.EndDisabled();
        if (selected is { PasswordProtected: true })
        {
            ImGui.SameLine();
            ImGui.TextDisabled("uses the session password above, or asks for it");
        }

        // Whisper the host of the picked session before joining, like a party-matching Whisper.
        if (selected != null)
        {
            FormInputs.AddVerticalSpace(4);
            if (useBackend)
                CollabLobbyWhisper.PollBackend(selected.Code, manager.LocalIdentity.DisplayName);

            var whispers = CollabLobbyWhisper.Messages;
            if (whispers.Count > 0)
            {
                foreach (var whisper in whispers)
                {
                    if (whisper.Mine)
                        ImGui.TextColored(UiColors.WhisperOwn.Rgba, "You");
                    else if (whisper.From.Length > 0)
                        ImGui.TextUnformatted(selected.HostName);
                    else
                        ImGui.TextColored(UiColors.StatusWarning.Rgba, "!");

                    ImGui.SameLine();
                    ImGui.TextWrapped(whisper.Text);
                }
            }

            ImGui.SetNextItemWidth(-150 * T3Ui.UiScaleFactor);
            ImGui.InputTextWithHint("##lobbyWhisper", $"Whisper {selected.HostName}...", ref _lobbyWhisperInput, 256);
            ImGui.SameLine();
            ImGui.BeginDisabled(_lobbyWhisperInput.Length == 0);
            if (ImGui.Button("Whisper") && selected != null)
            {
                if (useBackend)
                {
                    CollabLobbyWhisper.SendViaBackend(selected.Code, manager.LocalIdentity.DisplayName, _lobbyWhisperInput);
                }
                else
                {
                    CollabLobbyWhisper.Send(CollabLobby.RelayEndpoint, selected.Code,
                                            manager.LocalIdentity.DisplayName, manager.LocalIdentity.Lang,
                                            _lobbyWhisperInput);
                }

                _lobbyWhisperInput = "";
            }

            ImGui.EndDisabled();
            ImGui.TextDisabled("Asks the host something before you join. They can answer here.");
        }

        FormInputs.SetIndentToParameters();
    }

    /// <summary>
    /// Joins a room from either listing source: a backend row with a relay address joins on that
    /// relay; a row without one is a relay-free backend session and joins over WebRTC; the relay
    /// list joins on the relay the browser read it from.
    /// </summary>
    private static void JoinListedRoom(CollabManager manager, LobbyRoom room)
    {
        if (room.Relay.Length > 0)
        {
            ParseJoinAddress(room.Relay, CollabProtocol.DefaultRelayPort, out var joinHost, out var joinPort);
            manager.JoinViaRelay(joinHost, joinPort, room.Code, UserSettings.Config.CollabPassword ?? "");
            return;
        }

        if (room.Relay.Length == 0 && CollabWebLobby.IsConfigured)
        {
            manager.JoinViaBackend(room.Code);
            return;
        }

        ParseRelayAddress(CollabLobby.RelayEndpoint, out var relayHost, out var relayPort);
        manager.JoinViaRelay(relayHost, relayPort, room.Code, UserSettings.Config.CollabPassword ?? "");
    }

    /// <summary>The first listed session that passes the current filters; null when none does.</summary>
    private static LobbyRoom? FindAutoMatch(IReadOnlyList<LobbyRoom> rooms)
    {
        foreach (var room in rooms)
        {
            if (_lobbyPurposeFilter != "All" && !string.Equals(room.Purpose, _lobbyPurposeFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_lobbyFilter.Length > 0
                && !room.HostName.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase)
                && !room.Title.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase)
                && !room.Project.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            return room;
        }

        return null;
    }

    private static int _pendingHostTransferPeerId = -1;

    /// <summary>Two-step handover: the button asks, this confirms.</summary>
    private static void DrawHostTransferConfirmation(CollabManager manager)
    {
        if (_pendingHostTransferPeerId < 0)
            return;

        var name = manager.Roster.TryGetValue(_pendingHostTransferPeerId, out var found) ? found : $"#{_pendingHostTransferPeerId}";
        FormInputs.AddVerticalSpace(4);
        ImGui.TextWrapped($"Make {name} the host? The session continues on their machine and you stay in it as a participant.");

        if (ImGui.Button("Make Host"))
        {
            manager.TransferHostTo(_pendingHostTransferPeerId);
            _pendingHostTransferPeerId = -1;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##hostTransfer"))
            _pendingHostTransferPeerId = -1;
    }
}
