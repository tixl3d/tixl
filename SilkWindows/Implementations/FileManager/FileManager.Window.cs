using ImGuiNET;
using SilkWindows.Implementations.FileManager.ItemDrawers;

namespace SilkWindows.Implementations.FileManager;

public sealed partial class FileManager
{
    public override void Init()
    {
    }
    
    public override void OnRender(string windowName, double deltaSeconds, ImFonts fonts)
    {
        switch (_mode)
        {
            case FileManagerMode.PickDirectory:
                DrawPickButtonAndSetResult<DirectoryDrawer>("Pick directory");
                break;
            case FileManagerMode.PickFile:
                DrawPickButtonAndSetResult<FileDrawer>("Pick file");
                break;
            case FileManagerMode.Manage:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (_selectedRoot != null)
            {
                // check for tab position swap
            }
            
            _selectedRoot = null;
        }
        
        _columnsToDraw.Clear();
        _columnsMinimized.Clear();
        
        foreach (var column in _folderTabs)
        {
            if (column.Drawn)
            {
                _columnsToDraw.Add(column);
            }
            else
            {
                if (_selectedRoot == column.Drawer)
                    _selectedRoot = null;
                
                _columnsMinimized.Add(column);
            }
        }
        
        CheckForFileDrop();
        
        if (_columnsMinimized.Count > 0)
        {
            DrawCollapsedButtons(fonts, _columnsMinimized);
        }
        
        if (_columnsToDraw.Count > 0)
        {
            DrawTable(fonts, _columnsToDraw);
        }
        
        DragFileDragIndicators(fonts);
        
        // todo - log toasts
        //return;
        
        // ImGui.SetNextWindowScroll(new Vector2(0f, float.MaxValue));
        // ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        // ImGui.BeginChild("Logs" + _tableId);
        // while (_logs.Count > MaxLogCount)
        // {
        //     _logs.TryDequeue(out _);
        // }
        //
        // foreach (var log in _logs)
        // {
        //     ImGui.Text(log);
        // }
        //
        // ImGui.EndChild();
    }
    
    protected override void OnWindowUpdateImpl(double deltaSeconds)
    {
    }
    
    public override void OnWindowFocusChanged(bool changedTo)
    {
        ConsumeArray(ref _draggedPaths);
        ConsumeArray(ref _droppedPaths);
    }
}