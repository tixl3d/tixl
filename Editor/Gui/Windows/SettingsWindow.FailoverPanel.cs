#nullable enable
using ImGuiNET;
using T3.Core.Show;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// The "Failover" panel of the settings window: active/standby output arbitration for show-critical
/// network outputs (Artnet, sACN, NDI/Spout). Pair this with boygrouping or manual sync so both
/// machines run the same composition — the arbiter only decides who streams.
/// </summary>
internal sealed partial class SettingsWindow
{
    private static void DrawFailoverPanel(ref bool changed)
    {
        var config = UserSettings.Config;

        // Failover is switched through the menu-bar mode selector; this panel configures the pair.

        PanelLayout.BeginSection("Status");

        var roleColor = FailoverArbiter.RoleText.StartsWith("ACTIVE")
                            ? UiColors.StatusActivated
                            : FailoverArbiter.RoleText is "Standby" or "Handover"
                                ? UiColors.StatusWarning
                                : UiColors.TextMuted;

        PanelLayout.StatusPill(FailoverArbiter.RoleText, roleColor);
        PanelLayout.Space(PanelLayout.SpaceS);

        PanelLayout.ValueRow("Peer", FailoverArbiter.PeerSummary, UiColors.TextMuted);
        PanelLayout.ValueRow("Takeovers", FailoverArbiter.TakeoverCount.ToString(), UiColors.TextMuted);

        if (!string.IsNullOrEmpty(FailoverArbiter.LastEvent))
        {
            CustomComponents.SmallGroupHeader("Recent events");
            foreach (var failoverEvent in FailoverArbiter.RecentEvents)
            {
                ImGui.TextDisabled(failoverEvent);
            }
        }

        PanelLayout.EndSection();

        PanelLayout.BeginSection("Pairing", "Both machines of a pair use the same Show Id; the higher Priority becomes the master.");

        changed |= FormInputs.AddStringInput("Show Id",
                                             ref config.FailoverShowId,
                                             null,
                                             null,
                                             "Isolates failover pairs on one network. Both machines of a pair must use the same Show Id.");

        changed |= FormInputs.AddInt("Priority",
                                     ref config.FailoverPriority,
                                     0, 255, 1,
                                     "Higher priority wins. Give your preferred master a higher value (e.g. 100 vs 50). Ties break by lower IP / lower node id.",
                                     100);

        PanelLayout.EndSection();

        PanelLayout.BeginSection("Timing", "How fast the standby detects a missing partner and takes over.");

        changed |= FormInputs.AddInt("Heartbeat (ms)",
                                     ref config.FailoverHeartbeatMs,
                                     100, 5000, 10,
                                     "How often failover partners announce each other. Shorter = faster takeover detection, slightly more LAN traffic.",
                                     500);

        changed |= FormInputs.AddInt("Timeout (× heartbeat)",
                                     ref config.FailoverTimeoutFactor,
                                     2, 20, 1,
                                     "Heartbeats that may be missed before the Standby promotes itself. 3 × 500 ms ≈ 1.5 s takeover.",
                                     3);

        changed |= FormInputs.AddCheckBox("Preempt on master return",
                                          ref config.FailoverPreempt,
                                          """
                                          When the higher-priority machine comes back, it reclaims ACTIVE through a brief
                                          handover (both sides mute output for one heartbeat window). Turn off to let the
                                          backup keep streaming until it dies.
                                          """,
                                          true);

        PanelLayout.EndSection();

        DrawNetworkAdapterHint("Failover", ref UserSettings.Config.FailoverInterface, ref changed);

        PanelLayout.BeginSection("Setting up a redundant pair");
        CustomComponents.HelpText("""
                                  1. Run the same project and composition on both machines (boygrouping or manual copies).
                                  2. Enable failover on both, same Show Id, master gets the higher Priority.
                                  3. Configure your Artnet/sACN/stream outputs identically on both.
                                  The Standby machine renders silently and starts streaming within the timeout when the
                                  ACTIVE machine stops answering. Heartbeats use multicast so both machines can also be
                                  rehearsed on a single PC.
                                  """);
        PanelLayout.EndSection();
    }
}
