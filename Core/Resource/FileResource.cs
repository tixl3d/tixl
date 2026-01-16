#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Stats;
using T3.Core.Utils;

namespace T3.Core.Resource;

public sealed partial class FileResource: IResource
{
    IResourcePackage? IResource.OwningPackage => ResourcePackage;
    private FileResource(IResourcePackage? package, string absolutePath)
    {
        ResourcePackage = package;
        AbsolutePath = absolutePath;
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
            Log.Warning($"Resource {GetType()} \"{AbsolutePath}\" was deleted.");
            FileDeleted?.Invoke(this, EventArgs.Empty);
        }
        
        FileChanged?.Invoke(this, changeTypes);
    }
    
    private void ChangeFilePath(string absolutePath)
    {
        if (!_registered)
        {
            AbsolutePath = absolutePath;
            return;
        }
        
        // todo - packages should pre-load resources to manage them themselves? what about resources not owned by a package?
        // todo - check to see if owning package has changed
        // todo - adjust dependencies if owning package did change
        
        lock (CollectionLock)
        {
            Unregister(this);
            
            AbsolutePath = absolutePath;
            _fileInfo = null;
            Register(this);
        }
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
    
    public static bool TryGetFileResource(string? relativePath, IResourceConsumer? owner, [NotNullWhen(true)] out FileResource? resource)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            resource = null;
            return false;
        }
        
        if (!ResourceManager.TryResolveRelativePath(relativePath, owner, out var absolutePath, out var resourceContainer))
        {
            var message = $"Path not found: '{relativePath}' (Resolved to '{absolutePath}').";
            if (owner is Instance instance)
            {
                instance.LogErrorState(message);
            }
            else
            {
                Log.Error(message);
            }
            resource = null;
            return false;
        }
        
        lock (CollectionLock)
        {
            if (FileResources.TryGetValue(absolutePath, out resource))
                return true;
            
            resource = new FileResource(resourceContainer, absolutePath);
        }
        
        return true;
    }
    
    private static void Unregister(FileResource resource)
    {
        var path = resource.AbsolutePath;
        
        lock (CollectionLock)
        {
            if (!FileResources.Remove(path, out _))
            {
                return;
            }
        }
        
        resource.ResourcePackage?.FileWatcher?.RemoveFileHook(path, resource._onResourceChanged);
        resource._registered = false;
    }
    
    private static void Register(FileResource resource)
    {
        var path = resource.AbsolutePath.ToForwardSlashes();
        lock (CollectionLock)
        {
            if (!FileResources.TryAdd(path, resource))
            {
                return;
            }
        }
        
        resource.ResourcePackage?.FileWatcher?.AddFileHook(path, resource._onResourceChanged);
        resource._registered = true;
    }
    
    public string AbsolutePath { get; private set; }
    public string? FileExtension => FileInfo?.Extension;
    public IResourcePackage? ResourcePackage { get; private set; }
    
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
    
    public event EventHandler? FileDeleted;
    public event EventHandler<string>? FileMoved;
    public event EventHandler<WatcherChangeTypes>? FileChanged;
    
    private readonly FileWatcherAction _onResourceChanged;
    private bool _registered;
    
    private static readonly Dictionary<string, FileResource> FileResources = new();
    public static readonly IReadOnlyCollection<FileResource> AllFileResources = FileResources.Values;
    private readonly List<IResource> _instantiatedResources = new();
    private static readonly object CollectionLock = new();
}
