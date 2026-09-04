using System.Globalization;
using ImGuiNET;

namespace SilkWindows.Implementations.FileManager.ItemDrawers;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
internal class FileDrawer : FileSystemDrawer
{
    internal sealed override string DisplayName => _file.Name;
    internal sealed override string Path => _file.FullName;
    internal sealed override bool IsDirectory => false;
    internal override DirectoryDrawer RootDirectory => ParentDirectoryDrawer!.RootDirectory;
    public sealed override bool IsReadOnly => ParentDirectoryDrawer!.IsReadOnly;
    
    static FileDrawer()
    {
        const string defaultDateFormat = "dd-MM-yy";
        DateFormat = _dateFormatsByCulture.TryGetValue(CultureInfo.CurrentUICulture.ToString(), out var format) ? format : defaultDateFormat;
    }
    
    public FileDrawer(IFileManager fileManager, FileInfo file, DirectoryDrawer parent, string relativePath) : base(fileManager, parent)
    {
        _file = file;
        var extension = _file.Extension;
        if (extension.Length == file.Name.Length // e.g. .gitignore
            || extension.Length == 0) // e.g. LICENSE
        {
            _fileFormatDisplay = string.Empty;
        }
        else
        {
            _fileFormatDisplay = string.Format(FileExtensionFormat, extension);
        }
        
        if (ParentDirectoryDrawer == null)
            throw new InvalidOperationException("File drawer must have a parent directory drawer");
        
       
        _relativePathDisplay = fileManager.FormatPathForDisplay(relativePath);
        
        _lastModifiedDisplay = file.LastWriteTime.ToString(DateFormat);
    }
    
    protected override void DrawSelectable(ImFonts fonts, bool isSelected)
    {
        if (Expanded)
            DrawExpanded(isSelected, fonts);
        else
            DrawBasic(isSelected, fonts);
    }
    
    protected override void CompleteDraw(ImFonts fonts, bool hovered, bool isSelected)
    {
    }
    
    protected sealed override void OnDoubleClicked() => FileManager.DoubleClicked(this, false);
    
    /// <summary>
    /// Top item in the stack must be the selectable/hoverable item
    /// </summary>
    protected virtual void DrawBasic(bool isCurrentlySelected, ImFonts fonts)
    {
        ImGui.Selectable(_file.Name, isCurrentlySelected);
    }
    
    /// <summary>
    /// Top item in the stack must be the selectable/hoverable item
    /// </summary>
    protected virtual void DrawExpanded(bool isCurrentlySelected, ImFonts fonts)
    {
        // does nothing extra right now - can be overridden
        DrawBasic(isCurrentlySelected, fonts);
    }
    
    protected override void DrawContextMenuContents(ImFonts fonts)
    {
        if (ImGui.MenuItem("Open in System File Manager"))
        {
            FileManager.ShowInSystemFileManager(this);
        }
        
        if (ImGui.MenuItem("Open in External Editor"))
        {
            FileManager.DoubleClicked(this, true);
        }
    }
    
    protected override void DrawTooltipContents(ImFonts fonts)
    {
        ImGui.Text(_relativePathDisplay);
    }
    
    protected internal sealed override FileSystemInfo FileSystemInfo => _file;
    
    public void DrawFileFormat(ImFonts fonts)
    {
        ImGui.PushFont(fonts.Small);
        ImGui.Text(_fileFormatDisplay);
        if (ImGui.BeginItemTooltip())
        {
            ImGui.Text("File Extension: " + _fileFormatDisplay);
            ImGui.EndTooltip();
        }
        
        ImGui.PopFont();
    }
    
    public void DrawSize(ImFonts fonts)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double toKb = 1 / kb;
        const double toMb = 1 / mb;
        var sizeInBytes = _file.Length;
        
        var showInMb = sizeInBytes > mb;
        var display = showInMb ? $"{sizeInBytes * toMb: 0.0} MB" : $"{sizeInBytes * toKb: 0.0} KB";
        ImGui.PushFont(fonts.Small);
        ImGui.Text(display);
        if (ImGui.BeginItemTooltip())
        {
            ImGui.Text("Size: " + display);
            ImGui.EndTooltip();
        }
        
        ImGui.PopFont();
    }
    
    public void DrawLastModified(ImFonts fonts)
    {
        ImGui.PushFont(fonts.Small);
        ImGui.Text(_lastModifiedDisplay);
        
        if (ImGui.BeginItemTooltip())
        {
            ImGui.Text("Time last modified: " + _lastModifiedDisplay);
            ImGui.EndTooltip();
        }
        
        ImGui.PopFont();
    }
    
    private readonly FileInfo _file;
    private readonly string _relativePathDisplay;
    private readonly string _fileFormatDisplay;
    private readonly string _lastModifiedDisplay;
    
    private static readonly Dictionary<string, string> _dateFormatsByCulture = new()
                                                                                   {
                                                                                       { "en-US", "MM-dd-yy" }
                                                                                   };
    
    private static readonly string DateFormat;
    private const string FileExtensionFormat = "[{0}]";
}