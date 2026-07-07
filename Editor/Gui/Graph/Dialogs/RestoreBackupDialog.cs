#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.Dialogs;

/// <summary>
/// Confirms restoring a project from one backup picked in the project context menu. Restore
/// overwrites the project's files on disk, then restarts the editor — the running instance keeps
/// stale assemblies and package info in memory and becomes unstable once the files change under
/// it. Because users restoring are usually already in trouble, the current state is archived as
/// a pinned backup first (on by default), so even a wrong pick loses nothing.
/// </summary>
internal sealed class RestoreBackupDialog : ModalDialog
{
    internal void ShowNextFrame(string projectName, string projectFolder, AutoBackup.AutoBackup.BackupEntry backup)
    {
        _projectName = projectName;
        _projectFolder = projectFolder;
        _backup = backup;
        _archiveCurrentState = true;
        _resultMessage = null;
        ShowNextFrame();
    }

    public void Draw()
    {
        DialogSize = new Vector2(460, 200);
        if (BeginDialog("Restore from backup"))
        {
            if (_resultMessage != null)
                DrawResult();
            else
                DrawConfirm();

            EndDialogContent();
        }

        EndDialog();
    }

    private void DrawConfirm()
    {
        CustomComponents.StylizedText($"Restore '{_projectName}'?", Fonts.FontBold, UiColors.Text);

        var when = _backup.Timestamp == DateTime.MinValue
                       ? "unknown time"
                       : ((DateTime?)_backup.Timestamp).GetReadableRelativeTime();
        ImGui.TextColored(UiColors.TextMuted, $"Backup v{_backup.Index} — saved {when}");
        FormInputs.AddVerticalSpace(4);

        ImGui.TextWrapped("This replaces the project's operators and source with the backup — anything "
                          + "added since is removed — and deletes bin/ and obj/. Assets are kept. "
                          + "TiXL will restart afterwards to load the restored project.");
        FormInputs.AddVerticalSpace(4);

        ImGui.Checkbox("Archive current state before restoring", ref _archiveCurrentState);
        FormInputs.AddVerticalSpace(6);

        if (CustomComponents.DrawCtaButton("Restore and Restart", Icon.None, CustomComponents.ButtonStates.Emphasized))
        {
            _resultMessage = TryRestoreAndRestart();
        }

        ImGui.SameLine();
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
            ImGui.CloseCurrentPopup();
    }

    /// <summary>Returns a message only when something failed or the restart couldn't be triggered — success exits the process.</summary>
    private string TryRestoreAndRestart()
    {
        if (_archiveCurrentState)
        {
            var tag = $"preRestore{DateTime.Now:yyyyMMdd.HHmmss}";
            if (!AutoBackup.AutoBackup.CreatePinnedBackup(_projectFolder, tag, out _))
            {
                return "Could not archive the current state, so the restore was not started. "
                       + "See the log for details.";
            }
        }

        var restored = AutoBackup.AutoBackup.RestoreFromArchive(_projectFolder, _backup.FilePath);
        if (!restored)
            return "Restore failed — see the log for details. The project's files may be incomplete.";

        if (!TryRestartEditor())
        {
            return $"Restored '{_projectName}' from backup v{_backup.Index}, but restarting automatically "
                   + "failed. Please close and reopen TiXL to load the restored project.";
        }

        // Unreachable in practice — the restart exits the application.
        return $"Restored '{_projectName}'. Restarting...";
    }

    /// <summary>
    /// Spawns a fresh editor process and exits this one. Uses the plain exit path (which does not
    /// save projects) — saving here would overwrite the just-restored files with in-memory state.
    /// </summary>
    private static bool TryRestartEditor()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Log.Warning("Could not determine the editor executable for a restart.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
                                {
                                    FileName = exePath,
                                    // Launch via the shell so the child isn't part of this process's job
                                    // object. Under a debugger (Rider) that job is killed when we exit,
                                    // which would take the freshly spawned editor down with it.
                                    UseShellExecute = true,
                                };

            // Keep flags like --override-version-id so the new instance uses the same folders,
            // but drop stale wait flags from earlier restarts.
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--wait-for-exit=", StringComparison.Ordinal))
                    continue;

                startInfo.ArgumentList.Add(args[i]);
            }

            // The new instance waits for this process to fully exit — starting earlier races
            // our save-on-quit settings writes and kills the new instance during startup.
            startInfo.ArgumentList.Add($"--wait-for-exit={Environment.ProcessId}");

            Process.Start(startInfo);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to start a new editor instance: {e.Message}");
            return false;
        }

        Log.Info("Restarting after backup restore...");
        EditorUi.Instance.ExitApplication();
        return true;
    }

    private void DrawResult()
    {
        ImGui.TextWrapped(_resultMessage);
        FormInputs.AddVerticalSpace(8);

        if (ImGui.Button("Reveal in Explorer"))
            CoreUi.Instance.OpenWithDefaultApplication(_projectFolder);

        ImGui.SameLine();
        if (ImGui.Button("Close"))
            ImGui.CloseCurrentPopup();
    }

    private string _projectName = string.Empty;
    private string _projectFolder = string.Empty;
    private AutoBackup.AutoBackup.BackupEntry _backup;
    private bool _archiveCurrentState = true;
    private string? _resultMessage;
}
