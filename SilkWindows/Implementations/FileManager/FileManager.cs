using System.Collections.Concurrent;
using ImGuiNET;
using SilkWindows.Implementations.FileManager.ItemDrawers;
using T3.Core.SystemUi;

namespace SilkWindows.Implementations.FileManager;

public readonly record struct ManagedDirectory(string Path, bool IsReadOnly, bool startExpanded = true, string? Alias = null);

public enum FileManagerMode
{
    PickDirectory,
    PickFile,
    Manage
}

public record PathInformation(string AbsolutePath, string? RelativePathWithAlias, string RelativePath);

public sealed partial class FileManager : AsyncImguiDrawer<PathInformation>, IFileManager
{
    public FileManager(FileManagerMode mode, IEnumerable<ManagedDirectory> rootDirectories, Func<string, bool>? fileFilter = null)
    {
        _mode = mode;
        _fileConflictResolver = FileConflictWindow;
        
        _fileFilter = fileFilter ?? (_ => true);
        _folderTabs = rootDirectories
                     .Select(dir =>
                             {
                                 var directoryInfo = new DirectoryInfo(dir.Path);
                                 if (!directoryInfo.Exists)
                                     throw new DirectoryNotFoundException(directoryInfo.FullName);
                                 
                                 var drawer = new DirectoryDrawer(this, directoryInfo, dir.IsReadOnly, null, dir.startExpanded, dir.Alias);
                                 
                                 var id = "##col_" + directoryInfo.FullName;
                                 return new Column(drawer, dir.startExpanded, id);
                             }).ToArray();
    }
    
    public FileManager(FileManagerMode mode, ManagedDirectory rootDirectory, Func<string, bool>? fileFilter = null) : this(mode, [rootDirectory], fileFilter)
    {
    }
    
    private readonly Column[] _folderTabs;
    private readonly List<Column> _columnsToDraw = [];
    private readonly List<Column> _columnsMinimized = [];
    
    private void DrawCollapsedButtons(ImFonts fonts, List<Column> collapsed)
    {
        ImGui.SameLine();
        
        // draw right-aligned buttons
        var startPosition = ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX();
        foreach (var column in collapsed)
        {
            startPosition -= column.Drawer.LastDrawnSize.X + ImGui.GetStyle().ItemSpacing.X;
            //startPosition -= ImGui.CalcTextSize(column.Drawer.DisplayName).X + innerSpacing;
        }
        
        ImGui.SetCursorPosX(startPosition);
        
        foreach (var column in collapsed)
        {
            var drawer = column.Drawer;
            drawer.Draw(fonts, true);
            ImGui.SameLine();
            continue;
            // if (ImGui.Button(drawer.DisplayName + "##expand_" + drawer.Path))
            // {
            //     column.Drawn = true;
            // }
            //
            // ImGui.SameLine();
        }
        
        ImGui.NewLine();
    }
    
    private void DrawPickButtonAndSetResult<T>(string buttonName) where T : FileSystemDrawer
    {
        var first = _selections.FirstOrDefault() as T;
        var isEnabled = first is not null;
        
        if (!isEnabled)
        {
            ImGui.BeginDisabled();
        }
        
        if (ImGui.Button(buttonName))
        {
            PickItem(first!);
        }
        
        if (!isEnabled)
        {
            ImGui.EndDisabled();
        }
    }
    
    private FileConflictOption FileConflictWindow(FileInfo file)
    {
        FileConflictOption? result = BlockingWindow.Instance.ShowMessageBox($"File \"{file.FullName}\" already exists.\n\nDo you want to overwrite it?",
                                                                            "File already exists",
                                                                            val => val.ToString(),
                                                                            Enum.GetValues<FileConflictOption>());
        
        if (result.HasValue)
            return result.Value;
        
        Log($"Window closed without selection - skipping {file.FullName}");
        return FileConflictOption.Skip;
    }
    
    #region IFileManager Implementation
    #region Feature: Selection
    public void ItemClicked(FileSystemDrawer drawer)
    {
        if (drawer is DirectoryDrawer { IsRoot: true } directoryDrawer)
        {
            _selectedRoot = directoryDrawer;
            _selections.Clear();
            return;
        }
        
        _selectedRoot = null;
        var wasSelected = IsSelected(drawer);
        
        var ctrl = ImGui.GetIO().KeyCtrl;
        // todo - shift to select/deselect multiple
        
        if (!ctrl)
            _selections.Clear();
        else if (wasSelected)
        {
            RemoveFromSelection(drawer);
            return;
        }
        
        _selections.Add(drawer);
    }
    
    public void RemoveFromSelection(FileSystemDrawer drawer)
    {
        _selections.Remove(drawer);
    }
    
    public bool IsSelected(FileSystemDrawer drawer)
    {
        return _selections.Contains(drawer);
    }
    
    private PathInformation CreatePathInformation(FileSystemDrawer drawer)
    {
        var path = drawer.Path;
        var rootDir = drawer.RootDirectory;
        var relativePath = Path.GetRelativePath(rootDir.Path, path);
        var relativePathWithAlias = rootDir.Alias == null ? relativePath : Path.Combine('/' + rootDir.Alias, relativePath);
        return new PathInformation(path, relativePathWithAlias, relativePath);
    }
    #endregion
    
    IEnumerable<FileSystemInfo> IFileManager.GetDirectoryContents(DirectoryInfo directory)
    {
        return directory.EnumerateFileSystemInfos()
                        .Where(x => x is DirectoryInfo || x is FileInfo fileInfo && _fileFilter(fileInfo.FullName));
    }
    
    void IFileManager.DoubleClicked(FileDrawer drawer, bool inExternalEditor)
    {
        //if (_multiSelectEnabled)
        //  return;
        
        if (_selections.Count != 1)
        {
            Log($"Cannot open {_selections.Count} files at once");
            return;
        }
        
        if (_selections.First() != drawer)
        {
            Log("Selection does not match double clicked item");
            ItemClicked(drawer); // todo: this is a temporary hack - probably not even necessary at time of writing
        }
        
        if (inExternalEditor)
        {
            // todo: open in external editor if not read-only
            // should not be able to enter this mode if directory is read-only
            return;
        }
        
        PickItem(drawer);
        
        // todo: mode - file picker, open in default app, etc
        var path = drawer.Path;
        
        Log(drawer, "Opening " + path);
    }
    
    private void PickItem(FileSystemDrawer drawer)
    {
        switch (_mode)
        {
            case FileManagerMode.PickDirectory:
                if(drawer.IsDirectory)
                    Result = CreatePathInformation(drawer);
                break;
            case FileManagerMode.PickFile:
                if (!drawer.IsDirectory)
                    Result = CreatePathInformation(drawer);
                
                break;
            case FileManagerMode.Manage:
                // todo: open in external editor if not read-only
                // should not be able to enter this mode if directory is read-only
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    
    void IFileManager.ShowInSystemFileManager(FileSystemDrawer drawer)
    {
        // todo: open in system file manager
        var directory = drawer.IsDirectory ? drawer.Path : drawer.ParentDirectoryDrawer!.Path;
        
        Log(drawer, $"Showing in system file manager");
    }
    
    public void Log(FileSystemDrawer drawer, string log)
    {
        var parent = drawer.ParentDirectoryDrawer;
        var fileOrDirectory = drawer.IsDirectory ? "Directory " : "File ";
        var msg = parent != null
                      ? fileOrDirectory + $"[..{parent.DisplayName}/{drawer.DisplayName}]: {log}"
                      : $"[{drawer.DisplayName}]: {log}";
        
        Log(msg);
    }
    
    void IFileManager.CreateNewSubfolder(DirectoryDrawer directoryDrawer, bool consumeDroppedFiles)
    {
        var nameFolderDialog = new NewSubfolderWindow(directoryDrawer.DirectoryInfo);
        var result = ImGuiWindowService.Instance.Show("Create new subfolder", nameFolderDialog);
        
        if (result is not { Exists: true })
        {
            if (consumeDroppedFiles)
                ConsumeArray(ref _droppedPaths);
            
            return;
        }
        
        if (consumeDroppedFiles)
        {
            if (_droppedPaths.Length == 0)
            {
                Log("No dropped files to consume??");
                return;
            }
            
            _ = TryDropPathsInto(directoryDrawer.RootDirectory.DirectoryInfo, result, _droppedPaths);
            
            ConsumeArray(ref _droppedPaths);
        }
        
        directoryDrawer.MarkNeedsRescan();
    }
    
    public string FormatPathForDisplay(string path)
    {
        return path.Replace('\\', '/');
    }
    #endregion
    
    // ReSharper disable once RedundantAssignment
    // a silly little helper method to keep the way I'm handling the dragged/dropped files consistent and easy to find
    private static void ConsumeArray<T>(ref T[] paths) => paths = [];
    
    private void Log(string log) => _logs.Enqueue(log);
    
    private static readonly HashSet<FileSystemDrawer> _selections = [];
    
    private static DirectoryDrawer? _selectedRoot;
    //private static Vector2 _tabDragPosition;
    
    private static int _fileManagerCount = 0;
    private readonly string _tableId = "##" + Interlocked.Increment(ref _fileManagerCount);
    
    private readonly FileManagerMode _mode;
    private readonly ConcurrentQueue<string> _logs = new();
    private const int MaxLogCount = 100;
    private readonly Func<string, bool> _fileFilter;
    private readonly Func<FileInfo, FileConflictOption> _fileConflictResolver;
}