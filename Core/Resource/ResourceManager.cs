#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Utils;

namespace T3.Core.Resource;

/// <summary>
/// File handler and GPU resource generator. Should probably be split into multiple classes, but for now it is a
/// multi-file partial class.
/// Todo: rename to `Resources`? for ease of use
/// </summary>
public static partial class ResourceManager
{
    public const char PathSeparator = '/';

    static ResourceManager()
    {
    }

    public static bool TryResolveRelativePath(string? relativePath,
                                              IResourceConsumer? consumer,
                                              out string absolutePath,
                                              out IResourcePackage? resourceContainer,
                                              bool isFolder = false)
    {
        var packages = consumer?.AvailableResourcePackages.ToArray();
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            absolutePath = string.Empty;
            resourceContainer = null;
            return false;
        }

        relativePath.ToForwardSlashesUnsafe();

        if (relativePath.StartsWith('/')) // todo: this will be the only way to reference resources in the future?
        {
            return HandleAlias(relativePath, packages, out absolutePath, out resourceContainer, isFolder);
        }

        RelativePathBackwardsCompatibility(relativePath, out var isAbsolute, out var backCompatRanges);
        if (isAbsolute)
        {
            absolutePath = relativePath.ToForwardSlashes();
            resourceContainer = null;
            return Exists(absolutePath, isFolder);
        }

        IReadOnlyList<string>? backCompatPaths = null;

        if (packages != null)
        {
            if (CheckRelativeFilePath(relativePath, packages, out absolutePath, out resourceContainer, isFolder))
                return true;

            backCompatPaths ??= PopulateBackCompatPaths(relativePath, backCompatRanges);

            foreach (var backCompatPath in backCompatPaths)
            {
                if (CheckRelativeFilePath(backCompatPath, packages, out absolutePath, out resourceContainer, isFolder))
                    return true;
            }
        }

        // TODO: What is that "*.hlsl" extension here? This method should be file type agnostic.
        var sharedResourcePackages = relativePath.EndsWith(".hlsl") ? _shaderPackages : _sharedResourcePackages;

        if (CheckRelativeFilePath(relativePath, sharedResourcePackages, out absolutePath, out resourceContainer, isFolder))
        {
            return true;
        }

        backCompatPaths ??= PopulateBackCompatPaths(relativePath, backCompatRanges);

        foreach (var backCompatPath in backCompatPaths)
        {
            if (CheckRelativeFilePath(backCompatPath, sharedResourcePackages, out absolutePath, out resourceContainer, isFolder))
                return true;
        }

        absolutePath = string.Empty;
        resourceContainer = null;
        return false;
    }

    private static bool Exists(string absolutePath, bool isFolder) => isFolder
                                                                          ? Directory.Exists(absolutePath)
                                                                          : File.Exists(absolutePath);

    private static bool CheckRelativeFilePath(string relativePath,
                                              IEnumerable<IResourcePackage> resourceContainers,
                                              out string absolutePath,
                                              out IResourcePackage? resourceContainer,
                                              bool isFolder)
    {
        foreach (var package in resourceContainers)
        {
            var resourcesFolder = package.ResourcesFolder;
            var path = Path.Combine(resourcesFolder, relativePath);

            if (Exists(path, isFolder))
            {
                absolutePath = path;
                absolutePath.ToForwardSlashesUnsafe();
                resourceContainer = package;
                return true;
            }
        }

        absolutePath = string.Empty;
        resourceContainer = null;
        return false;
    }

    private static bool HandleAlias(string relativePath, IEnumerable<IResourcePackage>? resourceContainers, out string absolutePath,
                                    out IResourcePackage? resourceContainer, bool isFolder)
    {
        var relativePathAliased = relativePath.AsSpan(1);
        var aliasEnd = relativePathAliased.IndexOf('/');

        if (aliasEnd == -1)
        {
            absolutePath = string.Empty;
            resourceContainer = null;
            return false;
        }

        var alias = relativePathAliased[..aliasEnd];
        var relativePathWithoutAlias = relativePathAliased[(aliasEnd + 1)..].ToString();

        if (resourceContainers != null)
        {
            foreach (var container in resourceContainers)
            {
                if (CheckAliasPath(container, alias, relativePathWithoutAlias, out absolutePath, isFolder))
                {
                    resourceContainer = container;
                    return true;
                }
            }
        }

        var sharedResourcePackages = relativePathAliased.EndsWith(".hlsl") ? _shaderPackages : _sharedResourcePackages;
        foreach (var container in sharedResourcePackages)
        {
            if (CheckAliasPath(container, alias, relativePathWithoutAlias, out absolutePath, isFolder))
            {
                resourceContainer = container;
                return true;
            }
        }

        absolutePath = string.Empty;
        resourceContainer = null;
        return false;

        static bool CheckAliasPath(IResourcePackage container, ReadOnlySpan<char> alias, string relativePathWithoutAlias, out string absolutePath,
                                   bool isFolder)
        {
            var containerAlias = container.Alias;
            if (containerAlias == null)
            {
                absolutePath = string.Empty;
                return false;
            }

            if (StringUtils.Equals(containerAlias, alias, true))
            {
                var path = Path.Combine(container.ResourcesFolder, relativePathWithoutAlias);
                if (Exists(path, isFolder))
                {
                    absolutePath = path;
                    absolutePath.ToForwardSlashesUnsafe();
                    return true;
                }
            }

            absolutePath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Converts an arbitrary absolute filepath to a relative asset location.
    /// </summary>
    internal static bool TryGetAssetUrlForAbsolutePath(string filePath, Instance composition, [NotNullWhen(true)] out string? location,
                                                       out IResourcePackage? package)
    {
        var filePaths = EnumerateResources([],
                                           isFolder: false,
                                           composition.AvailableResourcePackages,
                                           ResourceManager.PathMode.Aliased);

        var conformed = filePath.Replace("\\", "/");

        foreach (var aliasedPath in filePaths)
        {
            if (!TryResolveRelativePath(aliasedPath, composition, out var absolutePath, out package))
            {
                continue;
            }

            var conformedAssetPath = absolutePath.Replace("\\", "/");
            if (conformed == conformedAssetPath)
            {
                location = conformed;
                return true;
            }
        }

        location = null;
        package = null;

        return false;
    }

    public static bool TryConstructAssetAddress(string absolutePath, 
                                                Instance composition, 
                                                [NotNullWhen(true)] out string? assetAddress)
    {
        assetAddress = null;
        if (string.IsNullOrWhiteSpace(absolutePath)) return false;

        var normalizedPath = absolutePath.Replace("\\", "/");
        
        var localPackage = composition.Symbol.SymbolPackage;
        var localRoot = localPackage.ResourcesFolder.TrimEnd('/') + "/";

        if (normalizedPath.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Dropping the root folder gives us the local relative path
            assetAddress = normalizedPath[localRoot.Length..];
            return true;
        }

        // 3. Check other packages
        foreach (var p in composition.AvailableResourcePackages)
        {
            if (p == localPackage) continue;

            var packageRoot = p.ResourcesFolder.TrimEnd('/') + "/";
            if (normalizedPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Tixl 4.0 format...
                assetAddress = $"/{p.Alias}/{normalizedPath[packageRoot.Length..]}";
                return true;
            }
        }

        // 4. Fallback to Absolute
        assetAddress = normalizedPath;
        return true;
    }    
    

    internal static void AddSharedResourceFolder(IResourcePackage resourcePackage, bool allowSharedNonCodeFiles)
    {
        if (allowSharedNonCodeFiles)
            _sharedResourcePackages.Add(resourcePackage);

        _shaderPackages.Add(resourcePackage);
        resourcePackage.ResourcesFolder.ToForwardSlashesUnsafe();
    }

    internal static void RemoveSharedResourceFolder(IResourcePackage resourcePackage)
    {
        _shaderPackages.Remove(resourcePackage);
        _sharedResourcePackages.Remove(resourcePackage);
    }

    public static IReadOnlyList<IResourcePackage> SharedShaderPackages => _shaderPackages;
    private static readonly List<IResourcePackage> _sharedResourcePackages = new(4);
    private static readonly List<IResourcePackage> _shaderPackages = new(4);

    public enum PathMode
    {
        Absolute,
        Relative,
        Aliased,
        Raw
    }

    public static void RaiseFileWatchingEvents()
    {
        // dispatched to main thread
        lock (_fileWatchers)
        {
            foreach (var fileWatcher in _fileWatchers)
            {
                fileWatcher.RaiseQueuedFileChanges();
            }
        }
    }

    internal static void UnregisterWatcher(ResourceFileWatcher resourceFileWatcher)
    {
        lock (_fileWatchers)
            _fileWatchers.Remove(resourceFileWatcher);
    }

    internal static void RegisterWatcher(ResourceFileWatcher resourceFileWatcher)
    {
        lock (_fileWatchers)
            _fileWatchers.Add(resourceFileWatcher);
    }

    private static readonly List<ResourceFileWatcher> _fileWatchers = [];
}