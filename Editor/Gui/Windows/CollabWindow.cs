#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Collab;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Show;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// The collab home, in three tabs like a party-matching screen: <b>Party</b> (the session
/// browser and hosting controls), <b>Session</b> (participants, listing, whispers, leave) and
/// <b>Chat</b>. Configuration (identity, password, backend) stays in the Settings window.
/// </summary>
[HelpUiID("CollabWindow")]
internal sealed class CollabWindow : Window
{
    internal CollabWindow()
    {
        Config.Title = "Collab";
        Config.Visible = false;
        WindowSizeOverride = new Vector2(640, 660);
    }

    internal override IReadOnlyList<Window> GetInstances() => [this];

    /// <summary>The window's areas: chat, party matching, party management, remote and boygroup.</summary>
    private enum Tab
    {
        Chat,
        PartyMatching,
        PartyManagement,
        Remote,
        Boygroup,
    }

    private static Tab _selectedTab = Tab.PartyMatching;

    /// <summary>Selects a tab by name (Chat, PartyMatching, PartyManagement, Remote, Boygroup, or
    /// the aliases Session/Search); used by the debug bridge.</summary>
    internal bool TrySelectTab(string name)
    {
        if (string.Equals(name, "Chat", StringComparison.OrdinalIgnoreCase))
        {
            _selectedTab = Tab.Chat;
            return true;
        }

        if (string.Equals(name, "Session", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Management", StringComparison.OrdinalIgnoreCase))
        {
            _selectedTab = Tab.PartyManagement;
            return true;
        }

        if (string.Equals(name, "Search", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Matching", StringComparison.OrdinalIgnoreCase))
        {
            _selectedTab = Tab.PartyMatching;
            return true;
        }

        if (!Enum.TryParse<Tab>(name, true, out var tab))
            return false;

        _selectedTab = tab;
        return true;
    }

    protected override void DrawContent()
    {
        var collab = CollabManager.Instance;
        UpdateRemoteJoin();
        DrainRemoteUiActions();
        if (!collab.IsEnabled)
        {
            PanelLayout.BeginSection("Collaboration is off",
                                     "This window hosts parties, lists open sessions and carries the session chat. Turn collaboration on to use it.");
            if (ImGui.Button("Open Collaboration Settings"))
            {
                var settings = WindowManager.SettingsWindow;
                settings.Config.Visible = true;
                settings.TrySelectCategory("Collaboration");
            }

            PanelLayout.EndSection();
            return;
        }

        DrawTabLine();
        PanelLayout.Space(PanelLayout.SpaceS);

        switch (_selectedTab)
        {
            case Tab.Chat:
                DrawChatTab(collab);
                break;

            case Tab.PartyMatching:
                DrawPartyTab(collab);
                break;

            case Tab.PartyManagement:
                DrawManagementTab(collab);
                break;

            case Tab.Remote:
                DrawRemoteTab(collab);
                break;

            case Tab.Boygroup:
                DrawBoygroupTab(collab);
                break;
        }
    }

    /** The two host-form choices drawn as segmented pills instead of radio rows. */
    private enum PartyType { Controllable, Spectating }

    private enum PartyReach { Local, Online }

    /// <summary>The tabs as one segmented control; the active option sits in its pill.</summary>
    private static void DrawTabLine()
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + PanelLayout.SpaceS * T3Ui.UiScaleFactor);
        CustomComponents.SegmentedButton(ref _selectedTab);
    }

    // ---------------------------------------------------------------------------------------
    // Party matching tab: the session browser plus the hosting controls
    // ---------------------------------------------------------------------------------------

    private static void DrawPartyTab(CollabManager collab)
    {
        if (collab.State != CollabManager.SessionState.Off)
        {
            PanelLayout.BeginSection("In a session");
            PanelLayout.StatusPill(collab.State == CollabManager.SessionState.Hosting ? "hosting" : "joined", UiColors.StatusActivated);
            ImGui.TextWrapped(collab.StatusText);
            PanelLayout.Space(PanelLayout.SpaceXS);
            CustomComponents.HelpText("Leave the session on the Session tab to browse and join another party.");
            PanelLayout.Space(PanelLayout.SpaceS);
            if (ImGui.Button("Go to Session"))
                _selectedTab = Tab.PartyManagement;

            PanelLayout.EndSection();
            return;
        }

        DrawHostControls(collab);
        DrawSessionBrowser(collab);
    }

    /// <summary>The connected session's management view: status, the party roster with every
    /// member, their ping and the per-row actions, join requests, whispers, leave.</summary>
    private static void DrawManagementTab(CollabManager collab)
    {
        if (collab.State == CollabManager.SessionState.Off)
        {
            CustomComponents.HelpText("No session is running. Host one on the Party matching tab or join a listed session there.");
            FormInputs.ApplyIndent();
            if (ImGui.Button("Open Party matching"))
                _selectedTab = Tab.PartyMatching;

            return;
        }

        DrawSessionTab(collab);
    }

    private static void DrawHostControls(CollabManager collab)
    {
        var uiScale = T3Ui.UiScaleFactor;
        PanelLayout.BeginSection("Host a party", "The party stays alive while you browse other projects, until you close the party, unload the project or exit TiXL.");
        var password = UserSettings.Config.CollabPassword ?? "";
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##collab-pwd", "Session password (optional)", ref password, 64);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            UserSettings.Config.CollabPassword = password;
            UserSettings.Save();
        }

        // -- Type: Controllable (everyone edits) or Spectating (the host drops their changes).
        PanelLayout.Space(PanelLayout.SpaceM);
        CustomComponents.SmallGroupHeader("Type");
        var isHosting = collab.State == CollabManager.SessionState.Hosting;
        var controllable = isHosting ? collab.ParticipantsCanEdit : UserSettings.Config.CollabPartyControllable;
        var partyType = controllable ? PartyType.Controllable : PartyType.Spectating;
        if (CustomComponents.SegmentedButton(ref partyType))
        {
            UserSettings.Config.CollabPartyControllable = partyType == PartyType.Controllable;
            UserSettings.Save();
            if (isHosting)
                collab.SetParticipantsCanEdit(partyType == PartyType.Controllable);
        }

        CustomComponents.TooltipForLastItem("""
                                            Controllable: everyone can change the shared project.
                                            Spectating: participants see your cursors, presence and chat, but
                                            the host drops their graph changes.
                                            """);

        // -- Reach: Local (LAN) or Online (through the backend party server).
        PanelLayout.Space(PanelLayout.SpaceM);
        CustomComponents.SmallGroupHeader("Reach");
        var online = UserSettings.Config.CollabPartyOnline;
        var reach = online ? PartyReach.Online : PartyReach.Local;
        if (CustomComponents.SegmentedButton(ref reach))
        {
            UserSettings.Config.CollabPartyOnline = reach == PartyReach.Online;
            UserSettings.Save();
        }

        CustomComponents.TooltipForLastItem("""
                                            Local: people on the same network join directly (no internet).
                                            Online: the party is listed on the backend server and joiners
                                            connect over WebRTC from anywhere.
                                            """);

        // -- Form the party.
        PanelLayout.Space(PanelLayout.SpaceL);
        var wantOnline = UserSettings.Config.CollabPartyOnline;
        var canForm = !wantOnline || CollabWebLobby.IsConfigured;
        var projectName = ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "";

        ImGui.BeginDisabled(!canForm);
        if (CustomComponents.DrawCtaButton("Form Party", Icon.None, UiColors.ForegroundFull, UiColors.StatusActivated, Color.Transparent))
        {
            ShowControlMode.Activate(ShowControlMode.Collab);
            if (wantOnline)
            {
                collab.StartHostViaBackend();
            }
            else
            {
                collab.StartHost(UserSettings.Config.CollabPort,
                                 UserSettings.Config.CollabPassword ?? "",
                                 UserSettings.Config.CollabLanDiscovery,
                                 projectName.Length > 0 ? projectName : null);
            }

            _selectedTab = Tab.PartyManagement;
        }

        ImGui.EndDisabled();
        if (!canForm)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Set the Backend URL in Settings first");
        }

        PanelLayout.EndSection();
    }

    // ---------------------------------------------------------------------------------------
    // Remote tab: pair this computer, and manage the owner's other installations
    // ---------------------------------------------------------------------------------------

    private static string _pairCodeInput = "";
    private static string _pairError = "";
    private static string _pairingCode = "";
    private static DateTime _pairingExpires = DateTime.MinValue;
    private static string _pairingStatus = "";
    private static long _nextPairPollTicks;
    private static long _nextDeviceRefreshTicks;
    private static string _remoteClaim = "";
    private static long _nextConnectPollTicks;

    private static void DrawRemoteTab(CollabManager collab)
    {
        DrainRemoteUiActions();
        UpdateDeviceList();
        PanelLayout.BeginSection("This computer");
        DrawThisComputer();
        PanelLayout.EndSection();

        PanelLayout.BeginSection("My computers");
        DrawMyComputers(collab);
        PanelLayout.EndSection();
    }

    /// <summary>Installation side: pairing, consent and the name the owner sees.</summary>
    private static void DrawThisComputer()
    {
        var uiScale = T3Ui.UiScaleFactor;

        if (CollabRemote.IsPaired)
        {
            ImGui.TextDisabled("Paired with");
            ImGui.SameLine();
            ImGui.TextColored(UiColors.StatusActivated, CollabRemote.PairedOwner);

            var allow = UserSettings.Config.CollabRemoteAllow;
            if (ImGui.Checkbox("Allow remote sessions on this computer", ref allow))
            {
                UserSettings.Config.CollabRemoteAllow = allow;
                UserSettings.Save();
            }

            CustomComponents.TooltipForLastItem("""
                                                While this is on, the owner can connect to this
                                                computer and work on the running project. Turning it
                                                off makes the computer look offline to them.
                                                """);

            var name = UserSettings.Config.CollabRemoteName;
            ImGui.SetNextItemWidth(-110 * uiScale);
            if (ImGui.InputTextWithHint("##remoteName", CollabRemote.DeviceName, ref name, 48))
            {
                UserSettings.Config.CollabRemoteName = name;
                UserSettings.Save();
            }

            ImGui.SameLine();
            if (PanelLayout.DestructiveButton("Forget Pairing"))
                CollabRemote.ForgetInstallation();

            if (CollabRemote.InstallationStatus.Length > 0)
                ImGui.TextDisabled(CollabRemote.InstallationStatus);

            return;
        }

        CustomComponents.HelpText("""
                                  To let yourself (or a colleague) reach this computer, pair it once:
                                  on the controlling machine open this tab, log in with Discord and press
                                  "Add remote TiXL". Then type the 6-digit code it shows here.
                                  """);

        ImGui.SetNextItemWidth(96 * uiScale);
        ImGui.InputTextWithHint("##pairCode", "6-digit code", ref _pairCodeInput, 6);
        ImGui.SameLine();
        ImGui.BeginDisabled(_pairCodeInput.Trim().Length < 6);
        if (ImGui.Button("Pair This Computer"))
        {
            var code = _pairCodeInput.Trim();
            _pairError = "";
            _ = Task.Run(async () =>
                             {
                                 var error = await CollabRemote.PairInstallationAsync(code, CancellationToken.None);
                                 _remoteUiActions.Enqueue(() =>
                                                          {
                                                              if (error != null)
                                                                  _pairError = error;
                                                              else
                                                                  _pairCodeInput = "";
                                                          });
                             });
        }

        ImGui.EndDisabled();
        if (_pairError.Length > 0)
            ImGui.TextColored(UiColors.StatusWarning, _pairError);
    }

    /// <summary>Controller side: the owner's installations with connect controls.</summary>
    private static void DrawMyComputers(CollabManager collab)
    {
        var uiScale = T3Ui.UiScaleFactor;

        if (!CollabDiscordAuth.HasValidToken)
        {
            CustomComponents.HelpText("""
                                      Log in with Discord (Settings > Collaboration) to see and connect
                                      to your paired computers.
                                      """);
            return;
        }

        if (ImGui.Button("Add remote TiXL"))
            StartPairing();

        if (_pairingCode.Length > 0)
            DrawPairingCode();

        if (CollabRemote.IsLoading)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("refreshing...");
        }

        if (CollabRemote.ListError.Length > 0)
        {
            ImGui.TextColored(UiColors.StatusWarning, CollabRemote.ListError);
            return;
        }

        var devices = CollabRemote.Devices;
        if (devices.Count == 0)
        {
            ImGui.TextDisabled("No computers paired yet.");
            return;
        }

        PanelLayout.Space(PanelLayout.SpaceS);
        const ImGuiTableFlags flags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg;
        if (!ImGui.BeginTable("##remoteDevices", 4, flags))
            return;

        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 14 * uiScale);
        ImGui.TableSetupColumn("Computer", ImGuiTableColumnFlags.WidthStretch, 0.42f);
        ImGui.TableSetupColumn("Project", ImGuiTableColumnFlags.WidthStretch, 0.38f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 220 * uiScale);
        ImGui.TableHeadersRow();

        foreach (var device in devices)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(device.Online ? UiColors.StatusActivated : UiColors.TextMuted, "●");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.IsNullOrEmpty(device.Name) ? device.Id : device.Name);
            ImGui.SameLine();
            if (device.Online)
                PanelLayout.StatusPill("online", UiColors.StatusActivated);
            else
                PanelLayout.StatusPill(LastSeen(device.LastSeen), UiColors.TextMuted);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.IsNullOrEmpty(device.Project) ? "--" : device.Project);

            ImGui.TableNextColumn();
            ImGui.PushID(device.Id);
            if (device.Room.Length > 0)
            {
                if (ImGui.Button("Join Session"))
                    collab.JoinViaBackend(device.Room);

                ImGui.SameLine();
                if (PanelLayout.DestructiveButton("Stop"))
                    DisconnectFrom(device.Id);
            }
            else
            {
                ImGui.BeginDisabled(!device.Online || !device.Enabled);
                if (ImGui.Button("Connect"))
                    ConnectTo(device);
                ImGui.EndDisabled();

                ImGui.SameLine();
                if (ImGui.Button("Rename"))
                    _renameDeviceId = device.Id;
            }

            ImGui.PopID();
            DrawRenamePopup(device);
        }

        ImGui.EndTable();
    }

    /// <summary>Opens the Remote tab and starts a pairing (debug bridge / menu action).</summary>
    internal static void StartRemotePairing()
    {
        _selectedTab = Tab.Remote;
        if (_pairingCode.Length > 0)
            return;

        StartPairing();
    }

    /// <summary>The code currently shown on the Remote tab ("" when none).</summary>
    internal static string RemotePairingCode => _pairingCode;

    /// <summary>Connects to a paired computer by id (debug bridge).</summary>
    internal static void ConnectRemoteDevice(string deviceId)
    {
        foreach (var device in CollabRemote.Devices)
        {
            if (device.Id != deviceId)
                continue;

            ConnectTo(device);
            return;
        }

        // Not cached yet (e.g. a fresh editor): fetch the list, then connect when it arrives.
        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 await CollabRemote.RefreshDevicesAsync(CancellationToken.None);
                                 _remoteUiActions.Enqueue(() =>
                                                              {
                                                                  foreach (var device in CollabRemote.Devices)
                                                                  {
                                                                      if (device.Id != deviceId)
                                                                          continue;

                                                                      ConnectTo(device);
                                                                      return;
                                                                  }

                                                                  _pairError = $"No paired computer '{deviceId}' in the device list";
                                                              });
                             }
                             catch (Exception e)
                             {
                                 _remoteUiActions.Enqueue(() => _pairError = e.Message);
                             }
                         });
    }

    private static string _renameDeviceId = "";
    private static string _renameInput = "";

    private static void DrawRenamePopup(CollabRemoteApi.Device device)
    {
        if (_renameDeviceId != device.Id)
            return;

        if (_renameInput.Length == 0)
            _renameInput = device.Name;

        ImGui.OpenPopup("Rename computer");
        if (!ImGui.BeginPopup("Rename computer"))
            return;

        var uiScale = T3Ui.UiScaleFactor;
        ImGui.SetNextItemWidth(220 * uiScale);
        ImGui.InputText("##rename", ref _renameInput, 48);
        if (ImGui.Button("Save"))
        {
            var name = _renameInput.Trim();
            _renameDeviceId = "";
            if (name.Length > 0)
                RunOwnerAction(token => CollabRemoteApi.SetAsync(token, device.Id, null, name, CancellationToken.None));

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _renameDeviceId = "";
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static string LastSeen(long unixSeconds)
    {
        if (unixSeconds <= 0)
            return "never";

        var ago = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (ago.TotalMinutes < 1)
            return "offline just now";
        if (ago.TotalHours < 1)
            return $"offline {ago.TotalMinutes:0} min";

        return $"offline {ago.TotalHours:0} h";
    }

    /// <summary>Starts a pairing and shows the code; polled until the installation redeems it.</summary>
    private static void StartPairing()
    {
        _pairingStatus = "Creating a code...";
        _pairError = "";
        var token = CollabDiscordAuth.GetValidAccessToken();
        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 var start = await CollabRemoteApi.StartPairingAsync(token, CancellationToken.None);
                                 _remoteUiActions.Enqueue(() =>
                                                          {
                                                              _pairingCode = start.Code;
                                                              _pairingExpires = start.ExpiresUtc.ToLocalTime();
                                                              _pairingStatus = "Waiting for the other computer...";
                                                              _nextPairPollTicks = 0;
                                                          });
                             }
                             catch (Exception e)
                             {
                                 _remoteUiActions.Enqueue(() =>
                                                          {
                                                              _pairingStatus = "";
                                                              _pairError = e.Message;
                                                          });
                             }
                         });
    }

    private static void DrawPairingCode()
    {
        var remaining = _pairingExpires - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            _pairingCode = "";
            _pairingStatus = "";
            _pairError = "The code expired - press Add remote TiXL again.";
            return;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("code");
        ImGui.SameLine();
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextColored(UiColors.CollabAccent.Rgba, _pairingCode);
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextDisabled($"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00} left  ·  {_pairingStatus}");

        if (Environment.TickCount64 >= _nextPairPollTicks)
        {
            _nextPairPollTicks = Environment.TickCount64 + 2000;
            var token = CollabDiscordAuth.GetValidAccessToken();
            var code = _pairingCode;
            _ = Task.Run(async () =>
                             {
                                 try
                                 {
                                     var status = await CollabRemoteApi.PairStatusAsync(token, code, CancellationToken.None);
                                     if (status.State != CollabRemoteApi.PairState.Waiting)
                                     {
                                         _remoteUiActions.Enqueue(() =>
                                                                  {
                                                                      _pairingCode = "";
                                                                      _pairingStatus = "";
                                                                      _pairError = status.State == CollabRemoteApi.PairState.Paired
                                                                                       ? $"Paired with {status.DeviceName}"
                                                                                       : "The code expired";
                                                                  });
                                     }
                                 }
                                 catch
                                 {
                                     // Transient; the next poll retries.
                                 }
                             });
        }
    }

    /// <summary>Owner: asks a device to host, then joins the room it reports.</summary>
    private static void ConnectTo(CollabRemoteApi.Device device)
    {
        var token = CollabDiscordAuth.GetValidAccessToken();
        _pairError = "";
        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 var result = await CollabRemoteApi.ConnectAsync(token, device.Id, CancellationToken.None);
                                 _remoteUiActions.Enqueue(() =>
                                                          {
                                                              _remoteClaim = result.Claim;
                                                              _remoteDeviceId = device.Id;
                                                              _nextConnectPollTicks = Environment.TickCount64 + 2500;
                                                          });
                             }
                             catch (Exception e)
                             {
                                 _remoteUiActions.Enqueue(() => _pairError = e.Message);
                             }
                         });
    }

    private static string _remoteDeviceId = "";

    private static void DisconnectFrom(string deviceId)
    {
        RunOwnerAction(token => CollabRemoteApi.DisconnectAsync(token, deviceId, CancellationToken.None));
        if (_remoteDeviceId == deviceId && CollabManager.Instance.State != CollabManager.SessionState.Off)
            CollabManager.Instance.Leave(manual: true);
    }

    private static void RunOwnerAction(Func<string, Task> action)
    {
        var token = CollabDiscordAuth.GetValidAccessToken();
        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 await action(token);
                             }
                             catch (Exception e)
                             {
                                 _remoteUiActions.Enqueue(() => _pairError = e.Message);
                             }
                         });
    }

    /// <summary>Per-frame: joins the remote party once the installation reports its room.</summary>
    internal static void UpdateRemoteJoin()
    {
        if (_remoteClaim.Length == 0 || Environment.TickCount64 < _nextConnectPollTicks)
            return;

        _nextConnectPollTicks = Environment.TickCount64 + 2500;
        var collab = CollabManager.Instance;
        if (collab.State != CollabManager.SessionState.Off)
            return;

        var deviceId = _remoteDeviceId;
        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 var token = CollabDiscordAuth.GetValidAccessToken();
                                 if (token.Length == 0)
                                     return;

                                 var devices = await CollabRemoteApi.ListAsync(token, CancellationToken.None);
                                 foreach (var device in devices)
                                 {
                                     if (device.Id != deviceId || device.Room.Length == 0)
                                         continue;

                                     var room = device.Room;
                                     var claim = _remoteClaim;
                                     _remoteUiActions.Enqueue(() =>
                                                              {
                                                                  _remoteClaim = "";
                                                                  // The claim lets the managed host auto-approve
                                                                  // this join without an approval popup.
                                                                  CollabManager.Instance.JoinViaBackend(room, claim);
                                                              });
                                     await CollabRemoteApi.AttachAsync(token, deviceId, claim, CancellationToken.None);
                                     return;
                                 }
                             }
                             catch
                             {
                                 // The installation may still be starting; the next poll retries.
                             }
                         });
    }

    /// <summary>UI-thread queue for the remote calls' results (they run on pool threads).</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<Action> _remoteUiActions = new();

    internal static void DrainRemoteUiActions()
    {
        while (_remoteUiActions.TryDequeue(out var action))
            action();
    }

    /// <summary>Refreshes the device list while the Remote tab is visible.</summary>
    private static void UpdateDeviceList()
    {
        if (_selectedTab != Tab.Remote || Environment.TickCount64 < _nextDeviceRefreshTicks)
            return;

        _nextDeviceRefreshTicks = Environment.TickCount64 + 4000;
        _ = Task.Run(async () =>
                         {
                             try
                             {
                                 await CollabRemote.RefreshDevicesAsync(CancellationToken.None);
                             }
                             catch
                             {
                                 // Reported through ListError.
                             }
                         });
    }

    // ---------------------------------------------------------------------------------------
    // Boygroup tab: the show distribution's state, with a shortcut to its settings
    // ---------------------------------------------------------------------------------------

    private static readonly System.Text.StringBuilder _sharedNameList = new();

    private static void DrawBoygroupTab(CollabManager collab)
    {
        var boygroup = BoygroupManager.Instance;
        var config = UserSettings.Config;
        PanelLayout.BeginSection("Boygrouping");

        var state = boygroup.State;
        if (state == BoygroupManager.BoygroupState.Off)
        {
            PanelLayout.StatusPill("Not running", UiColors.TextMuted);
            PanelLayout.Space(PanelLayout.SpaceS);

            // The intended shape, summarized from the settings so the tab is worth a glance.
            PanelLayout.ValueRow("Role", config.BoygroupIsMaster ? "Master (server)" : "Render node (slave)");
            PanelLayout.ValueRow("Port", $"{config.BoygroupPort}", UiColors.TextMuted);
            PanelLayout.ValueRow("Groups", string.Join(", ", config.BoygroupGroups), UiColors.TextMuted);

            PanelLayout.Space(PanelLayout.SpaceS);
            CustomComponents.HelpText("""
                                      Boygrouping distributes a show from this master to the render
                                      nodes of the installation. Node name, password, groups and
                                      discovery are configured in the settings.
                                      """);

            PanelLayout.Space(PanelLayout.SpaceS);
            if (ImGui.Button("Open Boygroup Settings"))
            {
                var settings = WindowManager.SettingsWindow;
                settings.Config.Visible = true;
                settings.TrySelectCategory("Boygroup");
            }
        }
        else
        {
            var isMaster = state == BoygroupManager.BoygroupState.Server;

            PanelLayout.ValueRow("Role", isMaster ? "Master" : "Render node", isMaster ? UiColors.StatusActivated : UiColors.Text);
            PanelLayout.ValueRow("State", boygroup.StatusText, UiColors.TextMuted);

            if (!string.IsNullOrEmpty(boygroup.InputRelaySummary))
                PanelLayout.ValueRow("Inputs", boygroup.InputRelaySummary, UiColors.TextMuted);

            if (isMaster)
            {
                FormInputs.DrawInputLabel("Clients");
                if (boygroup.Clients.Count == 0)
                {
                    ImGui.TextDisabled("none yet — start TiXL on the render nodes and join");
                }
                else
                {
                    _sharedNameList.Clear();
                    foreach (var client in boygroup.Clients)
                    {
                        if (_sharedNameList.Length > 0)
                            _sharedNameList.Append(", ");

                        _sharedNameList.Append(client.Name);
                    }

                    ImGui.TextDisabled(_sharedNameList.ToString());
                }
            }

            PanelLayout.Space(PanelLayout.SpaceM);
            if (ImGui.Button("Boygroup Settings"))
            {
                var settings = WindowManager.SettingsWindow;
                settings.Config.Visible = true;
                settings.TrySelectCategory("Boygroup");
            }

            ImGui.SameLine(0, PanelLayout.SpaceL * T3Ui.UiScaleFactor);
            if (PanelLayout.DestructiveButton("Stop Boygrouping"))
                boygroup.Leave(manual: true);
        }

        PanelLayout.EndSection();
    }

    // ---------------------------------------------------------------------------------------
    // Session tab: status, participants, listing, whispers, leave
    // ---------------------------------------------------------------------------------------

    private static void DrawSessionTab(CollabManager collab)
    {
        PanelLayout.BeginSection(collab.State == CollabManager.SessionState.Hosting ? "Hosting" : "Session");
        DrawSessionHeader(collab);

        if (collab.State == CollabManager.SessionState.Off)
        {
            CustomComponents.HelpText("No session is running. Host one on the Party matching tab or join a listed session there.");
            PanelLayout.EndSection();
            return;
        }

        PanelLayout.Space(PanelLayout.SpaceS);

        // Spectator state: the host toggles, guests see the badge.
        if (collab.State == CollabManager.SessionState.Hosting)
        {
            var canEdit = collab.ParticipantsCanEdit;
            if (ImGui.Checkbox("Participants can edit (off = spectators)", ref canEdit))
                collab.SetParticipantsCanEdit(canEdit);

            // The shared feed is the SELECTED operator's video (webcam / NDI / Spout / file) - never
            // an output window; everyone renders their own output locally.
            var shareFeed = UserSettings.Config.CollabSharePreview;
            if (ImGui.Checkbox("Share a video feed with the party", ref shareFeed))
            {
                UserSettings.Config.CollabSharePreview = shareFeed;
                UserSettings.Save();
                if (!shareFeed)
                    collab.StopSharingFeed();
            }

            CustomComponents.TooltipForLastItem("""
                                                Select a webcam, NDI, Spout or video operator in the graph,
                                                then press Share: its picture is sent to every participant as
                                                a live video track. Nothing is sent without a chosen operator.
                                                """);

            if (ImGui.SmallButton("Share selected operator"))
                collab.ShareSelectedOperatorAsFeed();

            if (collab.SharedFeedName.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"sharing {collab.SharedFeedName}  ·  {collab.VideoStreamer?.ViewerCount ?? 0} watching");
                ImGui.SameLine();
                if (ImGui.SmallButton("Stop##feed"))
                    collab.StopSharingFeed();
            }
        }
        else if (collab.ReadOnlyForParticipants)
        {
            ImGui.TextColored(UiColors.StatusWarning, "Read-only session: the host set participants to spectators.");
        }

        PanelLayout.EndSection();

        DrawHostPreview(collab);
        DrawRoster(collab);
        DrawJoinRequests(collab);
        DrawListingControls(collab);
        DrawWhispers(collab);
        DrawLeaveButton(collab);
    }

    /// <summary>Host: the pending join requests, also shown in the popup window.</summary>
    private static void DrawJoinRequests(CollabManager collab)
    {
        var requests = collab.PendingJoinRequests;
        if (requests.Count == 0 || collab.State != CollabManager.SessionState.Hosting)
            return;

        PanelLayout.BeginSection("Join requests");
        PanelLayout.StatusPill($"{requests.Count} waiting", UiColors.StatusAttention);
        PanelLayout.Space(PanelLayout.SpaceXS);

        // The button handlers remove from the list while this loop is iterating it.
        foreach (var request in requests.ToArray())
        {
            ImGui.PushID(request.TicketId);
            ImGui.TextUnformatted(request.Name);
            ImGui.SameLine();
            if (ImGui.SmallButton("Confirm"))
                collab.ApproveJoin(request.TicketId);

            ImGui.SameLine();
            if (ImGui.SmallButton("Decline"))
                collab.RejectJoin(request.TicketId, block: false);

            ImGui.PopID();
        }

        PanelLayout.EndSection();
    }

    private static void DrawLeaveButton(CollabManager collab)
    {
        if (PanelLayout.DestructiveButton("Leave Session"))
            collab.Leave(manual: true);
    }

    private static void DrawListingControls(CollabManager collab)
    {
        // Host + relay or backend: the listing is editable while the session runs.
        var isBackend = collab.RelayEndpoint == CollabWebLobby.BackendUrl && CollabWebLobby.IsConfigured;
        if (collab.State != CollabManager.SessionState.Hosting
            || (collab.RelayEndpoint.Length == 0 && !isBackend))
            return;

        var config = UserSettings.Config;
        PanelLayout.BeginSection("Session browser entry");
        FormInputs.AddCheckBox("Listed in session browser", ref config.CollabListSession,
                               """
                               On: the session shows up in other people's session browser with the
                               title and purpose below. Off: only people with the session ID can
                               find it. Applies right away; the session keeps running either way.
                               """,
                               false);
        FormInputs.AddStringInput("Title", ref config.CollabSessionTitle,
                                  $"{collab.LocalIdentity.DisplayName}'s session",
                                  null, "What the session browser shows.");
        DrawPurposePicker(ref config.CollabSessionPurpose);
        if (ImGui.Button("Update Entry"))
        {
            collab.UpdateSessionListing(config.CollabSessionTitle, config.CollabSessionPurpose,
                                        ProjectView.Focused?.OpenedProject.Package.DisplayName ?? "",
                                        config.CollabListSession);
        }

        CustomComponents.TooltipForLastItem("Sends the title, purpose and visibility above to the backend or relay so other people's browsers see them.");
        PanelLayout.EndSection();
    }

    /// <summary>The purpose combo, standalone (moved from the settings panel's shared form).</summary>
    private static void DrawPurposePicker(ref string purpose)
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##partyPurpose", purpose))
        {
            foreach (var option in SessionPurposes)
            {
                if (ImGui.Selectable(option, option == purpose))
                    purpose = option;
            }

            ImGui.EndCombo();
        }
    }

    private static void DrawWhispers(CollabManager collab)
    {
        if (collab.State != CollabManager.SessionState.Hosting)
            return;

        PanelLayout.BeginSection("Whispers");
        var whispers = collab.LobbyWhispers;
        if (whispers.Count == 0)
        {
            CustomComponents.HelpText("People can whisper you from the Party matching tab before they join.");
        }
        else
        {
            foreach (var whisper in whispers)
            {
                if (whisper.FromHost)
                    ImGui.TextColored(UiColors.WhisperOwn.Rgba, "You");
                else
                    ImGui.TextUnformatted(whisper.From);

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
            var uiScale = T3Ui.UiScaleFactor;
            ImGui.SetNextItemWidth(-72 * uiScale);
            ImGui.InputTextWithHint("##whisperReply", $"Reply to {_replyToName}...", ref _whisperReplyInput, 256);
            ImGui.SameLine();
            ImGui.BeginDisabled(_whisperReplyInput.Length == 0);
            if (ImGui.Button("Send##whisper"))
            {
                collab.ReplyToLobbyWhisper(_replyToName, _whisperReplyInput);
                _whisperReplyInput = "";
            }

            ImGui.EndDisabled();
        }

        PanelLayout.EndSection();
    }

    // ---------------------------------------------------------------------------------------
    // Chat tab
    // ---------------------------------------------------------------------------------------

    private string _chatInput = "";
    private int _lastChatCount;
    private bool _scrollToBottom = true;

    private void DrawChatTab(CollabManager collab)
    {
        if (collab.State == CollabManager.SessionState.Off)
        {
            CustomComponents.HelpText("Chat needs a session. Host one or join a listed party on the Party matching tab.");
            FormInputs.ApplyIndent();
            if (ImGui.Button("Open Party matching"))
                _selectedTab = Tab.PartyMatching;

            return;
        }

        DrawChat(collab);
    }

    private void DrawChat(CollabManager collab)
    {
        var log = collab.ChatLog;
        var uiScale = T3Ui.UiScaleFactor;
        var height = Math.Max(80 * uiScale, ImGui.GetContentRegionAvail().Y - 30 * uiScale);
        if (ImGui.BeginChild("##collabwindowchat", new Vector2(0, height), ImGuiChildFlags.Borders))
        {
            if (_scrollToBottom || log.Count != _lastChatCount)
            {
                ImGui.SetScrollHereY(1f);
                _lastChatCount = log.Count;
            }

            foreach (var entry in log)
            {
                // System lines (join/left) stay muted and smaller; messages carry a small
                // timestamp and a stable per-participant name color.
                if (entry.Name == null)
                {
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.TextColored(UiColors.TextMuted, $"{entry.Time:HH:mm}  {entry.Text}");
                    ImGui.PopFont();
                    continue;
                }

                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextDisabled($"{entry.Time:HH:mm}");
                ImGui.PopFont();
                ImGui.SameLine(0, 6 * uiScale);
                ImGui.TextColored(ChatNameColor(collab, entry.Name), entry.Name);
                ImGui.SameLine();
                ImGui.TextWrapped(entry.Text);
            }
        }
        ImGui.EndChild();

        ImGui.SetNextItemWidth(-70 * uiScale);
        var send = ImGui.InputTextWithHint("##chat", "Message...", ref _chatInput, 256,
                                           ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("Send") || send)
        {
            collab.SendChat(_chatInput);
            _chatInput = "";
            _scrollToBottom = true;
            ImGui.SetKeyboardFocusHere(-1);
        }
    }

    /** A stable color per participant: your own messages in the whisper green, everyone else hashed into a collab-friendly palette. */
    private static Color ChatNameColor(CollabManager collab, string name)
    {
        if (name == collab.LocalIdentity.DisplayName)
            return UiColors.WhisperOwn;

        unchecked
        {
            var hash = 0;
            foreach (var t in name)
                hash = hash * 31 + t;

            Color[] palette =
                [
                    UiColors.CollabAccent,
                    new Color(0.46f, 0.76f, 0.62f, 1f),
                    new Color(0.88f, 0.68f, 0.38f, 1f),
                    new Color(0.52f, 0.72f, 0.9f, 1f),
                    new Color(0.86f, 0.56f, 0.58f, 1f),
                ];
            return palette[(uint)hash % palette.Length];
        }
    }

    // ---------------------------------------------------------------------------------------
    // Shared session pieces (header, preview, roster)
    // ---------------------------------------------------------------------------------------

    private static void DrawSessionHeader(CollabManager collab)
    {
        PanelLayout.StatusPill(collab.State == CollabManager.SessionState.Hosting ? "hosting" : "joined", UiColors.StatusActivated);
        ImGui.SameLine();
        ImGui.TextWrapped(collab.StatusText);

        if (collab.State != CollabManager.SessionState.Off)
        {
            if (collab.IsStreamingPreview)
                ImGui.TextDisabled($"Preview: {collab.PreviewEncoderName}");
            if (!string.IsNullOrEmpty(collab.ErrorText))
                ImGui.TextColored(UiColors.StatusWarning, collab.ErrorText);
        }
    }

    /// <summary>The host's rendered output, once the video track has delivered a frame.</summary>
    private static void DrawHostPreview(CollabManager collab)
    {
        if (collab.VideoReceiver is not { } receiver
            || !receiver.TryGetTexture(out var view, out var width, out var height)
            || view == null
            || width <= 0
            || height <= 0)
            return;

        PanelLayout.BeginSection("Host Preview");
        var available = ImGui.GetContentRegionAvail();
        var scale = available.X / width;
        var maxHeight = available.Y * 0.45f;
        if (height * scale > maxHeight)
            scale = maxHeight / height;

        ImGui.Image((IntPtr)view.ImGuiTextureId, new Vector2(width * scale, height * scale));
        PanelLayout.EndSection();
    }

    private static void DrawRoster(CollabManager collab)
    {
        PanelLayout.BeginSection($"Party ({collab.Roster.Count + 1})");

        var dl = ImGui.GetWindowDrawList();
        var isHost = collab.State == CollabManager.SessionState.Hosting;
        if (!isHost)
            _pendingKickPeerId = -1;

        // The host's own row carries Leave; every guest row carries Kick and Pin.
        DrawPartyRow(collab, null, isHost);

        foreach (var (peerId, _) in collab.Roster)
        {
            DrawPartyRow(collab, peerId, isHost);
        }

        DrawKickConfirmation(collab);
        DrawHostTransferConfirmation(collab);
        PanelLayout.EndSection();
    }

    private static void DrawPartyRow(CollabManager collab, int? peerId, bool isHost)
    {
        var uiScale = T3Ui.UiScaleFactor;
        var avatarSize = 26 * uiScale;
        var dl = ImGui.GetWindowDrawList();

        var name = peerId.HasValue ? collab.Roster[peerId.Value] : collab.LocalIdentity.DisplayName;
        var isSelf = !peerId.HasValue;
        var isFollowing = peerId.HasValue && CollabSpectator.IsSpectatingPeer(peerId.Value);
        var isSpectated = peerId.HasValue && (collab.GetPresence(peerId.Value)?.IsAddingOperator ?? false);

        ImGui.PushID(peerId ?? -1);

        // The row background doubles as the click target for the context menu.
        var rowMin = ImGui.GetCursorScreenPos();
        var rowHeight = avatarSize + 8 * uiScale;
        var rowWidth = ImGui.GetContentRegionAvail().X;
        dl.AddRectFilled(rowMin, rowMin + new Vector2(rowWidth, rowHeight),
                         (isFollowing ? UiColors.StatusAutomated : UiColors.ForegroundFull).Fade(0.12f), 4 * uiScale);
        ImGui.Dummy(new Vector2(rowWidth, rowHeight));

        // The context menu closes over _menuPeerId; only attach it for actual peers - the self row
        // has no peer id, and Follow/Kick/Make Host make no sense against yourself.
        if (peerId.HasValue)
        {
            _menuPeerId = peerId.Value;
            CustomComponents.ContextMenuForItem(DrawPeerMenu, name, "##peerMenu");
        }

        var cursor = rowMin + new Vector2(4 * uiScale, 4 * uiScale);

        // Avatar: the Discord logo, or a rounded tile with the initial while it loads.
        var avatarUrl = isSelf ? collab.LocalAvatarUrl : collab.GetPeerAvatarUrl(peerId.Value);
        var avatarTexture = CollabAvatars.GetTexture(avatarUrl);
        var avatarMin = cursor;
        var avatarMax = avatarMin + new Vector2(avatarSize);
        ImGui.SetCursorScreenPos(avatarMin);
        if (avatarTexture != null)
        {
            ImGui.Image((IntPtr)avatarTexture.ImGuiTextureId, new Vector2(avatarSize));
        }
        else
        {
            dl.AddRectFilled(avatarMin, avatarMax, UiColors.StatusAutomated.Fade(0.35f), 4 * uiScale);
            var initial = string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();
            var textSize = ImGui.CalcTextSize(initial);
            dl.AddText(avatarMin + (new Vector2(avatarSize) - textSize) * 0.5f, UiColors.Text, initial);
            ImGui.Dummy(new Vector2(avatarSize));
        }

        ImGui.SameLine();
        cursor = ImGui.GetCursorScreenPos() + new Vector2(6 * uiScale, 0);

        // Name on the first line, role note on the second.
        ImGui.SetCursorScreenPos(cursor + new Vector2(0, 2 * uiScale));
        ImGui.BeginGroup();
        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(name);
        ImGui.PopFont();

        var roleNote = isHost && isSelf ? "host"
                     : !isSelf && !isHost ? "host"
                     : isFollowing ? "following"
                     : isSpectated ? "adding operator"
                     : "";
        var location = isSelf ? CollabUserIdentity.FormatLang(collab.LocalIdentity.Lang) : CollabUserIdentity.FormatLang(collab.GetPeerLang(peerId.Value));
        string secondLine;
        if (roleNote.Length > 0 && location.Length > 0)
        {
            secondLine = $"{roleNote}  ·  {location}";
        }
        else
        {
            secondLine = roleNote.Length > 0 ? roleNote : location;
        }
        if (secondLine.Length > 0)
        {
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.TextDisabled(secondLine);
            ImGui.PopFont();
        }

        ImGui.EndGroup();

        // Ping on the right, then the row buttons.
        ImGui.SameLine();
        var rightEdge = rowMin + new Vector2(rowWidth, rowHeight);
        var ping = isSelf ? 0 : collab.GetPeerPingMs(peerId.Value);
        if (ping > 0)
        {
            var pingLabel = $"{ping} ms";
            var pingSize = ImGui.CalcTextSize(pingLabel);
            ImGui.SetCursorScreenPos(new Vector2(rightEdge.X - 150 * uiScale, rowMin.Y + (rowHeight - pingSize.Y) * 0.5f));
            ImGui.TextDisabled(pingLabel);
        }

        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(rightEdge.X - 96 * uiScale, rowMin.Y + 3 * uiScale));
        ImGui.BeginGroup();
        if (!isSelf)
        {
            if (ImGui.SmallButton("Pin"))
            {
                if (!collab.PinPeerWork(peerId.Value))
                    collab.AppendChat(null, $"{name}'s work is not synced here yet");
            }

            CustomComponents.TooltipForLastItem("Show their work in your output, rendered locally from the synced project");

            if (isHost)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Kick"))
                    _pendingKickPeerId = peerId.Value;
            }
        }
        else if (isHost)
        {
            if (ImGui.SmallButton("Leave"))
                collab.Leave();
        }

        ImGui.EndGroup();

        ImGui.SetCursorScreenPos(rowMin + new Vector2(0, rowHeight + 6 * uiScale));
        ImGui.Dummy(Vector2.Zero);
        ImGui.PopID();
    }

    private static void DrawKickConfirmation(CollabManager collab)
    {
        if (_pendingKickPeerId < 0)
            return;

        if (!collab.Roster.TryGetValue(_pendingKickPeerId, out var name))
        {
            _pendingKickPeerId = -1;
            return;
        }

        FormInputs.AddVerticalSpace(4);
        ImGui.TextUnformatted($"Remove {name} from the session?");
        if (ImGui.Button("Kick"))
        {
            collab.KickParticipant(_pendingKickPeerId);
            _pendingKickPeerId = -1;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            _pendingKickPeerId = -1;
    }

    private static void DrawPeerMenu()
    {
        if (CollabSpectator.IsSpectatingPeer(_menuPeerId))
        {
            if (CustomComponents.DrawMenuItem(1, "Unfollow"))
                CollabSpectator.Stop();
        }
        else if (CustomComponents.DrawMenuItem(2, "Follow"))
        {
            CollabSpectator.Start(_menuPeerId);
        }

        if (CollabManager.Instance.State == CollabManager.SessionState.Hosting && CustomComponents.DrawMenuItem(3, "Kick"))
            _pendingKickPeerId = _menuPeerId;

        if (CollabManager.Instance.State == CollabManager.SessionState.Hosting && CustomComponents.DrawMenuItem(4, "Make Host"))
            _pendingHostTransferPeerId = _menuPeerId;
    }

    private static void DrawHostTransferConfirmation(CollabManager collab)
    {
        if (_pendingHostTransferPeerId < 0)
            return;

        var name = collab.Roster.TryGetValue(_pendingHostTransferPeerId, out var found) ? found : $"#{_pendingHostTransferPeerId}";
        FormInputs.AddVerticalSpace(4);
        ImGui.TextWrapped($"Make {name} the host? The session continues on their machine and you stay in it as a participant.");

        if (ImGui.Button("Make Host"))
        {
            collab.TransferHostTo(_pendingHostTransferPeerId);
            _pendingHostTransferPeerId = -1;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            _pendingHostTransferPeerId = -1;
    }

    private static int _pendingKickPeerId = -1;
    private static int _pendingHostTransferPeerId = -1;
    private static int _menuPeerId;
    private static string _whisperReplyInput = "";
    private static string _replyToName = "";

    /// <summary>Draws a chip after the previous one, wrapping to a new line when the row is full.</summary>
    private static void DrawNameChip(ImDrawListPtr dl, string label, Color color, bool isFirst)
    {
        var uiScale = T3Ui.UiScaleFactor;
        var gap = 4 * uiScale;
        var padding = new Vector2(6, 3) * uiScale;
        var size = ImGui.CalcTextSize(label) + padding * 2;

        var rightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (!isFirst && ImGui.GetItemRectMax().X + gap + size.X <= rightEdge)
            ImGui.SameLine(0, gap);

        var pos = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(pos, pos + size, UiColors.ForegroundFull.Fade(0.08f), 4 * uiScale);
        dl.AddText(pos + padding, color, label);
        ImGui.Dummy(size);
    }

    // ---------------------------------------------------------------------------------------
    // Party browser (moved from the settings panel: the party-matching table)
    // ---------------------------------------------------------------------------------------

    private static string _lobbyFilter = "";
    private static string _lobbyPurposeFilter = "All";
    private static string _lobbySelectedCode = "";
    private static bool _lobbyLoadedOnce;
    private static string _lobbyWhisperInput = "";

    internal static readonly string[] SessionPurposes = ["Jam", "Show", "Teaching", "Help"];

    private static void DrawSessionBrowser(CollabManager collab)
    {
        PanelLayout.BeginSection("Open sessions");

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
            CollabLobby.Refresh(relayHost, relayPort);
        }

        var rooms = useBackend ? CollabWebLobby.Rooms : CollabLobby.Rooms;

        var fontSize = ImGui.GetFontSize();
        var uiScale = T3Ui.UiScaleFactor;
        FormInputs.SetIndentToLeft();
        ImGui.SetNextItemWidth(fontSize * 11);
        ImGui.InputTextWithHint("##lobbyFilter", "Name or title", ref _lobbyFilter, 64);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(fontSize * 7);
        if (ImGui.BeginCombo("##lobbyPurpose", _lobbyPurposeFilter))
        {
            if (ImGui.Selectable("All", _lobbyPurposeFilter == "All"))
                _lobbyPurposeFilter = "All";

            foreach (var option in SessionPurposes)
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

        var autoMatch = FindAutoMatch(rooms);
        ImGui.SameLine();
        ImGui.BeginDisabled(autoMatch == null);
        if (ImGui.Button("Auto Match") && autoMatch != null)
        {
            ShowControlMode.Activate(ShowControlMode.Collab);
            CollabLobbyWhisper.Reset();
            JoinListedRoom(collab, autoMatch);
        }

        ImGui.EndDisabled();

        if (useBackend ? CollabWebLobby.IsRefreshing : CollabLobby.IsRefreshing)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("loading...");
        }

        if (!string.IsNullOrEmpty(useBackend ? CollabWebLobby.ErrorText : CollabLobby.ErrorText))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
            ImGui.TextWrapped(useBackend ? CollabWebLobby.ErrorText : CollabLobby.ErrorText);
            ImGui.PopStyleColor();
        }

        LobbyRoom? selected = null;
        var joinNow = false;
        const ImGuiTableFlags tableFlags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        var tableHeight = ImGui.GetFrameHeightWithSpacing() * 9;
        if (ImGui.BeginTable("##collabLobby", 7, tableFlags, new Vector2(0, tableHeight)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Party ID", ImGuiTableColumnFlags.WidthFixed, fontSize * 4.5f);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, fontSize * 6f);
            ImGui.TableSetupColumn("Platform", ImGuiTableColumnFlags.WidthFixed, fontSize * 3.2f);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, fontSize * 4.2f);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 0.28f);
            ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch, 0.46f);
            ImGui.TableSetupColumn("Members", ImGuiTableColumnFlags.WidthFixed, fontSize * 4.5f);
            ImGui.TableHeadersRow();

            foreach (var room in rooms)
            {
                if (_lobbyPurposeFilter != "All" && !string.Equals(room.Purpose, _lobbyPurposeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_lobbyFilter.Length > 0
                    && !room.HostName.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase)
                    && !room.Title.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase)
                    && !room.Project.Contains(_lobbyFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var isSelected = room.Code == _lobbySelectedCode;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                // The party id doubles as the row selector (spanning all cells), like picking a
                // line in the matching list.
                ImGui.PushID(room.Code);
                if (ImGui.Selectable(room.Code, isSelected,
                                     ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
                {
                    _lobbySelectedCode = room.Code;
                    isSelected = true;
                    joinNow = ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
                }

                ImGui.PopID();
                if (isSelected)
                    selected = room;

                // The row selector doubles as the tooltip anchor: everything that does not fit a
                // column (purpose, project) lives here.
                if (room.Purpose.Length > 0 || room.Project.Length > 0)
                {
                    CustomComponents.TooltipForLastItem($"Party {room.Code}",
                                                        string.Join("\n",
                                                                    new[]
                                                                        {
                                                                            room.Purpose.Length > 0 ? $"Purpose: {room.Purpose}" : "",
                                                                            room.Project.Length > 0 ? $"Project: {room.Project}" : "",
                                                                        }
                                                                       .Where(s => s.Length > 0)));
                }

                // Type: whether participants can edit (the host's spectator toggle).
                ImGui.TableNextColumn();
                PanelLayout.StatusPill(room.Editable ? "Controllable" : "Spectating",
                                       room.Editable ? UiColors.StatusActivated : UiColors.StatusWarning);

                // Race: the host's platform.
                ImGui.TableNextColumn();
                var platform = room.Platform.Length > 0 ? room.Platform : "--";
                ImGui.TextUnformatted(platform);

                // Location: the account's region.
                ImGui.TableNextColumn();
                var region = room.Region.Length > 0 ? room.Region : "--";
                ImGui.TextUnformatted(region);

                // Name: the host, with the unverified marker.
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.IsNullOrEmpty(room.HostName) ? "unnamed" : room.HostName);
                if (!room.HostVerified)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("?");
                    CustomComponents.TooltipForLastItem("This name is not confirmed by a Discord login.");
                }

                // Title, with a lock glyph for password-protected parties and the purpose beside it.
                ImGui.TableNextColumn();
                if (room.PasswordProtected)
                {
                    Icon.Locked.DrawAtCursor(UiColors.TextMuted);
                    ImGui.SameLine(0, 3 * uiScale);
                }

                ImGui.TextUnformatted(room.Title);
                if (room.Purpose.Length > 0)
                {
                    ImGui.SameLine(0, 8 * uiScale);
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.TextDisabled(room.Purpose);
                    ImGui.PopFont();
                }

                if (!string.IsNullOrEmpty(room.Project))
                    CustomComponents.TooltipForLastItem($"Project: {room.Project}");

                // Members: joined / capacity.
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{room.Members}/{room.MaxMembers}");
            }

            ImGui.EndTable();
        }

        if (rooms.Count == 0 && !(useBackend ? CollabWebLobby.IsRefreshing : CollabLobby.IsRefreshing)
            && string.IsNullOrEmpty(useBackend ? CollabWebLobby.ErrorText : CollabLobby.ErrorText))
            ImGui.TextDisabled(useBackend ? "No listed sessions on the backend right now." : "No listed sessions on this relay right now.");

        ImGui.BeginDisabled(selected == null);
        if (ImGui.Button("Join Session") || (joinNow && selected != null))
        {
            if (selected != null)
            {
                ShowControlMode.Activate(ShowControlMode.Collab);
                CollabLobbyWhisper.Reset();
                JoinListedRoom(collab, selected);
                _selectedTab = Tab.PartyManagement;
            }
        }

        ImGui.EndDisabled();
        if (selected is { PasswordProtected: true })
        {
            ImGui.SameLine();
            ImGui.TextDisabled("uses the session password from Settings");
        }

        // Whisper the host of the picked session before joining.
        if (selected != null)
        {
            FormInputs.AddVerticalSpace(4);
            if (useBackend)
                CollabLobbyWhisper.PollBackend(selected.Code, collab.LocalIdentity.DisplayName);

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

            ImGui.SetNextItemWidth(-150 * uiScale);
            ImGui.InputTextWithHint("##lobbyWhisper", $"Whisper {selected.HostName}...", ref _lobbyWhisperInput, 256);
            ImGui.SameLine();
            ImGui.BeginDisabled(_lobbyWhisperInput.Length == 0);
            if (ImGui.Button("Whisper") && selected != null)
            {
                if (useBackend)
                {
                    CollabLobbyWhisper.SendViaBackend(selected.Code, collab.LocalIdentity.DisplayName, _lobbyWhisperInput);
                }
                else
                {
                    CollabLobbyWhisper.Send(CollabLobby.RelayEndpoint, selected.Code,
                                            collab.LocalIdentity.DisplayName, collab.LocalIdentity.Lang,
                                            _lobbyWhisperInput);
                }

                _lobbyWhisperInput = "";
            }

            ImGui.EndDisabled();
            ImGui.TextDisabled("Asks the host something before you join. They can answer on the Party management tab.");
        }

        FormInputs.SetIndentToParameters();
        PanelLayout.EndSection();
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
            // The relay address is chosen by the room's host; the Discord token goes only to a relay the user configured.
            ParseRelayAddress(UserSettings.Config.CollabRelayAddress, out var trustedHost, out var trustedPort);
            var trusted = string.Equals(joinHost, trustedHost, StringComparison.OrdinalIgnoreCase) && joinPort == trustedPort;
            manager.JoinViaRelay(joinHost, joinPort, room.Code, UserSettings.Config.CollabPassword ?? "", trusted);
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
}
// ZCODE-SENTINEL-1795206001
