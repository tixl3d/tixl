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

/// <summary>
/// Confirms leaving the editor. Return triggers the highlighted default (Save and Exit when there are
/// unsaved changes, otherwise Exit); Escape cancels.
/// </summary>
internal sealed class ExitDialog : ModalDialog
{
    internal void Draw()
    {
        DialogSize = new Vector2(420, 120);

        // ImGui 1.90 rejects empty popup ids; use ### to keep the title bar visually empty.
        if (BeginDialog("###ExitDialog"))
        {
            FormInputs.AddSectionHeader("Are you leaving?");

            var changeCount = GetChangedSymbolCount();
            var hasChanges = changeCount > 0;
            if (hasChanges)
            {
                ImGui.TextUnformatted($"You have {changeCount} unsaved changes.");
            }

            FormInputs.AddVerticalSpace(2);

            // Finish a pending "Save and Exit" once the background save has flushed all changes.
            if (_exitAfterSave && !T3Ui.IsCurrentlySaving && !hasChanges)
            {
                _exitAfterSave = false;
                Log.Debug("Exiting after saving");
                EditorUi.Instance.ExitApplication();
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                ImGui.CloseCurrentPopup();
            }

            // Cancel sits left, the outcome buttons are pushed to the right edge.
            var itemSpacing = ImGui.GetStyle().ItemSpacing.X;
            var cancelWidth = CustomComponents.GetCtaButtonSize("Cancel").X;
            var exitWidth = CustomComponents.GetCtaButtonSize("Exit").X;
            var rightWidth = hasChanges
                                 ? CustomComponents.GetCtaButtonSize("Save and Exit").X + itemSpacing + exitWidth
                                 : exitWidth;
            var gap = MathF.Max(itemSpacing, ImGui.GetContentRegionAvail().X - cancelWidth - rightWidth);

            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, gap);

            if (hasChanges)
            {
                if (CustomComponents.DrawCtaButton("Exit", Icon.None, CustomComponents.ButtonStates.Default))
                {
                    Log.Debug("Shutting down without saving");
                    EditorUi.Instance.ExitApplication();
                }

                ImGui.SameLine();

                if (CustomComponents.DrawCtaButton("Save and Exit", isEnabled: !T3Ui.IsCurrentlySaving, enableTriggerWithReturn: true))
                {
                    _exitAfterSave = true;
                    T3Ui.SaveInBackground(saveAll: false);
                }
            }
            else if (CustomComponents.DrawCtaButton("Exit", isEnabled: true, enableTriggerWithReturn: true))
            {
                Log.Debug("Shutting down");
                EditorUi.Instance.ExitApplication();
            }

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

    private bool _exitAfterSave;
}
