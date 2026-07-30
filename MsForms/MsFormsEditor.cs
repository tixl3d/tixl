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
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
            }
            else
            {
                // Clipboard requires STA; crash reporting can call this from background threads.
                // An unhandled exception on a raw thread kills the process, so catch inside the thread.
                var staThread = new Thread(() =>
                                           {
                                               try
                                               {
                                                   Clipboard.SetText(text, TextDataFormat.UnicodeText);
                                               }
                                               catch
                                               {
                                                   // Losing the clipboard copy is acceptable
                                               }
                                           });
                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();
                staThread.Join();
            }
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