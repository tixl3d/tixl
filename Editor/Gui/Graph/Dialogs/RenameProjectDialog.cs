#nullable enable
using System.IO;
using ImGuiNET;
using T3.Editor.Compilation;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;

/// <summary>
/// Renames a project from the Hub's context menu. The rename itself happens at the next start (see
/// <see cref="ProjectRenaming"/>), so the dialog saves, stages the rename and restarts the editor.
/// </summary>
internal sealed class RenameProjectDialog : ModalDialog
{
    internal void ShowNextFrame(EditableSymbolProject project)
    {
        _projectFolder = project.Folder;
        _oldRootNamespace = project.CsProjectFile.RootNamespace;
        _oldName = _oldRootNamespace[(_oldRootNamespace.LastIndexOf('.') + 1)..];
        _newName = ProjectRenaming.GetPending(project.Folder)?.NewName ?? _oldName;
        _resultMessage = null;
        ShowNextFrame();
    }

    public void Draw()
    {
        DialogSize = new Vector2(550, 280);
        if (BeginDialog("Rename project"))
        {
            if (_resultMessage != null)
            {
                DrawResult();
            }
            else
            {
                DrawForm();
            }

            EndDialogContent();
        }

        EndDialog();
    }

    private void DrawForm()
    {
        var warning = _newName == _oldName ? null : ProjectRenaming.ValidateName(_newName, _projectFolder);
        FormInputs.AddStringInput("Name", ref _newName, "(mandatory)", warning,
                                  "Must start with a capital letter and not contain spaces or special characters.",
                                  autoFocus: ImGui.IsWindowAppearing());

        var isValid = warning == null && _newName != _oldName;
        var newRootNamespace = _oldRootNamespace[..(_oldRootNamespace.Length - _oldName.Length)] + _newName;
        FormInputs.SetCursorToParameterEdit();
        ImGui.TextColored(isValid ? UiColors.TextMuted : UiColors.StatusError, newRootNamespace);

        FormInputs.SetIndentToLeft();
        FormInputs.AddHint("""
                           Renames the project's folder, .csproj, namespace and home operator.
                           TiXL saves and restarts to apply it. A backup is kept, and restoring it
                           undoes the rename.
                           """);
        FormInputs.SetIndentToParameters();

        AlignCtaButtons("Cancel", "Rename and Restart");
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
            ImGui.CloseCurrentPopup();

        ImGui.SameLine();
        if (CustomComponents.DrawCtaButton("Rename and Restart", isEnabled: isValid))
            _resultMessage = TryRenameAndRestart();
    }

    /// <summary>Returns a message only when something failed or the restart couldn't be triggered — success exits the process.</summary>
    private string TryRenameAndRestart()
    {
        if (!TryGetProject(out var project))
            return "The project is no longer loaded.";

        // The restart exits without saving, so unsaved changes would be lost and missing from the backup.
        T3Ui.Save(false);

        if (!ProjectRenaming.TryStage(project, _newName, out var error))
            return error ?? "The rename could not be staged.";

        if (!EditorRestart.TryRestart())
            return $"'{_oldName}' will be renamed to '{_newName}' the next time TiXL starts. Please restart TiXL.";

        // Unreachable in practice — the restart exits the application.
        return "Restarting...";
    }

    private bool TryGetProject([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EditableSymbolProject? project)
    {
        foreach (var candidate in EditableSymbolProject.AllProjects)
        {
            if (string.Equals(Path.GetFullPath(candidate.Folder), Path.GetFullPath(_projectFolder), StringComparison.OrdinalIgnoreCase))
            {
                project = candidate;
                return true;
            }
        }

        project = null;
        return false;
    }

    private void DrawResult()
    {
        ImGui.TextWrapped(_resultMessage);
        FormInputs.AddVerticalSpace(8);
        if (ImGui.Button("Close"))
            ImGui.CloseCurrentPopup();
    }

    private string _projectFolder = string.Empty;
    private string _oldRootNamespace = string.Empty;
    private string _oldName = string.Empty;
    private string _newName = string.Empty;
    private string? _resultMessage;
}
