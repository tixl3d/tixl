#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using T3.Core.Compilation;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
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
        
        ActivePackages.Add(newProject);

        UpdateSymbolPackage(newProject);
        InitializePackageResources(newProject);
        return true;
    }

    internal static void RemoveSymbolPackage(EditorSymbolPackage package, bool needsDispose)
    {
        if (!ActivePackages.Remove(package))
            throw new InvalidOperationException($"Failed to remove package {package}: does not exist");

        if (needsDispose)
            package.Dispose();
    }

    private static void AddToLoadedPackages(EditorSymbolPackage package)
    {
        if (!ActivePackages.Add(package))
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

        var needingReload = ActivePackages.Where(x => x.NeedsAssemblyLoad).ToArray();
        if (needingReload.Length > 0)
        {
            Log.Info($"Reloading {needingReload.Length} packages that need reloading...");
            UpdateSymbolPackages(needingReload);
        }

      /*  foreach (var symbol in ActivePackages.SelectMany(x => x.Symbols.Values)
                                             .Where(x => x.NeedsReconnections))
        {
            symbol.ReconnectAll();
        }*/
    }

    private static readonly HashSet<EditorSymbolPackage> ActivePackages = new();
    internal static readonly IEnumerable<SymbolPackage> AllPackages = ActivePackages;

    private readonly record struct SymbolUiLoadInfo(SymbolUi[] NewlyLoaded, SymbolUi[] PreExisting);
}