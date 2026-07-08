#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using T3.Core.Compilation;
using T3.Core.IO;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Settings;
using T3.Core.Utils;
using T3.Editor.External;

namespace T3.Editor.UiModel;

// todo - make abstract, create NugetSymbolPackage
/// <summary>
/// EditorSymbolPackage is a readonly <see cref="SymbolPackage"/>  with  <see cref="SymbolUi"/>s.
/// </summary>
/// <remarks>
/// Is used to create an <see cref="EditableSymbolProject"/>.
/// </remarks>
internal class EditorSymbolPackage : SymbolPackage
{
    /// <summary>
    /// Constructor for a successfully compiled package
    /// </summary>
    /// <param name="assembly">Main assembly of the package</param>
    /// <param name="directory">The main package directory, if different from the directory from the provided assembly information</param>
    /// <param name="initializeResources"></param>
    public EditorSymbolPackage(AssemblyInformation assembly, string? directory, bool initializeResources = true) : base(assembly, directory, initializeResources)
    {
        if(CoreSettings.Config.LogCompilationDetails)
            Log.Debug($"Added package {assembly.Name}");
        
        SymbolAdded += OnSymbolAdded;
        assembly.Unloaded += OnAssemblyUnloaded;
        assembly.UnloadComplete += OnAssemblyUnloadComplete;
    }

    private void OnAssemblyUnloadComplete(AssemblyInformation obj)
    {
        UnloadInProgress = false;
    }

    protected virtual void OnSymbolAdded(string? path, Symbol symbol)
    {
        var id = symbol.Id;
        if (_filePathHandlers.TryGetValue(id, out var handler))
        {
            // A symbol removed and re-added across recompiles gets a new Symbol object under the same id -
            // re-point the surviving handler so it doesn't act on the stale instance.
            handler.Symbol = symbol;
            return;
        }

        handler = new SymbolPathHandler(symbol, path);
        _filePathHandlers[id] = handler;
    }

    protected virtual void OnSymbolUiLoaded(string? path, SymbolUi symbolUi)
    {
        var id = symbolUi.Symbol.Id;
        _filePathHandlers[id].UiFilePath = path;
    }

    private void OnSourceCodeLocated(string path, Guid guid)
    {
        if (_filePathHandlers.TryGetValue(guid, out var handler))
        {
            handler.SourceCodePath = path;
        }
        else
        {
            Log.Error($"No file path handler found for {guid} / '{path}'");
        }
    }

    /// <summary>
    /// Loads UI files for the provided symbols. 
    /// This method does not delete UIs that have since been deleted
    /// </summary>
    /// <param name="parallel">If true, load and process in parallel</param>
    /// <param name="newlyReadSymbols">The symbols for which UI files should be loaded - just the new ones</param>
    /// <param name="newlyReadSymbolUis">Brand spankin new symbol UIs</param>
    /// <param name="preExistingSymbolUis">Symbol UIs that already existed at runtime - if this is the first time this package was loaded, this will be empty</param>
    public void LoadUiFiles(bool parallel, List<Symbol> newlyReadSymbols, out SymbolUi[] newlyReadSymbolUis,
                            out SymbolUi[] preExistingSymbolUis)
    {
        NeedsAssemblyLoad = false;
        var newSymbols = newlyReadSymbols.ToDictionary(result => result.Id, symbol => symbol);
        var newSymbolsWithoutUis = new ConcurrentDictionary<Guid, Symbol>(newSymbols);
        preExistingSymbolUis = SymbolUiDict.Values.ToArray();
        if(CoreSettings.Config.LogCompilationDetails)
            Log.Debug($"{AssemblyInformation.Name}: Loading Symbol UIs from \"{Folder}\"");

        var enumerator = parallel ? SymbolUiSearchFiles.AsParallel() : SymbolUiSearchFiles;
        var newlyReadSymbolUiList = enumerator
                                   .Select(TryReadSymbolUiFile)
                                   .Where(result => result != null && newSymbols.ContainsKey(result.Guid))
                                   .Select(uiJson =>
                                           {
                                               var file = uiJson!;
                                               if (!SymbolUiJson.TryReadSymbolUi(file.JToken, newSymbols[file.Guid], out var symbolUi))
                                               {
                                                   Log.Error($"Error reading symbol Ui for {file.Guid} from file \"{file.FilePath}\"");
                                                   return null;
                                               }

                                               newSymbolsWithoutUis.Remove(symbolUi.Symbol.Id, out _);
                                               var id = symbolUi.Symbol.Id;

                                               if (!SymbolUiDict.TryAdd(id, symbolUi))
                                               {
                                                   Log.Error($"{AssemblyInformation.Name}: Duplicate symbol UI for {symbolUi.Symbol.Name}?");
                                                   return null;
                                               }

                                               OnSymbolUiLoaded(file.FilePath, symbolUi);
                                               return symbolUi;
                                           })
                                   .Where(symbolUi => symbolUi != null)
                                   .Select(symbolUi => symbolUi!)
                                   .ToList();

        if (newSymbolsWithoutUis.Count == 0)
        {
            newlyReadSymbolUis = newlyReadSymbolUiList.ToArray();
            return;
        }

        foreach (var (guid, symbol) in newSymbolsWithoutUis)
        {
            var symbolUi = new SymbolUi(symbol, false);

            if (!SymbolUiDict.TryAdd(guid, symbolUi))
            {
                Log.Error($"{AssemblyInformation.Name}: Duplicate symbol UI for {symbol.Name}?");
                continue;
            }

            newlyReadSymbolUiList.Add(symbolUi);
            OnSymbolUiLoaded(null, symbolUi);
        }

        newlyReadSymbolUis = newlyReadSymbolUiList.ToArray();
        return;

        JsonFileResult<SymbolUi>? TryReadSymbolUiFile(string filePath)
        {
            try
            {
                return JsonFileResult<SymbolUi>.ReadAndCreate(filePath);
            }
            catch (Exception e)
            {
                // A corrupt .t3ui holds only UI layout, not the graph — but it must not abort the load.
                // Skip it (the symbol falls back to default UI) and record it so the package won't be
                // saved over, keeping the file recoverable from a backup.
                Log.Error($"Skipping corrupted symbol UI file '{filePath}': {e.Message}");
                RecordCorruptedSymbolFile(filePath);
                return null;
            }
        }
    }

    public void RegisterUiSymbols(SymbolUi[] newSymbolUis, SymbolUi[] preExistingSymbolUis)
    {
        if(CoreSettings.Config.LogCompilationDetails)
            Log.Debug($@"{DisplayName}: Registering UI entries...");
        
        foreach (var symbolUi in preExistingSymbolUis)
        {
            RegisterSymbolUi(symbolUi);
        }

        foreach (var symbolUi in newSymbolUis)
        {
            RegisterSymbolUi(symbolUi);
        }

        return;

        void RegisterSymbolUi(SymbolUi symbolUi)
        {
            symbolUi.UpdateConsistencyWithSymbol();
            symbolUi.ClearModifiedFlag();
            //Log.Debug($"Add UI for {symbolUi.Symbol.Name} {symbolUi.Symbol.Id}");
        }
    }

    public override void Dispose()
    {
        ClearSymbolUis();
        base.Dispose();
        ShaderLinter.RemovePackage(this);
        return;
        
        void ClearSymbolUis()
        {
            var symbolUis = SymbolUiDict.Values.ToArray();

            foreach (var symbolUi in symbolUis)
            {
                try
                {
                    var symbol = symbolUi.Symbol;
                    SymbolUiDict.TryRemove(symbol.Id, out _);
                }
                catch (KeyNotFoundException)
                {
                    Log.Warning("Can't remove obsolete symbol.");
                }
            }
        }
    }

    /// <summary>
    /// Looks for source codes in the project folder and subfolders and tries to find the symbol id in the source code
    /// </summary>
    public virtual void LocateSourceCodeFiles()
    {
        #if DEBUG
        int sourceCodeCount = 0;
        int sourceCodeAttempts = 0;
        #endif

        SourceCodeSearchFiles
           .AsParallel()
           .ForAll(ParseCodeFile);

        #if DEBUG
        if (sourceCodeCount == 0 && sourceCodeAttempts != 0)
        {
            Log.Error($"{AssemblyInformation.Name}: No source code files found in project folder.");
        }
        else
        {
            if(CoreSettings.Config.LogCompilationDetails)
                Log.Debug($"{AssemblyInformation.Name}: Found {sourceCodeCount} operator source code files out of {sourceCodeAttempts} C# files.");
        }
        #endif

        return;

        void ParseCodeFile(string file)
        {
            #if DEBUG
            Interlocked.Increment(ref sourceCodeAttempts);
            #endif

            var streamReader = new StreamReader(file);

            var guid = Guid.Empty;
            while (!streamReader.EndOfStream)
            {
                var line = streamReader.ReadLine();
                if (line == null)
                    break;

                if (!StringUtils.TryFindIgnoringAllWhitespace(line, "[Guid(\"", StringUtils.SearchResultIndex.AfterTerm, out var guidStartIndex))
                    continue;

                var indexOfQuote = line.IndexOf('"', guidStartIndex);
                var guidSpan = line.AsSpan(guidStartIndex, indexOfQuote - guidStartIndex);

                if (!Guid.TryParse(guidSpan, out guid))
                {
                    Log.Error($"{DisplayName}: Failed to parse guid from {guidSpan.ToString()} in \"{file}\"");
                    continue;
                }

                break;
            }

            streamReader.Close();
            streamReader.Dispose();

            if (guid == Guid.Empty)
                return;

            #if DEBUG
            Interlocked.Increment(ref sourceCodeCount);
            #endif

            OnSourceCodeLocated(file, guid);
        }
    }

    public bool TryGetSourceCodePath(Symbol symbol, out string? path)
    {
        if (_filePathHandlers.TryGetValue(symbol.Id, out var filePathInfo))
        {
            path = filePathInfo.SourceCodePath;
            return path != null;
        }

        path = null;
        return false;
    }

    public void InitializeShaderLinting(IReadOnlyList<IResourcePackage> sharedShaderPackages)
    {
        ShaderLinter.AddPackage(this, sharedShaderPackages);
    }

    internal bool HasHome
    {
        get
        {
            if (HasHomeSymbol(out var error)) 
                return true;
            
            if (error != null)
                Log.Warning(error);

            return false;

        }
    }

    public bool HasHomeSymbol(out string? error)
    {
        error = null;

        try
        {
            if (HomeSymbolId == Guid.Empty)
                return false;

            if (Symbols.ContainsKey(HomeSymbolId)) 
                return true;

            if (Symbols.Count == 0)
            {
                error = $"Package {Name} has no Symbols definition.";
                return false;
            }
            
            error = $"Home symbol {HomeSymbolId} not found";
            return false;
        }
        catch (Exception e)
        {
            error = "Failed to parse project: " + e.Message;
            return false;
        }
    }

    protected readonly ConcurrentDictionary<Guid, SymbolUi> SymbolUiDict = new();
    public IReadOnlyDictionary<Guid, SymbolUi> SymbolUis => SymbolUiDict;

    protected virtual IEnumerable<string> SymbolUiSearchFiles
    {
        get
        {
            var dir = Path.Combine(Folder, FileLocations.SymbolUiSubFolder);
            if (!Directory.Exists(dir))
            {
                return [];
            }
            
            return Directory.EnumerateFiles(dir, $"*{SymbolUiExtension}", SearchOption.AllDirectories);
        }
    }

    protected virtual IEnumerable<string> SourceCodeSearchFiles
    {
        get
        {
            var dir = Path.Combine(Folder, FileLocations.SourceCodeSubFolder);
            
            if (!Directory.Exists(dir))
            {
                return [];
            }
            
            return Directory.EnumerateFiles(dir, $"*{SourceCodeExtension}", SearchOption.AllDirectories);
        }
    }

    private readonly ConcurrentDictionary<Guid, SymbolPathHandler> _filePathHandlers = new();
    protected IDictionary<Guid, SymbolPathHandler> FilePathHandlers => _filePathHandlers;
    public Guid HomeSymbolId => OverrideHomeGuid != Guid.Empty 
                                    ? OverrideHomeGuid 
                                    : ReleaseInfo.HomeGuid;

    public Guid OverrideHomeGuid = Guid.Empty;

    internal const string SourceCodeExtension = ".cs";
    public const string SymbolUiExtension = ".t3ui";

    public static IEnumerable<Symbol> AllSymbols => AllPackages
                                                   .Cast<EditorSymbolPackage>()
                                                   .Select(x => x.SymbolDict)
                                                   .SelectMany(x => x.Values);

    public static IEnumerable<SymbolUi> AllSymbolUis => AllPackages
                                                       .Cast<EditorSymbolPackage>()
                                                       .Select(x => x.SymbolUiDict)
                                                       .SelectMany(x => x.Values);

    public void Reload(SymbolUi symbolUi)
    {
        var symbol = symbolUi.Symbol;
        var id = symbol.Id;

        if (!_filePathHandlers.TryGetValue(id, out var pathHandler))
        {
            throw new Exception($"No path handler found for symbol {id}");
        }

        var symbolPath = pathHandler.SymbolFilePath;
        if (symbolPath == null)
        {
            throw new Exception($"No symbol path found for symbol {id}");
        }

        var symbolUiPath = pathHandler.UiFilePath;
        if (symbolUiPath == null)
        {
            throw new Exception($"No symbol ui path found for symbol {id}");
        }

        // reload single ui
        JsonFileResult<Symbol> symbolJson;
        try
        {
            symbolJson = JsonFileResult<Symbol>.ReadAndCreate(symbolPath);
        }
        catch (Exception e)
        {
            Log.Error($"Not reloading [{symbol}]: its symbol file is corrupt ({e.Message}). Keeping the loaded version.");
            return;
        }

        var result = SymbolJson.ReadSymbolRoot(symbol.Id, symbolJson.JToken, symbol.InstanceType, this);
        if (result.Symbol == null)
        {
            Log.Error($"Failed to reload read-only symbol {symbol} for {id} from {symbolPath}");
            return;
        }
        
        symbol.ReplaceWithContentsOf(result.Symbol);
        
        // hacky workaround to avoid creating duplicate children in the next step
        var fakeResult = result with { Symbol = symbol };

        if (!TryReadAndApplyChildren(fakeResult))
        {
            Log.Error($"Failed to reload symbol for symbol {id}");
            return;
        }

        UpdateSymbolInstances(symbol, forceTypeUpdate: true);

        JsonFileResult<SymbolUi> symbolUiJson;
        try
        {
            symbolUiJson = JsonFileResult<SymbolUi>.ReadAndCreate(symbolUiPath);
        }
        catch (Exception e)
        {
            Log.Error($"Not reloading UI for [{symbol}]: its .t3ui file is corrupt ({e.Message}). Keeping the loaded version.");
            return;
        }

        if (!SymbolUiJson.TryReadSymbolUi(symbolUiJson.JToken, symbol, out var newSymbolUi))
        {
            throw new Exception($"Failed to reload symbol ui for symbol {id}");
        }

        // override registry values
        newSymbolUi.UpdateConsistencyWithSymbol();
        symbolUi.ReplaceWith(newSymbolUi);
        symbolUi.ClearModifiedFlag();
    }

    public bool TryGetSymbolUi(Guid rSymbolId, [NotNullWhen(true)] out SymbolUi? symbolUi)
    {
        return SymbolUiDict.TryGetValue(rSymbolId, out symbolUi);
    }

    // todo - output should be an IDisposable wrapper and RemoveSymbolUi should be called in Dispose and made private
    internal bool TryCreateNewSymbol<T>([NotNullWhen(true)] out SymbolUi? symbolUi)
    {
        var containerOp = CreateSymbol(typeof(T), Guid.NewGuid());

        if (!SymbolDict.TryAdd(containerOp.Id, containerOp))
        {
            Log.Error($"Failed to add new symbol for {containerOp.Name} ({containerOp.Id})");
            symbolUi = null;
            return false;
        }

        symbolUi = new SymbolUi(containerOp, true);
        if (SymbolUiDict.TryAdd(containerOp.Id, symbolUi))
            return true;

        Log.Error($"Failed to add new symbol ui for {containerOp.Name} ({containerOp.Id})");
        return false;
    }

    internal bool RemoveSymbolUi(SymbolUi newContainerUi)
    {
        var symbolId = newContainerUi.Symbol.Id;
        return SymbolUiDict.TryRemove(symbolId, out _) && SymbolDict.TryRemove(symbolId, out _);
    }

    protected sealed override void OnSymbolsLoaded()
    {
        var assemblyInformation = AssemblyInformation;
        //var types = assemblyInformation.TypesInheritingFrom(typeof(IEditorUiExtension)).ToArray();
        
        // register descriptive UI
        // TODO: Implement this
        // foreach (var operatorInfo in assemblyInformation.OperatorTypeInfo.Values)
        // {
        //     if (operatorInfo.IsDescriptiveFileNameType)
        //     {
        //         CustomChildUiRegistry.Register(operatorInfo.Type, DescriptiveUi.DrawChildUiDelegate, _descriptiveUiTypes);
        //     }
        // }

        // load ui initializers
        // foreach (var type in types)
        // {
        //     var activated = assemblyInformation.CreateInstance(type);
        //     if (activated == null)
        //     {
        //         Log.Error($"Created null object for {type.Name}");
        //         continue;
        //     }
        //
        //     // var extension = (IEditorUiExtension)activated;
        //     // try
        //     // {
        //     //     extension.Initialize();
        //     // }
        //     // catch (Exception e)
        //     // {
        //     //     Log.Error($"Failed to initialize UI extension {type.Name}: {e}");
        //     //     continue;
        //     // }
        //
        //     // Log.Info($"Loaded UI initializer for {assemblyInformation.Name}: {type.Name}");
        //     // _extensions.Add(extension);
        // }

        // if (_extensions.Count != 0 && assemblyInformation.OperatorTypeInfo.Count > 0)
        // {
        //     BlockingWindow.Instance.ShowMessageBox("Custom UI extensions are not supported in projects that also have symbols defined. " +
        //                                            "This may cause issues with exporting. It is recommended to start a new C# project for custom UIs.",
        //                                            "Warning");
        // }
    }

    public bool NeedsAssemblyLoad { get; private set; } = true;
    
    // remove all possible references to the assembly
    private void OnAssemblyUnloaded(AssemblyInformation asm)
    {
        UnloadInProgress = true;
        try
        {
            AssemblyUnloading?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error($"Failed to unload assembly {asm.Name}: {e}");
        }
        Log.Info("Unloading assembly " + asm.Name);
        //remove type information from symbols
        
        // it is critical that all references to the assembly are removed to allow the assembly to be unloaded
        // this includes all instances
        foreach (var symbol in SymbolDict.Values)
        {
            symbol.RemoveAllReferencesToType();
        }
        
        
        // unload custom UIs
        // for (var index = _extensions.Count - 1; index >= 0; index--)
        // {
        //     var extension = _extensions[index];
        //     _extensions.RemoveAt(index);
        //     try
        //     {
        //         extension.Uninitialize();
        //     }
        //     catch (Exception e)
        //     {
        //         Log.Error($"Failed to uninitialize UI extension {extension.GetType().Name}: {e}");
        //     }
        // }
        
        // unload descriptive uis
        // FIXME: implement this.
        // for (var index = _descriptiveUiTypes.Count - 1; index >= 0; index--)
        // {
        //     var type = _descriptiveUiTypes[index];
        //     CustomChildUiRegistry.Remove(type, _descriptiveUiTypes);
        // }
        
        NeedsAssemblyLoad = true;
    }
    

    protected bool UnloadInProgress { get; private set; }
    public event Action? AssemblyUnloading;
    private readonly List<Type> _descriptiveUiTypes = [];

    public static void NotifySymbolStructureChange()
    {
        SymbolStructureVersionCounter++;
    }
    
    public static int SymbolStructureVersionCounter { get; private set; }
}