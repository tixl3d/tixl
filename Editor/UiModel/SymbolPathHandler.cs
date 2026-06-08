#nullable enable
using System.IO;
using T3.Core.Model;
using T3.Core.Operator;

namespace T3.Editor.UiModel;

internal sealed class SymbolPathHandler
{
    private string? _name;
    private string? _namespace;
    
    private string? _symbolFilePath;
    public string? SymbolFilePath
    {
        get => _symbolFilePath;
        set
        {
            _symbolFilePath = value;
            CheckInitialization();
        }
    }

    private string? _sourceCodePath;
    public string? SourceCodePath
    {
        get => _sourceCodePath;
        set
        {
            _sourceCodePath = value;
            CheckInitialization();
        }
    }

    private string? _uiFilePath;
    public string? UiFilePath
    {
        get => _uiFilePath;
        set
        {
            _uiFilePath = value;
            CheckInitialization();
        }
    }

    public Symbol Symbol;
    private bool _initialized;
    private string _projectFolder = string.Empty;
    
    public event Action<SymbolPathHandler>? AllFilesReady;

    public SymbolPathHandler(Symbol symbol, string? symbolFilePath)
    {
        Symbol = symbol;
        _symbolFilePath = symbolFilePath;
    }

    private bool HasAllFiles => _sourceCodePath != null && _uiFilePath != null && _symbolFilePath != null;

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Global
    public bool UpdateFromSymbol()
    {
        var project = (EditableSymbolProject)Symbol.SymbolPackage;

        var name = Symbol.Name;
        var @namespace = Symbol.Namespace;
        var projectFolder = project.Folder;
        var rootNamespace = project.CsProjectFile.RootNamespace;

        if (_name == name && _namespace == @namespace && _projectFolder == projectFolder)
            return false;

        var newFolder = GetCorrectDirectory(@namespace, rootNamespace, projectFolder);
        var fmt = GetPathFormat(newFolder, name);

        var changed = false;
        if (_initialized && TryCreateDirectory(newFolder))
        {
            changed |= MoveFileIfNecessary(_sourceCodePath!, EditorSymbolPackage.SourceCodeExtension, fmt, out var sourceCodePath);
            _sourceCodePath = sourceCodePath;

            changed |= MoveFileIfNecessary(_symbolFilePath!, SymbolPackage.SymbolExtension, fmt, out var symbolPath);
            _symbolFilePath = symbolPath;

            changed |= MoveFileIfNecessary(_uiFilePath!, EditorSymbolPackage.SymbolUiExtension, fmt, out var uiPath);
            _uiFilePath = uiPath;
        }

        _name = name;
        _namespace = @namespace;
        _projectFolder = projectFolder;
        return changed;
    }

    public static string GetCorrectPath(string name, string? @namespace, string projectFolder, string? rootNamespace, string fileExtension)
    {
        var folder = GetCorrectDirectory(@namespace, rootNamespace, projectFolder);
        var fmt = GetPathFormat(folder, name);
        return string.Format(fmt, fileExtension);
    }

    public static string GetCorrectPath(Symbol symbol, EditableSymbolProject project)
    {
        return GetCorrectPath(symbol.Name, symbol.Namespace, project.Folder, project.CsProjectFile.RootNamespace, SymbolPackage.SymbolExtension);
    }

    public static string GetCorrectPath(SymbolUi ui, EditableSymbolProject project)
    {
        var symbol = ui.Symbol;
        return GetCorrectPath(symbol.Name, symbol.Namespace, project.Folder, project.CsProjectFile.RootNamespace, EditorSymbolPackage.SymbolUiExtension);
    }

    public static string GetCorrectSourceCodePath(string name, string @namespace, EditableSymbolProject project)
    {
        return GetCorrectPath(name, @namespace, project.Folder, project.CsProjectFile.RootNamespace, EditorSymbolPackage.SourceCodeExtension);
    }

    private static string GetCorrectDirectory(string? @namespace, string? rootNamespace, string projectFolder)
    {
        @namespace ??= string.Empty;
        rootNamespace ??= string.Empty;

        // Strip the root-namespace prefix (case-insensitive — csproj RootNamespace casing may differ from
        // the operators' namespace). Length-strip, not String.Replace, to drop only the prefix.
        var symbolNamespace = @namespace.StartsWith(rootNamespace, StringComparison.OrdinalIgnoreCase)
                                  ? @namespace[rootNamespace.Length..]
                                  : @namespace;

        string directory;

        if (string.IsNullOrWhiteSpace(symbolNamespace) || symbolNamespace == rootNamespace)
        {
            directory = projectFolder;
        }
        else
        {
            var namespaceParts = symbolNamespace.Split('.').Where(x => x.Length > 0);
            var subfolders = new[] { projectFolder }.Concat(namespaceParts).ToArray();
            directory = Path.Combine(subfolders);
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private static bool MoveFileIfNecessary(string currentFilePath, string extension, string fmt, out string updatedFilePath)
    {
        var correctFilePath = string.Format(fmt, extension);

        if (currentFilePath == correctFilePath)
        {
            updatedFilePath = currentFilePath;
            return false;
        }

        try
        {
            File.Move(currentFilePath, correctFilePath);
            updatedFilePath = correctFilePath;
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to move symbol \"{currentFilePath}\" to \"{correctFilePath}\"\n{e}");
            updatedFilePath = currentFilePath;
            return false;
        }
    }

    private static string GetPathFormat(string folder, string name) => Path.Combine(folder, name + "{0}");
    

    private void CheckInitialization()
    {
        if (!_initialized && HasAllFiles)
        {
            _initialized = true;
            AllFilesReady?.Invoke(this);
        }
    }

    public bool TryCreateDirectory(string? folder = null)
    {
        folder ??= Symbol.SymbolPackage.Folder;
        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create directory \"{folder}\"\n{e}");
            return false;
        }
    }
}