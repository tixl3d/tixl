#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Settings;

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

    private static string ShadowCopyRootFolder => Path.Combine(FileLocations.TempFolder, "ShadowCopy");
    private static string RootShadowCopyDir => Path.Combine(ShadowCopyRootFolder, $"{Environment.ProcessId}");

    private readonly string _shadowCopyDirectory;
    private readonly bool _shouldCopyBinaries;


    static TixlAssemblyLoadContext()
    {
        CleanUpStaleShadowCopies();

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
        if (CoreSettings.Config.LogCompilationDetails)
            Log.Debug($"{Name}: Creating new assembly load context for {assemblyName}");

        if (CoreSettings.Config.LogAssemblyLoadingDetails)
        {
            Unloading += (_) => { Log.Debug($"{Name!}: Unloading assembly context"); };
        }

        lock (_loadContextLock)
        {
            _loadContexts.Add(this);
        }

        MainDirectory = directory;
        _shouldCopyBinaries = !isReadOnly;
        _shadowCopyDirectory = _shouldCopyBinaries ? ComputeShadowCopyDirectory(directory, Name!) : string.Empty;
        _dllImportResolver = NativeDllResolver;

        var path = Path.Combine(directory, Name!) + ".dll";

        try
        {
            var asm = LoadAssembly(path, this);
            Root = new AssemblyTreeNode(asm, this, true, true, _dllImportResolver);

            if (CoreSettings.Config.LogAssemblyLoadingDetails)
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
            if (CoreSettings.Config.LogAssemblyLoadingDetails)
                Log.Debug($"{ctx.Name}: Loading assembly from '{path}'...");

            return ctx.LoadFromAssemblyPath(path);
        }

        var shadowCopyDirectory = tixlCtx._shadowCopyDirectory;
        if (!Directory.Exists(shadowCopyDirectory))
        {
            CreateShadowCopy(tixlCtx.MainDirectory, shadowCopyDirectory);
        }
        else
        {
            // Keep a reused cache folder young so the startup cleanup retires old fingerprints, not this one.
            try
            {
                Directory.SetLastWriteTimeUtc(shadowCopyDirectory, DateTime.UtcNow);
            }
            catch (IOException)
            {
            }
        }

        // Map the path into the shadow copy - but only for files inside the package folder. Candidates
        // discovered in the shadow copy itself (unreferenced-dll lookups enumerate the loaded assembly's
        // own directory) must load as-is: re-mapping them built a ..-relative path that only resolved
        // correctly by accident while the shadow folder happened to sit at the same depth as bin/.
        var relativePath = Path.GetRelativePath(tixlCtx.MainDirectory, path);
        if (!relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath))
        {
            path = Path.Combine(shadowCopyDirectory, relativePath);
        }

        if (CoreSettings.Config.LogAssemblyLoadingDetails)
            Log.Debug($"{ctx.Name}: Loading assembly from '{path}'...");

        return ctx.LoadFromAssemblyPath(path);
    }

    /// <summary>
    /// Shadow copies live in per-package folders named by a content fingerprint, so unchanged packages
    /// reuse the copy of a previous run instead of re-copying (and re-AV-scanning) hundreds of megabytes
    /// on every editor start. <see cref="CoreSettings.ConfigData.UseProcessScopedShadowCopies"/> restores
    /// the previous per-process behaviour as an escape hatch, which is also the fallback when the
    /// fingerprint can't be computed.
    /// </summary>
    private static string ComputeShadowCopyDirectory(string mainDirectory, string contextName)
    {
        if (!CoreSettings.Config.UseProcessScopedShadowCopies)
        {
            try
            {
                var fingerprint = ComputeContentFingerprint(mainDirectory);
                return Path.Combine(ShadowCopyRootFolder, contextName, fingerprint);
            }
            catch (Exception e)
            {
                Log.Warning($"{contextName}: Falling back to a process-scoped shadow copy: {e.Message}");
            }
        }

        return Path.Combine(RootShadowCopyDir, contextName, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
    }

    /// <summary>
    /// Stable id for "this exact set of binaries": relative path, size and write time of every file the
    /// shadow copy would include. Any rebuild rewrites outputs and thus changes it, so an existing folder
    /// with this name can be trusted as complete and current. Metadata only - no file is opened.
    /// </summary>
    private static string ComputeContentFingerprint(string mainDirectory)
    {
        var entries = new List<string>();
        foreach (var (relativePath, file) in EnumerateShadowCopyFiles(mainDirectory))
        {
            entries.Add($"{relativePath}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
        }

        entries.Sort(StringComparer.Ordinal);

        var builder = new StringBuilder("v1"); // bump to invalidate all cached copies when the layout changes
        foreach (var entry in entries)
        {
            builder.Append('\n').Append(entry);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>
    /// Copies the package binaries into <paramref name="targetDirectory"/> via a process-private staging
    /// folder and an atomic rename: a fingerprint-named folder therefore either exists complete or not at
    /// all - a crash mid-copy or two editors starting concurrently can't leave a half-populated cache
    /// that a later start would trust.
    /// </summary>
    private static void CreateShadowCopy(string mainDirectory, string targetDirectory)
    {
        var copyStopwatch = Stopwatch.StartNew();
        var stagingDirectory = $"{targetDirectory}.staging-{Environment.ProcessId}";

        try
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, true);

            Directory.CreateDirectory(stagingDirectory);

            foreach (var (relativePath, file) in EnumerateShadowCopyFiles(mainDirectory))
            {
                var destination = Path.Combine(stagingDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                file.CopyTo(destination, true);
                ShadowCopyStatistics.AddBytes(file.Length);
            }

            Directory.Move(stagingDirectory, targetDirectory);

            if (CoreSettings.Config.LogAssemblyLoadingDetails)
                Log.Debug($"Created shadow copy at {targetDirectory}");
        }
        catch (IOException) when (Directory.Exists(targetDirectory))
        {
            // another editor instance completed the same fingerprint first - use theirs
            TryDeleteDirectory(stagingDirectory);
        }

        ShadowCopyStatistics.AddMilliseconds(copyStopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// The files a shadow copy consists of: binaries and their sidecars next to the root assembly, plus
    /// those in subfolders that aren't symbol/asset/source content. Shared by the fingerprint and the copy
    /// so the two can never disagree.
    /// </summary>
    private static IEnumerable<(string RelativePath, FileInfo File)> EnumerateShadowCopyFiles(string mainDirectory)
    {
        var mainDirectoryInfo = new DirectoryInfo(mainDirectory);
        foreach (var file in mainDirectoryInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
        {
            if (IsShadowCopyFileType(file.Name))
                yield return (file.Name, file);
        }

        foreach (var dir in mainDirectoryInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
        {
            var directoryName = dir.Name;
            if (directoryName.StartsWith('.') ||
                directoryName.Equals("bin", StringComparison.Ordinal) ||
                directoryName.Equals("obj", StringComparison.Ordinal) ||
                directoryName.Equals(FileLocations.ReleaseSymbolsSubfolder, StringComparison.Ordinal) ||
                directoryName.Equals(FileLocations.SymbolUiSubFolder, StringComparison.Ordinal) ||
                directoryName.Equals(FileLocations.AssetsSubfolder, StringComparison.Ordinal) ||
                directoryName.Equals(FileLocations.SourceCodeSubFolder, StringComparison.Ordinal))
            {
                continue; // skip hidden, bin, obj and resources folders
            }

            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (IsShadowCopyFileType(file.Name))
                    yield return (Path.GetRelativePath(mainDirectory, file.FullName), file);
            }
        }
    }

    private static bool IsShadowCopyFileType(string fileName)
    {
        return fileName.EndsWith(".dll") ||
               fileName.EndsWith(".exe") ||
               fileName.EndsWith(".pdb") ||
               fileName.EndsWith(".so") ||
               fileName.EndsWith(".xml") ||
               fileName.EndsWith(".json");
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
        var result = SearchNugetForAssemblies(asmName, name);
        LogResolution(result, asmName);
        return result;

        void LogResolution(Assembly? resultAsm, AssemblyName searchName)
        {
            if (!CoreSettings.Config.LogAssemblyLoadingDetails)
                return;

            if (resultAsm != null)
            {
                // check versions of the assembly - if different, log a warning.
                // todo: actually do something with this information later
                if (CoreSettings.Config.LogAssemblyVersionMismatches)
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
        // Resolution can stall behind the global locks (directory scans, AV on fresh files). Surface
        // slow resolutions when loading details are requested, so such stalls are attributable from
        // the log instead of looking like unexplained silence.
        if (!CoreSettings.Config.LogAssemblyLoadingDetails)
            return LoadCore(assemblyName);

        var stopwatch = Stopwatch.StartNew();
        var result = LoadCore(assemblyName);
        if (stopwatch.ElapsedMilliseconds > 100)
            Log.Debug($"{Name!}: Resolving assembly '{assemblyName.Name}' took {stopwatch.ElapsedMilliseconds}ms ({(result != null ? "found" : "not found")})");

        return result;
    }

    private Assembly? LoadCore(AssemblyName assemblyName)
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
                if (CoreSettings.Config.LogAssemblyLoadingDetails)
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

            if (frames is { Length: > 0 } && frames.Any(f =>
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

    /// <summary>
    /// Each run shadow-copies its assemblies into a per-process subfolder; closed or crashed instances never
    /// clean theirs up, so they pile up across rebuilds. Remove every folder that isn't owned by a still-running
    /// process — those are skipped because their assemblies are loaded and locked.
    /// </summary>
    private const int KeptShadowCopiesPerPackage = 3;

    private static void CleanUpStaleShadowCopies()
    {
        // Runs in the static constructor: any escaping exception would poison the type initializer
        // and make every project fail to load, so cleanup must never throw.
        try
        {
            var rootFolder = ShadowCopyRootFolder;
            if (!Directory.Exists(rootFolder))
                return;

            var currentProcessId = Environment.ProcessId;
            foreach (var topLevelFolder in Directory.EnumerateDirectories(rootFolder))
            {
                var folderName = Path.GetFileName(topLevelFolder);
                if (TryCleanUpLeftoverFolder(topLevelFolder, folderName, currentProcessId))
                    continue;

                if (int.TryParse(folderName, out var processId))
                {
                    // per-process layout: pre-content-keyed, or the UseProcessScopedShadowCopies fallback
                    if (processId != currentProcessId && IsProcessRunning(processId))
                        continue;

                    RetireDirectory(topLevelFolder);
                    continue;
                }

                CleanUpPackageShadowCopies(topLevelFolder, currentProcessId);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to clean up stale shadow copies: {e.Message}");
        }
    }

    /// <summary>
    /// Keeps the most recently used <see cref="KeptShadowCopiesPerPackage"/> fingerprint folders of one
    /// package and retires the rest. Reuse refreshes a folder's write time, so actively used caches stay.
    /// </summary>
    private static void CleanUpPackageShadowCopies(string packageFolder, int currentProcessId)
    {
        List<(string Path, DateTime LastWriteUtc)> fingerprintFolders = [];
        foreach (var folder in Directory.EnumerateDirectories(packageFolder))
        {
            var name = Path.GetFileName(folder);
            if (TryCleanUpLeftoverFolder(folder, name, currentProcessId))
                continue;

            fingerprintFolders.Add((folder, Directory.GetLastWriteTimeUtc(folder)));
        }

        fingerprintFolders.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
        for (var index = KeptShadowCopiesPerPackage; index < fingerprintFolders.Count; index++)
        {
            RetireDirectory(fingerprintFolders[index].Path);
        }
    }

    /// <summary>
    /// Removes an interrupted copy (*.staging-pid) or an interrupted delete (*.trash-pid) once its owner
    /// process is gone. Returns true when the folder was one of those, whether or not it could be removed.
    /// </summary>
    private static bool TryCleanUpLeftoverFolder(string folder, string folderName, int currentProcessId)
    {
        if (!folderName.Contains(".staging-", StringComparison.Ordinal)
            && !folderName.Contains(".trash-", StringComparison.Ordinal))
        {
            return false;
        }

        var pidPart = folderName[(folderName.LastIndexOf('-') + 1)..];
        if (int.TryParse(pidPart, out var ownerProcessId)
            && ownerProcessId != currentProcessId
            && IsProcessRunning(ownerProcessId))
        {
            return true; // its owner is still working with it
        }

        TryDeleteDirectory(folder);
        return true;
    }

    /// <summary>
    /// Deletes a shadow-copy folder without ever leaving a half-deleted folder under its original name:
    /// rename first (fails cleanly while any file inside is still mapped by a running editor), then delete
    /// the renamed folder. An interrupted delete leaves only a *.trash-* folder that is never reused.
    /// </summary>
    private static void RetireDirectory(string directory)
    {
        var trashPath = $"{directory}.trash-{Environment.ProcessId}";
        try
        {
            Directory.Move(directory, trashPath);
        }
        catch (Exception)
        {
            return; // still in use (or already retired) - keep it for a later cleanup
        }

        TryDeleteDirectory(trashPath);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception e)
        {
            Log.Debug($"Failed to delete shadow copy directory {directory}: {e.Message}");
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception)
        {
            // E.g. access denied when the pid was reused by an elevated or protected process.
            // If we can't tell, assume it's running and keep its folder - it gets cleaned next run.
            return true;
        }
    }
}

internal static class AssemblyNameExtensions
{
    public static string GetName(this AssemblyName asmName) => asmName.FullName;
}