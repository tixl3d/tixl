#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using T3.Core.Compilation;
using T3.Core.DataTypes;
using T3.Core.IO;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Core.UserData;
using T3.Editor.Compilation;
using T3.Editor.Gui;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.UiHelpers;
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

            
        // traverse starting at output and collect everything
        var exportInfo = new ExportInfo();
        exportInfo.TryAddSymbol(symbol);

        exportDir = Path.Combine(UserSettings.Config.ProjectsFolder, FileLocations.ExportFolderName, childUi.SymbolChild.ReadableName);

        try
        {
            if(Directory.Exists(exportDir))
            {
                Directory.Move(exportDir, exportDir + '_' + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            }
        }
        catch (Exception e)
        {
            reason = $"Failed to move export dir: {exportDir}. Exception: {e}";
            return false;
        }

        Directory.CreateDirectory(exportDir);

        var operatorDir = Path.Combine(exportDir, "Operators");
        Directory.CreateDirectory(operatorDir);

        // copy assemblies into export dir
        // get symbol packages directly used by the exported symbols
            
        if (!TryExportPackages(out reason, exportInfo.SymbolPackages, operatorDir))
            return false;

        // Copy referenced resources
        RecursivelyCollectExportData(output, exportInfo);
        exportInfo.PrintInfo();

        var resourceDir = Path.Combine(exportDir, FileLocations.ResourcesSubfolder);
        Directory.CreateDirectory(resourceDir);

        if (TryFindSoundtrack(exportedInstance, symbol, out var file, out var relativePath))
        {
            var fileInfo = file.FileInfo;
            if (fileInfo is null || !fileInfo.Exists)
            {
                reason = $"Soundtrack file does not exist: {fileInfo?.FullName}";
                return false;
            }

            var absolutePath = fileInfo.FullName;

            // todo - determine if a path is relative or not even if it's "rooted" with an alias (for cross-platform)
            if (Path.IsPathFullyQualified(relativePath))
            {
                reason = $"Soundtrack path is not relative: \"{relativePath}\"";
                return false;
            }

            var newPath = Path.Combine(resourceDir, relativePath);
            if (!TryCopyFile(absolutePath, newPath))
            {
                reason = $"Failed to copy soundtrack from \"{absolutePath}\" to \"{newPath}\"";
                return false;
            }
        }
        else
        {
            const string yes = "Yes";
            var choice = BlockingWindow.Instance.ShowMessageBox("No defined soundtrack found. Continue with export?", "No soundtrack", yes, "No, cancel export");
                
            if (choice != yes)
            {
                reason = $"Failed to find soundTrack for [{symbol.Name}] - export cancelled, see log for details";
                return false;
            }
        }

        if(!TryCopyDirectory(SharedResources.Directory, resourceDir, out reason))
            return false;
            
        var playerDirectory = Path.Combine(RuntimeAssemblies.CoreDirectory, "Player");
        if(!TryCopyDirectory(playerDirectory, exportDir, out reason))
            return false;

        if (!TryCopyFiles(exportInfo.ResourcePaths, resourceDir))
        {
            reason = "Failed to copy resource files - see log for details";
            return false;
        }

        // Update project settings
        var exportSettings = new ExportSettings(OperatorId: symbol.Id, 
                                                ApplicationTitle: symbol.Name, 
                                                WindowMode: WindowMode.Fullscreen, 
                                                ConfigData: ProjectSettings.Config,
                                                Author: symbol.SymbolPackage.AssemblyInformation?.Name ?? string.Empty, // todo - actual author name
                                                BuildId: Guid.NewGuid(),
                                                EditorVersion: Program.VersionText);
            
        const string exportSettingsFile = "exportSettings.json";
        if(!JsonUtils.TrySaveJson(exportSettings, Path.Combine(exportDir, exportSettingsFile)))
        {
            reason = $"Failed to save export settings to {exportSettingsFile}";
            return false;
        }

        reason = "Exported successfully to " + exportDir;
        return true;
    }

    // todo - can we handle resource references here too?
    private static bool TryExportPackages(out string reason, IEnumerable<SymbolPackage> symbolPackages, string operatorDir)
    {
        // note: I think this is only intended to export dll files? if so, this should make use of TixlAssemblyLoadContexts instead to get specific dlls in use
        string[] excludeSubdirectories = [FileLocations.SymbolUiSubFolder, FileLocations.SourceCodeSubFolder, ".git", FileLocations.ResourcesSubfolder];
        foreach (var package in symbolPackages)
        {
            Log.Debug($"Exporting package {package.AssemblyInformation?.Name}");
            var packageName = package.AssemblyInformation?.Name;
            if (packageName == null)
            {
                Log.Warning(" Skipping unnamed package " + package);
                continue;
            }
            
            
            var targetDirectory = Path.Combine(operatorDir, packageName);
            _ = Directory.CreateDirectory(targetDirectory);
            if (package is EditableSymbolProject project)
            {
                project.SaveModifiedSymbols();
                if (!project.CsProjectFile.TryCompileRelease(false))
                {
                    reason = $"Failed to compile project \"{packageName}\"";
                    return false;
                }
                
                // copy the resulting directory into the target directory
                var sourceDir = project.CsProjectFile.GetBuildTargetDirectory(CsProjectFile.PlayerBuildMode);
                
                // copy contents recursively into the target directory
                if (!TryCopyDirectory(sourceDir, targetDirectory, out reason, excludeSubdirectories))
                    return false;
            }
            else
            {
                // Copy full directory into target directory recursively, maintaining folder layout
                var directoryToCopy = package?.AssemblyInformation?.Directory;
                if (directoryToCopy == null)
                {
                    reason = "invalid package AssemblyInformation";
                    return false;
                }
                
                if (!TryCopyDirectory(directoryToCopy, targetDirectory, out reason, excludeSubdirectories))
                    return false;
            }
        }
            
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Recursively copies a directory to a target directory, excluding specified subfolders, files, and file extensions.
    /// </summary>
    /// <param name="directoryToCopy"></param>
    /// <param name="targetDirectory"></param>
    /// <param name="reason"></param>
    /// <param name="excludeSubFolders"></param>
    /// <param name="excludeFiles"></param>
    /// <param name="excludeFileExtensions"></param>
    /// <returns></returns>
    private static bool TryCopyDirectory(string directoryToCopy, string targetDirectory, out string reason, string[]? excludeSubFolders = null, string[]? excludeFiles = null, string[]? excludeFileExtensions = null)
    {
        try
        {
            var rootFiles = Directory.EnumerateFiles(directoryToCopy, "*", SearchOption.TopDirectoryOnly);
            var subfolderFiles = Directory.EnumerateDirectories(directoryToCopy, "*", SearchOption.TopDirectoryOnly)
                                          .Where(subDir =>
                                                 {
                                                     if(excludeSubFolders == null)
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
                    foreach(var extension in excludeFileExtensions!)
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

    private readonly struct ResourcePath(string relativePath, string absolutePath)
    {
        public readonly string RelativePath = relativePath;
        public readonly string AbsolutePath = absolutePath;
            
        // equality operators
        public static bool operator ==(ResourcePath left, ResourcePath right) => left.AbsolutePath == right.AbsolutePath;
        public static bool operator !=(ResourcePath left, ResourcePath right) => left.AbsolutePath != right.AbsolutePath;
        public override int GetHashCode() => AbsolutePath.GetHashCode();
        public override bool Equals(object? obj) => obj is ResourcePath other && other == this;
            
        public override string ToString() => $"\"{RelativePath}\" (\"{AbsolutePath}\")";
    }

    private static bool TryCopyFiles(IEnumerable<ResourcePath> resourcePaths, string targetDir)
    {
        var successInt = Convert.ToInt32(true);
        resourcePaths
           .AsParallel()
           .ForAll(resourcePath =>
                   {
                       var targetPath = Path.Combine(targetDir, resourcePath.RelativePath);
                       var success = TryCopyFile(resourcePath.AbsolutePath, targetPath);

                       // Check for success
                       Interlocked.And(ref successInt, Convert.ToInt32(success));
                       if (!success)
                       {
                           Log.Error($"Failed to copy resource file for export: {resourcePath.AbsolutePath}");
                       }
                   });

        return Convert.ToBoolean(successInt);
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

    private static void RecursivelyCollectExportData(ISlot slot, ExportInfo exportInfo)
    {
        var gotConnection = slot.TryGetFirstConnection(out var firstConnection);
        if (slot is IInputSlot)
        {
            if (gotConnection)
            {
                RecursivelyCollectExportData(firstConnection, exportInfo);
            }

            CheckInputForResourcePath(slot, exportInfo);
            return;
        }

        if (gotConnection)
        {
            // slot is an output of an composition op
            RecursivelyCollectExportData(firstConnection, exportInfo);
            exportInfo.TryAddInstance(slot.Parent);
            return;
        }

        var parent = slot.Parent;

        if (!exportInfo.TryAddInstance(parent))
            return; // already visited

        foreach (var input in parent.Inputs)
        {
            CheckInputForResourcePath(input, exportInfo);

            if (!input.HasInputConnections)
                continue;

            if (input.TryGetAsMultiInput(out var multiInput))
            {
                foreach (var entry in multiInput.GetCollectedInputs())
                {
                    RecursivelyCollectExportData(entry, exportInfo);
                }
            }
            else if (input.TryGetFirstConnection(out var inputsFirstConnection))
            {
                RecursivelyCollectExportData(inputsFirstConnection, exportInfo);
            }
        }
    }

    private static bool TryFindSoundtrack(Instance instance, Symbol symbol, [NotNullWhen(true)] out FileResource? file, [NotNullWhen(true)] out string? relativePath)
    {
        var playbackSettings = symbol.PlaybackSettings;
        if (playbackSettings?.GetMainSoundtrack(instance, out var soundtrack) is not true)
        {
            if (PlaybackUtils.TryFindingSoundtrack(out soundtrack, out _))
            {
                Log.Warning($"You should define soundtracks withing the exported operators. Falling back to {soundtrack.Clip.FilePath} set in parent...");
            }
            else
            {
                file = null;
                relativePath = null;
                return false;
            }

            Log.Debug("No soundtrack defined within operator.");
        }

        relativePath = soundtrack.Clip.FilePath;
        return FileResource.TryGetFileResource(soundtrack.Clip.FilePath, instance, out file);
    }

    private static void CheckInputForResourcePath(ISlot inputSlot, ExportInfo exportInfo)
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

        switch (stringInputUi.Usage)
        {
            case StringInputUi.UsageType.FilePath:
            {
                var relativePath = stringValue.Value;
                exportInfo.TryAddSharedResource(relativePath, parent.AvailableResourcePackages);
                break;
            }
            case StringInputUi.UsageType.DirectoryPath:
            {
                var relativeDirectory = stringValue.Value;

                if (!ResourceManager.TryResolvePath(relativeDirectory, parent, out var absoluteDirectory, out _))
                {
                    Log.Warning($"Directory '{relativeDirectory}' was not found in any resource folder");
                }

                Log.Debug($"Export all entries folder {absoluteDirectory}...");
                var rootDirectory = absoluteDirectory.Replace(relativeDirectory, string.Empty);
                foreach (var absolutePath in Directory.EnumerateFiles(absoluteDirectory, "*", SearchOption.AllDirectories))
                {
                    var relativePath = absolutePath.Replace(rootDirectory, string.Empty);
                    exportInfo.TryAddResourcePath(new ResourcePath(relativePath, absolutePath));
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
}