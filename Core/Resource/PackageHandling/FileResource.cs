#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Logging;
using T3.Core.Model;

using T3.Core.Resource.Assets;
using T3.Core.Utils;

namespace T3.Core.Resource;

public sealed class FileResource: IResource
{
    public Asset Asset { get; private set; }
    
    // Delegate to the asset for convenience
    public string AbsolutePath => Asset.FullPath;
    public IResourcePackage? ResourcePackage => Asset.Package; // Resolved via Registry
    
    IResourcePackage? IResource.OwningPackage => ResourcePackage;
    private FileResource(Asset asset)
    {
        Asset = asset;
        _onResourceChanged = OnFileResourceChanged;
    }
    
    ~FileResource()
    {
        lock (CollectionLock)
        {
            if(_registered)
                Unregister(this);
        }
    }
    
    private void OnFileResourceChanged(WatcherChangeTypes changeTypes, string absolutePath)
    {
        // todo - do we need to dispatch this to the main thread?
        if (changeTypes.WasMoved())
        {
            ChangeFilePath(absolutePath);
            FileMoved?.Invoke(this, absolutePath);
        }
        else if (changeTypes.WasDeleted())
        {
            Log.Debug($"Resource {GetType().BaseType} deleted: \"{AbsolutePath}\"");
            FileDeleted?.Invoke(this, EventArgs.Empty);
        }
        
        FileChanged?.Invoke(this, changeTypes);
    }
    
    private void ChangeFilePath(string absolutePath)
    {
        Log.Warning($"FileResource.ChangeFilePath({absolutePath}) currently not implemented.");
        // TODO: Check
        // if (!_registered)
        // {
        //     AbsolutePath = absolutePath;
        //     return;
        // }
        //
        // // todo - packages should pre-load resources to manage them themselves? what about resources not owned by a package?
        // // todo - check to see if owning package has changed
        // // todo - adjust dependencies if owning package did change
        //
        // lock (CollectionLock)
        // {
        //     Unregister(this);
        //     
        //     AbsolutePath = absolutePath;
        //     _fileInfo = null;
        //     Register(this);
        // }
    }
    
    internal void Claim(IResource owner)
    {
        lock (CollectionLock)
        {
            Debug.Assert(!_instantiatedResources.Contains(owner));
            _instantiatedResources.Add(owner);
            
            if(owner.OwningPackage is SymbolPackage symbolPackage)
                symbolPackage.AddResourceDependencyOn(this);
            
            if (_instantiatedResources.Count == 1)
                Register(this);
        }
    }
    
    internal void Release(IResource owner)
    {
        lock (CollectionLock)
        {
            var removed = _instantiatedResources.Remove(owner);
            Debug.Assert(removed);
            
            if(owner.OwningPackage is SymbolPackage symbolPackage)
                symbolPackage.RemoveResourceDependencyOn(this);
            
            if (_instantiatedResources.Count == 0)
            {
                Unregister(this);
            }
        }
    }
    
    
    public static bool TryGetFileResource(string? address, IResourceConsumer? owner, [NotNullWhen(true)] out FileResource? resource)
    {
        resource = null;
        if (string.IsNullOrWhiteSpace(address))
            return false;

        // 1. Strict Registry Lookup: If it was registered on startup, use it.
        if (!AssetRegistry.TryGetAsset(address, out var asset))
        {
            // 2. Not in registry? Treat it strictly as a disk path.
            // We normalize to forward slashes to match AssetRegistry conventions.
            var diskPath = address.ToForwardSlashes();
        
            if (File.Exists(diskPath))
            {
                // Register as an external file once. 
                // RegisterExternalFile handles deduplication internally.
                asset = AssetRegistry.GetOrRegisterExternalFileAsset(diskPath);
            }
        }

        // 3. Final Check: If we still don't have an asset, it's a "File Not Found" state.
        if (asset == null)
        {
            #if DEBUG
            Log.Warning($"Resource not found: '{address}'", owner!);
            #endif
            return false;
        }

        // 4. Use Asset.Id to deduplicate the live FileResource instance.
        lock (CollectionLock)
        {
            if (FileResourcesByAssetId.TryGetValue(asset.Id, out resource))
                return true;

            resource = new FileResource(asset);
            // Register(resource) is called inside Claim() or the constructor 
            // depending on your specific lifecycle implementation.
        }

        return true;
    }
    
    
    private static void Register(FileResource resource)
    {
        var asset = resource.Asset;
        var assetId = asset.Id;

        lock (CollectionLock)
        {
            if (!FileResourcesByAssetId.TryAdd(assetId, resource))
            {
                return;
            }
        }

        // Use the absolute path from the Asset for the OS-level file watcher hook
        var path = asset.FullPath;
        if (!string.IsNullOrEmpty(path))
        {
            resource.ResourcePackage?.FileWatcher?.AddFileHook(path, resource._onResourceChanged);
        }
    
        resource._registered = true;
    }

    private static void Unregister(FileResource resource)
    {
        var asset = resource.Asset;
        var assetId = asset.Id;

        lock (CollectionLock)
        {
            if (!FileResourcesByAssetId.Remove(assetId, out _))
            {
                return;
            }
        }

        var path = asset.FullPath.ToForwardSlashes();
        if (!string.IsNullOrEmpty(path))
        {
            resource.ResourcePackage?.FileWatcher?.RemoveFileHook(path, resource._onResourceChanged);
        }
    
        resource._registered = false;
    }
    
    //public string AbsolutePath { get; private set; }
    public string? FileExtension => FileInfo?.Extension;
    //public IResourcePackage? ResourcePackage { get; private set; }
    
    private FileInfo? _fileInfo;
    public FileInfo? FileInfo
    {
        get
        {
            if (_fileInfo == null)
            {
                try
                {
                    return _fileInfo = new FileInfo(AbsolutePath);
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to create file info for {AbsolutePath}: {e}");
                    return null;
                }
            }
            
            try
            {
                _fileInfo.Refresh();
            }
            catch (Exception e)
            {
                Log.Error($"Failed to refresh file info for {AbsolutePath}: {e}");
            }
            
            return _fileInfo;
        }
    }
    
    public bool TryOpenFileStream([NotNullWhen(true)] out FileStream? fileStream, [NotNullWhen(false)] out string? reason, FileAccess access,
                                  FileMode mode = FileMode.Open)
    {
        try
        {
            fileStream = new FileStream(AbsolutePath, mode, access);
            reason = null;
            return true;
        }
        catch (Exception e)
        {
            reason = e.ToString();
            fileStream = null;
            return false;
        }
    }
    
    public event EventHandler? FileDeleted;
    public event EventHandler<string>? FileMoved;
    public event EventHandler<WatcherChangeTypes>? FileChanged;
    
    private readonly FileWatcherAction _onResourceChanged;
    private bool _registered;
    
    private static readonly Dictionary<Guid, FileResource> FileResourcesByAssetId = new();
    
    //private static readonly Dictionary<string, FileResource> FileResources = new();
    //public static readonly IReadOnlyCollection<FileResource> AllFileResources = FileResources.Values;
    private readonly List<IResource> _instantiatedResources = new();
    private static readonly object CollectionLock = new();
}
