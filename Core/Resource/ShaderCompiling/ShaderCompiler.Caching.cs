#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using T3.Core.Logging;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.Core.Utils;

namespace T3.Core.Resource.ShaderCompiling;

public abstract partial class ShaderCompiler
{
    public static void DeleteShaderCache(bool all)
    {
        int failures = 0;
        int total = 0;
        long totalFileSize = 0;

        var directory = all ? _shaderCacheRootPath : _shaderCacheDirectory;
        if(!Directory.Exists(directory))
        {
            BlockingWindow.Instance.ShowMessageBox($"No shader cache found at \"{directory}\".", "No shader cache found");
            return;
        }
        
        lock (_shaderCacheLock)
        {
            Directory.EnumerateFiles(directory, $"*{FileExtension}", SearchOption.AllDirectories)
                     .AsParallel()
                     .ForAll(file =>
                             {
                                 Interlocked.Increment(ref total);
                                 try
                                 {
                                     var fileInfo = new FileInfo(file);
                                     var fileSize = fileInfo.Length;
                                     fileInfo.Delete();
                                     Interlocked.Add(ref totalFileSize, fileSize);
                                 }
                                 catch (Exception e)
                                 {
                                     Log.Error($"Failed to delete shader cache file '{file}': {e.Message}");
                                     Interlocked.Increment(ref failures);
                                 }
                             });
        }

        var deletionsInKb = totalFileSize / 1024d;
        var deletionsString = deletionsInKb < 1024d
                                  ? $"{deletionsInKb:0.0} KB"
                                  : $"{deletionsInKb / 1024d:0.0} MB";
        
        var isError = failures > 0;

        string message;
        string title;
        if (isError)
        {
            message = $"Failed to delete {failures} out of {total} shader cache files.";
            title = "Shader Cache Deletion Failed";
        }
        else
        {
            message = string.Empty;
            title = "Shader Cache Deleted Successfully";
        }

        var finalMessage = $"Deleted {deletionsString} of shader cache from \"{_shaderCacheRootPath}\".\n{message}\n" +
                           $"Restart the application to refresh the removed cache, as all shaders still reside in memory.";
        BlockingWindow.Instance.ShowMessageBox(finalMessage, title);
    }

    /// <summary>
    /// Removes cache files that have not been used for <paramref name="maxAge"/>. Files are touched on every
    /// disk hit, so the age reflects last use. Locked files are skipped.
    /// </summary>
    public static void PruneCache(TimeSpan maxAge)
    {
        if (!_diskCachingEnabled || string.IsNullOrEmpty(_shaderCacheDirectory) || !Directory.Exists(_shaderCacheDirectory))
            return;

        var threshold = DateTime.UtcNow - maxAge;
        var removed = 0;
        long removedBytes = 0;
        lock (_shaderCacheLock)
        {
            foreach (var file in Directory.EnumerateFiles(_shaderCacheDirectory, $"*{FileExtension}", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc >= threshold)
                        continue;

                    var length = info.Length;
                    info.Delete();
                    removed++;
                    removedBytes += length;
                }
                catch (Exception)
                {
                    // In use or no permission - try again next start
                }
            }
        }

        if (removed > 0)
            Log.Debug($"Removed {removed} shader cache entries ({removedBytes / (1024.0 * 1024.0):0.0} MB) unused for {maxAge.TotalDays:0} days");
    }

    /// <summary>
    /// Copies the cached bytecode of every shader compiled for one of <paramref name="owners"/> (plus shaders without
    /// an owner, like the shared fullscreen shaders) into <paramref name="targetDirectory"/>, so an exported player
    /// can start with a warm cache. Returns the number of written files.
    /// </summary>
    public static int ExportCacheEntries(IEnumerable<IResourceConsumer> owners, string targetDirectory)
    {
        var hashes = new HashSet<ulong>();
        lock (_shaderCacheLock)
        {
            hashes.UnionWith(_ownerlessHashes);
            foreach (var owner in owners)
            {
                if (_hashesByOwner.TryGetValue(owner, out var ownerHashes))
                    hashes.UnionWith(ownerHashes);
            }
        }

        if (hashes.Count == 0)
            return 0;

        Directory.CreateDirectory(targetDirectory);
        var written = 0;
        foreach (var hash in hashes)
        {
            byte[]? bytecode;
            lock (_shaderCacheLock)
            {
                if (!_shaderBytecodeCache.TryGetValue(hash, out bytecode) && !TryLoadBytecodeFromDisk(hash, out bytecode))
                    continue;
            }

            try
            {
                File.WriteAllBytes(Path.Combine(targetDirectory, hash + FileExtension), bytecode);
                written++;
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to export shader cache entry {hash}: {e.Message}");
            }
        }

        return written;
    }

    private static void CacheSuccessfulCompilation(byte[]? oldBytecode, ulong hash, byte[] newBytecode)
    {
        CacheShaderInMemory(oldBytecode, hash, newBytecode);
        SaveBytecodeToDisk(hash, newBytecode);
    }

    /// <summary>Remembers which consumer a shader belongs to so its cache entry can travel with an export.</summary>
    private static void RecordCacheOwner(ulong hash, IResourceConsumer? owner)
    {
        if (owner == null)
        {
            _ownerlessHashes.Add(hash);
            return;
        }

        var hashes = _hashesByOwner.GetOrCreateValue(owner);
        if (!hashes.Contains(hash))
            hashes.Add(hash);
    }

    private static void SaveBytecodeToDisk(ulong hash, byte[] byteCode)
    {
        if (!_diskCachingEnabled)
        {
            return;
        }
        
        var path = GetPathForShaderCache(hash);
        File.WriteAllBytes(path, byteCode);
    }

    private static bool TryLoadBytecodeFromDisk(ulong hash, [NotNullWhen(true)] out byte[]? bytecode)
    {
        if (!_diskCachingEnabled)
        {
            bytecode = null;
            return false;
        }
        
        // A seed directory (shipped with an export) is consulted first and never written to
        if (_shaderCacheSeedDirectory != null)
        {
            var seedPath = Path.Combine(_shaderCacheSeedDirectory, hash + FileExtension);
            if (File.Exists(seedPath))
            {
                bytecode = File.ReadAllBytes(seedPath);
                return true;
            }
        }

        var path = GetPathForShaderCache(hash);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            bytecode = null;
            return false;
        }

        bytecode = File.ReadAllBytes(path);
        try
        {
            // Touch so PruneCache measures last use, not creation
            file.LastWriteTimeUtc = DateTime.UtcNow;
        }
        catch (Exception)
        {
            // Read-only location - fine
        }

        return true;
    }

    private static void CacheShaderInMemory(byte[]? oldBytecode, ulong hash, byte[] newBytecode)
    {
        if (oldBytecode != null)
        {
            if (_shaderBytecodeHashes.Remove(oldBytecode, out var oldHash))
            {
                _shaderBytecodeCache.Remove(oldHash);
            }
        }

        _shaderBytecodeCache[hash] = newBytecode;
        _shaderBytecodeHashes[newBytecode] = hash;
    }

    private static string GetPathForShaderCache(ulong hashCode)
    {
        // ReSharper disable once StringLiteralTypo
        return Path.Combine(_shaderCacheDirectory, hashCode + FileExtension);
    }

    /// <summary>
    /// Cache key for a compilation. Must be stable across processes: string.GetHashCode() is randomized
    /// per process and would make the on-disk cache miss on every launch.
    /// </summary>
    private static ulong ComputeStableHash(string sourceCode, string entryPoint, string shaderTypeName)
    {
        var hash = sourceCode.ComputeStableHash();
        hash = entryPoint.ComputeStableHash(hash);
        return shaderTypeName.ComputeStableHash(hash);
    }

    private static readonly Dictionary<byte[], ulong> _shaderBytecodeHashes = new();
    private static readonly Dictionary<ulong, byte[]> _shaderBytecodeCache = new();
    
    private static readonly object _shaderCacheLock = new();
    private static string _shaderCacheRootPath = Path.Combine(FileLocations.TempFolder, "Cache");
    private static string _shaderCacheDirectory = string.Empty;
    private static string? _shaderCacheSeedDirectory;
    private static readonly HashSet<ulong> _ownerlessHashes = [];
    private static readonly ConditionalWeakTable<IResourceConsumer, List<ulong>> _hashesByOwner = new();

    /// <summary>
    /// Root folder of the on-disk cache. Must be set before <see cref="ShaderCacheSubdirectory"/>; the default is
    /// the app's temp folder.
    /// </summary>
    public static string ShaderCacheRootPath
    {
        set
        {
            if (!string.IsNullOrEmpty(_shaderCacheDirectory))
                throw new InvalidOperationException("Shader cache root must be set before the subdirectory.");

            _shaderCacheRootPath = value;
        }
    }

    /// <summary>
    /// Optional read-only folder of precompiled entries (e.g. shipped with an exported player), consulted before
    /// the writable cache.
    /// </summary>
    public static string? ShaderCacheSeedDirectory
    {
        set => _shaderCacheSeedDirectory = value != null && Directory.Exists(value) ? value : null;
    }

    public static string ShaderCacheSubdirectory
    {
        set
        {
            if(!string.IsNullOrEmpty(_shaderCacheDirectory) )
                throw new InvalidOperationException("Shader cache subdirectory can only be set once.");
            
            _shaderCacheDirectory = Path.Combine(_shaderCacheRootPath, value);
            
            var potentialCacheFilePath = Path.Combine(_shaderCacheDirectory, ulong.MaxValue + FileExtension);

            if (potentialCacheFilePath.Length > 259)
            {
                string message = $"File path for shader cache is too long: \"{_shaderCacheDirectory}\". Disk caching will be disabled.";
                BlockingWindow.Instance.ShowMessageBox(message);
                Log.Error(message);
                _diskCachingEnabled = false;
                return;
            }

            Directory.CreateDirectory(_shaderCacheDirectory);
        }
    }

    private static bool _diskCachingEnabled = true;
    private const string FileExtension = ".shadercache";
}