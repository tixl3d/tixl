#nullable enable
using System.IO;
using ImGuiNET;
using T3.Editor.Compilation;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;

/// <summary>
/// Shown once after startup when projects could not be loaded because a sync tool (OneDrive,
/// Dropbox, ...) most likely blocked file access (see <see cref="SyncToolConflicts"/>). Users
/// usually don't know syncing is even active, so awareness is the main point of the dialog; the
/// offered fix moves the affected project folders to a non-synced location and restarts.
/// </summary>
internal sealed class CouldNotLoadProjectDialog : ModalDialog
{
    /// <summary>Opens the dialog if any project failed to load with a likely sync-tool conflict.</summary>
    internal void ShowIfProjectsBlocked()
    {
        _affectedProjects.Clear();
        foreach (var broken in ProjectSetup.BrokenProjects)
        {
            if (broken.LikelySyncConflict)
                _affectedProjects.Add(broken);
        }

        if (_affectedProjects.Count == 0)
            return;

        _targetOptions.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive is { DriveType: DriveType.Fixed, IsReady: true })
                _targetOptions.Add(Path.Combine(drive.RootDirectory.FullName, "TiXL"));
        }

        _selectedTargetIndex = 0;
        _customFolder = string.Empty;
        _moveProjects = false;
        _createBackupBeforeMove = true;
        _validatedTargetFolder = null;
        _conflictingTargets.Clear();
        _resultMessage = null;
        ShowNextFrame();
    }

    public void Draw()
    {
        DialogSize = new Vector2(560, 280);
        if (BeginDialog("Could not load Project"))
        {
            if (_resultMessage != null)
                DrawResult();
            else
                DrawContent();

            EndDialogContent();
        }

        EndDialog();
    }

    private void DrawContent()
    {
        CustomComponents.StylizedText("TiXL was not allowed to load the following projects:", Fonts.FontBold, UiColors.Text);

        foreach (var project in _affectedProjects)
        {
            var parentPath = (Path.GetDirectoryName(project.Folder) ?? string.Empty).Replace('\\', '/');
            ImGui.TextColored(UiColors.TextMuted, parentPath + "/");
            ImGui.SameLine(0, 0);
            CustomComponents.StylizedText(Path.GetFileName(project.Folder), Fonts.FontBold, UiColors.Text);
        }

        FormInputs.AddVerticalSpace(4);
        ImGui.TextWrapped("This frequently indicates interference with sync tools like OneDrive or Dropbox. "
                          + "These are often active without users being aware of it.");
        FormInputs.AddVerticalSpace(4);
        ImGui.Separator();
        FormInputs.AddVerticalSpace(4);

        CustomComponents.StylizedText("Suggested Fix", Fonts.FontBold, UiColors.TextMuted);
        FormInputs.AddVerticalSpace(2);

        ImGui.Checkbox("Try to move these project folders to...", ref _moveProjects);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160 * T3Ui.UiScaleFactor);
        if (ImGui.BeginCombo("##moveTarget", GetTargetLabel(_selectedTargetIndex)))
        {
            for (var index = 0; index <= _targetOptions.Count; index++)
            {
                if (ImGui.Selectable(GetTargetLabel(index), index == _selectedTargetIndex))
                    _selectedTargetIndex = index;
            }

            ImGui.EndCombo();
        }

        if (IsCustomTargetSelected)
        {
            ImGui.TextColored(UiColors.TextMuted, "Folder");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(260 * T3Ui.UiScaleFactor);
            ImGui.InputText("##customFolder", ref _customFolder, 512);
            ImGui.SameLine();
            if (ImGui.Button("..."))
            {
                var picked = TryPickFolder();
                if (picked != null)
                    _customFolder = picked;
            }
        }

        FormInputs.AddVerticalSpace(6);

        if (_moveProjects)
        {
            ImGui.Checkbox("Create backup before moving", ref _createBackupBeforeMove);
            FormInputs.AddVerticalSpace(2);

            var targetFolder = GetSelectedTargetFolder();
            var targetValid = !string.IsNullOrWhiteSpace(targetFolder) && Path.IsPathRooted(targetFolder);
            if (targetValid && SyncToolConflicts.IsInsideWindowsManagedFolder(targetFolder))
            {
                ImGui.TextColored(UiColors.StatusWarning, "This folder is also managed by Windows and might be synced.");
                FormInputs.AddVerticalSpace(2);
            }

            ValidateTargetConflicts(targetValid ? targetFolder : null);
            if (_conflictingTargets.Count > 0)
            {
                ImGui.TextColored(UiColors.StatusWarning, "Can't move because these folders already exist and are not empty:");
                foreach (var conflictingFolder in _conflictingTargets)
                {
                    ImGui.TextColored(UiColors.StatusWarning, "  " + conflictingFolder.Replace('\\', '/'));
                }

                FormInputs.AddVerticalSpace(2);
            }

            if (CustomComponents.DrawCtaButton("Move and Restart", isEnabled: targetValid && _conflictingTargets.Count == 0))
            {
                _resultMessage = TryMoveAndRestart(targetFolder!);
            }

            ImGui.SameLine();
            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
                ImGui.CloseCurrentPopup();
        }
        else
        {
            if (CustomComponents.DrawCtaButton("Close", Icon.None, CustomComponents.ButtonStates.Default))
                ImGui.CloseCurrentPopup();
        }
    }

    private void DrawResult()
    {
        ImGui.TextWrapped(_resultMessage);
        FormInputs.AddVerticalSpace(8);

        if (ImGui.Button("Close"))
            ImGui.CloseCurrentPopup();
    }

    private bool IsCustomTargetSelected => _selectedTargetIndex >= _targetOptions.Count;

    private string GetTargetLabel(int index)
    {
        if (index >= _targetOptions.Count)
            return "Custom";

        return _targetOptions[index].Replace('\\', '/') + "/";
    }

    private string? GetSelectedTargetFolder()
    {
        return IsCustomTargetSelected
                   ? _customFolder.Trim()
                   : _targetOptions[_selectedTargetIndex];
    }

    /// <summary>
    /// Moves the affected project folders to the target, registers it as a project directory and
    /// restarts the editor. Returns a message only when something failed or the restart couldn't
    /// be triggered — a fully successful move exits the process.
    /// </summary>
    private string? TryMoveAndRestart(string targetFolder)
    {
        try
        {
            targetFolder = Path.GetFullPath(targetFolder);
            Directory.CreateDirectory(targetFolder);
        }
        catch (Exception e)
        {
            return $"Could not create \"{targetFolder}\":\n{e.Message}";
        }

        var failures = new List<string>();
        var movedCount = 0;

        foreach (var project in _affectedProjects)
        {
            var sourceFolder = project.Folder;
            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                failures.Add($"The folder of '{project.Name}' no longer exists.");
                continue;
            }

            var targetProjectFolder = Path.Combine(targetFolder, Path.GetFileName(sourceFolder));
            if (IsNonEmptyDirectory(targetProjectFolder))
            {
                failures.Add($"\"{targetProjectFolder}\" already exists and is not empty — '{project.Name}' was not moved.");
                continue;
            }

            if (_createBackupBeforeMove)
            {
                var tag = $"preMove{DateTime.Now:yyyyMMdd.HHmmss}";
                if (!AutoBackup.AutoBackup.CreatePinnedBackup(sourceFolder, tag, out _))
                {
                    failures.Add($"Could not create a backup of '{project.Name}', so it was not moved. See the log for details.");
                    continue;
                }
            }

            // The original is only removed after the copy completed and was verified, so a failure
            // at any point leaves the project intact in one place instead of scattered across two.
            try
            {
                // Copy instead of Directory.Move so moving across drives works. bin/ and obj/ are
                // regenerated on load and are the folders most likely to hold sync-tool locks.
                CopyDirectoryRecursive(sourceFolder, targetProjectFolder);
            }
            catch (Exception e)
            {
                failures.Add($"Copying '{project.Name}' failed: {e.Message}");
                TryDeleteDirectory(targetProjectFolder);
                continue;
            }

            var expectedFileCount = CountFilesRecursive(sourceFolder);
            var copiedFileCount = CountFilesRecursive(targetProjectFolder);
            if (copiedFileCount != expectedFileCount)
            {
                failures.Add($"Verifying the copy of '{project.Name}' failed ({copiedFileCount} of {expectedFileCount} files) — "
                             + "the incomplete copy was removed and the project was not moved.");
                TryDeleteDirectory(targetProjectFolder);
                continue;
            }

            movedCount++;
            try
            {
                Directory.Delete(sourceFolder, recursive: true);
            }
            catch (Exception)
            {
                // The copy succeeded but the original couldn't be removed (often the sync tool
                // again). Remove at least the .csproj so the next scan doesn't load both copies.
                try
                {
                    project.FileInfo.Delete();
                    failures.Add($"The old folder \"{sourceFolder}\" could not be removed completely — please delete it manually.");
                }
                catch (Exception)
                {
                    failures.Add($"The old folder \"{sourceFolder}\" could not be removed and might load as a duplicate — please delete it manually.");
                }
            }
        }

        if (movedCount > 0)
        {
            var alreadyRegistered = false;
            foreach (var directory in UserSettings.Config.ProjectDirectories)
            {
                if (string.Equals(Path.TrimEndingDirectorySeparator(directory), Path.TrimEndingDirectorySeparator(targetFolder),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            if (!alreadyRegistered)
                UserSettings.Config.ProjectDirectories.Add(targetFolder);

            UserSettings.Save();
        }

        if (failures.Count > 0)
        {
            var summary = movedCount > 0
                              ? $"Moved {movedCount} project(s) to \"{targetFolder}\", but there were problems:\n\n"
                              : "The projects could not be moved:\n\n";
            var suffix = movedCount > 0
                             ? "\n\nPlease restart TiXL to load the moved project(s)."
                             : string.Empty;
            return summary + string.Join("\n", failures) + suffix;
        }

        if (!EditorRestart.TryRestart())
        {
            return $"Moved {movedCount} project(s) to \"{targetFolder}\", but restarting automatically failed. "
                   + "Please close and reopen TiXL to load them.";
        }

        // Unreachable in practice — the restart exits the application.
        return "Restarting...";
    }

    /// <summary>
    /// Rechecks the target for existing non-empty project folders. Cached by target path because
    /// this runs from the draw loop — the file system is only touched when the target changes.
    /// </summary>
    private void ValidateTargetConflicts(string? targetFolder)
    {
        if (targetFolder == _validatedTargetFolder)
            return;

        _validatedTargetFolder = targetFolder;
        _conflictingTargets.Clear();
        if (string.IsNullOrWhiteSpace(targetFolder))
            return;

        foreach (var project in _affectedProjects)
        {
            var targetProjectFolder = Path.Combine(targetFolder, Path.GetFileName(project.Folder));
            if (IsNonEmptyDirectory(targetProjectFolder))
                _conflictingTargets.Add(targetProjectFolder);
        }
    }

    private static bool IsNonEmptyDirectory(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return false;

            using var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
            return entries.MoveNext();
        }
        catch (Exception)
        {
            // If the folder can't even be inspected, treat it as a conflict — moving there would fail anyway.
            return true;
        }
    }

    /// <summary>Counts with the same bin/obj exclusion as <see cref="CopyDirectoryRecursive"/> so source and copy are comparable.</summary>
    private static int CountFilesRecursive(string folder)
    {
        var count = 0;
        foreach (var _ in Directory.EnumerateFiles(folder))
        {
            count++;
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(folder))
        {
            var directoryName = Path.GetFileName(directoryPath);
            if (directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count += CountFilesRecursive(directoryPath);
        }

        return count;
    }

    private static void CopyDirectoryRecursive(string sourceFolder, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        foreach (var filePath in Directory.EnumerateFiles(sourceFolder))
        {
            File.Copy(filePath, Path.Combine(targetFolder, Path.GetFileName(filePath)));
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceFolder))
        {
            var directoryName = Path.GetFileName(directoryPath);
            if (directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectoryRecursive(directoryPath, Path.Combine(targetFolder, directoryName));
        }
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not clean up \"{folder}\": {e.Message}");
        }
    }

    private static string? TryPickFolder()
    {
        try
        {
            using var folderBrowser = EditorUi.Instance.CreateFilePicker();
            folderBrowser.ValidateNames = false;
            folderBrowser.CheckFileExists = false;
            folderBrowser.CheckPathExists = true;
            folderBrowser.FileName = "Folder Selection.";
            if (!folderBrowser.ChooseFile())
                return null;

            return Path.GetDirectoryName(folderBrowser.FileName);
        }
        catch (Exception e)
        {
            Log.Warning("Couldn't open folder picker: " + e.Message);
            return null;
        }
    }

    private readonly List<ProjectSetup.BrokenProjectInfo> _affectedProjects = [];
    private readonly List<string> _targetOptions = [];
    private readonly List<string> _conflictingTargets = [];
    private int _selectedTargetIndex;
    private string _customFolder = string.Empty;
    private bool _moveProjects;
    private bool _createBackupBeforeMove = true;
    private string? _validatedTargetFolder;
    private string? _resultMessage;
}
