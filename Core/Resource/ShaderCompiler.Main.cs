#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Model;

namespace T3.Core.Resource;

public abstract partial class ShaderCompiler
{
    internal static bool TryCompileShaderFromSource<TShader>(ShaderCompilationArgs args, bool useCache,
                                                             bool forceRecompile, [NotNullWhen(true)] out TShader? shader, out string reason)
        where TShader : AbstractShader
    {
        var includes = GetIncludesFrom(args.SourceCode);
        var includeDirectories = args.Owner;

        foreach (var include in includes)
        {
            if (!ResourceManager.TryResolveRelativePath(include, includeDirectories, out _, out _))
            {
                reason = $"Can't find include file: {include}";
                shader = null;
                return false;
            }
        }
        
        if (string.IsNullOrWhiteSpace(args.EntryPoint))
            args.EntryPoint = "main";

        var hashCombination = new ULongFromTwoInts(args.SourceCode.GetHashCode(), args.EntryPoint.GetHashCode());
        var hash = hashCombination.Value;
        bool success = false;
        byte[]? compiledBlob;

        if (useCache)
        {
            lock (_shaderCacheLock)
            {
                if (!forceRecompile && TryLoadCached(hash, out compiledBlob, out reason))
                {
                    success = true;
                    reason = "loaded from cache";
                }
                else
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    if (Instance.CompileShaderFromSource<TShader>(args, out compiledBlob, out reason))
                    {
                        timer.Stop();
                        success = true;
                        CacheSuccessfulCompilation(args.OldBytecode, hash, compiledBlob);
                        reason = $"compiled in: {timer.Elapsed.TotalMilliseconds:0.0} ms";
                    }
                        
                }
            }
        }
        else
        {   
            Stopwatch timer = Stopwatch.StartNew();
            if (Instance.CompileShaderFromSource<TShader>(args, out compiledBlob, out reason))
            {   
                timer.Stop();
                reason = $"compiled in: {timer.Elapsed.TotalMilliseconds:0.0} ms";
                success = true;
            }
        }

        var name = args.Name;
        if (success)
        {
            Instance.CreateShaderInstance(name, compiledBlob, out TShader s);
            shader = s; // make nullable happy
            Log.Debug($"{name} - {args.EntryPoint} {reason}");
            return true;
        }

        reason = $"Failed to compile shader '{name}'.\n{reason}";
        shader = null;
        return false;

        static bool TryLoadCached(ulong hash, [NotNullWhen(true)] out byte[]? compiledBlob, out string reason)
        {
            if (_shaderBytecodeCache.TryGetValue(hash, out compiledBlob))
            {
                reason = string.Empty;
                reason = "Shader already compiled.";
                return true;
            }

            if (TryLoadBytecodeFromDisk(hash, out compiledBlob))
            {
                reason = string.Empty;
                CacheShaderInMemory(compiledBlob, hash, compiledBlob);
                reason = $"Loaded cached shader from disk ({hash}).";
                return true;
            }

            reason = "No cached shader found.";
            return false;
        }
    }

    /// <summary>
    /// A simple method to prepare <see cref="ShaderCompilationArgs"/> before calling the actual shader compilation.
    /// </summary>
    internal static bool TryPrepareSourceFile(ShaderCompilationFileArgs fileArgs,
                                              out string errorMessage, out ShaderCompilationArgs args)
    {
        var fileResource = fileArgs.FileResource;
        var file = fileResource.FileInfo;
        if (file is null || !file.Exists)
        {
            errorMessage = $"Shader file '{fileResource.AbsolutePath}' not found.";
            Log.Error(errorMessage);
            args = default;
            return false;
        }

        try
        {
            var sourceCodeText = File.ReadAllText(fileResource.AbsolutePath);
            errorMessage = string.Empty;
            var directoryPackage = new ShaderResourcePackage(file.Directory!);
            IResourceConsumer consumer;
            if (fileArgs.Owner != null)
            {
                var packages = fileArgs.Owner.AvailableResourcePackages.Append(directoryPackage).ToArray();
                consumer = new TempResourceConsumer(packages);
            }
            else
            {
                consumer = new TempResourceConsumer([directoryPackage]);
            }
            
            args = new ShaderCompilationArgs(sourceCodeText, fileArgs.EntryPoint, consumer, file.Name, fileArgs.OldBytecode);
            return true;
        }
        catch (Exception e)
        {
            errorMessage = $"Failed to read shader file '{fileResource.AbsolutePath}'.\n{e.Message}";
            Log.Error(errorMessage);
            args = default;
            return false;
        }
    }

    public static IEnumerable<string> GetIncludesFrom(string shaderText)
    {
        // todo - optimize with spans
        return shaderText.Split('\n')
                                 .Where(l => l.StartsWith("#include"))
                                 .Select(x => x.Split('\"'))
                                 .Where(x => x.Length > 1)
                                 .Select(x => x[1]);
    }

    internal record struct ShaderCompilationFileArgs(FileResource FileResource, string EntryPoint, IResourceConsumer? Owner, byte[]? OldBytecode);
    public record struct ShaderCompilationArgs(string SourceCode, string EntryPoint, IResourceConsumer Owner, string Name, byte[]? OldBytecode);

    public sealed class ShaderResourcePackage : IResourcePackage
    {
        public string DisplayName => ResourcesFolder;
        public string? Alias => null;
        public string ResourcesFolder { get; }
        public ResourceFileWatcher? FileWatcher => _resourceConsumer?.Package?.FileWatcher;
        public string? RootNamespace => _resourceConsumer?.Package?.RootNamespace;
        public bool IsReadOnly => true;
        public IReadOnlyCollection<DependencyCounter> Dependencies => _resourceConsumer?.Package?.Dependencies ?? Array.Empty<DependencyCounter>();
        public IReadOnlyList<IResourcePackage> AvailableResourcePackages { get; }
        private readonly IResourceConsumer? _resourceConsumer;

        public ShaderResourcePackage(FileInfo shaderFile)
        {
            ResourcesFolder = shaderFile.DirectoryName!;
            AvailableResourcePackages = new List<IResourcePackage> { this };
        }

        public ShaderResourcePackage(DirectoryInfo directoryInfo)
        {
            ResourcesFolder = directoryInfo.FullName;
            AvailableResourcePackages = new List<IResourcePackage> { this };
        }

        public ShaderResourcePackage(IResourceConsumer resourceConsumer)
        {
            _resourceConsumer = resourceConsumer;
            ResourcesFolder = resourceConsumer.Package!.ResourcesFolder;
            AvailableResourcePackages = resourceConsumer.AvailableResourcePackages;
        }
        
        public ShaderResourcePackage(FileInfo shaderFile, IResourceConsumer resourceConsumer)
        {
            _resourceConsumer = resourceConsumer;
            ResourcesFolder = shaderFile?.DirectoryName ?? resourceConsumer?.Package!.ResourcesFolder!;
            
            if(ResourcesFolder == null)
                throw new ArgumentException("ResourcesFolder must not be null");

            if (resourceConsumer != null)
            {
                var ownerPackages = resourceConsumer.AvailableResourcePackages;
                var availableResourcePackages = new List<IResourcePackage>(ownerPackages.Count + 1)
                                                    {
                                                        this
                                                    };

                availableResourcePackages.AddRange(ownerPackages);
                AvailableResourcePackages = availableResourcePackages;
            }
            else
            {
                AvailableResourcePackages = new List<IResourcePackage> { this };
            }
        }

        //public SymbolPackage? Package => _resourceConsumer?.Package;
        //public event Action? Disposing;
    }
}