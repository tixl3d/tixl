#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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

    private static void CacheSuccessfulCompilation(byte[]? oldBytecode, ulong hash, byte[] newBytecode)
    {
        CacheShaderInMemory(oldBytecode, hash, newBytecode);
        SaveBytecodeToDisk(hash, newBytecode);
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
        
        var path = GetPathForShaderCache(hash);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            bytecode = null;
            return false;
        }

        bytecode = File.ReadAllBytes(path);
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
    private static readonly string _shaderCacheRootPath = Path.Combine(FileLocations.TempFolder, "Cache");
    private static string _shaderCacheDirectory = string.Empty;

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