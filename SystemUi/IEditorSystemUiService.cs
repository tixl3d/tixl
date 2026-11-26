using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace T3.SystemUi;

public interface IEditorSystemUiService : ICoreSystemUiService
{
    public void EnableDpiAwareScaling();
    public void SetClipboardText(string text);
    public string GetClipboardText();

    /// <summary>
    /// The image on the system clipboard as encoded file bytes (e.g. PNG or BMP), for pasting screenshots.
    /// </summary>
    public bool TryGetClipboardImage([NotNullWhen(true)] out byte[]? data, [NotNullWhen(true)] out string? mimeType);
    public IFilePicker CreateFilePicker();
    public IReadOnlyList<IScreen> AllScreens { get; }
}
    
// mimics System.Windows.Forms.Screen
public interface IScreen 
{
    public int BitsPerPixel { get; }
    public Rectangle Bounds { get; }
    public Rectangle WorkingArea { get; }
    public string DeviceName { get; }
    public bool Primary { get; }
}

public interface IFilePicker : IDisposable
{
    // implements an interface for OpenFileDialog
    public string FileName { get; set; }
    public string Filter { get; set; }
    public string InitialDirectory { get; set; }
    public bool Multiselect { get; set; }
    public bool RestoreDirectory { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowReadOnly { get; set; }
    public string Title { get; set; }
    public bool ValidateNames { get; set; }
        
    /// <summary>
    /// Returns true if the user confirmed a choice
    /// </summary>
    /// <returns></returns>
    public bool ChooseFile();
    public bool CheckFileExists { get; set; }
    public bool CheckPathExists { get; set; }
    public int FilterIndex { get; set; }
}