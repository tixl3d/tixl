#nullable enable
using System.Diagnostics;
using System.IO;
using System.Text;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Core.UserData;
using T3.Core.Utils;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Interaction.StartupCheck;

internal static class ConformAssetPaths
{
    /// <summary>
    /// Should be called before loading assets
    /// </summary>
    /// <returns>True if something was moved</returns>
    public static bool RenameResourcesToAssets(SymbolPackage package)
    {
        var oldPath = Path.Combine(package.Folder, FileLocations.LegacyResourcesSubfolder);
        var newPath = Path.Combine(package.Folder, FileLocations.AssetsSubfolder);

        if (!Directory.Exists(oldPath))
            return false;

        try
        {
            // 1. Physical Move/Merge
            if (Directory.Exists(newPath))
            {
                MoveFilesRecursively(oldPath, newPath);
                Directory.Delete(oldPath, true);
            }
            else
            {
                Directory.Move(oldPath, newPath);
            }

            // 2. Patch the .csproj file
            UpdateCsprojFile(package.Folder);
            Log.Info($"Migrated {package.Name}: Resources -> Assets (Folder and Project updated)");
        }
        catch (Exception e)
        {
            Log.Error($"Migration failed for {package.Name}: {e.Message}");
        }

        return true;
    }

    private static void MoveFilesRecursively(string sourcePath, string targetPath)
    {
        var sourceDi = new DirectoryInfo(sourcePath);
        Directory.CreateDirectory(targetPath);

        foreach (var file in sourceDi.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file.FullName);
            var targetFile = Path.Combine(targetPath, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            // Use true to overwrite if a file with the same name exists in the target
            file.MoveTo(targetFile, true);
        }
    }

    private static void UpdateCsprojFile(string packageFolder)
    {
        var projectFiles = Directory.GetFiles(packageFolder, "*.csproj");
        foreach (var projFile in projectFiles)
        {
            var content = File.ReadAllText(projFile);

            // This targets both the Include and Link attributes in your ItemGroup
            var updatedContent = content
                                .Replace("Include=\"Resources/", "Include=\"Assets/")
                                .Replace("Include=\"Resources\\", "Include=\"Assets\\")
                                .Replace("<Link>Resources/", "<Link>Assets/");

            if (content != updatedContent)
            {
                File.WriteAllText(projFile, updatedContent, Encoding.UTF8);
                Log.Debug($"Updated project file: {Path.GetFileName(projFile)}");
            }
        }
    }

    /// <summary>
    /// Validates and updates the asset-paths of all loaded symbols 
    /// </summary>
    /// <remarks>
    ///
    ///        Sadly, for legacy projects, this could be...
    ///         either a project a subfolder in the t3 `Resources/`:
    ///              Old: Resources\fonts\Roboto-Black.fnt
    ///               in: .\tixl\Resources\fonts\Roboto-Black.fnt  
    ///              New: tixl.lib:fonts/Roboto-Black.fnt
    ///               in: .\tixl\Operators\Lib\Assets\fonts\Roboto-Black.fnt
    ///                        
    ///         a local package sub folder:
    ///              Old: Resources/images/basic/white-pixel.png 
    ///               in .\tixl\Operators\Lib\Resources\images\basic\white-pixel.png 
    ///              New: tixl.lib:images/basic/white-pixel.png
    ///               in .\tixl\Operators\Lib\Assets\images\basic\white-pixel.png 
    ///         
    ///         Or a package(!) with shared resource:
    ///              Old: Resources/lib/img/fx/Default2-vs.hlsl 
    ///               in .\tixl\Operators\Lib\Resources\img\fx\Default2-vs.hlsl 
    ///              New: tixl.lib:img/fx/Default2-vs.hlsl
    ///               in .\tixl\Operators\Lib\Assets\shaders\img\fx\Default2-vs.hlsl 
    ///
    /// </remarks>
    internal static void ConformAllPaths()
    {
        //BuildAssetIndex();
        Log.Debug("Updating asset references...");

        foreach (var package in SymbolPackage.AllPackages)
        {
            foreach (var symbol in package.Symbols.Values)
            {
                // Symbol Defaults
                SymbolUi? symbolUi = null;
                foreach (var inputDef in symbol.InputDefinitions)
                {
                    if (inputDef.ValueType != typeof(string))
                        continue;

                    symbolUi ??= symbol.GetSymbolUi();
                    if (!symbolUi.InputUis.TryGetValue(inputDef.Id, out var inputUi))
                        continue;

                    if (inputDef.DefaultValue is not InputValue<string> stringValue)
                        continue;

                    ProcessStringInputUi(inputUi, stringValue, symbol);
                }

                // Symbol children
                foreach (var child in symbol.Children.Values)
                {
                    foreach (var input in child.Inputs.Values)
                    {
                        if (input.IsDefault)
                            continue;

                        if (input.InputDefinition.ValueType != typeof(string))
                            continue;

                        if (input.Value is not InputValue<string> stringValue)
                            continue;

                        if (string.IsNullOrEmpty(stringValue.Value))
                            continue;

                        if (!SymbolUiRegistry.TryGetSymbolUi(child.Symbol.Id, out var childSymbolUi))
                            continue;

                        if (!childSymbolUi.InputUis.TryGetValue(input.Id, out var inputUi))
                            continue;

                        ProcessStringInputUi(inputUi, stringValue, symbol, child);
                    }
                }
            }
        }
    }

    private static void ProcessStringInputUi(IInputUi inputUi, InputValue<string> stringValue, Symbol symbol,
                                             Symbol.Child? symbolChild = null)
    {
        if (inputUi is not StringInputUi stringUi)
            return;

        switch (stringUi.Usage)
        {
            case StringInputUi.UsageType.FilePath:
            {
                if (TryMigrateFilterString(stringUi.FileFilter, out var migratedFilter))
                {
                    stringUi.FileFilter = migratedFilter;
                    symbol.GetSymbolUi().FlagAsModified();
                }

                if (TryConvertResourcePathFuzzy(stringValue.Value, symbol, out var converted, out var asset))
                {
                    Log.Debug($"Migrated asset reference for {symbol}.{inputUi.InputDefinition.Name}: {stringValue.Value} -> {converted}");
                    stringValue.Value = converted;
                }

                if (asset != null)
                {
                    // register default value reference
                    if (symbolChild != null)
                    {
                        Debug.Assert(symbolChild.Parent != null);
                        AssetRegistry.AddAssetReference(asset, symbolChild.Parent.Id, symbolChild.Id, stringUi.Id);
                    }
                    else
                    {
                        AssetRegistry.AddAssetReference(asset, symbol.Id, Guid.Empty, stringUi.Id);
                    }
                }
                break;
            }
            case StringInputUi.UsageType.DirectoryPath:
                if (TryConvertResourceFolderPath(stringValue.Value, symbol, out var convertedFolderPath))
                {
                    Log.Debug($"{symbol}.{inputUi.InputDefinition.Name} Folder:  {symbol.SymbolPackage.Name}: {stringValue.Value} -> {convertedFolderPath}");
                    stringValue.Value = convertedFolderPath;
                }

                if (!AssetRegistry.TryResolveAddress(stringValue.Value, null, out var absolutePath, out _, isFolder: true)
                    && !string.IsNullOrEmpty(stringValue.Value))
                {
                    Log.Warning(symbolChild == null
                                    ? $"Can't find default asset folder for: {symbol}.{inputUi.InputDefinition.Name}:  {stringValue.Value} => '{absolutePath}'"
                                    : $"Can't find asset folder: {symbolChild.Parent?.Name} / {symbolChild.Symbol.Name}.{inputUi.InputDefinition.Name}: {stringValue.Value} => '{absolutePath}'");
                }

                return;
        }
    }

    /// <summary>
    /// Sadly, we can't use Path.IsPathRooted() because we the legacy filepaths also starts with "/"
    /// So we're testing for windows paths likes c: 
    /// </summary>
    /// 
    private static bool IsAbsoluteFilePath(string path)
    {
        var colon = path.IndexOf(':');
        return colon == 1;
    }

    private static bool TryConvertResourceFolderPath(string path, Symbol symbol, out string newPath)
    {
        path = path.Replace('\\', '/');
        newPath = path;

        if (string.IsNullOrWhiteSpace(path)) return false;

        var colon = path.IndexOf(':');
        if (colon != -1)
        {
            return false;
        }

        if (!path.EndsWith("/"))
        {
            path += '/';
        }

        var pathSpan = path.AsSpan();

        var firstSlash = pathSpan.IndexOf('/');
        if (firstSlash == -1)
        {
            newPath = $"{symbol.SymbolPackage.Name}:{newPath}";
            return true;
        }

        var isRooted = firstSlash == 0;
        var nonRooted = isRooted ? pathSpan[1..] : pathSpan;

        // Skip "Resources" prefix
        var resourcesPrefix = "Resources/";
        if (nonRooted.StartsWith(resourcesPrefix))
        {
            nonRooted = nonRooted[resourcesPrefix.Length..];
        }

        // Skip package name
        if (nonRooted.StartsWith(symbol.SymbolPackage.Name + "/"))
        {
            nonRooted = nonRooted[(symbol.SymbolPackage.Name.Length + 1)..];
        }

        newPath = $"{symbol.SymbolPackage.Name}:{nonRooted}";
        return true;
    }

    private static bool TryConvertResourcePathFuzzy(string path, Symbol symbol, out string newPath, out Asset? asset)
    {
        newPath = path;
        asset = null;
        
        if (string.IsNullOrWhiteSpace(path) || IsAbsoluteFilePath(path))
            return false;

        var separatorCount = path.Count(c => c == AssetRegistry.PackageSeparator);

        // If it's a valid address already, leave it be.
        if (separatorCount == 1 && AssetRegistry.TryGetAsset(path, out asset))
            return false;

        // Ignore URLs
        if (path.Contains("://"))
            return false;

        // Ignore wildcards...
        if (path.IndexOfAny(['{', '}', '*']) != -1)
            return false;

        string fileName;
        if (separatorCount > 0)
        {
            // Fix double-alias: take everything after the last colon
            var lastAddressIndex = path.LastIndexOf(AssetRegistry.PackageSeparator);
            fileName = path[(lastAddressIndex + 1)..];

            // If the remainder is a path, get just the filename for healing
            if (fileName.Contains('/'))
                fileName = Path.GetFileName(fileName);
        }
        else
        {
            fileName = Path.GetFileName(path);
        }

        if (TryFixAddressForFilename(fileName, path, symbol, out var healedAddress))
        {
            newPath = healedAddress;
            return true;
        }

        // Strip legacy prefixes
        var conformed = path.Replace("\\", "/").TrimStart('/');
        const string legacyPrefix = "Resources/";
        if (conformed.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            conformed = conformed[legacyPrefix.Length..];
        }

        if (separatorCount == 1)
        {
            var separator = conformed.IndexOf(AssetRegistry.PackageSeparator);
            conformed = conformed[(separator + 1)..];
        }

        newPath = $"{symbol.SymbolPackage.Name}:{conformed}";
        return !string.Equals(newPath, path, StringComparison.Ordinal);
    }

    private static bool TryFixAddressForFilename(string fileName, string originalAddress, Symbol symbol, out string matchedAddress)
    {
        matchedAddress = string.Empty;
        if (!AssetRegistry.TryGetAssetsForFilename(fileName, out var candidates))
        {
            return false;
        }

        if (candidates.Count == 1)
        {
            matchedAddress = candidates[0].Address;
            return true;
        }

        // Multiple matches found: Use scoring
        matchedAddress = candidates
                        .OrderBy(candidate => CalculateMatchScore(originalAddress, candidate.Address, symbol.SymbolPackage.Name))
                        .First()
                        .Address;
        return true;
    }
    

    private static float CalculateMatchScore(string legacyPath, string candidateAddress, string currentPackageName)
    {
        // 1. Heavy preference for the same package
        float score = candidateAddress.StartsWith(currentPackageName + ":") ? 0f : 100f;

        // 2. Levenshtein distance between the paths
        // We compare "Resources/images/helmet10.png" to "Lib:images/helmet10.png"
        score += StringUtils.LevenshteinDistance(legacyPath, candidateAddress);

        return score;
    }
    

    
    private static bool TryMigrateFilterString(string legacyFilter, out string migratedFilter)
    {
        migratedFilter = legacyFilter;

        // If it doesn't contain the Windows separator, it might already be migrated
        if (string.IsNullOrWhiteSpace(legacyFilter) || !legacyFilter.Contains('|'))
            return false;

        try
        {
            // Extract the extension pattern part (e.g., "*.mp4;*.mov")
            var parts = legacyFilter.Split('|');
            var patterns = parts.Length > 1 ? parts[1] : parts[0];

            // Clean up into a simple list: "mp4, mov"
            var extensions = patterns.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(ext => ext.Trim().TrimStart('*', '.'))
                                     .Where(ext => !string.Equals(ext, "*", StringComparison.Ordinal)) // Ignore wildcards
                                     .Distinct();

            migratedFilter = string.Join(", ", extensions);
            return !string.Equals(legacyFilter, migratedFilter, StringComparison.Ordinal);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to migrate filter string '{legacyFilter}': {e.Message}");
            return false;
        }
    }
}