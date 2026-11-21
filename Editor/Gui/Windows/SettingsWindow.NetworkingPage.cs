#nullable enable
using System;
using System.Collections.Generic;
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// One page binds every network protocol and show service to an adapter on this machine. Bind
/// addresses used to be inputs on the operators, which meant a composition shared by collaboration,
/// boygrouping or failover carried the editor's own Ethernet address to every other machine. Here the
/// choices are per-machine state: operators and services read them, the project never contains them,
/// and a multi-homed show machine keeps every socket on its show network while its Wi-Fi, VPN and
/// Hyper-V adapters idle.
/// </summary>
internal sealed partial class SettingsWindow
{
    private const string AutomaticLabel = "Automatic (all adapters)";
    private const string LoopbackLabel = "Loopback (127.0.0.1)";

    private static void DrawNetworkingPanel(ref bool changed)
    {
        FormInputs.AddVerticalSpace();
        DrawProtocolBindings(ref changed);
        FormInputs.AddVerticalSpace();
        DrawSessionAdapters(ref changed);
    }

    private static void DrawProtocolBindings(ref bool changed)
    {
        var adapters = CachedUsableInterfaces();

        PanelLayout.BeginSection("Protocol Bindings");

        CustomComponents.HelpText("""
                                  The adapter each protocol binds its sockets to. Stored per computer and
                                  never saved with the project, so collaboration, boygrouping and failover
                                  always run on every machine's own adapter.
                                  Automatic binds all adapters and follows the OS route; Loopback only
                                  talks to programs on this machine. Operators show the effective address
                                  but no longer own it.
                                  """);

        ImGui.NewLine();

        foreach (var (key, label) in ProtocolInterfaces.Protocols)
        {
            DrawProtocolRow(key, label, adapters, ref changed);
        }

        PanelLayout.EndSection();
    }

    private static void DrawProtocolRow(string protocolKey, string label, IReadOnlyList<LanInterface> adapters, ref bool changed)
    {
        var selection = ProtocolInterfaces.GetSelection(protocolKey);
        var preview = PreviewFor(selection, adapters);

        FormInputs.DrawInputLabel(label);
        var inputSize = FormInputs.GetAvailableInputSize(null, false, true);
        inputSize.X = MathF.Min(inputSize.X, 260f);
        ImGui.SetNextItemWidth(inputSize.X);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new System.Numerics.Vector2(0.02f, 0.5f));

        if (ImGui.BeginCombo($"##interface_{protocolKey}", preview, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.Selectable(AutomaticLabel, string.IsNullOrEmpty(selection)))
                Select(protocolKey, ProtocolInterfaces.AnyKey, ref changed);

            if (ImGui.Selectable(LoopbackLabel, selection == ProtocolInterfaces.LoopbackKey))
                Select(protocolKey, ProtocolInterfaces.LoopbackKey, ref changed);

            foreach (var adapter in adapters)
            {
                if (ImGui.Selectable(adapter.DisplayName, selection == adapter.Name))
                    Select(protocolKey, adapter.Name, ref changed);
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleVar();

        if (adapters.Count > 0 && !string.IsNullOrEmpty(selection)
            && selection != ProtocolInterfaces.LoopbackKey
            && !ContainsAdapter(adapters, selection))
        {
            CustomComponents.TooltipForLastItem($"The saved adapter '{selection}' is not connected. The protocol binds all adapters until it returns.");
            FormInputs.SetCursorToParameterEdit();
            ImGui.SameLine();
            ImGui.TextDisabled("adapter missing");
        }
    }

    /// <summary>
    /// Boygrouping, collaboration and failover each get their own adapter: one machine can serve its
    /// boygroup nodes on the show LAN while a collaboration session runs over another network, and a
    /// failover pair can use a dedicated heartbeat link. Stored in the user settings like the protocol
    /// bindings, so nothing of it travels with the project.
    /// </summary>
    private static void DrawSessionAdapters(ref bool changed)
    {
        var adapters = CachedUsableInterfaces();

        PanelLayout.BeginSection("Session Adapters");

        CustomComponents.HelpText("""
                                  The adapter each show service binds when it hosts. Stored per computer,
                                  never saved with the project. Automatic uses all adapters and follows the
                                  OS route; a change takes effect the next time the service starts.
                                  """);

        ImGui.NewLine();

        DrawServiceRow("Boygrouping",
                       """
                       The adapter boygroup hosting and LAN discovery bind to. The render nodes must
                       reach this address, so pick the adapter that shares their network.
                       """,
                       ref UserSettings.Config.BoygroupInterface, ref changed);

        DrawPeerNetworkCheck(adapters, "Boygrouping", ref UserSettings.Config.BoygroupInterface, ref changed);

        DrawServiceRow("Collaboration",
                       """
                       The adapter collaboration hosting binds to for direct LAN sessions. Sessions
                       relayed through a server are unaffected: only the host's local bind changes.
                       """,
                       ref UserSettings.Config.CollabInterface, ref changed);

        DrawPeerNetworkCheck(adapters, "Collaboration", ref UserSettings.Config.CollabInterface, ref changed);

        DrawServiceRow("Failover",
                       """
                       The adapter the failover heartbeats bind to. Both machines of a pair must use
                       the same network for the heartbeat; a dedicated adapter survives a show-network
                       outage that would silence a shared one.
                       """,
                       ref UserSettings.Config.FailoverInterface, ref changed);

        DrawPeerNetworkCheck(adapters, "Failover", ref UserSettings.Config.FailoverInterface, ref changed);

        PanelLayout.EndSection();
    }

    private static void DrawServiceRow(string serviceName, string tooltip, ref string adapterField, ref bool changed)
    {
        var adapters = CachedUsableInterfaces();
        var selection = adapterField;
        var preview = PreviewFor(selection, adapters);

        FormInputs.DrawInputLabel(serviceName);
        var inputSize = FormInputs.GetAvailableInputSize(null, false, true);
        inputSize.X = MathF.Min(inputSize.X, 260f);
        ImGui.SetNextItemWidth(inputSize.X);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new System.Numerics.Vector2(0.02f, 0.5f));

        if (ImGui.BeginCombo($"##service_{serviceName}", preview, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.Selectable(AutomaticLabel, string.IsNullOrWhiteSpace(selection)))
            {
                adapterField = "";
                UserSettings.Save();
                changed = true;
            }

            foreach (var adapter in adapters)
            {
                if (!ImGui.Selectable(adapter.DisplayName, string.Equals(selection, adapter.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                adapterField = adapter.Name;
                UserSettings.Save();
                changed = true;
            }

            ImGui.EndCombo();
        }

        ImGui.PopStyleVar();
        FormInputs.AppendTooltip(tooltip);

        if (adapters.Count > 0 && !string.IsNullOrWhiteSpace(selection) && !ContainsAdapter(adapters, selection))
        {
            FormInputs.SetCursorToParameterEdit();
            ImGui.SameLine();
            ImGui.TextDisabled("adapter missing");
        }
    }

    private static void Select(string protocolKey, string selection, ref bool changed)
    {
        ProtocolInterfaces.SetSelection(protocolKey, selection);
        changed = true;
    }

    private static string PreviewFor(string selection, IReadOnlyList<LanInterface> adapters)
    {
        if (string.IsNullOrEmpty(selection))
            return AutomaticLabel;

        if (selection == ProtocolInterfaces.LoopbackKey)
            return LoopbackLabel;

        var adapter = FindAdapter(adapters, selection);
        return adapter != null ? adapter.DisplayName : $"{selection} (not connected)";
    }

    private static LanInterface? FindAdapter(IReadOnlyList<LanInterface> adapters, string name)
    {
        foreach (var adapter in adapters)
        {
            if (string.Equals(adapter.Name, name, StringComparison.OrdinalIgnoreCase))
                return adapter;
        }

        return null;
    }

    private static bool ContainsAdapter(IReadOnlyList<LanInterface> adapters, string name) => FindAdapter(adapters, name) != null;
}
