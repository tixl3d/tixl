using ImGuiNET;
using SilkWindows.Implementations.FileManager.ItemDrawers;

// ReSharper disable InvertIf

namespace SilkWindows.Implementations.FileManager;

public sealed partial class FileManager
{
    public override void OnFileDrop(string[] filePaths)
    {
        _droppedPaths = FileOperations.PathsToFileSystemInfo(filePaths).ToArray();
    }
    
    public bool IsDraggingPaths => _draggedPaths.Length > 0;
    
    bool IFileManager.IsDropTarget(FileSystemDrawer drawer, out DirectoryDrawer targetDir)
    {
        targetDir = GetDropLocationFromHoveredDrawer(drawer);
        
        if (drawer.IsReadOnly)
        {
            return false;
        }
        
        if (_draggedPaths.Length == 0)
        {
            Console.WriteLine("No dragged files");
            return false;
        }
        
        return drawer switch
                   {
                       DirectoryDrawer directoryDrawer => CanReceiveDraggedFiles(directoryDrawer, _draggedPaths),
                       FileDrawer fileDrawer           => CanReceiveDraggedFiles(fileDrawer.ParentDirectoryDrawer!, _draggedPaths),
                       _                               => throw new ArgumentOutOfRangeException(nameof(drawer), drawer, null)
                   };
        
        static bool CanReceiveDraggedFiles(DirectoryDrawer directoryDrawer, FileSystemInfo[] draggedPaths)
        {
            var targetDirectory = directoryDrawer.DirectoryInfo.FullName;
            foreach (var fileSystemInfo in draggedPaths)
            {
                var path = fileSystemInfo.FullName;
                if (path == targetDirectory)
                    return false;
                
                DirectoryInfo? parent = null;
                switch (fileSystemInfo)
                {
                    case FileInfo fileInfo:
                        parent = fileInfo.Directory;
                        break;
                    case DirectoryInfo directoryInfo:
                        
                        // can't drag a parent directory into its child
                        if (targetDirectory.StartsWith(directoryInfo.FullName))
                            return false;
                        
                        parent = directoryInfo.Parent;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(fileSystemInfo), fileSystemInfo, null);
                }
                
                if (parent?.FullName != targetDirectory)
                    continue;
                
                Console.WriteLine($"Can't receive dropped files from its own directory.\nSource: \"{path}\nTarget: \"{targetDirectory}\"");
                return false;
            }
            
            return true;
        }
    }
    
    void IFileManager.ConsumeDroppedFiles(FileSystemDrawer drawer)
    {
        if (_droppedPaths.Length == 0)
            throw new InvalidOperationException("Files were already consumed");
        
        var directoryDrawer = GetDropLocationFromHoveredDrawer(drawer);
        
        var droppedPaths = _droppedPaths;
        ConsumeArray(ref _droppedPaths);
        
        if (directoryDrawer.IsReadOnly)
        {
            return;
        }
        
        if (TryDropPathsInto(directoryDrawer.RootDirectory.DirectoryInfo, directoryDrawer.DirectoryInfo, droppedPaths))
        {
            directoryDrawer.MarkNeedsRescan();
        }
    }
    
    private DirectoryDrawer GetDropLocationFromHoveredDrawer(FileSystemDrawer drawer)
    {
        var dropInParent = ShouldDropInParent;
        var directoryDrawer = drawer switch
                                  {
                                      DirectoryDrawer dir when dropInParent  => dir.ParentDirectoryDrawer ?? dir,
                                      DirectoryDrawer dir when !dropInParent => dir,
                                      FileDrawer fileDrawer                  => fileDrawer.ParentDirectoryDrawer!,
                                      _                                      => throw new ArgumentOutOfRangeException(nameof(drawer), drawer, null)
                                  };
        return directoryDrawer;
    }
    
    private bool TryDropPathsInto(DirectoryInfo rootDirectory, DirectoryInfo targetDirectory, IEnumerable<FileSystemInfo> paths)
    {
        var targetRootDirectoryPath = rootDirectory.FullName;
        var droppedDirectories = new List<DirectoryInfo>();
        var droppedFiles = new List<FileInfo>();
        
        foreach (var fileSystemInfo in paths)
        {
            if (!fileSystemInfo.Exists)
            {
                Console.WriteLine($"Path does not exist: {fileSystemInfo.FullName}");
                continue;
            }
            
            if (fileSystemInfo is FileInfo fileInfo)
            {
                droppedFiles.Add(fileInfo);
            }
            else
            {
                droppedDirectories.Add((DirectoryInfo)fileSystemInfo);
            }
        }
        
        // remove any duplicate directories
        for (var i = droppedDirectories.Count - 1; i >= 0; i--)
        {
            var directory = droppedDirectories[i];
            bool shouldRemove = false;
            
            foreach (var otherDirectory in droppedDirectories)
            {
                if (otherDirectory == directory)
                    continue;
                
                if (otherDirectory.FullName.StartsWith(directory.FullName))
                {
                    shouldRemove = true;
                    break;
                }
            }
            
            if (shouldRemove)
            {
                droppedDirectories.RemoveAt(i);
            }
        }
        
        // remove any duplicate or unwanted files
        var files = droppedFiles
                   .Where(file => _fileFilter(file.FullName))
                   .Where(x => !droppedDirectories
                               .Select(dir => dir.FullName)
                               .Any(x.FullName.StartsWith))
                   .ToArray();
        
        var movedOne = false;
        foreach (var file in files)
        {
            var moveType = file.FullName.StartsWith(targetRootDirectoryPath) ? FileOperations.MoveType.Move : FileOperations.MoveType.Copy;
            movedOne |= FileOperations.TryMoveFile(moveType, file, targetDirectory, _fileConflictResolver);
        }
        
        foreach (var directory in droppedDirectories)
        {
            var moveType = directory.FullName.StartsWith(targetRootDirectoryPath) ? FileOperations.MoveType.Move : FileOperations.MoveType.Copy;
            movedOne |= FileOperations.TryMoveDirectory(moveType, directory, targetDirectory, _fileConflictResolver);
        }
        
        return movedOne;
    }
    
    public void BeginDragOn(FileSystemDrawer drawer)
    {
        if (IsDraggingPaths)
            return;
        
        if (drawer is DirectoryDrawer { IsRoot: true })
            return;
        
        _selections.Add(drawer);
        _draggedPaths = FileOperations.PathsToFileSystemInfo(_selections.Select(x => x.Path)).ToArray();
    }
    
    private void CheckForFileDrop()
    {
        if (!IsDraggingPaths)
            return;
        
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;
        
        _droppedPaths = _draggedPaths;
        ConsumeArray(ref _draggedPaths);
    }
    
    private void DragFileDragIndicators(ImFonts fonts)
    {
        var isDragging = IsDraggingPaths;
        ImGui.SetMouseCursor(isDragging ? ImGuiMouseCursor.Hand : ImGuiMouseCursor.Arrow);
        IEnumerable<FileSystemDrawer> selections = _selections;
        if (!isDragging)
        {
            if (_selectedRoot == null)
                return;
            
            selections = new[] { _selectedRoot };
        }
        
        var originalCursorPos = ImGui.GetCursorScreenPos();
        var mousePos = ImGui.GetMousePos();
        
        ImGui.SetCursorScreenPos(mousePos);
        ImGui.BeginGroup();
        foreach (var item in selections)
        {
            var expanded = item.Expanded;
            item.Expanded = false;
            item.Draw(fonts,  true);
            item.Expanded = expanded;
        }
        
        ImGui.EndGroup();
        ImGui.SetCursorScreenPos(originalCursorPos);
    }
    
    private bool ShouldDropInParent => ImGui.GetIO().KeyShift;
    private FileSystemInfo[] _droppedPaths = [];
    private FileSystemInfo[] _draggedPaths = [];
    public bool HasDroppedFiles => _droppedPaths.Length > 0;
}