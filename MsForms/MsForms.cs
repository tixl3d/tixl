using System.Diagnostics;
using T3.SystemUi;

// ReSharper disable ConvertToAutoPropertyWithPrivateSetter

namespace T3.MsForms;

public class MsForms : ICoreSystemUiService
{

    public void SetUnhandledExceptionMode(bool throwException)
    {
        System.Windows.Forms.Application.SetUnhandledExceptionMode(throwException
                                                                       ? UnhandledExceptionMode.ThrowException
                                                                       : UnhandledExceptionMode.CatchException);
    }

    void ICoreSystemUiService.OpenWithDefaultApplication(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new Exception("Uri is empty");
        }

        var startInfo = new ProcessStartInfo
                            {
                                FileName = "cmd",
                                // The first empty argument is the title of the console window.
                                Arguments = $"/c start \"\" \"{uri}\"",
                            };

        Process.Start(startInfo);
    }

    void ICoreSystemUiService.RevealInFileBrowser(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new Exception("Path is empty");
        }

        // Explorer rejects forward slashes, which reach us from paths assembled in msbuild properties.
        var nativePath = Path.GetFullPath(path);

        // "/select," expects a file - handing it a folder would open that folder's parent instead.
        var arguments = File.Exists(nativePath)
                            ? $"/select,\"{nativePath}\""
                            : $"\"{nativePath}\"";

        Process.Start(new ProcessStartInfo
                          {
                              FileName = "explorer",
                              Arguments = arguments,
                          });
    }

    void ICoreSystemUiService.ExitApplication()
    {
        if (System.Windows.Forms.Application.MessageLoop)
        {
            // Use this since we are a WinForms app
            System.Windows.Forms.Application.Exit();
        }
        else
        {
            // Use this since we are a console app
            System.Environment.Exit(1);
        }
    }

    void ICoreSystemUiService.ExitThread()
    {
        Application.ExitThread();
    }

    public ICursor Cursor => FirstCursor ??= new CursorWrapper();

    public static void TrackKeysOf(Form form)
    {
        form.KeyDown += HandleKeyDown;
        form.KeyUp += HandleKeyUp;
    }

    public static void TrackMouseOf(Form form)
    {
        var cursorWrapper = new CursorWrapper();
        cursorWrapper.TrackMouseOf(form);

        if (TrackedCursors.Count == 0)
            FirstCursor = cursorWrapper;
        
        TrackedCursors.Add(form, cursorWrapper);
    }

    private static void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        var keyIndex = (int)e.KeyCode;
        KeyHandler.SetKeyDown(keyIndex);
    }

    private static void HandleKeyUp(object? sender, KeyEventArgs e)
    {
        var keyIndex = (int)e.KeyCode;
        KeyHandler.SetKeyUp(keyIndex);
    }

    internal static ICursor? FirstCursor;
    private static readonly Dictionary<Form, CursorWrapper> TrackedCursors = new();
}