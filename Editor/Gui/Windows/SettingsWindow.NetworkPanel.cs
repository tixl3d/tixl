#nullable enable
using System;
using System.Collections.Generic;
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Show;

namespace T3.Editor.Gui.Windows;

internal sealed partial class SettingsWindow
{
    /// <summary>
    /// The adapters for collaboration, boygrouping and failover are chosen on the Networking page,
    /// next to the protocol bindings. These panels keep only the advice: when a linked machine sits
    /// on another adapter's network, say so and offer the fix where the session is being configured.
    /// </summary>
    private static void DrawNetworkAdapterHint(string serviceName, ref string adapterField, ref bool changed)
    {
        var interfaces = CachedUsableInterfaces();
        DrawPeerNetworkCheck(interfaces, serviceName, ref adapterField, ref changed);

        FormInputs.SetCursorToParameterEdit();
        ImGui.TextDisabled($"Network adapter for {serviceName}: chosen in Settings → Networking");
    }

    /// <summary>
    /// The adapter is stored per computer and never travels with a project or a deploy, so each machine
    /// checks itself against the machines it is linked to and offers the adapter that shares their network.
    /// </summary>
    private static void DrawPeerNetworkCheck(IReadOnlyList<LanInterface> interfaces, string serviceName, ref string adapterField, ref bool changed)
    {
        NetworkPeerCheck.CollectPeers(_networkPeers);
        if (_networkPeers.Count == 0)
            return;

        NetworkPeerCheck.FindProblems(adapterField, interfaces, _networkPeers, _networkFindings);
        if (_networkFindings.Count == 0)
        {
            FormInputs.SetCursorToParameterEdit();
            ImGui.TextDisabled($"{_networkPeers.Count} linked machine(s) can be reached on this network setup.");
            return;
        }

        var suggested = _networkFindings[0].Suggested;
        foreach (var finding in _networkFindings)
        {
            var message = finding.Kind switch
                              {
                                  NetworkPeerCheck.Kind.WrongAdapter
                                      => $"{finding.Peer.Source} {finding.Peer.Address} is on the network of {finding.Suggested.Name}, not of the selected {serviceName} adapter.",
                                  NetworkPeerCheck.Kind.SavedAdapterMissing
                                      => $"{finding.Peer.Source} {finding.Peer.Address} is on the network of {finding.Suggested.Name}; the saved {serviceName} adapter does not exist on this machine.",
                                  _ => $"{finding.Peer.Source} {finding.Peer.Address} is on the network of {finding.Suggested.Name}; this machine has several adapters and none is selected for {serviceName}.",
                              };

            FormInputs.SetCursorToParameterEdit();
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
            ImGui.TextWrapped(message);
            ImGui.PopStyleColor();
        }

        FormInputs.SetCursorToParameterEdit();
        if (ImGui.Button($"Use {suggested.Name}"))
        {
            adapterField = suggested.Name;
            UserSettings.Save();
            changed = true;
        }

        CustomComponents.TooltipForLastItem($"Selects the {serviceName} adapter that shares the network of the linked machine above. Takes effect the next time the service starts.");
    }

    private static readonly List<NetworkPeer> _networkPeers = [];
    private static readonly List<NetworkPeerCheck.Finding> _networkFindings = [];

    /// <summary>The usable adapter list, refreshed on a short interval: enumerating the OS
    /// adapters every frame costs a system call and a pile of allocations in draw code.</summary>
    private static List<LanInterface> CachedUsableInterfaces()
    {
        var now = Environment.TickCount64;
        if (_usableInterfaces == null || now - _interfacesRefreshedTicks > InterfaceRefreshIntervalMs)
        {
            _usableInterfaces = LanInterfaces.GetUsable();
            _interfacesRefreshedTicks = now;
        }

        return _usableInterfaces!;
    }

    private const int InterfaceRefreshIntervalMs = 5000;
    private static List<LanInterface>? _usableInterfaces;
    private static long _interfacesRefreshedTicks;
}
