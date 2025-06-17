#nullable enable
using ImGuiNET;
using T3.Core.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Dialog;

internal sealed class ExitDialog : ModalDialog
{
    private bool _exitAfterSave = false;

    internal void Draw()
    {
        DialogSize = new Vector2(330, 200);

        if (BeginDialog(string.Empty))
        {
            FormInputs.AddSectionHeader("Are you leaving?");

            var changeCount = GetChangedSymbolCount();
            if (changeCount > 0)
            {
                ImGui.Text($"Your have {changeCount} unsaved changes.");
                if (ImGui.Button("Save and Exit") && !T3Ui.IsCurrentlySaving)
                {
                    _exitAfterSave = true;
                    T3Ui.SaveInBackground(saveAll: false);
                }
              
            }
            // Check if we should exit after save completed
            if (_exitAfterSave && !T3Ui.IsCurrentlySaving && GetChangedSymbolCount() == 0)
            {
                _exitAfterSave = false;
                Log.Debug("Exiting after saving");
                EditorUi.Instance.ExitApplication();
            }

            FormInputs.AddVerticalSpace();
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f);
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundButton.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);

            var buttonSize = new Vector2(100f, 40f);
            var size = ImGui.GetContentRegionAvail();
            var spacing = (size.X - 200f);

            if (ImGui.Button("Cancel", buttonSize))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, spacing);

            ImGui.PushFont(Fonts.FontBold);
            if (ImGui.Button("Exit", buttonSize))
            {
                Log.Debug("Shutting down");
                EditorUi.Instance.ExitApplication();
            }

            ImGui.PopFont();

            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar();
            EndDialogContent();
        }

        EndDialog();
    }

    private static int GetChangedSymbolCount()
    {
        var changeCount = 0;
        if (ProjectView.Focused != null)
        {
            foreach (var package in SymbolPackage.AllPackages)
            {
                foreach (var x in package.Symbols.Values)
                {
                    if (x.GetSymbolUi().HasBeenModified)
                    {
                        changeCount++;
                    }
                }
            }
        }

        return changeCount;
    }
}