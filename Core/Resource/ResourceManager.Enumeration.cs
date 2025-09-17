using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Utils;

namespace T3.Core.Resource;

public static partial class ResourceManager
{
    public static IEnumerable<IResourcePackage> GetSharedPackagesForFilters(string[] fileExtensionFilters, bool isFolder, out string[] culledFilters)
    {
        // if the package is read-only and has no files that match any of the filters, it should not be included
        
        CullFilters(isFolder, ref fileExtensionFilters, out var filterAcceptsShaders, out var shaderFilters);
        var packages = _sharedResourcePackages
           .Where(package => !package.IsReadOnly || AllMatchingPathsIn(package, false, PathMode.Raw, fileExtensionFilters).Any());
        
        if (filterAcceptsShaders && shaderFilters.Length > 0)
        {
            // we don't need to check for read-only packages here as any package that is in ShaderPackages but not SharedResourcePackages will be read-only
            packages = packages
               .Concat(_shaderPackages.Except(_sharedResourcePackages)
                                     .Where(package => AllMatchingPathsIn(package, false, PathMode.Raw, shaderFilters).Any()));
        }
        
        culledFilters = fileExtensionFilters;
        return packages;
    }
    
    public static IEnumerable<string> EnumerateResources(string[] fileExtensionFilters, bool isFolder, IEnumerable<IResourcePackage> packages,
                                                         PathMode pathMode)
    {
        CullFilters(isFolder, ref fileExtensionFilters, out var filterAcceptsShaders, out var shaderFilters);
        
        var allFiles = packages
                      .Concat(_sharedResourcePackages)
                      .Distinct()
                      .SelectMany(x => AllMatchingPathsIn(x, isFolder, pathMode, fileExtensionFilters));
        
        if (filterAcceptsShaders && shaderFilters.Length > 0)
        {
            allFiles = allFiles.Concat(_shaderPackages.Except(_sharedResourcePackages)
                                                     .SelectMany(x => AllMatchingPathsIn(x, isFolder, pathMode, shaderFilters)));
        }
        
        
        // handle always-shared shaders
        return (!isFolder && filterAcceptsShaders
                   ? allFiles.Concat(SharedShaderPackages
                                    .Except(_sharedResourcePackages)
                                    .SelectMany(x => AllMatchingPathsIn(x, false, pathMode, shaderFilters)))
                   : allFiles).Distinct();
    }
    
    /// <summary>
    /// Simplifies the filters to only include the ones that are relevant for the given query, removing any that are redundant or irrelevant
    /// </summary>
    /// <param name="isFolder">If the query is searching for files or folders</param>
    /// <param name="fileExtensionFilter"></param>
    /// <param name="filterAcceptsShaders">Whether or not the output filters can accept shader types</param>
    /// <param name="shaderFilters">Filters ending in shader extension (.hlsl)</param>
    private static void CullFilters(bool isFolder, ref string[] fileExtensionFilter, out bool filterAcceptsShaders, out string[] shaderFilters)
    {
        const string shaderExtension = ".hlsl";
        bool mightAcceptShaders = !isFolder;
        
        // the result of assigning this here is that even if a the requested filters are only wildcards, shaders will not show up unless explicitly requested
        // this is probably the desired behavior in most scenarios and can enforce a safer workflow as shaders could otherwise end up being
        // read for file IO when they're in a readonly package
        shaderFilters = mightAcceptShaders ? fileExtensionFilter.Where(x => x.EndsWith(shaderExtension)).ToArray() : [];
        
        // if there's an "all" wildcard, all other filters are irrelevant
        if (fileExtensionFilter.Length == 0 || fileExtensionFilter.Length > 0 && (fileExtensionFilter.Contains("*.*") || fileExtensionFilter.Contains("*")))
        {
            fileExtensionFilter = isFolder ? ["*"] : ["*.*"];
        }
        
        filterAcceptsShaders = mightAcceptShaders && shaderFilters.Length > 0 || fileExtensionFilter.Any(x => x.EndsWith('*'));
    }
    
    /// <summary>
    /// Method to enumerate all files in a package that match the given filters. If a file matches any of the filters, it will be included.
    /// Potentially performance-critical.
    /// </summary>
    /// <returns>Matching file path formatted by PathMode provided</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    private static IEnumerable<string> AllMatchingPathsIn(IResourcePackage package, bool useFolder, PathMode pathMode, string[] filters)
    {
        Func<string, string, SearchOption, IEnumerable<string>> searchFunc = useFolder
                                                                                 ? Directory.EnumerateDirectories
                                                                                 : Directory.EnumerateFiles;

        var directory = package.ResourcesFolder;
        if (!Directory.Exists(directory))
        {
            yield break;
        }
        
        IEnumerable<string> paths;
        try
        {
            paths = searchFunc(directory, "*", SearchOption.AllDirectories);
        }
        catch (Exception e)
        {
            Log.Debug("Failed to scan resources: " + e.Message);
            yield break; // can't even start enumeration
        }
        
        foreach (var path in paths)
        {
            path.ToForwardSlashesUnsafe();
            var lastSlashIndex = path.LastIndexOf('/');
            var endIndexExclusive = path.Length;
            var range  = lastSlashIndex == -1 ? new Range(0, endIndexExclusive) : new Range(lastSlashIndex + 1, endIndexExclusive);
            var fileNameSpan = path.AsSpan()[range];

            var passed = false;
            
            foreach (var filter in filters)
            {
                if (!StringUtils.MatchesSearchFilter(fileNameSpan, filter, true))
                    continue;
                
                if (filter.EndsWith('*'))
                {
                    if (PathIsShader(fileNameSpan))
                        continue;
                }

                passed = true;
                break;
            }

            if (!passed) 
                continue;
            
            // return the path in the requested format
            string result;
            switch (pathMode)
            {
                case PathMode.Absolute:
                    result = path;
                    break;
                    
                // the following caches reduce the number of string allocations significantly when iterating
                case PathMode.Relative:
                    if(!_relativePathsCache.TryGetValue(path, out result))
                    {
                        result = path[(directory.Length + 1)..];
                        _relativePathsCache.TryAdd(path, result);
                    }
                    break;
                case PathMode.Aliased:
                    if(!_aliasedPathsCache.TryGetValue(path, out result))
                    {
                        result = $"/{package.Alias}/{path.AsSpan()[(directory.Length + 1)..]}";
                        _aliasedPathsCache.TryAdd(path, result);
                    }
                    break;
                case PathMode.Raw:
                    result = path;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pathMode), pathMode, null);
            }

            yield return result;
        }
        
        CheckSizeOf(_relativePathsCache);
        CheckSizeOf(_aliasedPathsCache);

        yield break;

        static void CheckSizeOf(ConcurrentDictionary<string, string> pathCache)
        {
            // in situations where files are continuously generated, this could result in a memory leak,
            // so we naively clear the cache when it reaches a certain size
            if (pathCache.Count <= MaxPathCacheSize) 
                return;
            
            Log.Warning($"Path cache exceeded {MaxPathCacheSize} entries. Beware of huge GC pause.");
            pathCache.Clear();
        }
    }

    private static readonly ConcurrentDictionary<string, string> _aliasedPathsCache = new();
    private static readonly ConcurrentDictionary<string, string> _relativePathsCache = new();
    private const int MaxPathCacheSize = 1_000_000; // 1 million path entries ~= 320MB + dictionary cache overhead
    
    public const string DefaultShaderFilter = "*.hlsl";
    private static bool PathIsShader(ReadOnlySpan<char> path) => StringUtils.MatchesSearchFilter(path, DefaultShaderFilter, true);
}

public static class ResourceExtensions
{
    public static IEnumerable<IResourcePackage> PackagesInCommon(this IReadOnlyCollection<Instance> instances)
    {
        if(instances.Count == 0)
            return [];
        
        if(instances.Count == 1)
            return instances.First().AvailableResourcePackages;

        return instances.Select(x => x.AvailableResourcePackages)
                        .Aggregate<IEnumerable<IResourcePackage>>((a, b) => a.Intersect(b));
    }
}