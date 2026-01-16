#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using T3.Core.Logging;
using T3.Core.Utils;

namespace T3.Core.Resource;

public sealed class ResourceFileWatcher : IDisposable
{
    public ResourceFileWatcher(string watchedFolder)
    {
        Directory.CreateDirectory(watchedFolder);
        _watchedDirectory = watchedFolder.ToForwardSlashes();
        ResourceManager.RegisterWatcher(this);
    }

    public void Dispose()
    {
        DisposeFileWatcher(ref _fsWatcher);

        _fileChangeActions.Clear();
        ResourceManager.UnregisterWatcher(this);
    }

    internal void AddFileHook(string filepath, FileWatcherAction action)
    {
        if (string.IsNullOrWhiteSpace(filepath))
            return;

        ArgumentNullException.ThrowIfNull(action);

        if (!filepath.StartsWith(_watchedDirectory))
        {
            Log.Error($"Cannot watch file outside of watched directory: \"{filepath}\" is not in \"{_watchedDirectory}\"");
            return;
        }

        if (!_fileChangeActions.TryGetValue(filepath, out var existingActions))
        {
            existingActions = new List<FileWatcherAction>();
            _fileChangeActions.TryAdd(filepath, existingActions);
        }

        existingActions.Add(action);

        if (_fsWatcher != null) return;

        _fsWatcher = new FileSystemWatcher(_watchedDirectory)
                         {
                             IncludeSubdirectories = true,
                             EnableRaisingEvents = true,
                             NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
                         };

        _fsWatcher.Changed += OnFileChanged;
        _fsWatcher.Renamed += OnFileChanged;
        _fsWatcher.Created += OnFileCreated;
        _fsWatcher.Deleted += OnFileDeleted;
        _fsWatcher.Error += OnError;
    }

    internal void RemoveFileHook(string absolutePath, FileWatcherAction onResourceChanged)
    {
        if (!_fileChangeActions.TryGetValue(absolutePath, out var actions))
            return;

        actions.Remove(onResourceChanged);
        if (actions.Count != 0)
            return;

        _fileChangeActions.Remove(absolutePath, out _);

        if (_fileChangeActions.Count == 0)
        {
            DisposeFileWatcher(ref _fsWatcher);
        }
    }

    internal void RaiseQueuedFileChanges()
    {
        var currentTime = DateTime.UtcNow.Ticks;
            
        const long thresholdMs = 394;
        const long thresholdTicks = thresholdMs * TimeSpan.TicksPerMillisecond;
        lock (_eventLock)
        {
            if (_newFileEvents.Count == 0)
                return;

            var fileEvents = _newFileEvents.OrderBy(x => x.Value.TimeTicks).ToArray();
            FileKey previous = default;
            foreach (var (fileKey, details) in fileEvents)
            {
                if (currentTime - details.TimeTicks < thresholdTicks)
                    break;

                _newFileEvents.Remove(fileKey);

                if (fileKey == previous) // ignore duplicates
                    continue;

                previous = fileKey;

                if (fileKey.isRename)
                {
                    HandleRename((RenamedEventArgs)details.Args);
                }

                var path = details.Args.FullPath;
                if (!_fileChangeActions.TryGetValue(path, out var actions))
                {
                    continue;
                }

                // queue these actions so that we don't hold the lock while executing them
                foreach (var action in actions)
                {
                    _queuedActions.Enqueue(new FileWatchQueuedAction(details.Args, action));
                }
            }
        }

        while (_queuedActions.TryDequeue(out var queuedAction))
        {
            try
            {
                var args = queuedAction.Args;
                queuedAction.Action(args.ChangeType, args.FullPath);
            }
            catch (Exception exception)
            {
                Log.Error($"Error in file change action: {exception}");
            }
        }

        return;
            
        void HandleRename(RenamedEventArgs e)
        {
            if (!_fileChangeActions.Remove(e.OldFullPath, out var actions))
                return;

            var newPath = e.FullPath;
            if (_fileChangeActions.TryGetValue(newPath, out var previousActions))
            {
                previousActions.AddRange(actions);
                return;
            }

            _fileChangeActions.TryAdd(newPath, actions);
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Log.Info($"File created: {e.FullPath}");
        FileCreated?.Invoke(this, e.FullPath);
        OnFileChanged(this, e);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        e.FullPath.ToForwardSlashesUnsafe();
        var isRenamed = false;

        if (e is RenamedEventArgs renamedArgs)
        {
            renamedArgs.OldFullPath.ToForwardSlashesUnsafe();
            isRenamed = true;
        }
        
        var fileKey = new FileKey(e.FullPath, e.ChangeType, isRenamed);
        lock (_eventLock)
        {
            _newFileEvents[fileKey] = new FileWatchDetails(DateTime.UtcNow.Ticks, e);
            FileStateChangeCounter++;
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        Log.Error($"File watcher error: {e.GetException()}");
        _fsWatcher?.Dispose();
        _fsWatcher = new FileSystemWatcher(_watchedDirectory)
                         {
                             IncludeSubdirectories = true,
                             EnableRaisingEvents = true
                         };
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        OnFileChanged(sender, e);
        Log.Warning($"File deleted: {e.FullPath}");
    }

    private static void DisposeFileWatcher(ref FileSystemWatcher? watcher)
    {
        if (watcher == null)
            return;

        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
        watcher = null;
    }

    private record struct FileKey(string Path, WatcherChangeTypes ChangeType, bool isRename);

    private record struct FileWatchDetails(long TimeTicks, FileSystemEventArgs Args);

    private record struct FileWatchQueuedAction(FileSystemEventArgs Args, FileWatcherAction Action);

    private readonly Dictionary<FileKey, FileWatchDetails> _newFileEvents = new();
    private readonly object _eventLock = new();
    private readonly string _watchedDirectory;
    private FileSystemWatcher? _fsWatcher = null;

    private readonly ConcurrentDictionary<string, List<FileWatcherAction>> _fileChangeActions = new();
    private readonly Queue<FileWatchQueuedAction> _queuedActions = new();
    public event EventHandler<string>? FileCreated;
    
    /// <summary>
    /// This is incremented on every file change event and can be used for cache invalidation (e.g. for complex FileLists)
    /// </summary>
    public static int FileStateChangeCounter { get; private set; }
}

internal delegate void FileWatcherAction(WatcherChangeTypes changeTypes, string absolutePath);

internal static class WatcherChangeTypesExtensions
{
    public static bool WasDeleted(this WatcherChangeTypes changeTypes)
    {
        return (changeTypes & WatcherChangeTypes.Deleted) == WatcherChangeTypes.Deleted;
    }

    public static bool WasMoved(this WatcherChangeTypes changeTypes)
    {
        return (changeTypes & WatcherChangeTypes.Renamed) == WatcherChangeTypes.Renamed;
    }

    public static bool WasCreated(this WatcherChangeTypes changeTypes)
    {
        return (changeTypes & WatcherChangeTypes.Created) == WatcherChangeTypes.Created;
    }

    public static bool WasChanged(this WatcherChangeTypes changeTypes)
    {
        return (changeTypes & WatcherChangeTypes.Changed) == WatcherChangeTypes.Changed;
    }
}