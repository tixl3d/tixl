#nullable enable
using System;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Shown the first time the LASER master button is armed: a laser safety warning the user
/// must confirm before any output can light up. OK arms the master directly;
/// "Don't show this message again" persists the acknowledgement in the user settings;
/// Cancel leaves the master off.
/// </summary>
internal sealed class LaserArmWarningDialog : ModalDialog
{
    internal void Draw()
    {
        // No minimum height, so the dialog hugs its content without empty space below the buttons.
        DialogSize = new Vector2(560, 0);

        // The header is part of the content and Cancel/Escape close it, so a title bar would only repeat both.
        Flags = ImGuiWindowFlags.NoTitleBar;

        if (BeginDialog("###LaserArmWarning"))
        {
            // Every open starts with a clean checkbox: a tick only counts when it is on
            // screen while OK is pressed, so a stale tick from a cancelled open can't
            // silence the warning on the next arm.
            if (ImGui.IsWindowAppearing())
                _dontShowAgain = false;

            FormInputs.AddSectionHeader("Laser safety");

            FormInputs.AddVerticalSpace(4);

            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextUnformatted("A laser beam is concentrated light: it can hurt eyes faster than you can blink. A few habits keep everyone - and your hardware - safe.");
            ImGui.Spacing();
            ImGui.TextUnformatted("Keep the beam above head height, and never aim it at people, animals or reflective surfaces.");
            ImGui.Spacing();
            ImGui.TextUnformatted("Know where your projector's key switch and emergency stop are, and test them before every session.");
            ImGui.Spacing();
            ImGui.TextUnformatted("Check the rules where you perform - beams over an audience are regulated in most countries.");
            ImGui.Spacing();
            ImGui.TextUnformatted("Set a safe area and a sane scan rate on your laser output - your scanners will thank you.");
            ImGui.PopTextWrapPos();

            FormInputs.AddVerticalSpace(6);

            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(_footnoteColor, "TiXL keeps every beam dark until you arm it - but you are the safety system.");
            ImGui.PopTextWrapPos();

            FormInputs.AddVerticalSpace(6);

            ImGui.Checkbox("Don't show this message again", ref _dontShowAgain);

            FormInputs.AddVerticalSpace(2);

            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                ImGui.CloseCurrentPopup();
            }

            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (CustomComponents.DrawCtaButton("OK", isEnabled: true, enableTriggerWithReturn: true))
            {
                if (_dontShowAgain)
                {
                    UserSettings.Config.LaserSafetyWarningAcknowledged = true;
                    UserSettings.Save();
                    Log.Debug("Laser arm warning acknowledged - won't be shown again");
                }

                LaserMasterEnable.Enabled = true;
                Log.Debug("Laser master enable -> ENABLED (green) - armed through the safety warning");
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }

        EndDialog();
    }

    private bool _dontShowAgain;

    private static readonly Vector4 _footnoteColor = new(0.65f, 0.75f, 0.65f, 1f);
}