#nullable enable
using ImGuiNET;
using T3.Core.Model;
using T3.Editor.Gui.Input;
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
        DialogSize = new Vector2(420, 200);

        if (BeginDialog(string.Empty))
        {
            FormInputs.AddSectionHeader("Are you leaving?");

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5.0f);
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundButton.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);
            var buttonSize = new Vector2(110f, 40f) * T3Ui.UiScaleFactor;
            var size = ImGui.GetContentRegionAvail();
            float spacing;
            var changeCount = GetChangedSymbolCount();

            if (changeCount > 0)
            {
                spacing = (size.X - buttonSize.X * 3)/2;
            }
            else
            {
                spacing = (size.X - buttonSize.X * 2);
            }

            FormInputs.AddVerticalSpace(2);

            if (ImGui.Button("Cancel", buttonSize))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, spacing);

            if (changeCount > 0)
            {
                
                if (ImGui.Button("Save and Exit", buttonSize) && !T3Ui.IsCurrentlySaving)
                {
                    _exitAfterSave = true;
                    T3Ui.SaveInBackground(saveAll: false);
                }
                ImGui.SameLine(0, spacing);
            }
            // Check if we should exit after save completed
            if (_exitAfterSave && !T3Ui.IsCurrentlySaving && GetChangedSymbolCount() == 0)
            {
                _exitAfterSave = false;
                Log.Debug("Exiting after saving");
                EditorUi.Instance.ExitApplication();
            }

            ImGui.PushFont(Fonts.FontBold);
            if (ImGui.Button("Exit", buttonSize))
            {
                Log.Debug("Shutting down");
                EditorUi.Instance.ExitApplication();
            }
            FormInputs.AddVerticalSpace(2);
            if (changeCount > 0)
            {
                ImGui.Text($"Your have {changeCount} unsaved changes.");
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