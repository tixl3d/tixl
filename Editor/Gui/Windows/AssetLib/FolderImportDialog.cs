#nullable enable
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.AssetLib;

/// <summary>
/// Shown when a single folder is dropped onto the editor. Offers copying the folder into the
/// project's Assets/, copying its files sorted by asset type, or linking the folder in place
/// (a <c>.tixlLink</c> mount) so large media collections don't get duplicated.
/// </summary>
internal sealed class FolderImportDialog : ModalDialog
{
    private enum States
    {
        PickOption,
        Copying,
    }

    internal static readonly FolderImportDialog Instance = new();

    private FolderImportDialog()
    {
        DialogSize = new Vector2(550, 350);
    }

    internal static void ShowForFolder(string folderPath)
    {
        var project = ProjectView.Focused?.CompositionInstance?.Symbol.SymbolPackage as EditableSymbolProject;

        var d = Instance;
        d._sourceFolder = folderPath.ToForwardSlashes().TrimEnd('/');
        d._folderName = Path.GetFileName(d._sourceFolder);
        d._project = project;
        d._state = States.PickOption;
        d._copyProgressMessage = null;
        d.StartScan();
        d.ShowNextFrame();
    }

    internal void Draw()
    {
        if (BeginDialog("Import Folder"))
        {
            if (_project == null)
            {
                ImGui.TextWrapped("Open an editable project first, then drop the folder again.");
                FormInputs.AddVerticalSpace();
                if (CustomComponents.DrawCtaButton("Close"))
                    ImGui.CloseCurrentPopup();
            }
            else if (_state == States.Copying)
            {
                DrawCopyProgress();
            }
            else
            {
                DrawOptions(_project);
            }

            EndDialogContent();
        }

        EndDialog();
    }

    private void DrawOptions(EditableSymbolProject project)
    {
        FormInputs.AddSectionHeader(_folderName);

        var statsLabel = _scanCompleted
                             ? $"{_scannedFileCount} files, {StringUtils.GetReadableFileSize(_scannedByteCount)}"
                             : "Counting files...";
        CustomComponents.StylizedText($"{_sourceFolder}  ·  {statsLabel}", Fonts.FontSmall, UiColors.TextMuted);
        CustomComponents.StylizedText($"Import into project {project.Name}", Fonts.FontSmall, UiColors.TextMuted);

        FormInputs.AddVerticalSpace();

        // Option 1: copy as subfolder
        if (CustomComponents.DrawCtaButton("Copy folder into Assets", _scanCompleted))
        {
            StartCopy(project, sortedByType: false);
        }

        CustomComponents.StylizedText($"Copies everything to Assets/{_folderName}/ keeping the folder structure.",
                                      Fonts.FontSmall, UiColors.TextMuted);
        FormInputs.AddVerticalSpace();

        // Option 2: copy sorted by asset type
        if (CustomComponents.DrawCtaButton("Copy files sorted by type", _scanCompleted))
        {
            StartCopy(project, sortedByType: true);
        }

        CustomComponents.StylizedText("Copies the files into the standard subfolders for their type (images/, video/, audio/, ...).",
                                      Fonts.FontSmall, UiColors.TextMuted);
        FormInputs.AddVerticalSpace();

        // Option 3: link without copying
        if (CustomComponents.DrawCtaButton("Link folder without copying", true))
        {
            LinkFolder(project);
            ImGui.CloseCurrentPopup();
        }

        ImGui.TextWrapped("""
                          The folder shows up in the Assets window but stays where it is — nothing is copied.

                          Note that deleting files inside the linked folder deletes the originals, and
                          new files created there (e.g. proxies) also appear in the source folder.
                          """);

        if (SyncToolConflicts.IsInsideWindowsManagedFolder(project.Folder))
        {
            CustomComponents.StylizedText("This project seems to be in a synced folder (e.g. OneDrive). On other machines the linked files will be missing unless the same folder exists there.",
                                          Fonts.FontSmall, UiColors.StatusAttention);
        }

        FormInputs.AddVerticalSpace();

        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawCopyProgress()
    {
        FormInputs.AddSectionHeader("Copying...");

        var total = Math.Max(1, _scannedFileCount);
        var processed = _copyProcessedCount;
        ImGui.ProgressBar(processed / (float)total, new Vector2(-1, 0), $"{processed} / {_scannedFileCount}");

        FormInputs.AddVerticalSpace();
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
        {
            _copyCancelRequested = true;
        }

        if (_copyProgressMessage != null)
        {
            Log.Debug(_copyProgressMessage);
            _copyProgressMessage = null;
            _state = States.PickOption;
            ImGui.CloseCurrentPopup();
        }
    }

    private void StartScan()
    {
        _scanCompleted = false;
        _scannedFileCount = 0;
        _scannedByteCount = 0;

        var generation = ++_scanGeneration;
        var folder = _sourceFolder;

        Task.Run(() =>
                 {
                     var count = 0;
                     long bytes = 0;
                     try
                     {
                         foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.*", SearchOption.AllDirectories))
                         {
                             count++;
                             bytes += file.Length;
                         }
                     }
                     catch (Exception e)
                     {
                         Log.Warning($"Failed to scan {folder}: {e.Message}");
                     }

                     if (generation != _scanGeneration)
                         return;

                     _scannedFileCount = count;
                     _scannedByteCount = bytes;
                     _scanCompleted = true;
                 });
    }

    private void StartCopy(EditableSymbolProject project, bool sortedByType)
    {
        _state = States.Copying;
        _copyProcessedCount = 0;
        _copyCancelRequested = false;

        var sourceFolder = _sourceFolder;
        var folderName = _folderName;

        Task.Run(() =>
                 {
                     var skipped = 0;
                     var failed = 0;
                     try
                     {
                         if (sortedByType)
                         {
                             CopySortedByType(sourceFolder, project.AssetsFolder, ref skipped, ref failed);
                         }
                         else
                         {
                             var destRoot = GetUniqueFolderPath(project.AssetsFolder, folderName);
                             CopyRecursive(sourceFolder, destRoot, ref skipped, ref failed);
                         }
                     }
                     catch (Exception e)
                     {
                         Log.Warning($"Import failed: {e.Message}");
                         failed++;
                     }

                     var result = _copyCancelRequested
                                      ? $"Import cancelled after {_copyProcessedCount} files."
                                      : $"Imported {_copyProcessedCount} files from {sourceFolder}.";

                     if (skipped > 0)
                         result += $" Skipped {skipped} already existing.";

                     if (failed > 0)
                         result += $" {failed} failed (see log).";

                     _copyProgressMessage = result;
                     ResourceFileWatcher.FileStateChangeCounter++;
                 });
    }

    private void CopyRecursive(string sourceFolder, string destFolder, ref int skipped, ref int failed)
    {
        Directory.CreateDirectory(destFolder);

        foreach (var dir in Directory.EnumerateDirectories(sourceFolder))
        {
            if (_copyCancelRequested)
                return;

            CopyRecursive(dir, Path.Combine(destFolder, Path.GetFileName(dir)), ref skipped, ref failed);
        }

        foreach (var file in Directory.EnumerateFiles(sourceFolder))
        {
            if (_copyCancelRequested)
                return;

            CopyFileCounted(file, Path.Combine(destFolder, Path.GetFileName(file)), ref skipped, ref failed);
        }
    }

    private void CopySortedByType(string sourceFolder, string assetsFolder, ref int skipped, ref int failed)
    {
        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
        {
            if (_copyCancelRequested)
                return;

            AssetType.TryGetForFilePath(file, out var assetType, out _);
            var subfolder = assetType.Subfolders.Length > 0 ? assetType.Subfolders[0] : string.Empty;
            var destFolder = string.IsNullOrEmpty(subfolder) ? assetsFolder : Path.Combine(assetsFolder, subfolder);
            Directory.CreateDirectory(destFolder);
            CopyFileCounted(file, Path.Combine(destFolder, Path.GetFileName(file)), ref skipped, ref failed);
        }
    }

    private void CopyFileCounted(string sourcePath, string destPath, ref int skipped, ref int failed)
    {
        if (File.Exists(destPath))
        {
            skipped++;
            return;
        }

        try
        {
            File.Copy(sourcePath, destPath);
            Interlocked.Increment(ref _copyProcessedCount);
        }
        catch (Exception e)
        {
            Log.Warning($"Can't copy {sourcePath}: {e.Message}");
            failed++;
        }
    }

    private void LinkFolder(EditableSymbolProject project)
    {
        var mountName = _folderName;

        // Avoid colliding with an existing real folder or link of the same name
        var suffix = 1;
        while (Directory.Exists(Path.Combine(project.AssetsFolder, mountName))
               || File.Exists(Path.Combine(project.AssetsFolder, mountName + AssetLinkFolders.Extension)))
        {
            mountName = $"{_folderName} {suffix++}";
        }

        try
        {
            var linkFilePath = AssetLinkFolders.Write(project, _sourceFolder, mountName);
            AssetLinkFolders.TryMount(linkFilePath, project);
            Log.Debug($"Linked {_sourceFolder} as {project.Name}:{mountName}/");
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to link folder: {e.Message}");
        }
    }

    private static string GetUniqueFolderPath(string parentFolder, string name)
    {
        var path = Path.Combine(parentFolder, name);
        var suffix = 1;
        while (Directory.Exists(path) || File.Exists(path + AssetLinkFolders.Extension))
        {
            path = Path.Combine(parentFolder, $"{name} {suffix++}");
        }

        return path;
    }

    private string _sourceFolder = string.Empty;
    private string _folderName = string.Empty;
    private EditableSymbolProject? _project;
    private States _state;

    private int _scanGeneration;
    private volatile bool _scanCompleted;
    private volatile int _scannedFileCount;
    private long _scannedByteCount;

    private int _copyProcessedCount;
    private volatile bool _copyCancelRequested;
    private volatile string? _copyProgressMessage;
}
