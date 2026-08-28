#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using NuGet.Common;
using NuGet.Frameworks;
using T3.Core.Logging;

namespace T3.Core.Compilation;

internal sealed class AssemblyTreeNode
{
    public readonly Assembly Assembly;
    public readonly AssemblyName Name;
    public readonly string NameStr;

    private readonly List<AssemblyTreeNode> _references = [];

    public readonly AssemblyLoadContext LoadContext;

    private readonly Lock _assemblyLock = new();

    /// <summary>Dll files next to this assembly that are not (yet) loaded; null until first needed.</summary>
    private List<string>? _unreferencedDllPaths;

    private readonly Lock _unreferencedLock = new();
    private readonly DllImportResolver? _nativeResolver;

    private static readonly string[] _supportedRuntimeIdentifiers = ["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];


    private readonly string _parentName;
    private readonly bool _searchNestedFolders;

    // warning : not thread safe, must be wrapped in a lock around _assemblyLock
    public AssemblyTreeNode(Assembly assembly, AssemblyLoadContext parent, bool searchNestedFolders, bool canSearchDlls, DllImportResolver? nativeResolver)
    {
        Assembly = assembly;
        // Skip assemblies that install their own native resolver (see _assembliesWithOwnNativeResolver).
        if (nativeResolver != null && !_assembliesWithOwnNativeResolver.Contains(assembly.GetName().Name ?? string.Empty))
            NativeLibrary.SetDllImportResolver(assembly, nativeResolver);

        _nativeResolver = nativeResolver;
        Name = assembly.GetName();
        NameStr = Name.GetName();
        _searchNestedFolders = searchNestedFolders;

        _parentName = parent.Name!;
        LoadContext = parent;

        if (!canSearchDlls)
        {
            _unreferencedDllPaths = [];
        }

        // if (debug && !node.NameStr.StartsWith("System")) // don't log system assemblies - too much log spam for things that are probably not error-prone
        //Log.Debug($"{parent}: Loaded assembly {NameStr} from {assembly.Location}");
    }

    // Assemblies that install their own native DllImportResolver in their static constructor. .NET permits
    // one resolver per assembly and TiXL always registers first, so it must leave these alone — otherwise
    // their constructor throws "a resolver is already set" and poisons the type.
    //
    // This is listed here rather than read from an assembly attribute on purpose: reading custom attributes
    // (GetCustomAttributes) during the assembly-load path triggers reentrant assembly resolution and breaks
    // loading of every operator package. Matching by assembly name is metadata-only and safe.
    private static readonly HashSet<string> _assembliesWithOwnNativeResolver = new(StringComparer.Ordinal)
                                                                                   {
                                                                                       "Sdcb.FFmpeg", // locates its native libav* DLLs itself
                                                                                   };

    // this should only be called externally
    /// <summary>
    /// This should only be called externally or on non-root nodes of the same context
    /// It establishes a relationship between the assemblies and returns true
    /// if a dependency is formed between separate load contexts
    /// </summary>
    /// <param name="child"></param>
    /// <returns></returns>
    public bool AddReferenceTo(AssemblyTreeNode child)
    {
        lock (_assemblyLock)
        {
            if (_references.Contains(child))
            {
                return false;
            }

            lock (_unreferencedLock)
            {
                _unreferencedDllPaths?.Remove(child.Assembly.Location);
            }

            _references.Add(child);
        }

        return true;
    }

    /// <summary>
    /// Looks for a not-yet-loaded assembly file next to this node's assembly (and, for root nodes, in its
    /// runtime subfolders) and loads it into this node's context. Lookup is by file name, like the default
    /// .NET probing: opening files to read their assembly name is what the old full-folder scan did, and on
    /// a fresh shadow copy every first open pays a real-time AV scan - several seconds per package folder,
    /// serialized behind the resolution locks, which dominated editor startup.
    /// </summary>
    public bool TryFindUnreferenced(string nameToSearchFor, [NotNullWhen(true)] out AssemblyTreeNode? assembly)
    {
        lock (_assemblyLock)
        {
            lock (_unreferencedLock)
            {
                _unreferencedDllPaths ??= CollectDllFilePaths();

                var simpleName = GetSimpleName(nameToSearchFor);
                if (simpleName == null)
                {
                    assembly = null;
                    return false;
                }

                for (var index = _unreferencedDllPaths.Count - 1; index >= 0; index--)
                {
                    var path = _unreferencedDllPaths[index];
                    if (!string.Equals(Path.GetFileNameWithoutExtension(path), simpleName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        if (!File.Exists(path))
                        {
                            Log.Warning($"{_parentName}: Could not find assembly `{path}`");
                            continue;
                        }

                        // A native dll with a matching name throws here - skip it.
                        AssemblyName fileAssemblyName;
                        try
                        {
                            fileAssemblyName = AssemblyName.GetAssemblyName(path);
                        }
                        catch
                        {
                            continue;
                        }

                        if (fileAssemblyName.GetName() != nameToSearchFor)
                            continue;

                        var newAssembly = TixlAssemblyLoadContext.LoadAssembly(path, LoadContext);
                        assembly = new AssemblyTreeNode(newAssembly, LoadContext, false, false, _nativeResolver);
                        _unreferencedDllPaths.Remove(path);
                        AddReferenceTo(assembly);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Log.Error($"{_parentName}: Exception loading assembly: {e}");
                    }
                }
            }
        }

        assembly = null;
        return false;
    }

    private static string? GetSimpleName(string fullName)
    {
        try
        {
            return new AssemblyName(fullName).Name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Lists the dll files this node may load on demand - without opening any of them.
    /// </summary>
    private List<string> CollectDllFilePaths()
    {
        var result = new List<string>();
        var directory = Path.GetDirectoryName(Assembly.Location);
        var directoryInfo = new DirectoryInfo(directory!);
        if (!directoryInfo.Exists)
        {
            Log.Error($"{_parentName}: Directory does not exist: {directory}");
            return result;
        }

        if (!_searchNestedFolders)
        {
            // if we don't search nested folders, we can just check the current directory
            foreach (var file in directoryInfo.EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly))
            {
                AddIfNotSelf(file);
            }
        }
        else
        {
            // if we do search nested folders, we need to enumerate directories
            foreach (var info in directoryInfo.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
            {
                if (info is DirectoryInfo dir)
                {
                    var dirName = dir.Name;
                    if (_supportedRuntimeIdentifiers.Any(x => x == dirName))
                    {
                        // check for supported runtime
                        if (RuntimeInformation.RuntimeIdentifier != dirName)
                        {
                            // incompatible RID, skip
                            continue;
                        }
                    }

                    // get all files recursively
                    foreach (var file in dir.EnumerateFiles("*.dll", SearchOption.AllDirectories))
                    {
                        AddIfNotSelf(file);
                    }
                }
                else
                {
                    if (info.Extension != ".dll")
                        continue;
                    AddIfNotSelf((FileInfo)info);
                }
            }
        }

        return result;

        void AddIfNotSelf(FileInfo file)
        {
            try
            {
                if (file.FullName == Assembly.Location)
                    return;
            }
            catch (Exception e)
            {
                Log.Error($"{_parentName}: Exception getting assembly location: {e}");
            }

            result.Add(file.FullName);
        }
    }

    public bool TryFindExisting(string nameToSearchFor, [NotNullWhen(true)] out AssemblyTreeNode? assembly)
    {
        if (NameStr == nameToSearchFor)
        {
            assembly = this;
            return true;
        }

        lock (_assemblyLock)
        {
            foreach (var node in _references)
            {
                if (node.TryFindExisting(nameToSearchFor, out assembly))
                    return true;
            }
        }

        assembly = null;
        return false;
    }
}
