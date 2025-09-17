#nullable enable
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using T3.Core.Model;
using T3.Core.Operator;

namespace T3.Editor.UiModel;

internal sealed partial class EditableSymbolProject
{
    public void SaveAll()
    {
        if (IsSaving)
        {
            Log.Error($"{CsProjectFile.Name}: Saving is already in progress.");
            return;
        }

        Log.Debug($"{CsProjectFile.Name}: Saving...");

        MarkAsSaving();
        WriteAllSymbolFilesOf(SymbolUiDict.Values);
        UpdateLastModifiedDate();
        UnmarkAsSaving();
    }

    internal void Update(out bool needsUpdating)
    {
        if (CodeExternallyModified)
        {
            CodeExternallyModified = false;
            if (_lastRecompilationTimeUtc.HasValue && (DateTime.UtcNow - _lastRecompilationTimeUtc.Value).TotalSeconds < 0.5f)
            {
                Log.Info($"{DisplayName}: Skipping recompilation due to a presumed-misfired file change event");
                needsUpdating = false;
                return;
            }

            Log.Info($"{DisplayName}: Recompiling project due to external code change...");
            needsUpdating = true;
            if (!TryRecompile(false, out var failureLog))
            {
                Log.Error($"{DisplayName}: Recompilation failed.");
                Log.Debug(failureLog);
            }
        }
        else
        {
            needsUpdating = false;
        }
    }

    internal void SaveModifiedSymbols()
    {
        if (IsSaving)
        {
            Log.Error($"{CsProjectFile.Name}: Saving is already in progress.");
            return;
        }

        MarkAsSaving();

        var modifiedSymbolUis = SymbolUiDict
                               .Select(x => x.Value)
                               .Where(symbolUi => symbolUi.NeedsSaving)
                               .ToArray();

        if (modifiedSymbolUis.Length != 0)
        {
            Log.Debug($"{CsProjectFile.Name}: Saving {modifiedSymbolUis.Length} modified symbols...");

            WriteAllSymbolFilesOf(modifiedSymbolUis);
            UpdateLastModifiedDate();
        }

        UnmarkAsSaving();
    }

    public void UpdateLastModifiedDate() {
        CsProjectFile.UpdateLastModifiedDate();
        _allProjectsCache = null;
    }

    protected override void OnSymbolAdded(string? path, Symbol symbol)
    {
        path ??= SymbolPathHandler.GetCorrectPath(symbol.Name, symbol.Namespace, Folder, CsProjectFile.RootNamespace, SymbolExtension);
        base.OnSymbolAdded(path, symbol);

        if (!AutoOrganizeOnStartup)
            return;

        // ReSharper disable once HeuristicUnreachableCode
        #pragma warning disable CS0162 // Unreachable code detected
        FilePathHandlers[symbol.Id].AllFilesReady += CorrectFileLocations;
        #pragma warning restore CS0162 // Unreachable code detected
    }

    protected override void OnSymbolUiLoaded(string? path, SymbolUi symbolUi)
    {
        symbolUi.ReadOnly = false;
        path ??= SymbolPathHandler.GetCorrectPath(symbolUi.Symbol.Name, symbolUi.Symbol.Namespace, Folder, CsProjectFile.RootNamespace, SymbolUiExtension);
        base.OnSymbolUiLoaded(path, symbolUi);
    }

    private void OnSymbolUpdated(Symbol symbol)
    {
        var filePathHandler = FilePathHandlers[symbol.Id];

        if (symbol != filePathHandler.Symbol)
        {
            throw new Exception("Symbol mismatch when updating symbol files");
        }

        filePathHandler.UpdateFromSymbol();
    }

    /// <summary>
    /// Removal is a feature unique to editable projects - all others are assumed to be read-only and unchanging
    /// </summary>
    /// <param name="id">Id of the symbol to be removed</param>
    private void OnSymbolRemoved(Guid id)
    {
        SymbolDict.Remove(id, out var symbol);

        Debug.Assert(symbol != null);

        SymbolUiDict.Remove(id, out _);

        Log.Info($"Removed symbol {symbol.Name}");
    }

    private static Action<SymbolPathHandler> CorrectFileLocations => handler =>
                                                                     {
                                                                         handler.AllFilesReady -= CorrectFileLocations;
                                                                         handler.UpdateFromSymbol();
                                                                     };

    private void WriteAllSymbolFilesOf(IEnumerable<SymbolUi> symbolUis)
    {
        foreach (var symbolUi in symbolUis)
        {
            SaveSymbolFile(symbolUi);
        }
    }

    private void SaveSymbolFile(SymbolUi symbolUi)
    {
        var symbol = symbolUi.Symbol;
        var id = symbol.Id;
        var pathHandler = FilePathHandlers[id];

        if (!pathHandler.TryCreateDirectory())
        {
            Log.Error($"Could not create directory for symbol {symbol.Id}");
            return;
        }

        var sourceCodePath = pathHandler.SourceCodePath;
        if (sourceCodePath != null)
            WriteSymbolSourceToFile(id, sourceCodePath);
        else
            throw new Exception($"{CsProjectFile.Name}: No source code path found for symbol {id}");

        var symbolPath = pathHandler.SymbolFilePath ??= SymbolPathHandler.GetCorrectPath(symbol, this);
        SaveSymbolDefinition(symbol, symbolPath);
        pathHandler.SymbolFilePath = symbolPath;

        var uiFilePath = pathHandler.UiFilePath ??= SymbolPathHandler.GetCorrectPath(symbolUi, this);
        WriteSymbolUi(symbolUi, uiFilePath);
        pathHandler.UiFilePath = uiFilePath;

        #if DEBUG
        var debug = $"{CsProjectFile.Name}: Saved [{symbol.Name}] to:\nSymbol: \"{symbolPath}\"\nUi: \"{uiFilePath}\"\nSource: \"{sourceCodePath}\"\n";
        #else
                var debug = $"{DisplayName}: Saved [{symbol.Name}]";
        #endif
        Log.Debug(debug);
    }

    private static void WriteSymbolUi(SymbolUi symbolUi, string uiFilePath)
    {
        using var sw = new StreamWriter(uiFilePath, _saveOptions);
        using var writer = new JsonTextWriter(sw);

        writer.Formatting = Formatting.Indented;
        SymbolUiJson.WriteSymbolUi(symbolUi, writer);

        symbolUi.ClearModifiedFlag();
    }

    private void SaveSymbolDefinition(Symbol symbol, string filePath)
    {
        using var sw = new StreamWriter(filePath, _saveOptions);
        using var writer = new JsonTextWriter(sw);
        writer.Formatting = Formatting.Indented;
        SymbolJson.WriteSymbol(symbol, writer);
    }

    private void WriteSymbolSourceToFile(Guid id, string sourcePath)
    {
        if (!_pendingSource.Remove(id, out var sourceCode))
            return;

        using var sw = new StreamWriter(sourcePath, _saveOptions);
        sw.Write(sourceCode);
    }

    public bool CodeExternallyModified { get; private set; }
    private DateTime? _lastRecompilationTimeUtc;

    private void MarkAsSaving() => _csFileWatcher.EnableRaisingEvents = Interlocked.Increment(ref _savingCount) <= 0;

    private void UnmarkAsSaving()
    {
        var count = Interlocked.Decrement(ref _savingCount);
        if (count < 0)
        {
            Log.Error($"Saving count is negative: {count}. This should not happen.");
            _savingCount = count = 0;
        }

        _csFileWatcher.EnableRaisingEvents = count <= 0;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args)
    {
        var name = args.Name;
        if (name == null)
            return;

        // generated file by dotnet - ignore
        if (name.EndsWith("AssemblyInfo.cs"))
            return;

        Log.Info($"{DisplayName}: Code file changed: {name}");
        CodeExternallyModified = true;
        //TryRecompile(true); // don't recompile here - we need to make sure this happens on the main thread
    }

    private void OnCodeFileRenamed(object sender, RenamedEventArgs args)
    {
        Log.Error($"{DisplayName}: File {args.OldFullPath} renamed to {args.FullPath}. Please do not do this while the editor is running.");
        CodeExternallyModified = true;
        //TryRecompile(true); // don't recompile here - we need to make sure this happens on the main thread
    }

    public override void LocateSourceCodeFiles()
    {
        MarkAsSaving();
        base.LocateSourceCodeFiles();
        UnmarkAsSaving();
    }

    public bool IsSaving => Interlocked.Read(ref _savingCount) > 0;
    private long _savingCount;
    private static readonly FileStreamOptions _saveOptions = new() { Mode = FileMode.Create, Access = FileAccess.ReadWrite };

    private const bool AutoOrganizeOnStartup = false;
    private readonly Dictionary<Guid, string> _pendingSource = new();

    private sealed class CodeFileWatcher : FileSystemWatcher
    {
        public CodeFileWatcher(EditableSymbolProject project, FileSystemEventHandler onChange, RenamedEventHandler onRename) :
            base(project.Folder, "*.cs")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.DirectoryName;

            IncludeSubdirectories = true;
            Changed += onChange;
            Created += onChange;
            Renamed += onRename;
        }
    }
}