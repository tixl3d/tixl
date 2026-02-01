#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using T3.Core.Compilation;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Stats;
using T3.Core.UserData;

// ReSharper disable RedundantNameQualifier

namespace T3.Core.Model;

/// <summary>
/// The runtime-loaded Symbol information and base class of essentially what is a read only project. 
/// </summary>
/// <remarks>
/// Regarding naming, we consider all t3 operator packages as packages for the sake of consistency with future nuget terminology etc.
/// -- only the user's editable "packages" are referred to as projects
///
/// Is used to create an EditorSymbolPackage.
///</remarks>
public abstract partial class SymbolPackage : IResourcePackage
{
    public virtual ResourceFileWatcher? FileWatcher => null;

    
    public bool IsSharingResources => AssemblyInformation.ShouldShareResources;
    public readonly bool DoNotIncludedSharedPackages;

    public virtual bool IsReadOnly => true;

    public readonly AssemblyInformation AssemblyInformation;
    
    /** Primary directory of the package that contains the csproj, home and other other data */
    public string Folder { get; }

    public virtual string DisplayName => AssemblyInformation.Name;

    protected virtual IEnumerable<string> SymbolSearchFiles
    {
        get
        {
            var dir = Path.Combine(Folder, FileLocations.SymbolsSubfolder);
            if (!Directory.Exists(dir))
                return [];
            return Directory.EnumerateFiles(dir, $"*{SymbolExtension}", SearchOption.AllDirectories);
        }
    }

    protected event Action<string?, Symbol>? SymbolAdded;
    protected event Action<Symbol>? SymbolUpdated;
    protected event Action<Guid>? SymbolRemoved;

    private static ConcurrentBag<SymbolPackage> _allPackages = [];
    public static IEnumerable<SymbolPackage> AllPackages => _allPackages;

    public string AssetsFolder { get; private set; } = null!;

    public IReadOnlyCollection<DependencyCounter> Dependencies => (ReadOnlyCollection<DependencyCounter>)DependencyDict.Values;
    protected readonly ConcurrentDictionary<SymbolPackage, DependencyCounter> DependencyDict = new();
    public string RootNamespace => ReleaseInfo.RootNamespace;

    protected virtual ReleaseInfo ReleaseInfo
    {
        get
        {
            if (AssemblyInformation.TryGetReleaseInfo(out var releaseInfo))
                return releaseInfo;

            throw new InvalidOperationException($"Failed to get release info for package {AssemblyInformation.Name}");
        }
    }
    
    public string Name 
    {
        get
        {
            // 1. If AssemblyInfo already has a name (Debug/Dev), use it.
            if (!string.IsNullOrWhiteSpace(AssemblyInformation.Name))
                return AssemblyInformation.Name;

            // 2. Fallback to ReleaseInfo's RootNamespace (Release build).
            // Accessing ReleaseInfo triggers TryGetReleaseInfo() internally.
            try
            {
                return ReleaseInfo.RootNamespace;
            }
            catch (Exception)
            {
                // 3. Last resort fallback
                return AssemblyInformation.Name ?? string.Empty;
            }
        }
    }
    
    public Guid Id => AssemblyInformation.Id != Guid.Empty 
                          ? AssemblyInformation.Id 
                          : ReleaseInfo.PackageId; // Use a dedicated PackageId field

    
    static SymbolPackage()
    {
        RenderStatsCollector.RegisterProvider(new OpUpdateCounter());
        RegisterTypes();
    }

    protected SymbolPackage(AssemblyInformation assembly, string? mainDirectory, bool initializeResources)
    {
        AssemblyInformation = assembly;
        Folder = mainDirectory ?? assembly.Directory;
        lock (_allPackages)
            _allPackages.Add(this);

        DoNotIncludedSharedPackages = assembly.Name == FileLocations.LibPackageName;

        // This can be delayed for EditableSymbolPackages and the called directly from there
        if (initializeResources)
        {
            // ReSharper disable once VirtualMemberCallInConstructor
            InitializeAssets();
        }
    }

    protected virtual void InitializeAssets()
    {
        AssetsFolder = Path.Combine(Folder, FileLocations.AssetsSubfolder);

        // Force the assembly information to load the JSON metadata now
        // This ensures Name and Id are valid before registration starts.
        _ = ReleaseInfo;
        
        // Avoid creating resource folder in protected program folder
        if (!IsReadOnly)
        {
            Directory.CreateDirectory(AssetsFolder);
        }

        ResourceManager.AddSharedResourceFolder(this, AssemblyInformation.ShouldShareResources);
        AssetRegistry.RegisterAssetsFromPackage(this);
    }

    public virtual void Dispose()
    {
        AssetRegistry.UnregisterPackage(Id);
        ResourceManager.RemoveSharedResourceFolder(this);
        ClearSymbols();

        var currentPackages = _allPackages.ToList();
        currentPackages.Remove(this);
        lock (_allPackages)
            _allPackages = new ConcurrentBag<SymbolPackage>(currentPackages);

        AssemblyInformation.Unload();
        // Todo - symbol instance destruction...?

        return;

        void ClearSymbols()
        {
            if (SymbolDict.Count == 0)
                return;

            var symbols = SymbolDict.Values.ToArray();
            foreach (var symbol in symbols)
            {
                var id = symbol.Id;
                SymbolDict.Remove(id, out _);
            }
        }
    }

    /// <summary>
    /// Loads symbols from the assembly and locates their symbol .t3/json files
    /// </summary>
    /// <param name="parallel">if true, parallel loading is used</param>
    /// <param name="newlyRead">Newly read json files for new types</param>
    /// <param name="allNewSymbols">All new symbols, including those for which a json file was not found</param>
    public void LoadSymbols(bool parallel, out List<SymbolJson.SymbolReadResult> newlyRead, out List<Symbol> allNewSymbols)
    {
        Log.Debug($" Loading {AssemblyInformation.Name}...");

        if (!AssemblyInformation.TryLoadTypes())
        {
            var error = $"Failed to load types for {AssemblyInformation.Name}";
            Log.Error(error);
            newlyRead = [];
            allNewSymbols = [];
            return;
        }

        IDictionary<Guid, Type> newTypes;

        var removedSymbolIds = new HashSet<Guid>(SymbolDict.Keys);
        ConcurrentBag<Symbol> updatedSymbols = new();

        if (parallel)
        {
            newTypes = new ConcurrentDictionary<Guid, Type>();
            AssemblyInformation.OperatorTypeInfo
                               .AsParallel()
                               .ForAll(kvp => LoadTypes(kvp.Key, kvp.Value.Type, newTypes, updatedSymbols));
        }
        else
        {
            newTypes = new Dictionary<Guid, Type>();
            foreach (var (guid, type) in AssemblyInformation.OperatorTypeInfo)
            {
                LoadTypes(guid, type.Type, newTypes, updatedSymbols);
            }
        }

        // gather slot changes
        foreach (var symbol in updatedSymbols)
        {
            symbol.UpdateInstanceType(true);
        }

        // update symbol instances 
        foreach (var symbol in updatedSymbols)
        {
            Log.Debug("Update symbol instances");
            UpdateSymbolInstances(symbol);
            SymbolUpdated?.Invoke(symbol);
        }

        if (SymbolRemoved != null)
        {
            // remaining symbols have been removed from the assembly
            foreach (var symbolId in removedSymbolIds)
            {
                SymbolRemoved.Invoke(symbolId);
            }
        }

        newlyRead = [];
        allNewSymbols = [];

        if (newTypes.Count != 0)
        {
            var searchFileEnumerator = parallel ? SymbolSearchFiles.AsParallel() : SymbolSearchFiles;
            var symbolsRead = searchFileEnumerator
                             .Select(JsonFileResult<Symbol>.ReadAndCreate)
                             .Select(result =>
                                     {
                                         if (!newTypes.TryGetValue(result.Guid, out var type))
                                             return default;

                                         return ReadSymbolFromJsonFileResult(result, type);
                                     })
                             .Where(symbolReadResult => symbolReadResult.Result.Symbol is not null)
                             .ToArray();

            if (ProjectSettings.Config.LogCompilationDetails)
                Log.Debug($"{AssemblyInformation.Name}: Registering loaded symbols...");

            foreach (var readSymbolResult in symbolsRead)
            {
                var symbol = readSymbolResult.Result.Symbol;
                var id = symbol!.Id;

                if (!SymbolDict.TryAdd(id, symbol))
                {
                    Log.Error($"Can't load symbol for [{symbol.Name}]. Registry already contains id {symbol.Id}: [{SymbolDict[symbol.Id].Name}]");
                    continue;
                }

                newlyRead.Add(readSymbolResult.Result);
                newTypes.Remove(id, out _);
                allNewSymbols.Add(symbol);

                SymbolAdded?.Invoke(readSymbolResult.Path, symbol);
            }
        }

        // these do not have a symbol json file but are defined in the assembly
        foreach (var (guid, newType) in newTypes)
        {
            var symbol = CreateSymbol(newType, guid);

            var id = symbol.Id;
            if (!SymbolDict.TryAdd(id, symbol))
            {
                Log.Error($"{AssemblyInformation.Name}: Ignoring redefinition symbol {symbol.Name}.");
                continue;
            }

            //Log.Debug($"{AssemblyInformation.Name}: new added symbol: {newType}");

            allNewSymbols.Add(symbol);

            SymbolAdded?.Invoke(null, symbol);
        }

        OnSymbolsLoaded();
        return;

        void LoadTypes(Guid guid, Type type, IDictionary<Guid, Type> newTypesDict, ConcurrentBag<Symbol> updated)
        {
            if (SymbolDict.TryGetValue(guid, out var symbol))
            {
                removedSymbolIds.Remove(guid);
                if (symbol == null) // this should never happen??
                {
                    Log.Error($"Skipping update of invalid symbol {guid}. Symbol entry was null - this is a bug.");
                    return;
                }

                // we already have this symbol, so mark it as updated (but dont actually update it yet)
                symbol.UpdateTypeWithoutUpdatingDefinitionsOrInstances(type, this);
                updated.Add(symbol);
            }
            else
            {
                // it's a new type!!
                if (!newTypesDict.TryAdd(guid, type))
                {
                    Log.Error($"{DisplayName}: Failed to add new type {type} with guid '{guid}' - " +
                              "is there another operator type with the same guid in this project?");
                }
            }
        }

        SymbolJsonResult ReadSymbolFromJsonFileResult(JsonFileResult<Symbol> jsonInfo, Type type)
        {
            try
            {
                var result = SymbolJson.ReadSymbolRoot(jsonInfo.Guid, jsonInfo.JToken, type, this);
                jsonInfo.Object = result.Symbol;
                return new SymbolJsonResult(result, jsonInfo.FilePath);
            }
            catch (Exception e)
            {
                throw new FileCorruptedException(jsonInfo.FilePath, e.ToString());
            }
        }
    }

    protected virtual void OnSymbolsLoaded()
    {
    }

    protected static void UpdateSymbolInstances(Symbol symbol, bool forceTypeUpdate = false)
    {
        symbol.UpdateInstanceType(forceTypeUpdate);
        symbol.CreateAnimationUpdateActionsForSymbolInstances();
    }

    protected internal Symbol CreateSymbol(Type instanceType, Guid id)
    {
        return new Symbol(instanceType, id, this);
    }

    public static void ApplySymbolChildren(List<SymbolJson.SymbolReadResult> symbolsRead)
    {
        Parallel.ForEach(symbolsRead, result => TryReadAndApplyChildren(result));
    }

    protected static bool TryReadAndApplyChildren(SymbolJson.SymbolReadResult result)
    {
        if (SymbolJson.TryReadAndApplySymbolChildren(result)) return true;

        var symbol = result.Symbol;
        if (symbol == null)
        {
            Log.Error($"Problem obtaining children of 'null' with {result.ChildrenJsonArray.Length} children");
            return false;
        }

        Log.Error($"Problem obtaining children of {symbol.Name ?? "'null'"} ({symbol.Id})");
        return false;
    }

    public readonly record struct SymbolJsonResult(in SymbolJson.SymbolReadResult Result, string Path);

    public IReadOnlyDictionary<Guid, Symbol> Symbols => SymbolDict;
    protected readonly ConcurrentDictionary<Guid, Symbol> SymbolDict = new();

    public const string SymbolExtension = ".t3";

    public bool ContainsSymbolName(ReadOnlySpan<char> newSymbolName, ReadOnlySpan<char> symbolNamespace)
    {
        foreach (var existing in SymbolDict.Values)
        {
            var existingNamespace = existing.Namespace.AsSpan();
            var existingName = existing.Name.AsSpan();
            if (newSymbolName.SequenceEqual(existingName) && symbolNamespace.SequenceEqual(existingNamespace))
                return true;
        }

        return false;
    }

    public void AddResourceDependencyOn(FileResource resource)
    {
        if (!TryGetDependencyCounter(resource, out var dependencyCount))
            return;

        dependencyCount.ResourceCount++;
    }

    public void RemoveResourceDependencyOn(FileResource fileResource)
    {
        if (!TryGetDependencyCounter(fileResource, out var dependency))
            return;

        dependency.ResourceCount--;

        RemoveIfNoRemainingReferences(dependency);
    }

    public void AddDependencyOn(Symbol symbol)
    {
        if (symbol.SymbolPackage == this)
            return;

        if (!TryGetDependencyCounter(symbol, out var dependency))
            return;

        if (dependency.SymbolChildCount++ == 0)
        {
            // this is the first reference to this package
            DependencyDict.TryAdd((SymbolPackage)dependency.Package, dependency);
        }
    }

    public void RemoveDependencyOn(Symbol symbol)
    {
        if (symbol.SymbolPackage == this)
            return;

        if (!TryGetDependencyCounter(symbol, out var dependency))
            return;

        dependency.SymbolChildCount--;
        RemoveIfNoRemainingReferences(dependency);
    }

    private void RemoveIfNoRemainingReferences(DependencyCounter dependency)
    {
        if (dependency is { SymbolChildCount: 0, ResourceCount: 0 })
        {
            DependencyDict.Remove((SymbolPackage)dependency.Package, out _);
        }
    }

    private bool TryGetDependencyCounter(IResource fileResource, [NotNullWhen(true)] out DependencyCounter? dependencyCounter)
    {
        var owningPackage = fileResource.OwningPackage;
        if (owningPackage is not SymbolPackage symbolPackage)
        {
            dependencyCounter = null;
            return false;
        }

        if (DependencyDict.TryGetValue(symbolPackage, out dependencyCounter))
            return true;

        dependencyCounter = new DependencyCounter
                                {
                                    Package = symbolPackage
                                };
        if (DependencyDict.TryAdd(symbolPackage, dependencyCounter))
        {
            // this is the first reference to this package
            return true;
        }

        // another thread added it in the meantime??
        return DependencyDict.TryGetValue(symbolPackage, out dependencyCounter);
    }

    public bool OwnsNamespace(string namespaceName)
    {
        return namespaceName == RootNamespace
               || namespaceName.StartsWith(RootNamespace)
               || AssemblyInformation.Namespaces.Contains(namespaceName)
               || AssemblyInformation.Namespaces.Any(x => namespaceName.StartsWith(x));
    }
}

public sealed record DependencyCounter
{
    public required IResourcePackage Package { get; init; }
    internal int SymbolChildCount { get; set; }
    internal int ResourceCount { get; set; }

    public override string ToString()
    {
        return $"{Package.DisplayName}: Symbol References: {SymbolChildCount}, Resource References: {ResourceCount}";
    }
}