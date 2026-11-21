#nullable enable
using System.Collections.Generic;
using ImGuiNET;
using T3.Editor.Collab;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// The Silkroad-style join-request popup: who is asking, with Confirm / Decline / Block. The
/// Collab manager shows it when a visitor knocks; it closes when nothing is pending.
/// </summary>
[HelpUiID("CollabJoinRequestWindow")]
internal sealed class CollabJoinRequestWindow : Window
{
    internal CollabJoinRequestWindow()
    {
        Config.Title = "Join request";
        Config.Visible = false;
        WindowSizeOverride = new Vector2(360, 210);
    }

    internal override IReadOnlyList<Window> GetInstances() => [this];

    protected override void DrawContent()
    {
        var manager = CollabManager.Instance;
        var requests = manager.PendingJoinRequests;
        if (requests.Count == 0)
        {
            ImGui.TextDisabled("No pending join requests.");
            return;
        }

        FormInputs.AddSectionHeader("Wants to join your party");
        FormInputs.AddVerticalSpace(4);

        // Approve/Decline mutate the list while we are inside its loop (the button handlers run
        // mid-iteration), so iterate a snapshot.
        foreach (var request in requests.ToArray())
        {
            ImGui.PushID(request.TicketId);
            ImGui.TextUnformatted(request.Name);
            if (!string.IsNullOrEmpty(request.Lang))
            {
                ImGui.SameLine();
                ImGui.TextDisabled(CollabUserIdentity.FormatLang(request.Lang));
            }

            ImGui.TextDisabled($"asked {request.At:HH:mm:ss}");
            FormInputs.AddVerticalSpace(4);

            var buttonWidth = 96f * T3Ui.UiScaleFactor;
            if (ImGui.Button("Confirm", new Vector2(buttonWidth, 0)))
                manager.ApproveJoin(request.TicketId);

            ImGui.SameLine();
            if (ImGui.Button("Decline", new Vector2(buttonWidth, 0)))
                manager.RejectJoin(request.TicketId, block: false);

            ImGui.SameLine();
            if (ImGui.Button("Block", new Vector2(buttonWidth, 0)))
                manager.RejectJoin(request.TicketId, block: true);

            ImGui.PopID();
            FormInputs.AddVerticalSpace(8);
        }
    }
}
