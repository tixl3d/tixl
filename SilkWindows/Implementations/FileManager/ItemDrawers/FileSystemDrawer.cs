using System.Numerics;
using ImGuiNET;

namespace SilkWindows.Implementations.FileManager.ItemDrawers;

// todo: child class (DirectoryDrawer) being referenced in the base class is UGLY
public abstract class FileSystemDrawer
{
    private protected readonly IFileManager FileManager;
    
    private protected FileSystemDrawer(IFileManager fileManager, DirectoryDrawer? parent)
    {
        FileManager = fileManager;
        ParentDirectoryDrawer = parent;
    }
    
    internal abstract DirectoryDrawer RootDirectory { get; }
    public abstract bool IsReadOnly { get; }
    internal abstract bool IsDirectory { get; }
    internal abstract string DisplayName { get; }
    internal abstract string Path { get; }
    
    internal DirectoryDrawer? ParentDirectoryDrawer { get; }
    
    protected abstract void DrawTooltipContents(ImFonts fonts);
    
    protected internal abstract FileSystemInfo FileSystemInfo { get; }
    
    protected virtual ImGuiHoveredFlags HoverFlags => ImGuiHoveredFlags.None;
    protected internal bool Expanded { get; set; }
    
    protected bool IsHovered() => ImGui.IsItemHovered(HoverFlags);
    
    const ImGuiHoveredFlags FileDragHoverFlags =  ImGuiHoveredFlags.DelayNone 
                                                 | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem;
    
    protected bool HoveredByFileDrag(ImGuiHoveredFlags flags = FileDragHoverFlags) =>
        FileManager.IsDraggingPaths && ImGui.IsItemHovered(flags);
    
    protected abstract void DrawSelectable(ImFonts fonts, bool isSelected);
    protected abstract void CompleteDraw(ImFonts fonts, bool hovered, bool isSelected);
    
    public void Draw(ImFonts fonts, bool forceDeselected = false)
    {
        bool isSelected = FileManager.IsSelected(this);
        var fileInfo = FileSystemInfo;
        fileInfo.Refresh();
        var missing = !fileInfo.Exists;
        if (missing)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.2f, 0.2f, 1f));
        }
        
        DrawSelectable(fonts, isSelected && !forceDeselected);
        
        var hovered = IsHovered();
        var hoveredByFileDrag = HoveredByFileDrag();
        
        if (hovered)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                FileManager.ItemClicked(this);
            }
            
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left, lock_threshold: 2f)) // lock threshold is required distance of dragging (in pixels maybe??)
            {
                FileManager.BeginDragOn(this);
            }
            
            if (FileManager.HasDroppedFiles)
            {
                FileManager.ConsumeDroppedFiles(this);
            }
            
            if (ImGui.IsMouseDoubleClicked(0))
            {
                OnDoubleClicked();
            }
        }
        
        
        if (hoveredByFileDrag && FileManager.IsDropTarget(this, out var targetDirDrawer))
        {
            var imTheTarget = targetDirDrawer == this;
            if(imTheTarget)
                ImGui.Indent();
            ImGui.SeparatorText($"Drop in \"{targetDirDrawer.DisplayName}\"");
            if(imTheTarget)
                ImGui.Unindent();
        }
        else if (!missing)
        {
            if (ImGui.BeginPopupContextItem())
            {
                DrawContextMenuContents(fonts);
                
                ImGui.EndPopup();
            }
            
            // check for root directory drawer since we treat it specially. it should probably be its own class but here we are for now
            // the root directory has its own tooltip that this would mess up 
            if (!FileManager.IsDraggingPaths && hovered && this is not DirectoryDrawer { IsRoot: true })
            {
                if (ImGui.BeginTooltip())
                {
                    DrawTooltipContents(fonts);
                    ImGui.EndTooltip();
                }
            }
        }
        
        if (missing)
        {
            ImGui.PopStyleColor();
            ParentDirectoryDrawer?.MarkNeedsRescan();
        }
        else
        {
            CompleteDraw(fonts, hovered, isSelected);
        }
    }
    
    protected abstract void DrawContextMenuContents(ImFonts fonts);
    
    protected abstract void OnDoubleClicked();
}