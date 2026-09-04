using System.Numerics;
using ImGuiNET;
using SilkWindows.Implementations.FileManager.ItemDrawers;

namespace SilkWindows.Implementations.FileManager;

public sealed partial class FileManager
{
    private void DrawTable(ImFonts fonts, List<Column> expanded)
    {
        const ImGuiTableFlags tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.NoClip;
        const ImGuiTableColumnFlags columnFlags = ImGuiTableColumnFlags.NoHide |
                                                  ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.NoHeaderWidth | ImGuiTableColumnFlags.WidthStretch;
        const ImGuiWindowFlags windowFlags = ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.HorizontalScrollbar;
        
        var mouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Middle);
        
        if (!mouseDown)
        {
            _dragScrollingColumn = null;
        }
        
        if (ImGui.BeginTable(_tableId, expanded.Count, tableFlags))
        {
            ImGui.TableNextRow();
            for (var index = 0; index < expanded.Count; index++)
            {
                var column = expanded[index];
                var directoryDrawer = column.Drawer;
                
                ImGui.TableSetupColumn(column.Id, columnFlags);
                ImGui.TableSetColumnIndex(index);
                
                ImGui.BeginChild(directoryDrawer.Path, new Vector2(0, 0), ImGuiChildFlags.Borders, windowFlags);
                
                if (_dragScrollingColumn == column)
                {
                    var mousePos = ImGui.GetMousePos();
                    var scroll = _dragStart - mousePos;
                    ImGui.SetScrollX(scroll.X);
                    ImGui.SetScrollY(scroll.Y);
                }
                
                directoryDrawer.Draw(fonts);
                
                ImGui.EndChild();
                
                if(mouseDown && _dragScrollingColumn == null && ImGui.IsItemHovered(ImGuiHoveredFlags.ChildWindows))
                {
                    _dragScrollingColumn = column;
                    _dragStart = ImGui.GetMousePos();
                }
            }
            
            ImGui.EndTable();
        }
    }
    
    private Column? _dragScrollingColumn;
    private Vector2 _dragStart;
    
    private sealed class Column
    {
        public readonly string Id;
        public readonly DirectoryDrawer Drawer;
        public bool Drawn;
        
        public Column(DirectoryDrawer drawer, bool drawn, string id)
        {
            Id = id;
            Drawer = drawer;
            Drawn = drawn;
            drawer.ToggleButtonPressed = () => Drawn = !Drawn;
        }
    }
}