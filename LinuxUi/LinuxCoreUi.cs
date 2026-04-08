using System.Diagnostics;
using T3.SystemUi;

namespace T3.LinuxUi;

/// <summary>
/// Linux implementation of ICoreSystemUiService. Used by the Player on Linux.
/// </summary>
public class LinuxCoreUi : ICoreSystemUiService
{
    public void OpenWithDefaultApplication(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new Exception("Uri is empty");

        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            Arguments = uri,
            UseShellExecute = false,
        });
    }

    public void ExitApplication()
    {
        Environment.Exit(0);
    }

    public void ExitThread()
    {
        // No WinForms message loop on Linux; just exit
    }

    public ICursor Cursor => _cursor ??= new LinuxCursor();

    public void SetUnhandledExceptionMode(bool throwException)
    {
        if (throwException)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                Console.Error.WriteLine("Unhandled exception: " + args.ExceptionObject);
                Environment.Exit(1);
            };
        }
    }

    private ICursor? _cursor;
}
