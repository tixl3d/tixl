#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Compilation;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.UserData;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Compilation;

/// <summary>
/// handles the creation, loading, unloading, and general management of projects and packages
/// todo: simplify/refactor as it's pretty confusing
/// </summary>
internal static partial class ProjectSetup
{
    public const string EnvironmentVariableName = "T3_ASSEMBLY_PATH";
    static ProjectSetup()
    {
        SetEnvironmentVariable(EnvironmentVariableName, RuntimeAssemblies.CoreDirectory);
    }


    public static string ToBasicVersionString(this Version versionPrefix)
    {
        return $"{versionPrefix.Major}.{versionPrefix.Minor}.{versionPrefix.Build}";
    }

    
    private static void SetEnvironmentVariable(string envVar, string envValue)
    {
        Environment.SetEnvironmentVariable(envVar, envValue, EnvironmentVariableTarget.Process);

        // todo - this will not work on linux
        var existing = Environment.GetEnvironmentVariable(envVar, EnvironmentVariableTarget.User);
        if (existing == envValue)
            return;

        Environment.SetEnvironmentVariable(envVar, envValue, EnvironmentVariableTarget.User);
    }
    public static bool TryCreateProject(string nameSpace, 
                                        bool shareResources,
                                        [NotNullWhen(true)] out EditableSymbolProject? newProject, 
                                        [NotNullWhen(false)] out string? failureLog)
    {
        var name = nameSpace.Split('.').Last();
        var newCsProj = CsProjectFile.CreateNewProject(name, nameSpace, shareResources, UserSettings.Config.ProjectDirectories[0]);

        if (!newCsProj.TryRecompile(true, out failureLog))
        {
            newProject = null;
            return false;
        }

        newProject = new EditableSymbolProject(newCsProj);
        
        if(!newProject.AssemblyInformation.TryGetReleaseInfo(out var releaseInfo))
        {
            failureLog = $"Failed to get release info for project {name}";
            newProject.Dispose();
            newProject = null;
            return false;
        }
        
        if (releaseInfo.HomeGuid == Guid.Empty)
        {
            failureLog = $"No project home found for project {name}";
            newProject = null;
            return false;
        }
        
        _activePackages.Add(newProject);

        UpdateSymbolPackage(newProject);
        InitializePackageResources(newProject);
        return true;
    }

    internal static void RemoveSymbolPackage(EditorSymbolPackage package, bool needsDispose)
    {
        if (!needsDispose)
        {
            if (!_activePackages.Remove(package))
                throw new InvalidOperationException($"Failed to remove package {package}: does not exist");
        }
        else
        {
            package.Dispose();  // This will also remove
        }
    }

    private static void AddToLoadedPackages(EditorSymbolPackage package)
    {
        if (!_activePackages.Add(package))
            throw new InvalidOperationException($"Failed to add package {package.DisplayName} already exists");
    }

    private static void InitializePackageResources(EditorSymbolPackage package)
    {
        #if RELEASE
        if (package.IsReadOnly)
            return;
        #endif
        
        package.InitializeShaderLinting(ResourcePackageManager.SharedResourcePackages);
    }

    public static void DisposePackages()
    {
        var allPackages = SymbolPackage.AllPackages.ToArray();
        foreach (var package in allPackages)
            package.Dispose();
    }

    internal static void UpdateSymbolPackage(EditorSymbolPackage package)
    {
        UpdateSymbolPackages(package);
    }

    public static void UpdateSymbolPackages(params EditorSymbolPackage[] packages)
    {
        var parallel = UserSettings.Config.LoadMultiThreaded;
        
        var stopWatch = Stopwatch.StartNew();
        // Actually update the symbol packages
        // this switch statement exists to avoid the overhead of parallelization for a single package, e.g. when compiling changes to a single project
        switch (packages.Length)
        {
            case 0:
                Log.Warning($"Tried to update symbol packages but none were provided");
                return;
            case 1:
            {
                
                Log.Debug("Updating symbol packages " + (parallel ? "(parallel)":""));
                var package = packages[0];
                
                // ADD THIS: Ensure context is generated and types are scanned
                // before symbols are loaded and children are instantiated.
                package.AssemblyInformation.GenerateLoadContext();
                
                package.LoadSymbols(parallel, out var newlyRead, out var allNewSymbols);
                SymbolPackage.ApplySymbolChildren(newlyRead);
                package.LoadUiFiles(parallel, allNewSymbols, out var newlyLoadedUis, out var preExistingUis);
                package.LocateSourceCodeFiles();
                package.RegisterUiSymbols(newlyLoadedUis, preExistingUis);

                var count = package.Symbols.Sum(x => x.Value.InstancesOfSelf.Count());
                Log.Debug($"Updated symbol package {package.DisplayName} in {stopWatch.ElapsedMilliseconds}ms with {count} instances of its symbols");
                return;
            }
        }
        

        // do the same as above, just in several steps so we can do them in parallel
        ConcurrentDictionary<EditorSymbolPackage, List<SymbolJson.SymbolReadResult>> loadedSymbols = new();
        ConcurrentDictionary<EditorSymbolPackage, List<Symbol>> loadedOrCreatedSymbols = new();

        // generate load contexts synchronously
        foreach (var package in packages)
        {
            package.AssemblyInformation.GenerateLoadContext();
        }

        Log.Info("Loading symbols...");
        if (parallel)
        {
            packages
               .AsParallel()
               .ForAll(package => //pull out for non-editable ones too
                       {
                           package.LoadSymbols(parallel, out var newlyRead, out var allNewSymbols);
                           loadedSymbols.TryAdd(package, newlyRead);
                           loadedOrCreatedSymbols.TryAdd(package, allNewSymbols);
                       });
        }
        else
        {
            for (var index = packages.Length - 1; index >= 0; index--)
            {
                var package = packages[index];
                package.LoadSymbols(parallel, out var newlyRead, out var allNewSymbols);
                loadedSymbols.TryAdd(package, newlyRead);
                loadedOrCreatedSymbols.TryAdd(package, allNewSymbols);
            }
        }

        Log.Info("Applying children...");
        loadedSymbols
           .AsParallel()
           .ForAll(pair => SymbolPackage.ApplySymbolChildren(pair.Value));

        Log.Info("Loading symbol UIs...");
        ConcurrentDictionary<EditorSymbolPackage, SymbolUiLoadInfo> loadedSymbolUis = new();
        packages
           .AsParallel()
           .ForAll(package =>
                   {
                       var newlyRead = loadedOrCreatedSymbols[package];
                       package.LoadUiFiles(false, newlyRead, out var newlyReadUis, out var preExisting);
                       loadedSymbolUis.TryAdd(package, new SymbolUiLoadInfo(newlyReadUis, preExisting));
                   });

        Log.Info("Locating Source code files...");
        loadedSymbolUis
           .AsParallel()
           .ForAll(pair => { pair.Key.LocateSourceCodeFiles(); });

        foreach (var (symbolPackage, symbolUis) in loadedSymbolUis)
        {
            symbolPackage.RegisterUiSymbols(symbolUis.NewlyLoaded, symbolUis.PreExisting);
        }
        
        Log.Debug($">> Updated {packages.Length} symbol packages in {stopWatch.ElapsedMilliseconds/1000:0.0}s");

        var needingReload = _activePackages.Where(x => x.NeedsAssemblyLoad).ToArray();
        if (needingReload.Length > 0)
        {
            Log.Info($"Reloading {needingReload.Length} packages that need reloading...");
            UpdateSymbolPackages(needingReload);
        }
    }
    
    public static void SetProjectArchived(CsProjectFile projectFile, bool archive)
    {
        projectFile.IsArchived = archive;

        if (archive)
        {
            // 1. Find and Unload the active package if it exists
            var package = _activePackages.FirstOrDefault(p => p is EditableSymbolProject ep && ep.CsProjectFile == projectFile);
            if (package != null)
            {
                RemoveSymbolPackage(package, needsDispose: true); // Stops watchers and cleans up
            }
            
            if (ArchivedProjects.All(p => p.ProjectFile != projectFile))
            {
                ArchivedProjects.Add(new ArchivedProjectInfo(projectFile));
            }
        }
        else
        {
            // 1. Remove from archived list
            ArchivedProjects.RemoveAll(p => p.ProjectFile == projectFile);

            // 2. Initialize the project
            var newProject = new EditableSymbolProject(projectFile);
    
            // 3. Register Assets (Missing from your current implementation)
            // This mirrors the 'Initial Startup Migration' phase in LoadAll
            AssetRegistry.RegisterAssetsFromPackage(newProject);

            // 4. Register the package so it's visible globally
            AddToLoadedPackages(newProject);

            // 5. Critical: Generate the assembly context for type resolution
            newProject.AssemblyInformation.GenerateLoadContext();

            // 6. Load symbols and UIs
            UpdateSymbolPackages(newProject);

            // 7. Initialize shader linting and package resources
            InitializePackageResources(newProject);
    
            // 8. Force re-evaluation of instances
            // This helps fix the "Failed to create child instances" by poking the registry
            foreach (var symbol in newProject.Symbols.Values)
            {
                SymbolPackage.UpdateSymbolInstances(symbol);
            }            

        }
        EditorSymbolPackage.NotifySymbolStructureChange();
    }

    internal sealed record ArchivedProjectInfo(CsProjectFile ProjectFile) : IResourcePackage
    {
        public string DisplayName => ProjectFile.Name;
        public string Name => ProjectFile.Name;
        public Guid Id  => ProjectFile.PackageId;
        public string Folder => ProjectFile.Directory;
        public string AssetsFolder => Path.Combine(Folder, FileLocations.AssetsSubfolder);
        public string? RootNamespace => ProjectFile.RootNamespace;
    
        // Archived projects don't actively watch files or track live dependencies
        public ResourceFileWatcher? FileWatcher => null;
        public bool IsReadOnly => true;
        public IReadOnlyCollection<DependencyCounter> Dependencies => Array.Empty<DependencyCounter>();
    }
    
    private static readonly HashSet<EditorSymbolPackage> _activePackages = [];
    internal static readonly List<ArchivedProjectInfo> ArchivedProjects = [];
    internal static readonly IEnumerable<SymbolPackage> AllPackages = _activePackages;

    private readonly record struct SymbolUiLoadInfo(SymbolUi[] NewlyLoaded, SymbolUi[] PreExisting);
}