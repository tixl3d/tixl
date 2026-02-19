#nullable enable
using System.Collections.Generic;
using T3.Core.Utils;

namespace T3.Core.Resource;

/// <summary>
/// File handler and GPU resource generator. 
/// </summary>
/// Todo: Should probably be split into multiple classes
public static class ResourcePackageManager
{
    static ResourcePackageManager()
    {
    }

    internal static void AddSharedResourceFolder(IResourcePackage resourcePackage, bool allowSharedNonCodeFiles)
    {
        _sharedResourcePackages.Add(resourcePackage);
        resourcePackage.AssetsFolder.ToForwardSlashesUnsafe();
    }

    internal static void RemoveSharedResourceFolder(IResourcePackage resourcePackage)
    {
        _sharedResourcePackages.Remove(resourcePackage);
    }

    public static IReadOnlyList<IResourcePackage> SharedResourcePackages => _sharedResourcePackages;
    private static readonly List<IResourcePackage> _sharedResourcePackages = new(4);

    
    /// <summary>
    /// Called at the beginning of each frame.
    /// </summary>
    public static void RaiseFileWatchingEvents()
    {
        // Dispatched to main thread
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