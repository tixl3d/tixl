#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.UserData;
using T3.Core.Utils;

namespace T3.Core.Resource.Assets;

public static class AssetRegistry
{
    public static bool TryGetAsset(string? address, [NotNullWhen(true)] out Asset? asset)
    {
        if (address != null) 
            return _assetsByAddress.TryGetValue(address, out asset);
        
        asset = null;
        return false;
    }

    public static bool TryResolveAddress(string? address,
                                         IResourceConsumer? consumer,
                                         out string absolutePath,
                                         [NotNullWhen(true)] out IResourcePackage? resourceContainer,
                                         bool isFolder = false,
                                         bool logWarnings = false)
    {
        resourceContainer = null;
        absolutePath = string.Empty;

        if (string.IsNullOrWhiteSpace(address))
            return false;

        // 1. High-performance registry lookup
        if (TryGetAsset(address, out var asset))
        {
            if (asset.FileSystemInfo != null && asset.IsDirectory == isFolder)
            {
                absolutePath = asset.FileSystemInfo.FullName;
                resourceContainer = null;

                foreach (var c in ResourceManager.SharedResourcePackages)
                {
                    if (c.Id != asset.PackageId) continue;
                    resourceContainer = c;
                    break;
                }

                return resourceContainer != null;
            }
        }

        address.ToForwardSlashesUnsafe();
        var span = address.AsSpan();

        // 2. Fallback for internal editor resources
        if (span.StartsWith("./"))
        {
            absolutePath = Path.GetFullPath(address);
            if (!logWarnings)
                return false;

            if (consumer is Instance instance)
            {
                Log.Warning($"Can't resolve relative asset '{address}'", instance);
            }
            else
                Log.Warning($"Can't relative resolve asset '{address}'");

            return false;
        }

        var projectSeparator = address.IndexOf(PackageSeparator);

        // 3. Legacy windows absolute paths (e.g. C:/...)
        if (projectSeparator == 1)
        {
            absolutePath = address;
            return isFolder
                       ? Directory.Exists(absolutePath)
                       : File.Exists(absolutePath);
        }

        if (projectSeparator == -1)
        {
            if (logWarnings)
                Log.Warning($"Can't resolve asset '{address}'");

            return false;
        }

        // 4. Fallback search through packages
        var packageName = span[..projectSeparator];
        var localPath = span[(projectSeparator + 1)..];

        var packages = consumer?.AvailableResourcePackages ?? ResourceManager.SharedResourcePackages;
        if (packages.Count == 0)
        {
            if (logWarnings)
                Log.Warning($"Can't resolve asset '{address}' (no packages found)");

            return false;
        }

        foreach (var package in packages)
        {
            if (!package.Name.AsSpan().Equals(packageName, StringComparison.Ordinal))
                continue;

            resourceContainer = package;
            absolutePath = $"{package.AssetsFolder}/{localPath}";
            return isFolder
                       ? Directory.Exists(absolutePath)
                       : File.Exists(absolutePath);
        }

        return false;
    }

    public static bool TryToGetAssetFromFilepath(string absolutePath, bool isFolder, [NotNullWhen(true)] out Asset? asset)
    {
        asset = null;
        return TryConvertFilepathToAddress(absolutePath, isFolder, out var address)
               && _assetsByAddress.TryGetValue(address, out asset);
    }

    internal static bool TryConvertFilepathToAddress(string absolutePath, bool isFolder, [NotNullWhen(true)] out string? relativeAddress)
    {
        absolutePath.ToForwardSlashesUnsafe();
        foreach (var package in SymbolPackage.AllPackages)
        {
            var folder = package.AssetsFolder;
            if (absolutePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                var dirSuffix = isFolder ? "/" : string.Empty;
                    
                // Trim the folder length AND the following slash if it exists
                var relativePart = absolutePath[folder.Length..].TrimStart('/');
                relativeAddress = $"{package.Name}{PackageSeparator}{relativePart}{dirSuffix}";
                return true;
            }
        }

        relativeAddress = null;
        return false;
    }

    public static void RegisterAssetsFromPackage(SymbolPackage package)
    {
        var root = package.AssetsFolder;
        if (!Directory.Exists(root)) return;

        var di = new DirectoryInfo(root);

        RegisterPackageEntry(di, package, isDirectory: true);

        // Register all files
        foreach (var fileInfo in di.EnumerateFiles("*.*", SearchOption.AllDirectories))
        {
            if (FileLocations.IgnoredFiles.Contains(fileInfo.Name))
                continue;

            var asset = RegisterPackageEntry(fileInfo, package, false);

            // Collect all possible addresses for this filename
            var list = _assetsMatchingFilenames.GetOrAdd(fileInfo.Name, _ => []);
            lock (list)
            {
                if (!list.Contains(asset)) list.Add(asset);
            }
        }

        // Register all directories
        foreach (var dirInfo in di.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if (FileLocations.IgnoredFiles.Contains(dirInfo.Name))
                continue;

            RegisterPackageEntry(new FileInfo(dirInfo.FullName), package, true);
        }

        //Log.Debug($"{packageAlias}: Registered {_assetsByAddress.Count(a => a.Value.PackageId == packageId)} assets (including directories).");
    }

    public static Asset RegisterPackageEntry(FileSystemInfo info, IResourcePackage package, bool isDirectory)
    {
        info.Refresh();

        // If the info is the root itself, relative path is empty string
        var relativePath = Path.GetRelativePath(package.AssetsFolder, info.FullName).Replace("\\", "/");

        var isPackageFolder = relativePath == "."; 
        if (isPackageFolder) 
            relativePath = string.Empty;

        var dirSuffix = (isDirectory && !isPackageFolder) ? "/" : string.Empty; 
        
        var address = $"{package.Name}{PackageSeparator}{relativePath}{dirSuffix}";

        // Pre-calculate path parts
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathParts = new List<string>(parts.Length + 1) { package.Name };

        // Logic for folder structure
        var partCount = isDirectory ? parts.Length : parts.Length - 1;
        for (var i = 0; i < partCount; i++)
        {
            pathParts.Add(parts[i]);
        }

        AssetType.TryGetForFilePath(info.Name, out var assetType, out var extensionId);

        var asset = new Asset(address)
                        {
                            PackageId = package.Id,
                            FileSystemInfo = info,
                            AssetType = assetType,
                            IsDirectory = isDirectory,
                            PathParts = pathParts.ToArray(),
                            ExtensionId = extensionId,
                        };

        _assetsByAddress[address] = asset;
        return asset;
    }

    internal static void UnregisterPackage(Guid packageId)
    {
        var addressesToRemove = _assetsByAddress.Values
                                                .Where(a => a.PackageId == packageId)
                                                .ToList();

        foreach (var asset in addressesToRemove)
        {
            _assetsByAddress.TryRemove(asset.Address, out _);
            ReferencesForAssetId.Remove(asset.Id, out _);
        }
    }

    /// <summary>
    /// This will try to first create a localUrl, then a packageUrl,
    /// and finally fall back to an absolute path.
    ///
    /// This method is useful to test if path would be valid before before dropping and external file
    /// into the editor the asset is being registered...
    /// </summary>
    public static bool TryConstructAddressFromFilePath(string absolutePath,
                                                       Instance composition,
                                                       [NotNullWhen(true)] out string? address,
                                                       [NotNullWhen(true)] out IResourcePackage? package)
    {
        address = null;
        package = null;
        if (string.IsNullOrWhiteSpace(absolutePath)) return false;

        var normalizedPath = absolutePath.Replace("\\", "/");

        var localPackage = composition.Symbol.SymbolPackage;

        // Disable localUris for now
        var localRoot = localPackage.AssetsFolder.TrimEnd('/') + "/";
        if (normalizedPath.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Dropping the root folder gives us the local relative path
            address = localPackage.Name + ":" + normalizedPath[localRoot.Length..];
            package = localPackage;
            return true;
        }

        // 3. Check other packages
        foreach (var p in composition.AvailableResourcePackages)
        {
            if (p == localPackage) continue;

            var packageRoot = p.AssetsFolder.TrimEnd('/') + "/";
            if (normalizedPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                address = $"{p.Name}:{normalizedPath[packageRoot.Length..]}";
                package = p;
                return true;
            }
        }

        // 4. Fallback to Absolute
        address = normalizedPath;
        return false;
    }

    public static Asset? UpdateMovedAsset(string oldPath, string newPath)
    {
        var isDir = Directory.Exists(newPath);

        // Remove old address
        if (!TryConvertFilepathToAddress(oldPath, isDir,out var oldAddress))
        {
            Log.Warning("Can't resolve old path");
            return null;
        }

        if (!_assetsByAddress.TryRemove(oldAddress, out var oldAsset))
        {
            Log.Warning("Can't resolve old path");
            return null;
        }
        
        var package = ResourceManager.SharedResourcePackages.FirstOrDefault(p => p.Id == oldAsset.PackageId);
        if (package == null)
        {
            Log.Warning("Can't resolve old path package");
            return null;
        }

        // Register new address
        FileSystemInfo info = isDir ? new DirectoryInfo(newPath) : new FileInfo(newPath);
        var newAsset = RegisterPackageEntry(info, package, isDir);

        ResourceFileWatcher.FileStateChangeCounter++;    
        
        // Update references...
        if (!ReferencesForAssetId.Remove(oldAsset.Id, out var references))
            return newAsset;

        foreach (var r in references)
        {
            if (!UpdateAddressForReference(r, newAsset))
                Log.Warning("Failed to update asset reference: " + r);
        }

        
        return newAsset;
    }

    private static bool UpdateAddressForReference(AssetReference reference, Asset newAsset)
    {
        if (!SymbolRegistry.TryGetSymbol(reference.SymbolId, out var symbol))
        {
            Log.Debug("Symbol for asset reference not found? " + reference.SymbolId);
            return false;
        }

        if (reference.IsDefaultValueReference)
        {
            var inputDefinition = symbol.InputDefinitions.FirstOrDefault(i => i.Id == reference.InputId);
            if (inputDefinition == null)
                return false;

            if (inputDefinition.DefaultValue.ValueType != typeof(string))
                return false;

            inputDefinition.DefaultValue.Assign(new InputValue<string>(newAsset.Address));
            AddAssetReference(newAsset, symbol.Id, Guid.Empty, inputDefinition.Id);
        }
        else
        {
            if (!symbol.Children.TryGetValue(reference.SymbolChildId, out var symbolChild))
            {
                return false;
            }

            if (!symbolChild.Inputs.TryGetValue(reference.InputId, out var input))
            {
                return false;
            }

            input.Value.Assign(new InputValue<string>(newAsset.Address));
            AddAssetReference(newAsset, symbol.Id, symbolChild.Id, input.Id);
        }

        return true;
    }

    public static void UnregisterAbsoluteFilePath(string absolutePath, SymbolPackage package)
    {
        // Convert the absolute disk path back to our conformed "Alias:Path"
        var root = package.AssetsFolder;
        if (!absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return;

        var relativePath = Path.GetRelativePath(root, absolutePath).Replace("\\", "/");
        if (relativePath.Equals(".", StringComparison.Ordinal))
            relativePath = string.Empty;

        var address = $"{package.Name}{PackageSeparator}{relativePath}";

        var wasDirectory = false;
        if (_assetsByAddress.TryRemove(address, out var asset))
        {
            Log.Debug($"Removed {address} from registry.");
        }
        else if (_assetsByAddress.TryRemove(address + "/", out asset))
        {
            Log.Debug($"Removed {address}/ from registry.");
            wasDirectory = true;
        }

        if (!wasDirectory)
        {
            var lastSlash = relativePath.LastIndexOf('/');
            var filename = lastSlash == -1
                               ? relativePath
                               : relativePath[lastSlash..];

            if (_assetsMatchingFilenames.TryRemove(filename, out _))
            {
                Log.Debug($"Removed {address} from file matches.");
            }
        }

        if (asset != null && ReferencesForAssetId.Remove(asset.Id))
        {
            Log.Debug($"Removed {address} from file matches.");
        }
    }

    public static void RemoveObsoleteAsset(Asset? asset)
    {
        if (asset == null)
            return;
        
        Log.Debug("Remove obsolete asset definition " + asset);
        
        _assetsByAddress.Remove(asset.Address, out _);
        if (!asset.IsDirectory && asset.TryGetFileName(out var filename))
        {
            _assetsMatchingFilenames.TryRemove(filename.ToString(), out _);
        }

        ReferencesForAssetId.Remove(asset.Id);
        ResourceFileWatcher.FileStateChangeCounter++;
    }
    

    public static void AddAssetReference(Asset asset, Guid symbolId, Guid symbolChildId, Guid stringUiId)
    {
        if (!ReferencesForAssetId.TryGetValue(asset.Id, out var list))
        {
            list = [];
            ReferencesForAssetId[asset.Id] = list;
        }

        foreach (var reference in list)
        {
            var alreadyExists = reference.SymbolId == symbolId
                    && reference.SymbolChildId == symbolChildId
                    && reference.InputId == stringUiId;
            
            if (alreadyExists)
                return;
        }

        list.Add(new AssetReference
                     {
                         Asset = asset,
                         SymbolId = symbolId,
                         SymbolChildId = symbolChildId,
                         InputId = stringUiId
                     });
    }

    public static bool TryGetAssetsForFilename(string filename, [NotNullWhen(true)] out List<Asset>? matches)
        => _assetsMatchingFilenames.TryGetValue(filename, out matches);

    public const char PathSeparator = '/';
    public const char PackageSeparator = ':';

    public static readonly Dictionary<Guid, List<AssetReference>> ReferencesForAssetId = new(512);
    public static ICollection<Asset> AllAssets => _assetsByAddress.Values;

    private static readonly ConcurrentDictionary<string, Asset> _assetsByAddress = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, List<Asset>> _assetsMatchingFilenames = new(StringComparer.OrdinalIgnoreCase);
}