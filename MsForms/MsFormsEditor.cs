using T3.SystemUi;

namespace T3.MsForms;

public class MsFormsEditor : MsForms, IEditorSystemUiService
{
    void IEditorSystemUiService.EnableDpiAwareScaling()
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitor);
        Application.SetCompatibleTextRenderingDefault(false);
    }

    void IEditorSystemUiService.SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        
        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // TODO: should log this 
        }
    }

    string IEditorSystemUiService.GetClipboardText()
    {
        return Clipboard.GetText();
    }

    IFilePicker IEditorSystemUiService.CreateFilePicker()
    {
        return new OpenFileDialogWrapper();
    }

    public IReadOnlyList<IScreen> AllScreens => Screen.AllScreens
                                                      .Select(x => new ScreenWrapper(x))
                                                      .ToArray();

    class ScreenWrapper : IScreen
    {
        Screen _screen;

        public ScreenWrapper(Screen screen)
        {
            _screen = screen;
        }

        public int BitsPerPixel => _screen.BitsPerPixel;
        public Rectangle Bounds => _screen.Bounds;
        public Rectangle WorkingArea => _screen.WorkingArea;
        public string DeviceName => _screen.DeviceName;
        public bool Primary => _screen.Primary;
    }
}