#nullable enable
using System.Diagnostics;
using System.IO;
using T3.Core.Compilation;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.SystemUi;
using T3.Editor.Compilation;

namespace T3.Editor.UiModel;

/// <summary>
/// EditableSymbolProject is an <see cref="EditorSymbolPackage"/> that contains runtime package manipulation logic.
/// </summary>
[DebuggerDisplay("{DisplayName}")]
internal sealed partial class EditableSymbolProject : EditorSymbolPackage
{
    public override string DisplayName { get; }

    /// <summary>
    /// Create a new <see cref="EditableSymbolProject"/> using the given <see cref="CsProjectFile"/>.
    /// </summary>
    public EditableSymbolProject(CsProjectFile csProjectFile) : 
        base(assembly: AssemblyInformation.CreateUninitialized(), directory: csProjectFile.Directory, false)
    {
        AssemblyInformation.Initialize(csProjectFile.GetBuildTargetDirectory(), false);
        CsProjectFile = csProjectFile;
        //Log.Info($"Adding project {csProjectFile.Name}...");
        _csFileWatcher = new CodeFileWatcher(this, OnFileChanged, OnCodeFileRenamed);
        _csFileWatcher.EnableRaisingEvents = true;
        DisplayName = $"{csProjectFile.Name} ({CsProjectFile.RootNamespace})";
        SymbolUpdated += OnSymbolUpdated;
        SymbolRemoved += OnSymbolRemoved;
        InitializeAssets();

        _allProjectsCache = null;
    }

    public void OpenProjectInCodeEditor()
    {
        CoreUi.Instance.OpenWithDefaultApplication(CsProjectFile.FullPath);
    }
    
    public bool TryOpenCSharpInEditor(Symbol symbol)
    {
        var guid = symbol.Id;
        if (!FilePathHandlers.TryGetValue(guid, out var filePathHandler))
        {
            Log.Error($"No file path handler found for symbol {guid}");
            return false;
        }

        var sourceCodePath = filePathHandler.SourceCodePath;
        if (string.IsNullOrWhiteSpace(sourceCodePath))
        {
            Log.Error($"No source code path found for symbol {guid}");
            return false;
        }
        
        OpenProjectInCodeEditor();
        CoreUi.Instance.OpenWithDefaultApplication(sourceCodePath);
        return true;
    }

    public void ReplaceSymbolUi(SymbolUi symbolUi)
    {
        var symbol = symbolUi.Symbol;
        SymbolUiDict[symbol.Id] = symbolUi;
        symbolUi.FlagAsModified();
        symbolUi.ReadOnly = false;
        SaveSymbolFile(symbolUi);

        Log.Debug($"Replaced symbol ui for {symbol.Name}");
    }

    private void GiveSymbolToPackage(Guid id, EditableSymbolProject newDestinationProject)
    {
        SymbolDict.Remove(id, out var symbol);
        SymbolUiDict.Remove(id, out var symbolUi);
        FilePathHandlers.Remove(id, out var symbolPathHandler);
        
        if(_pendingSource.Remove(id, out var source))
            newDestinationProject._pendingSource.TryAdd(id, source);
        
        Debug.Assert(symbol != null);
        Debug.Assert(symbolUi != null);
        Debug.Assert(symbolPathHandler != null);

        symbol.SymbolPackage = newDestinationProject;
        symbolUi.UpdateSymbolPackage(newDestinationProject);

        newDestinationProject.SymbolDict.TryAdd(id, symbol);
        newDestinationProject.SymbolUiDict.TryAdd(id, symbolUi);
        newDestinationProject.FilePathHandlers.TryAdd(id, symbolPathHandler);
        
        // move files to new project - incorrect paths will be corrected by the loading process
        symbolPathHandler.UpdateFromSymbol();
        
        OnSymbolMoved?.Invoke(id, newDestinationProject);

        symbolUi.FlagAsModified();
    }


    private static readonly string[] _folderExclusions = ["bin", "obj", "dependencies"];

    protected override IEnumerable<string> SymbolUiSearchFiles => FindFilesOfType(SymbolUiExtension);

    protected override IEnumerable<string> SymbolSearchFiles => FindFilesOfType(SymbolExtension);
    
    protected override IEnumerable<string> SourceCodeSearchFiles => FindFilesOfType(SourceCodeExtension);

    private IEnumerable<string> FindFilesOfType(string fileExtension)
    {
        var directoryInfo = new DirectoryInfo(Folder);
        return directoryInfo.EnumerateDirectories()
                        .Where(x => !_folderExclusions.Contains(x.Name))
                        .SelectMany(x => x.EnumerateFiles($"*{fileExtension}", SearchOption.AllDirectories))
                        .Concat(directoryInfo.EnumerateFiles($"*{fileExtension}")).Select(x => x.FullName);
    }

    protected override void InitializeAssets()
    {
        base.InitializeAssets();
        _resourceFileWatcher = new ResourceFileWatcher(AssetsFolder);
        
        _resourceFileWatcher.FileCreated += (_, path) => 
                                            {
                                                AssetRegistry.RegisterPackageEntry(new FileInfo(path), this, false);
                                            };

        _resourceFileWatcher.FileRenamed += (oldPath, newPath) => 
                                            {
                                                AssetRegistry.UpdateMovedAsset(oldPath, newPath);
                                            };

        _resourceFileWatcher.FileDeleted += (_, path) => 
                                            {
                                                AssetRegistry.UnregisterAbsoluteFilePath(path, this);
                                            };
    }

    public override void Dispose()
    {
        base.Dispose();
        FileWatcher?.Dispose();
        ProjectSetup.RemoveSymbolPackage(this, false);

        _allProjectsCache = null;
    }

    /// <summary>
    /// Event raised when a symbol is moved to a different project or deleted
    /// </summary>
    public event Action<Guid, EditableSymbolProject?>? OnSymbolMoved;
    public readonly CsProjectFile CsProjectFile;
    private ResourceFileWatcher? _resourceFileWatcher;
    public override ResourceFileWatcher? FileWatcher => _resourceFileWatcher;
    public override bool IsReadOnly => false;
    

    private static IEnumerable<EditableSymbolProject>? _allProjectsCache;
    public static IEnumerable<EditableSymbolProject> AllProjects
    {
        get
        {
            _allProjectsCache ??= ProjectSetup
                                 .AllPackages
                                 .Where(x => x is EditableSymbolProject)
                                 .Cast<EditableSymbolProject>()
                                 .OrderByDescending(x => x.CsProjectFile.ModifiedAt)
                                 .ToList();
            
            return _allProjectsCache;
        }
    }
    
    //  New helper to force reload internally
    public void MarkCodeExternallyModified()
    {
        CodeExternallyModified = true;
    }
}