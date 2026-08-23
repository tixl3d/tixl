#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Audio;
using T3.Core.Compilation;
using T3.Core.DataTypes;
using T3.Core.IO;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Settings;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.SystemUi;
using T3.Editor.Compilation;
using T3.Editor.Gui;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using T3.Editor.Gui.Interaction.Timing;
using T3.Serialization;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;

namespace T3.Editor.UiModel.Exporting;

/// <summary>
/// Builds a standalone player folder for one operator: the reachable part of its graph, the assets it references,
/// the operator packages (pruned to what those operators need) and the player runtime.
/// </summary>
internal static partial class PlayerExporter
{
    /// <summary>
    /// Exports and reports the outcome to the user (message box, log, export folder opened on success).
    /// </summary>
    public static void ExportAndReport(Instance composition, SymbolUi.Child childUi)
    {
        var exportName = childUi.SymbolChild.ReadableName;
        if (TryExportInstance(composition, childUi, out var reason, out var exportDir))
        {
            Log.Info(reason);
            BlockingWindow.Instance.ShowMessageBox(reason, $"Exported {exportName} successfully!");
            CoreUi.Instance.OpenWithDefaultApplication(exportDir);
        }
        else
        {
            Log.Error(reason);
            BlockingWindow.Instance.ShowMessageBox(reason, $"Failed to export {exportName}");
        }
    }

    /// <summary>
    /// Where <see cref="TryExportInstance"/> writes the export of the given child.
    /// </summary>
    public static string GetExportDirectory(Instance composition, SymbolUi.Child childUi)
    {
        return Path.Combine(composition.Symbol.SymbolPackage.Folder, FileLocations.ExportSubFolder, childUi.SymbolChild.ReadableName);
    }

    public static bool TryExportInstance(Instance composition, SymbolUi.Child childUi, out string reason, out string exportDir)
    {
        T3Ui.Save(false);

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

        // The exported op's own settings win over inherited ones: they are what the Executable panel edits.
        var exportConfig = symbol.CompositionSettings?.Export ?? CompositionSettings.Current.Export;
        var exportData = new ExportData(symbol);

        // Traverse starting at output and collect everything that can evaluate in the player
        RecursivelyCollectExportData(output, exportData);
        CollectAutoCollectedOps(exportedInstance, exportData);
        exportData.FinishCollection(exportConfig.StripUnusedOperators);

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
            var choice = BlockingWindow.Instance.ShowMessageBox("""
                                                                No main soundtrack found.

                                                                The exported executable uses the main soundtrack for its runtime duration (when to end or loop) and for audio analysis. To define one, set the Display parameter of an [AudioClip] inside the exported operator to 'Background image'.

                                                                Continue export without a soundtrack?
                                                                """,
                                                                "No soundtrack", yes,
                                                                "No, cancel export");

            if (choice != yes)
            {
                reason = $"Failed to find soundTrack for [{symbol.Name}] - export cancelled, see log for details";
                exportDir = string.Empty;
                return false;
            }
        }

        // Include implicitly shared assets
        foreach (var shared in (string[]) [
                         "Lib:shaders/dx11/resolve-multisampled-depth-buffer-cs.hlsl",
                         "Lib:pbr/studio_small_08-prefiltered.dds",
                         "Lib:pbr/BRDF-LookUp.dds",
                     ])
        {
            if (AssetRegistry.TryGetAsset(shared, out var sharedAsset))
            {
                exportData.TryAddSharedAsset(sharedAsset);
            }
            else
            {
                Log.Warning("Can't resolved shared asset " + shared);
            }
        }

        exportData.PrintInfo();

        exportDir = GetExportDirectory(composition, childUi);

        if (!TryRemoveExistingExportDir(out reason, exportDir))
            return false;

        Directory.CreateDirectory(exportDir);

        var operatorDir = Path.Combine(exportDir, FileLocations.OperatorsSubFolder);
        Directory.CreateDirectory(operatorDir);

        var report = new CopyReport();
        var dependencyFilter = new DependencyFileFilter(exportData);

        // Copy assemblies into export dir. Get symbol packages directly used by the exported symbols
        if (!TryExportSymbolPackages(out reason, exportData, operatorDir, dependencyFilter, report))
            return false;

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
        if (!TryCopyDirectory(SharedResources.EditorResourcesDirectory, editorResourcesTargetDir, out reason, report))
            return false;

        // Copy the player runtime without the optional dependencies no exported operator declares
        var playerDirectory = Path.Combine(FileLocations.StartFolder, "Player");
        if (!TryCopyDirectory(playerDirectory, exportDir, out reason, report, shouldExcludeFile: dependencyFilter.ShouldExcludeFile))
            return false;

        var title = string.IsNullOrWhiteSpace(exportConfig.Title) ? symbol.Name : exportConfig.Title.Trim();
        var author = string.IsNullOrWhiteSpace(exportConfig.Author)
                         ? symbol.SymbolPackage.AssemblyInformation?.Name ?? string.Empty
                         : exportConfig.Author.Trim();

        if (!TryExportSettings(exportDir, symbol, exportConfig, title, author, out reason))
            return false;

        RenamePlayerExecutable(exportDir, title);

        // Ship the bytecode of every shader the editor compiled for the exported graph, so the player starts warm
        var seededShaders = ShaderCompiler.ExportCacheEntries(exportData.CollectedInstances,
                                                              Path.Combine(exportDir, FileLocations.ShaderCacheSubFolder));
        Log.Info($"Exported {seededShaders} precompiled shaders.");

        Log.Info($"Export copied {report.CopiedCount} files ({FormatBytes(report.CopiedBytes)}), " +
                 $"skipped {report.SkippedCount} files ({FormatBytes(report.SkippedBytes)}).");
        if (dependencyFilter.ExcludedPatterns.Count > 0)
        {
            Log.Debug("Skipped optional dependencies: " + string.Join(", ", dependencyFilter.ExcludedPatterns));
        }

        reason = "Exported successfully to " + exportDir;
        return true;
    }

    /// <summary>
    /// Compile EditableProjects and copy read only projects, pruned to the symbols and dependencies in use.
    /// </summary>
    private static bool TryExportSymbolPackages(out string reason, ExportData exportData, string operatorDir,
                                                DependencyFileFilter dependencyFilter, CopyReport report)
    {
        var excludeSubdirectories = new List<string>
                                        {
                                            ".git",
                                            FileLocations.SymbolUiSubFolder,
                                            FileLocations.SourceCodeSubFolder,
                                            FileLocations.ExportSubFolder,
                                            FileLocations.AssetsSubfolder, // Assets are filtered by referencing address and copied separately
                                        };

        // Stripped exports rewrite the symbol files instead of copying them
        if (exportData.StripsUnusedOperators)
            excludeSubdirectories.Add(FileLocations.ReleaseSymbolsSubfolder);

        var excludeSubdirectoryArray = excludeSubdirectories.ToArray();

        foreach (var package in exportData.SymbolPackages)
        {
            Log.Debug($"Exporting package {package.Name}...");
            var packageName = package.Name;
            var targetDirectory = Path.Combine(operatorDir, packageName);
            Directory.CreateDirectory(targetDirectory);

            string sourceDir;
            if (package is EditableSymbolProject project)
            {
                project.SaveModifiedSymbols();
                if (!project.CsProjectFile.TryCompileRelease(false, out var failureLog))
                {
                    reason = $"Failed to compile project \"{packageName}\" - \n{failureLog}";
                    return false;
                }

                sourceDir = project.CsProjectFile.GetBuildTargetDirectory(CsProjectFile.PlayerBuildMode);
            }
            else
            {
                sourceDir = package.AssemblyInformation.Directory;
            }

            if (!TryCopyDirectory(sourceDir, targetDirectory, out reason, report, excludeSubdirectoryArray,
                                  relativePath => dependencyFilter.ShouldExcludeFile(relativePath)
                                                  || IsForeignRuntimeFile(relativePath)
                                                  || IsNestedExportFile(relativePath)))
                return false;

            if (exportData.StripsUnusedOperators && !TryWriteStrippedSymbolFiles(package, exportData, targetDirectory, out reason))
                return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Writes the package's used symbols with unreachable children removed into the export's Symbols folder.
    /// </summary>
    private static bool TryWriteStrippedSymbolFiles(SymbolPackage package, ExportData exportData, string targetDirectory, out string reason)
    {
        reason = string.Empty;
        if (package is not EditorSymbolPackage editorPackage)
        {
            reason = $"Can't locate symbol files of package {package.Name}";
            return false;
        }

        var symbolsTargetDir = Path.Combine(targetDirectory, FileLocations.ReleaseSymbolsSubfolder);
        var removedChildren = 0;
        foreach (var symbol in exportData.GetSymbolsOfPackage(package))
        {
            if (!editorPackage.TryGetSymbolFilePath(symbol, out var sourcePath))
            {
                reason = $"Can't locate symbol file of [{symbol.Name}] in package {package.Name}";
                return false;
            }

            var relativePath = Path.GetRelativePath(package.Folder, sourcePath);
            if (relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                relativePath = symbol.Id + SymbolPackage.SymbolExtension;
            }
            else if (relativePath.StartsWith(FileLocations.ReleaseSymbolsSubfolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath[(FileLocations.ReleaseSymbolsSubfolder.Length + 1)..];
            }

            var targetPath = Path.Combine(symbolsTargetDir, relativePath);
            var keptChildren = exportData.GetReachableChildIds(symbol);
            if (!SymbolJson.TryWriteFilteredSymbolFile(sourcePath, targetPath, keptChildren, out var removedCount))
            {
                reason = $"Failed to write symbol file for [{symbol.Name}]";
                return false;
            }

            if (removedCount > 0)
            {
                removedChildren += removedCount;
                Log.Debug($"  [{symbol.Name}]: stripped {removedCount} unused child operators");
            }
        }

        if (removedChildren > 0)
            Log.Info($"{package.Name}: stripped {removedChildren} unused child operators");

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
    /// Projects created before the csproj excluded the Export folder copy earlier exports into their build output
    /// (Symbols/Export/..., SourceCode/Export/...), which would register every symbol twice in the player.
    /// </summary>
    private static bool IsNestedExportFile(string relativePath)
    {
        var exportSegment = Path.DirectorySeparatorChar + FileLocations.ExportSubFolder + Path.DirectorySeparatorChar;
        return relativePath.Contains(exportSegment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The player runs on win-x64 only; native libraries for other runtime identifiers are dead weight.
    /// </summary>
    private static bool IsForeignRuntimeFile(string relativePath)
    {
        const string runtimesFolder = "runtimes";
        if (!relativePath.StartsWith(runtimesFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith(runtimesFolder + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;

        var ridStart = runtimesFolder.Length + 1;
        var ridEnd = relativePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], ridStart);
        if (ridEnd < 0)
            return false;

        var rid = relativePath.AsSpan(ridStart, ridEnd - ridStart);
        return !rid.Equals("win-x64", StringComparison.OrdinalIgnoreCase)
               && !rid.Equals("win", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Recursively copies a directory to a target directory, excluding specified subfolders and files.
    /// </summary>
    private static bool TryCopyDirectory(string directoryToCopy, string targetDirectory, out string reason, CopyReport report,
                                         string[]? excludeSubFolders = null, Func<string, bool>? shouldExcludeFile = null)
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
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(directoryToCopy, file);
                if (shouldExcludeFile != null && shouldExcludeFile(relativePath))
                {
                    report.Skip(file);
                    continue;
                }

                var targetPath = Path.Combine(targetDirectory, relativePath);
                var targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir == null)
                {
                    reason = $"Failed to get directory for \"{targetPath}\" - is it missing a file extension?";
                    return false;
                }

                Directory.CreateDirectory(targetDir);
                File.Copy(file, targetPath, true);
                report.Copy(file);
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

    /// <summary>
    /// The player's render loop evaluates some direct children of the exported op without an output connection:
    /// auto-playing audio clips (<see cref="AudioClipCollector"/>) and loose audio sources
    /// (<see cref="AudioGraphCollector"/>). Include them and whatever feeds them.
    /// </summary>
    private static void CollectAutoCollectedOps(Instance exportedInstance, ExportData exportData)
    {
        foreach (var child in exportedInstance.Children.Values)
        {
            if (child is not (IAudioClipProvider or IAudioSource))
                continue;

            foreach (var childOutput in child.Outputs)
            {
                RecursivelyCollectExportData(childOutput, exportData);
            }

            // Ops without outputs are still collected
            exportData.TryAddInstance(child);
        }
    }

    private static bool TryFindSoundtrack(Instance instance, Symbol symbol,
                                          [NotNullWhen(true)] out string? address)
    {
        var playbackSettings = symbol.CompositionSettings;
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
                Log.Warning($"You should define soundtracks withing the exported operators. Falling back to {soundtrack.Clip.AssetPath} set in parent...");
            }
            else
            {
                address = null;
                return false;
            }

            Log.Debug("No soundtrack defined within operator.");
        }

        address = soundtrack.Clip.AssetPath;
        return FileResource.TryGetFileResource(soundtrack.Clip.AssetPath, instance, out _);
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
                if (!AssetRegistry.TryResolveAddress(address, parent, out var absoluteDirectory, out var package, isFolder: true))
                {
                    Log.Warning($" Directory '{address}' was not found in any resource folder");
                    break;
                }

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

    /// <summary>
    /// Renames the copied Player.exe after the export title. Safe because the apphost carries the
    /// path to Player.dll baked in at build time - only the .exe gets a new name.
    /// </summary>
    private static void RenamePlayerExecutable(string exportDir, string title)
    {
        var exeName = string.Join("_", title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrEmpty(exeName) || exeName.Equals("Player", StringComparison.OrdinalIgnoreCase))
            return;

        var sourcePath = Path.Combine(exportDir, "Player.exe");
        var targetPath = Path.Combine(exportDir, exeName + ".exe");
        try
        {
            File.Move(sourcePath, targetPath, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to rename Player.exe to {exeName}.exe: {e.Message}");
        }
    }

    private static bool TryExportSettings(string exportDir, Symbol symbol, CompositionSettings.ExportConfig exportConfig, string title, string author,
                                          out string reason)
    {
        reason = string.Empty;

        // Fresh defaults instead of the editor's live config: everything else in ConfigData is machine-specific
        // (input device names, MIDI capture limits) or debug state (log flags, mute/volume, DX debug) that must
        // not leak into an export running on someone else's machine.
        var configData = new CoreSettings.ConfigData
                             {
                                 DefaultOscPort = CoreSettings.Config.DefaultOscPort,
                                 TimeClipSuspending = CoreSettings.Config.TimeClipSuspending,
                             };

        var exportSettings = new ExportSettings(OperatorId: symbol.Id,
                                                ApplicationTitle: title,
                                                Author: author,
                                                BuildId: Guid.NewGuid(),
                                                EditorVersion: Program.VersionText,
                                                Export: exportConfig,
                                                ConfigData: configData);

        if (JsonUtils.TrySaveJson(exportSettings, Path.Combine(exportDir, ExportSettings.FileName)))
            return true;

        reason = $"Failed to save export settings to {ExportSettings.FileName}";
        return false;
    }

    private static bool TryRemoveExistingExportDir(out string reason, string exportDir)
    {
        try
        {
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }
        }
        catch (Exception e)
        {
            reason = $"Failed to remove export dir: {exportDir} ({e.Message}). Please close all files and File Explorer windows.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string FormatBytes(long bytes)
    {
        return bytes >= 1024 * 1024
                   ? $"{bytes / (1024.0 * 1024.0):0.0} MB"
                   : $"{bytes / 1024.0:0.0} KB";
    }
}
