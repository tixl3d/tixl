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
using T3.Core.UserData;
using T3.Core.Utils;

namespace T3.Core.Resource.Assets;

public static class AssetRegistry
{
    public static bool TryGetAsset(string address, [NotNullWhen(true)] out Asset? asset)
    {
        return _assetsByAddress.TryGetValue(address, out asset);
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
            return Exists(absolutePath, isFolder);
        }

        if (projectSeparator == -1)
        {
            if(logWarnings)
                Log.Warning($"Can't resolve asset '{address}'");
            
            return false;
        }

        // 4. Fallback search through packages
        var packageName = span[..projectSeparator];
        var localPath = span[(projectSeparator + 1)..];

        var packages = consumer?.AvailableResourcePackages ?? ResourceManager.SharedResourcePackages;
        if (packages.Count == 0)
        {
            if(logWarnings)
                Log.Warning($"Can't resolve asset '{address}' (no packages found)");
            
            return false;
        }

        foreach (var package in packages)
        {
            if (!package.Name.AsSpan().Equals(packageName, StringComparison.Ordinal))
                continue;

            resourceContainer = package;
            absolutePath = $"{package.ResourcesFolder}/{localPath}";
            return Exists(absolutePath, isFolder);
        }

        return false;
    }

    private static bool Exists(string absolutePath, bool isFolder) => isFolder
                                                                          ? Directory.Exists(absolutePath)
                                                                          : File.Exists(absolutePath);

    internal static bool TryConvertToRelativePath(string absolutePath, [NotNullWhen(true)] out string? relativeAddress)
    {
        absolutePath.ToForwardSlashesUnsafe();
        foreach (var package in SymbolPackage.AllPackages)
        {
            var folder = package.ResourcesFolder;
            if (absolutePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                // Trim the folder length AND the following slash if it exists
                var relativePart = absolutePath[folder.Length..].TrimStart('/'); 
                relativeAddress = $"{package.Name}{PackageSeparator}{relativePart}";
                return true;
            }
        }

        relativeAddress = null;
        return false;
    }

    public static void RegisterAssetsFromPackage(SymbolPackage package)
    {
        var root = package.ResourcesFolder;
        if (!Directory.Exists(root)) return;

        var di = new DirectoryInfo(root);
        var packageId = package.Id;
        var packageAlias = package.Name;

        RegisterEntry(di, root, packageAlias, packageId, isDirectory: true);

        // Register all files
        foreach (var fileInfo in di.EnumerateFiles("*.*", SearchOption.AllDirectories))
        {
            if (FileLocations.IgnoredFiles.Contains(fileInfo.Name))
                continue;

            var asset =RegisterEntry(fileInfo, root, packageAlias, packageId, false);
            
            // Collect all possible addresses for this filename
            var list = _healerIndex.GetOrAdd(fileInfo.Name, _ => []);
            lock (list)
            {
                if (!list.Contains(asset.Address)) list.Add(asset.Address);
            }
        }

        // Register all directories
        foreach (var dirInfo in di.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if (FileLocations.IgnoredFiles.Contains(dirInfo.Name))
                continue;

            RegisterEntry(new FileInfo(dirInfo.FullName), root, packageAlias, packageId, true);
        }

        //Log.Debug($"{packageAlias}: Registered {_assetsByAddress.Count(a => a.Value.PackageId == packageId)} assets (including directories).");
    }

    public static Asset RegisterEntry(FileSystemInfo info, string root, string packageAlias, Guid packageId, bool isDirectory)
    {
        info.Refresh();
        
        // If the info is the root itself, relative path is empty string
        var relativePath = Path.GetRelativePath(root, info.FullName).Replace("\\", "/");
        if (relativePath == ".") relativePath = string.Empty;

        var address = $"{packageAlias}{PackageSeparator}{relativePath}";

        // Pre-calculate path parts
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathParts = new List<string>(parts.Length + 1) { packageAlias };

        // Logic for folder structure
        var partCount = isDirectory ? parts.Length : parts.Length - 1;
        for (var i = 0; i < partCount; i++)
        {
            pathParts.Add(parts[i]);
        }

        AssetType.TryGetForFilePath(info.Name, out var assetType, out var extensionId);

        var asset = new Asset(address)
                        {
                            PackageId = packageId,
                            FileSystemInfo = info,
                            AssetType = assetType,
                            IsDirectory = isDirectory,
                            PathParts = pathParts,
                            ExtensionId = extensionId,
                        };

        _assetsByAddress[address] = asset;
        return asset;
    }

    internal static void UnregisterPackage(Guid packageId)
    {
        var addressesToRemove = _assetsByAddress.Values
                                           .Where(a => a.PackageId == packageId)
                                           .Select(a => a.Address)
                                           .ToList();

        foreach (var addr in addressesToRemove)
        {
            _assetsByAddress.TryRemove(addr, out _);
            //_usagesByAddress.TryRemove(addr, out _);
        }
    }

    /// <summary>
    /// This will try to first create a localUrl, then a packageUrl,
    /// and finally fall back to an absolute path.
    ///
    /// This method is useful to test if path would be valid before the asset is being registered...
    /// </summary>
    public static bool TryConstructAddressFromFilePath(string absolutePath,
                                                       Instance composition,
                                                       [NotNullWhen(true)] out string? address,
                                                       [NotNullWhen(true)] out IResourcePackage? package
        )
    {
        address = null;
        package = null;
        if (string.IsNullOrWhiteSpace(absolutePath)) return false;

        var normalizedPath = absolutePath.Replace("\\", "/");

        var localPackage = composition.Symbol.SymbolPackage;

        // Disable localUris for now
        var localRoot = localPackage.ResourcesFolder.TrimEnd('/') + "/";
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

            var packageRoot = p.ResourcesFolder.TrimEnd('/') + "/";
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
    
    public static void UpdateEntry(string oldPath, string newPath, SymbolPackage package)
    {
        var isDir = Directory.Exists(newPath);
        
        //TODO: Add Logic to rebuild the new address and re-insert
        
        // if (isDir)
        // {
        //     Recursive update for all assets under this folder
        //     if (TryConvertToRelativePath(oldPath, out var oldFolderAddress))
        //     {
        //         var prefix = oldFolderAddress + "/";
        //         var affectedAssets = _assetsByAddress.Keys
        //                                              .Where(k => k.StartsWith(prefix))
        //                                              .ToList();
        //         
        //         foreach (var oldAddress2 in affectedAssets)
        //         {
        //             if (_assetsByAddress.TryRemove(oldAddress2, out var asset))
        //             {
        //                 
        //             }
        //         }
        //     }
        // }        
        
        // 1. Remove old address
        if (TryConvertToRelativePath(oldPath, out var oldAddress))
        {
            if (_assetsByAddress.TryRemove(oldAddress, out _))
            {
                Log.Debug($"Removed old entry: {oldAddress}");
            }
        }

        // 2. Register new address
        var info = isDir ? (FileSystemInfo)new DirectoryInfo(newPath) : new FileInfo(newPath);
        RegisterEntry(info, package.ResourcesFolder, package.Name, package.Id, isDir);
    }
    
    
    public static void UnregisterEntry(string absolutePath, SymbolPackage package)
    {
        // Convert the absolute disk path back to our conformed "Alias:Path"
        var root = package.ResourcesFolder;
        if (!absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return;

        var relativePath = Path.GetRelativePath(root, absolutePath).Replace("\\", "/");
        if (relativePath == ".") relativePath = string.Empty;
    
        var address = $"{package.Name}{PackageSeparator}{relativePath}";

        if (_assetsByAddress.TryRemove(address, out _))
        {
            Log.Debug($"Removed {address} from registry.");
        }
    }

    public static bool TryGetHealerMatches(string filename, [NotNullWhen(true)] out List<string>? matches) 
        => _healerIndex.TryGetValue(filename, out matches);
    
    public const char PathSeparator = '/';
    public const char PackageSeparator = ':';

    public static ICollection<Asset> AllAssets => _assetsByAddress.Values;

    private static readonly ConcurrentDictionary<string, Asset> _assetsByAddress = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, List<string>> _healerIndex = new(StringComparer.OrdinalIgnoreCase);
}