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

    internal readonly record struct DllReference(string Path, string Name, AssemblyName AssemblyName);

    private readonly List<DllReference> _unreferencedDlls = [];
    private bool _collectedUnreferencedDlls;

    private readonly Lock _unreferencedLock = new();
    private readonly DllImportResolver? _nativeResolver;

    private static readonly string[] _supportedRuntimeIdentifiers = ["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];


    private readonly string _parentName;
    private readonly bool _searchNestedFolders;

    // warning : not thread safe, must be wrapped in a lock around _assemblyLock
    public AssemblyTreeNode(Assembly assembly, AssemblyLoadContext parent, bool searchNestedFolders, bool canSearchDlls, DllImportResolver? nativeResolver)
    {
        Assembly = assembly;
        if (nativeResolver != null)
            NativeLibrary.SetDllImportResolver(assembly, nativeResolver);

        _nativeResolver = nativeResolver;
        Name = assembly.GetName();
        NameStr = Name.GetName();
        _searchNestedFolders = searchNestedFolders;

        _parentName = parent.Name!;
        LoadContext = parent;

        if (!canSearchDlls)
        {
            _collectedUnreferencedDlls = true;
        }

        // if (debug && !node.NameStr.StartsWith("System")) // don't log system assemblies - too much log spam for things that are probably not error-prone
        //Log.Debug($"{parent}: Loaded assembly {NameStr} from {assembly.Location}");
    }

    private DllReference Reference => new(Assembly.Location, NameStr, Name);

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
                if (_collectedUnreferencedDlls)
                {
                    _ = _unreferencedDlls.Remove(child.Reference);
                }
            }

            _references.Add(child);
        }

        return true;
    }

    public bool TryFindUnreferenced(string nameToSearchFor, [NotNullWhen(true)] out AssemblyTreeNode? assembly)
    {
        // check unreferenced dlls
        lock (_assemblyLock)
        {
            lock (_unreferencedLock)
            {
                if (!_collectedUnreferencedDlls)
                {
                    FindUnreferencedDllFiles();
                    _collectedUnreferencedDlls = true;
                }

                var unreferencedDlls = _unreferencedDlls;
                for (var index = unreferencedDlls.Count - 1; index >= 0; index--)
                {
                    var dll = unreferencedDlls[index];
                    if (dll.Name != nameToSearchFor)
                        continue;

                    try
                    {
                        if (!File.Exists(dll.Path))
                        {
                            Log.Warning($"{_parentName}: Could not find assembly `{dll.Path}`");
                            continue;
                        }

                        var newAssembly = TixlAssemblyLoadContext.LoadAssembly(dll.Path, LoadContext);
                        assembly = new AssemblyTreeNode(newAssembly, LoadContext, false, false, _nativeResolver);
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

        /*lock (_assemblyLock)
        {
            // check those of our references
            foreach (var node in _references)
            {
                if (node.LoadContext != LoadContext)
                    continue;

                // search recursively
                if (node.TryFindUnreferenced(nameToSearchFor, out assembly))
                    return true;
            }
        }*/

        assembly = null;
        return false;
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

    void FindUnreferencedDllFiles()
    {
        if(_collectedUnreferencedDlls)
            throw new InvalidOperationException($"{_parentName}: Unreferenced DLLs already collected, cannot collect again.");
        
        // locate "not used" dlls in the directory without loading them
        var directory = Path.GetDirectoryName(Assembly.Location);
        var directoryInfo = new DirectoryInfo(directory!);
        if (!directoryInfo.Exists)
        {
            Log.Error($"{_parentName}: Directory does not exist: {directory}");
            return;
        }

        if (!_searchNestedFolders)
        {
            // if we don't search nested folders, we can just check the current directory
            foreach (var file in directoryInfo.EnumerateFiles("*.dll", SearchOption.TopDirectoryOnly))
            {
                CheckAssemblyFileAndAdd(file);
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
                        CheckAssemblyFileAndAdd(file);
                    }
                }
                else
                {
                    if (info.Extension != ".dll")
                        continue;
                    CheckAssemblyFileAndAdd((FileInfo)info);
                }
            }
        }

        return;

        void CheckAssemblyFileAndAdd(FileInfo file)
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

            foreach (var dep in _references)
            {
                try
                {
                    if (file.FullName == dep.Assembly.Location)
                    {
                        return;
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"{_parentName}: Exception getting assembly location: {e}");
                }
            }

            AssemblyName assemblyName;
            try
            {
                assemblyName = AssemblyName.GetAssemblyName(file.FullName);
            }
            catch
            {
                return;
            }

            var reference = new DllReference(file.FullName, assemblyName.GetName(), assemblyName);
            _unreferencedDlls.Add(reference);
        }
    }
}