#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.UserData;

namespace T3.Core.Compilation;

/// <summary>
/// This is the actual place where assemblies are loaded and dependencies are resolved on a per-dll level.
/// Inheriting from <see cref="AssemblyLoadContext"/> allows us to load assemblies in a custom way, which is required
/// as assemblies are loaded from different locations for each package.
///
/// Each package has its own <see cref="TixlAssemblyLoadContext"/> that is used to load the assemblies of that package. If a package relies on another package
/// from a CSProj-level, the dependency's load context and dlls are added to the dependent's load context such that the dependent's dlls can be loaded
/// referencing the types provided by the dependency.
///
/// For example, the LibEditor package has a dependency on Lib. When LibEditor is loaded, the Lib package is loaded first via LibEditor's load context. Then
/// the loading procedure continues until LibEditor is fully loaded with all its dependencies.
///
/// Unfortunately this process is very complex, and is not thoroughly tested with large dependency chains.
/// </summary>
internal sealed partial class TixlAssemblyLoadContext : AssemblyLoadContext
{
    public event EventHandler? UnloadBegan;
    internal event EventHandler? UnloadBeganInternal;
    private readonly Lock _dependencyLock = new();

    internal AssemblyTreeNode? Root { get; private set; }

    private readonly List<AssemblyLoadContext> _dependencyContexts = [];
    private static readonly List<AssemblyTreeNode> _coreNodes = [];

    private static readonly List<TixlAssemblyLoadContext> _loadContexts = [];
    private static readonly Lock _loadContextLock = new();
    private readonly DllImportResolver _dllImportResolver;
    private bool _unloaded;

    private static List<AssemblyTreeNode> CoreNodes => _coreNodes;
    public readonly string MainDirectory;
    private static readonly string _rootShadowCopyDir = Path.Combine(FileLocations.TempFolder, "ShadowCopy");
    private readonly string _shadowCopyDirectory;
    private readonly bool _shouldCopyBinaries;
    
    static TixlAssemblyLoadContext()
    {
        try
        {
            if (Directory.Exists(_rootShadowCopyDir))
            {
                Directory.Delete(_rootShadowCopyDir, true);
            }
        }
        catch (Exception e)
        {
            // This warning happens quite frequently
            Log.Debug($"Failed to delete shadow copy directory {_rootShadowCopyDir}: {e.Message}");
        }
        
        Directory.CreateDirectory(_rootShadowCopyDir);

        (AssemblyLoadContext Context, (Assembly Assembly, AssemblyName name)[] assemblies)[]? allAssemblies = All
           .Select(ctx => (
                              ctx: ctx,
                              assemblies: ctx.Assemblies
                                             .Select(x => (asm: x, name: x.GetName()))
                                             .ToArray()))
           .ToArray();

        // create "root" nodes for each assembly context - one per context and one per directory for each context
        foreach (var ctxGroup in allAssemblies)
        {
            List<string> directories = new();
            foreach (var assemblyDef in ctxGroup.assemblies)
            {
                string? directory;

                try
                {
                    directory = Path.GetDirectoryName(assemblyDef.Assembly.Location);
                }
                catch
                {
                    continue;
                }

                if (directory == null || directories.Contains(directory))
                    continue;

                directories.Add(directory);
                var node = new AssemblyTreeNode(assemblyDef.Assembly, ctxGroup.Context, false, true, null); // no native resolver bc they already have one
                _coreNodes.Add(node);
            }
        }

        DllImportResolver resolver = NativeDllResolverStatic;

        // add references to each core node where applicable, reusing existing nodes to create the tree
        for (var index = 0; index < _coreNodes.Count; index++)
        {
            var node = _coreNodes[index];
            var dependencies = node.Assembly.GetReferencedAssemblies();
            foreach (var dependencyName in dependencies)
            {
                foreach (var ctxGroup in allAssemblies)
                {
                    foreach (var asmAndName in ctxGroup.assemblies)
                    {
                        if (asmAndName.name != dependencyName)
                            continue;

                        AssemblyTreeNode? depNode = null;
                        var nameStr = dependencyName.GetName();
                        foreach (var coreNode in _coreNodes)
                        {
                            if (coreNode.TryFindExisting(nameStr, out depNode))
                                break;
                        }

                        depNode ??= new AssemblyTreeNode(asmAndName.Assembly, ctxGroup.Context, false, false, resolver);

                        node.AddReferenceTo(depNode);
                    }
                }
            }
        }
    }

    internal TixlAssemblyLoadContext(string assemblyName, string directory, bool isReadOnly) :
        base(assemblyName, true)
    {
        if(ProjectSettings.Config.LogCompilationDetails)
            Log.Debug($"{Name}: Creating new assembly load context for {assemblyName}");

        if (ProjectSettings.Config.LogAssemblyLoadingDetails)
        {
            Unloading += (_) => { Log.Debug($"{Name!}: Unloading assembly context"); };
        }

        lock (_loadContextLock)
        {
            _loadContexts.Add(this);
        }

        MainDirectory = directory;
        _shadowCopyDirectory = Path.Combine(_rootShadowCopyDir, Name!, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        _shouldCopyBinaries = !isReadOnly;
        _dllImportResolver = NativeDllResolver;

        var path = Path.Combine(directory, Name!) + ".dll";

        try
        {
            var asm = LoadAssembly(path, this);
            Root = new AssemblyTreeNode(asm, this, true, true, _dllImportResolver);
            
            if(ProjectSettings.Config.LogAssemblyLoadingDetails)
                Log.Debug($"{Name} : Loaded root assembly {asm.FullName} from '{path}'");
            
            _dependencyContext = Microsoft.Extensions.DependencyModel.DependencyContext.Load(Root!.Assembly);
        }
        catch (Exception e)
        {
            Log.Error($"{Name!}: Failed to load root assembly {Name}: {e}");
        }
    }

    /// <summary>
    /// A single place to define how we're loading managed assemblies.
    /// An unnecessary abstraction, but useful for testing different loading strategies.
    /// </summary>
    /// <param name="path">The path to the managed dll</param>
    /// <param name="ctx">The context to load the dll into</param>
    /// <returns>The loaded assembly</returns>
    /// <inheritdoc cref="AssemblyLoadContext.LoadFromAssemblyPath"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Assembly LoadAssembly(string path, AssemblyLoadContext ctx)
    {
        // try a shadow copy first
        if (ctx is not TixlAssemblyLoadContext { _shouldCopyBinaries: true } tixlCtx)
        {
            if(ProjectSettings.Config.LogAssemblyLoadingDetails)
                Log.Debug($"{ctx.Name}: Loading assembly from '{path}'...");
            
            return ctx.LoadFromAssemblyPath(path);
        }
        
        var shadowCopyDirectory = tixlCtx._shadowCopyDirectory;
        if (tixlCtx._shouldCopyBinaries && !Directory.Exists(shadowCopyDirectory))
        {
            Directory.CreateDirectory(shadowCopyDirectory);
            
            if(ProjectSettings.Config.LogAssemblyLoadingDetails)
                Log.Debug($"{tixlCtx.Name!}: Created shadow copy directory at {shadowCopyDirectory}");
            
            // copy all dlls recursively in the main directory to the shadow copy directory
            // being sure to ignore symbol and resource folders
            CopyFilesInDirectory(tixlCtx.MainDirectory, tixlCtx.MainDirectory, shadowCopyDirectory, false);

            // now search subfolders, excluding ignored folders
            foreach (var dir in Directory.EnumerateDirectories(tixlCtx.MainDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                var directoryName = Path.GetFileName(dir);
                if (directoryName.StartsWith('.') ||
                    directoryName.Equals("bin", StringComparison.Ordinal) ||
                    directoryName.Equals("obj", StringComparison.Ordinal) ||
                    directoryName.Equals(FileLocations.SymbolsSubfolder, StringComparison.Ordinal) ||
                    directoryName.Equals(FileLocations.SymbolUiSubFolder, StringComparison.Ordinal) ||
                    directoryName.Equals(FileLocations.ResourcesSubfolder, StringComparison.Ordinal) ||
                    directoryName.Equals(FileLocations.SourceCodeSubFolder, StringComparison.Ordinal))
                {
                    continue; // skip hidden, bin, obj and resources folders
                }

                CopyFilesInDirectory(tixlCtx.MainDirectory, dir, shadowCopyDirectory, true);
            }
        }

        // replace path with the shadow copy directory
        var relativePath = Path.GetRelativePath(tixlCtx.MainDirectory, path);
        path = Path.Combine(shadowCopyDirectory, relativePath);

        if(ProjectSettings.Config.LogAssemblyLoadingDetails)
            Log.Debug($"{ctx.Name}: Loading assembly from '{path}'...");
        
        return ctx.LoadFromAssemblyPath(path);
        
        static void CopyFilesInDirectory(string rootDirectory, string directory, string shadowCopyDirectory, bool recursive)
        {
            var newDirectory = directory != rootDirectory 
                                   ? Path.Combine(shadowCopyDirectory, Path.GetRelativePath(rootDirectory, directory)) 
                                   : shadowCopyDirectory;
            Directory.CreateDirectory(newDirectory);
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!file.EndsWith(".dll") &&
                    !file.EndsWith(".exe") &&
                    !file.EndsWith(".pdb") &&
                    !file.EndsWith(".so") &&
                    !file.EndsWith(".xml") &&
                    !file.EndsWith(".json"))
                {
                    continue;
                }
                    
                var newPath = Path.Combine(newDirectory, Path.GetFileName(file));
                File.Copy(file, newPath, true);
            }
            if (!recursive)
                return;
                
            foreach (var subDir in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                CopyFilesInDirectory(rootDirectory, subDir, shadowCopyDirectory, true);
            }
        }
    }

    // called if Load method returns null - searches other contexts and nuget packages
    private Assembly? OnResolving(AssemblyName asmName)
    {
        var name = asmName.GetName();

        #if DEBUG
        if (_unloaded)
        {
            Log.Error($"{Name!}: Attempted to resolve assembly {name} after unload");
            return null;
        }

        #endif

        // try other assembly contexts
        lock (_loadContextLock)
        {
            // try to find existing in others
            foreach (var ctx in _loadContexts)
            {
                if (ctx == this)
                    continue;

                var root = ctx.Root;

                if (root == null)
                    continue;

                if (root.TryFindExisting(name, out var asmNode))
                {
                    // add the dependency to our context
                    AddDependency(asmNode);
                    LogResolution(asmNode.Assembly, asmName);
                    return asmNode.Assembly;
                }
            }

            // try to find unreferenced in others
            foreach (var ctx in _loadContexts)
            {
                if (ctx == this)
                    continue;

                var root = ctx.Root;

                if (root == null)
                    continue;

                if (root.TryFindUnreferenced(name, out var asmNode))
                {
                    // add the dependency to our context
                    AddDependency(asmNode);
                    LogResolution(asmNode.Assembly, asmName);
                    return asmNode.Assembly;
                }
            }
        }

        // check nuget packages
        var result =  SearchNugetForAssemblies(asmName, name);
        LogResolution(result, asmName);
        return result;

        void LogResolution(Assembly? resultAsm, AssemblyName searchName)
        {
            if (!ProjectSettings.Config.LogAssemblyLoadingDetails)
                return;
            
            if (resultAsm != null)
            {
                // check versions of the assembly - if different, log a warning.
                // todo: actually do something with this information later
                if (ProjectSettings.Config.LogAssemblyVersionMismatches)
                {
                    var assemblyNameOfResult = resultAsm.GetName();

                    if (assemblyNameOfResult.Version != searchName.Version)
                    {
                        Log.Warning($"Assembly {searchName.Name} loaded with different version: {assemblyNameOfResult.Version} vs {searchName.Version}");
                    }
                }
                
                if (resultAsm.GetName().Name != searchName.Name)
                {
                    Log.Error($"{Name!}: Resolved assembly name mismatch: {resultAsm.GetName().Name} != {searchName.Name}");
                }
                else
                {
                    Log.Debug($"{Name!}: Resolved assembly {resultAsm.GetName().Name}");
                }
            }
            else
            {
                Log.Error($"{Name!}: Failed to resolve assembly '{searchName.Name}'");
            }
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        #if DEBUG
        if (_unloaded)
        {
            Log.Error($"{Name!}: Attempted to load assembly {assemblyName} after unload");
            return null;
        }
        #endif

        var name = assemblyName.GetName();

        foreach (var coreRef in CoreNodes)
        {
            if (coreRef.TryFindExisting(name, out var coreAssembly))
            {
                AddDependency(coreAssembly);
                return coreAssembly.Assembly;
            }

            if (coreRef.TryFindUnreferenced(name, out coreAssembly))
            {
                AddDependency(coreAssembly);
                return coreAssembly.Assembly;
            }
        }

        if (Root is null)
        {
            Log.Error($"{Name!}: Root is null, cannot resolve assembly {name}");
            return null;
        }

        if (Root!.TryFindExisting(name, out var node))
        {
            AddDependency(node);
            return node.Assembly;
        }

        if (Root!.TryFindUnreferenced(name, out node))
        {
            AddDependency(node);
            return node.Assembly;
        }

        return OnResolving(assemblyName);
    }

    private static IntPtr NativeDllResolverStatic(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        //Log.Debug($"{assembly.FullName}: Resolving native dll {libraryName} for assembly {assembly.FullName}");
        const DllImportSearchPath defaultSearchPath = DllImportSearchPath.AssemblyDirectory
                                                      | DllImportSearchPath.UseDllDirectoryForDependencies
                                                      | DllImportSearchPath.ApplicationDirectory;

        var search = searchPath ?? defaultSearchPath;
        if (NativeLibrary.TryLoad(libraryName, assembly, search, out var handle))
        {
            //Log.Debug($"{assembly.FullName!}: Successfully resolved native dll {libraryName}");
            return handle;
        }

        Log.Error($"{assembly.FullName!}: Failed to resolve native dll {libraryName} relative to assembly '{assembly.Location}'");
        return IntPtr.Zero;
    }
    
    private IntPtr NativeDllResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        //Log.Debug($"{assembly.FullName}: Resolving native dll {libraryName} for assembly {assembly.FullName}");
        const DllImportSearchPath defaultSearchPath = DllImportSearchPath.AssemblyDirectory
                                                      | DllImportSearchPath.UseDllDirectoryForDependencies
                                                      | DllImportSearchPath.ApplicationDirectory;

        var search = searchPath ?? defaultSearchPath;
        if (NativeLibrary.TryLoad(libraryName, assembly, search, out var handle))
        {
            //Log.Debug($"{assembly.FullName!}: Successfully resolved native dll {libraryName}");
            return handle;
        }

        if (assembly != Root!.Assembly && NativeLibrary.TryLoad(libraryName, Root.Assembly, search, out handle))
        {
            return handle;
        }

        Log.Error($"{assembly.FullName!}: Failed to resolve native dll {libraryName} relative to assembly '{assembly.Location}'");
        return IntPtr.Zero;
    }
    
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // manual dll resolution with the potential of having nothing but the name of the dll sans the extension
        string fullPath;
        bool pathFullyQualified = false;
        if (Path.IsPathFullyQualified(unmanagedDllName))
        {
            fullPath = unmanagedDllName;
            pathFullyQualified = true;
        }
        else if (!unmanagedDllName.EndsWith(".dll"))
        {
            fullPath = Path.Combine(MainDirectory, unmanagedDllName + ".dll");
        }
        else
        {
            fullPath = Path.Combine(MainDirectory, unmanagedDllName);
        }

        if (File.Exists(fullPath))
        {
            try
            {
                return LoadUnmanagedDllFromPath(fullPath);
            }
            catch (Exception e)
            {
                Log.Error($"{Name!}: Failed to load unmanaged dll {unmanagedDllName} from path {fullPath}: {e}");
                return IntPtr.Zero;
            }
        }

        // check for the .so extension on linux/mac
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
        {
            var unixPath = pathFullyQualified
                               ? Path.ChangeExtension(fullPath, ".so")
                               : Path.Combine(MainDirectory, unmanagedDllName + ".so");

            if (File.Exists(unixPath))
            {
                try
                {
                    return LoadUnmanagedDllFromPath(unixPath);
                }
                catch (Exception e)
                {
                    Log.Error($"{Name!}: Failed to load unmanaged dll {unmanagedDllName} from unix path {unixPath}: {e}");
                    return IntPtr.Zero;
                }
            }
        }

        return IntPtr.Zero;
    }

    private void AddDependency(AssemblyTreeNode node)
    {
        _ = Root!.AddReferenceTo(node);

        var ctx = node.LoadContext;
        if (ctx == this)
            return;

        lock (_dependencyLock)
        {
            if (!_dependencyContexts.Contains(ctx))
            {
                // subscribe to the unload event of the dependency context
                if (ctx is TixlAssemblyLoadContext tixlCtx)
                {
                    tixlCtx.UnloadBeganInternal += OnDependencyUnloaded;
                }
                else
                {
                    ctx.Unloading += OnNonTixlDependencyUnloaded;
                }

                _dependencyContexts.Add(ctx);
                if(ProjectSettings.Config.LogAssemblyLoadingDetails)
                    Log.Debug($"{Name!}: Added dependency {node.Name} from {ctx.Name}");
            }
        }
    }

    private void OnNonTixlDependencyUnloaded(AssemblyLoadContext ctx)
    {
        ctx.Unloading -= OnNonTixlDependencyUnloaded;
        RemoveDependency(ctx);
    }

    private void OnDependencyUnloaded(object? sender, EventArgs e)
    {
        var ctx = (TixlAssemblyLoadContext)sender!;
        ctx.UnloadBeganInternal -= OnDependencyUnloaded;
        RemoveDependency(ctx);
    }

    private void RemoveDependency(AssemblyLoadContext ctx)
    {
        lock (_dependencyLock)
        {
            _dependencyContexts.Remove(ctx);
            BeginUnload(); // begin unloading ourselves too
        }
    }

    internal void BeginUnload()
    {
        if (_unloaded)
        {
            var stackTrace = new System.Diagnostics.StackTrace();
            var frames = stackTrace.GetFrames();

            if (frames is {Length: > 0} && frames.Any(f =>
                                                 f.GetMethod()?.DeclaringType?.FullName == "System.Runtime.Loader.AssemblyLoadContext"
                                                 && f.GetMethod()?.Name == "OnProcessExit"))
            {
                Log.Debug($"{Name}: BeginUnload called during shutdown but was already unloaded.");
                return; // Suppress exception during shutdown
            }
            
            throw new InvalidOperationException($"Assembly context {Name} already unloaded");
        }
        _unloaded = true;

        lock (_dependencyLock)
        {
            // unsubscribe from all our dependencies
            for (int i = _dependencyContexts.Count - 1; i >= 0; i--)
            {
                var ctx = _dependencyContexts[i];
                if (ctx is TixlAssemblyLoadContext tixlCtx)
                {
                    tixlCtx.UnloadBeganInternal -= OnDependencyUnloaded;
                }
                else
                {
                    ctx.Unloading -= OnNonTixlDependencyUnloaded;
                }

                _dependencyContexts.RemoveAt(i);
            }
        }

        lock (_loadContextLock)
        {
            _loadContexts.Remove(this);
        }

        Root = null; // dereference our assembly as we will need to reload it 

        try
        {
            UnloadBeganInternal?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Log.Error($"{Name!}: Exception thrown on assembly unload (internal): {e}");
        }

        try
        {
            UnloadBegan?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Log.Error($"{Name!}: Exception thrown on assembly unload: {e}");
        }

        Unload();
    }
}

internal static class AssemblyNameExtensions
{
    public static string GetName(this AssemblyName asmName) => asmName.FullName;
}