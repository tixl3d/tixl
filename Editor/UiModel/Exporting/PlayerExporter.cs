#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Compilation;
using T3.Core.DataTypes;
using T3.Core.IO;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.SystemUi;
using T3.Core.UserData;
using T3.Editor.Compilation;
using T3.Editor.Gui;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using T3.Editor.Gui.Interaction.Timing;
using T3.Serialization;

namespace T3.Editor.UiModel.Exporting;

internal static partial class PlayerExporter
{
    public static bool TryExportInstance(Instance composition, SymbolUi.Child childUi, out string reason, out string exportDir)
    {
        T3Ui.Save(false);

        // Collect all ops and types
        var exportedInstance = composition.Children[childUi.SymbolChild.Id];
        var symbol = exportedInstance.Symbol;
        Log.Info($"Exporting {symbol.Name}...");

        var output = exportedInstance.Outputs.FirstOrDefault();
        if (output == null || output.ValueType != typeof(Texture2D))
        {
            reason = "Can only export ops with 'Texture2D' output";
            exportDir = string.Empty;
            return false;
        }

        // Traverse starting at output and collect everything
        var exportData = new ExportData();
        exportData.TryAddSymbol(symbol);

        var package = composition.Symbol.SymbolPackage;
        exportDir = Path.Combine(package.Folder, FileLocations.ExportSubFolder, childUi.SymbolChild.ReadableName);

        // if (!KeepCopyOfExportDir(out reason, exportDir)) 
        //     return false;

        Directory.CreateDirectory(exportDir);

        var operatorDir = Path.Combine(exportDir, FileLocations.OperatorsSubFolder);
        Directory.CreateDirectory(operatorDir);

        // Copy assemblies into export dir. Get symbol packages directly used by the exported symbols
        if (!TryExportSymbolPackages(out reason, exportData, operatorDir))
            return false;

        // Get soundtrack or show warning message
        if (TryFindSoundtrack(exportedInstance, symbol, out var address))
        {
            if (AssetRegistry.TryGetAsset(address, out var soundtrackAsset))
            {
                exportData.TryAddSharedAsset(soundtrackAsset);
            }
        }
        else
        {
            const string yes = "Yes";
            var choice = BlockingWindow.Instance.ShowMessageBox("No defined soundtrack found. Continue with export?", "No soundtrack", yes,
                                                                "No, cancel export");

            if (choice != yes)
            {
                reason = $"Failed to find soundTrack for [{symbol.Name}] - export cancelled, see log for details";
                return false;
            }
        }

        // Collect used assets
        RecursivelyCollectExportData(output, exportData);
        exportData.PrintInfo();

        if (!AssetExportItem.TryCopyItems(exportData.ExportItems, exportDir))
        {
            reason = "Failed to copy resource files - see log for details";
            return false;
        }

        if (!TryExportAssetsOnlyPackages(exportData, operatorDir, out reason))
            return false;

        // Copy shared assets
        var editorResourcesTargetDir = Path.Combine(exportDir, FileLocations.EditorResourcesSubfolder);
        Directory.CreateDirectory(editorResourcesTargetDir);
        if (!TryCopyDirectory(SharedResources.EditorResourcesDirectory, editorResourcesTargetDir, out reason))
            return false;

        var playerDirectory = Path.Combine(FileLocations.StartFolder, "Player");
        if (!TryCopyDirectory(playerDirectory, exportDir, out reason))
            return false;

        if (!TryExportSettings(exportDir, symbol, out reason))
            return false;

        reason = "Exported successfully to " + exportDir;
        return true;
    }

    /// <summary>
    /// Compile EditableProjects and copy read only projects.  
    /// </summary>
    private static bool TryExportSymbolPackages(out string reason, ExportData exportData, string operatorDir)
    {
        string[] excludeSubdirectories =
            [
                ".git",
                FileLocations.SymbolUiSubFolder,
                FileLocations.SourceCodeSubFolder,
                FileLocations.ExportSubFolder,
                FileLocations.AssetsSubfolder, // Assets are filtered by referencing address and copied separately 
            ];

        foreach (var package in exportData.SymbolPackages)
        {
            Log.Debug($"Exporting package {package.Name}...");
            var packageName = package.Name;
            var targetDirectory = Path.Combine(operatorDir, packageName);
            Directory.CreateDirectory(targetDirectory);

            if (package is EditableSymbolProject project)
            {
                project.SaveModifiedSymbols();
                if (!project.CsProjectFile.TryCompileRelease(false, out var failureLog))
                {
                    reason = $"Failed to compile project \"{packageName}\" - \n{failureLog}";
                    return false;
                }

                // Copy the resulting directory into the target directory
                var sourceDir = project.CsProjectFile.GetBuildTargetDirectory(CsProjectFile.PlayerBuildMode);

                // Copy contents recursively into the target directory
                if (!TryCopyDirectory(sourceDir, targetDirectory, out reason, excludeSubdirectories))
                    return false;
            }
            else
            {
                // Copy full directory into target directory recursively, maintaining folder layout
                var directoryToCopy = package.AssemblyInformation.Directory;

                if (!TryCopyDirectory(directoryToCopy, targetDirectory, out reason, excludeSubdirectories))
                    return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// If only Assets but no Symbols are used from a package, we still need to copy its OperatorPackage.json file,
    /// so the player can register these assets on startup. 
    /// </summary>
    private static bool TryExportAssetsOnlyPackages(ExportData exportData, string operatorDir, out string reason)
    {
        reason = string.Empty;
        foreach (var assetPackage in exportData.AssetPackages)
        {
            var alreadyIncluded = exportData.SymbolPackages.Any(sp => sp.Id == assetPackage.Id);
            if (alreadyIncluded)
                continue;

            var sourcePath = Path.Combine(assetPackage.Folder, ReleaseInfo.FileName);
            var targetPath = Path.Combine(operatorDir, assetPackage.Name, ReleaseInfo.FileName);

            if (!TryCopyFile(sourcePath, targetPath))
            {
                reason = $"Failed to copy {sourcePath} for asset package {assetPackage}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Recursively copies a directory to a target directory, excluding specified subfolders, files, and file extensions.
    /// </summary>
    private static bool TryCopyDirectory(string directoryToCopy, string targetDirectory, out string reason, string[]? excludeSubFolders = null,
                                         string[]? excludeFiles = null, string[]? excludeFileExtensions = null)
    {
        try
        {
            var rootFiles = Directory.EnumerateFiles(directoryToCopy, "*", SearchOption.TopDirectoryOnly);
            var subfolderFiles = Directory.EnumerateDirectories(directoryToCopy, "*", SearchOption.TopDirectoryOnly)
                                          .Where(subDir =>
                                                 {
                                                     if (excludeSubFolders == null)
                                                         return true;

                                                     var dirName = Path.GetRelativePath(directoryToCopy, subDir);
                                                     foreach (var excludeSubFolder in excludeSubFolders)
                                                     {
                                                         if (string.Equals(dirName, excludeSubFolder, StringComparison.OrdinalIgnoreCase))
                                                         {
                                                             return false;
                                                         }
                                                     }

                                                     return true;
                                                 })
                                          .SelectMany(subDir => Directory.EnumerateFiles(subDir, "*", SearchOption.AllDirectories));

            var files = rootFiles.Concat(subfolderFiles);
            var shouldExcludeFiles = excludeFiles != null;
            var shouldExcludeFileExtensions = excludeFileExtensions != null;
            foreach (var file in files)
            {
                if (shouldExcludeFiles && excludeFiles!.Contains(Path.GetFileName(file)))
                    continue;

                bool shouldSkipBasedOnExtension = false;
                if (shouldExcludeFileExtensions)
                {
                    foreach (var extension in excludeFileExtensions!)
                    {
                        if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                        {
                            shouldSkipBasedOnExtension = true;
                            break;
                        }
                    }
                }

                if (shouldSkipBasedOnExtension)
                    continue;

                var relativePath = Path.GetRelativePath(directoryToCopy, file);
                var targetPath = Path.Combine(targetDirectory, relativePath);
                var targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir == null)
                {
                    reason = $"Failed to get directory for \"{targetPath}\" - is it missing a file extension?";
                    return false;
                }

                Directory.CreateDirectory(targetDir);
                File.Copy(file, targetPath, true);
            }
        }
        catch (Exception e)
        {
            reason = $"Failed to copy directory {directoryToCopy} to {targetDirectory}. Exception:\n{e}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryCopyFile(string sourcePath, string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath);
        try
        {
            Directory.CreateDirectory(directory!);
            File.Copy(sourcePath, targetPath, true);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to copy resource file for export: {sourcePath}  {e.Message}");
        }

        return false;
    }

    private static void RecursivelyCollectExportData(ISlot slot, ExportData exportData)
    {
        var gotConnection = slot.TryGetFirstConnection(out var firstConnection);
        if (slot is IInputSlot)
        {
            if (gotConnection)
            {
                RecursivelyCollectExportData(firstConnection, exportData);
            }

            CheckInputForResourcePath(slot, exportData);
            return;
        }

        if (gotConnection)
        {
            // slot is an output of an composition op
            RecursivelyCollectExportData(firstConnection, exportData);
            exportData.TryAddInstance(slot.Parent);
            return;
        }

        var parent = slot.Parent;

        if (!exportData.TryAddInstance(parent))
            return; // already visited

        foreach (var input in parent.Inputs)
        {
            CheckInputForResourcePath(input, exportData);

            if (!input.HasInputConnections)
                continue;

            if (input.TryGetAsMultiInput(out var multiInput))
            {
                foreach (var entry in multiInput.GetCollectedInputs())
                {
                    RecursivelyCollectExportData(entry, exportData);
                }
            }
            else if (input.TryGetFirstConnection(out var inputsFirstConnection))
            {
                RecursivelyCollectExportData(inputsFirstConnection, exportData);
            }
        }
    }

    private static bool TryFindSoundtrack(Instance instance, Symbol symbol,
                                          [NotNullWhen(true)] out string? address)
    {
        var playbackSettings = symbol.PlaybackSettings;
        if (playbackSettings == null)
        {
            Log.Warning($"Project {symbol} has no playback settings");
            address = null;
            return false;
        }

        if (playbackSettings.TryGetMainSoundtrack(instance, out var soundtrack) is not true)
        {
            if (PlaybackUtils.TryFindingSoundtrack(out soundtrack, out _))
            {
                Log.Warning($"You should define soundtracks withing the exported operators. Falling back to {soundtrack.Clip.FilePath} set in parent...");
            }
            else
            {
                address = null;
                return false;
            }

            Log.Debug("No soundtrack defined within operator.");
        }

        address = soundtrack.Clip.FilePath;
        return FileResource.TryGetFileResource(soundtrack.Clip.FilePath, instance, out _);
    }

    private static void CheckInputForResourcePath(ISlot inputSlot, ExportData exportData)
    {
        var parent = inputSlot.Parent;
        var inputUi = parent.GetSymbolUi().InputUis[inputSlot.Id];
        if (inputUi is not StringInputUi stringInputUi)
            return;

        if (stringInputUi.Usage != StringInputUi.UsageType.FilePath && stringInputUi.Usage != StringInputUi.UsageType.DirectoryPath)
            return;

        var compositionSymbol = parent.Parent?.Symbol;
        if (compositionSymbol == null)
            return;

        var parentSymbolChild = compositionSymbol.Children[parent.SymbolChildId];
        var value = parentSymbolChild.Inputs[inputSlot.Id].Value;
        if (value is not InputValue<string> stringValue)
            return;

        var address = stringValue.Value;

        switch (stringInputUi.Usage)
        {
            case StringInputUi.UsageType.FilePath:
            {
                if (!AssetRegistry.TryGetAsset(address, out var asset))
                {
                    Log.Warning($" Asset not found '{address}'");
                    break;
                }

                exportData.TryAddSharedAsset(asset);
                break;
            }
            case StringInputUi.UsageType.DirectoryPath:
            {
                //var relativeDirectory = stringValue.Value;
                //var isFolder = relativeDirectory.EndsWith('/');

                if (!AssetRegistry.TryResolveAddress(address, parent, out var absoluteDirectory, out var package, isFolder: true))
                {
                    Log.Warning($" Directory '{address}' was not found in any resource folder");
                    break;
                }

                // if (package == null)
                // {
                //     Log.Warning($"Directory '{address}' can't be exported without a package");
                //     break;
                // }

                Log.Debug($"Export all files in folder {absoluteDirectory}...");
                foreach (var absolutePath in Directory.EnumerateFiles(absoluteDirectory, "*", SearchOption.AllDirectories))
                {
                    var relativePathInResourceFolder = Path.GetRelativePath(package.AssetsFolder, absolutePath);

                    exportData.TryAddExportAsset(new AssetExportItem(package.RootNamespace,
                                                                     relativePathInResourceFolder,
                                                                     absolutePath));
                }

                break;
            }
            case StringInputUi.UsageType.Default:
            case StringInputUi.UsageType.Multiline:
            case StringInputUi.UsageType.CustomDropdown:
            default:
                break;
        }
    }

    private static bool TryExportSettings(string exportDir, Symbol symbol, out string reason)
    {
        reason = string.Empty;

        // Update project settings
        var exportSettings = new ExportSettings(OperatorId: symbol.Id,
                                                ApplicationTitle: symbol.Name,
                                                WindowMode: ProjectSettings.Config.DefaultWindowMode,
                                                ConfigData: ProjectSettings.Config,
                                                Author: symbol.SymbolPackage.AssemblyInformation?.Name ?? string.Empty, // todo - actual author name
                                                BuildId: Guid.NewGuid(),
                                                EditorVersion: Program.VersionText);

        const string exportSettingsFile = "exportSettings.json";
        if (JsonUtils.TrySaveJson(exportSettings, Path.Combine(exportDir, exportSettingsFile)))
            return true;

        reason = $"Failed to save export settings to {exportSettingsFile}";
        return false;
    }

    private static bool KeepCopyOfExportDir(out string reason, string exportDir)
    {
        try
        {
            if (Directory.Exists(exportDir))
            {
                Directory.Move(exportDir, exportDir + '_' + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            }
        }
        catch (Exception e)
        {
            reason = $"Failed to move export dir: {exportDir} ({e.Message}). Please close all files and File Explorer windows.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}