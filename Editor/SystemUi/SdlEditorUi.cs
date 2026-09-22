using T3.Editor.App;
using T3.SdlPlatform;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.Editor.SystemUi;

/// <summary>
/// The editor's system services on SDL. The core ones are the Player's; exiting ends the editor's own loop, so
/// shutdown still saves and releases everything.
/// </summary>
internal sealed class SdlEditorUi : IEditorSystemUiService
{
    public ICursor Cursor => _core.Cursor;
    public IReadOnlyList<IScreen> AllScreens => Screens.Screens;

    /// <summary>Invalidated by the event loop when a display changes.</summary>
    internal readonly SdlScreenList Screens = new();

    public void ExitApplication()
    {
        if (AppWindow.IsLoopRunning)
        {
            AppWindow.RequestExit();
            return;
        }

        Environment.Exit(1);
    }

    public void ExitThread() => _core.ExitThread();
    public void OpenWithDefaultApplication(string uri) => _core.OpenWithDefaultApplication(uri);
    public void RevealInFileBrowser(string path) => _core.RevealInFileBrowser(path);
    public void SetUnhandledExceptionMode(bool throwException) => _core.SetUnhandledExceptionMode(throwException);

    /// <summary>Nothing to do: SDL makes the process DPI-aware itself when it initializes video.</summary>
    public void EnableDpiAwareScaling()
    {
    }

    public void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // SDL's clipboard belongs to the thread that runs its event loop; crash reporting calls from others.
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            Log.Warning("Could not copy to the clipboard from a background thread.");
            return;
        }

        if (!SDL_SetClipboardText(text))
            Log.Warning($"Could not copy to the clipboard: {SDL_GetError()}");
    }

    public string GetClipboardText() => SDL_GetClipboardText() ?? string.Empty;

    public IFilePicker CreateFilePicker() => new SdlFilePicker();

    private readonly SdlCoreUi _core = new();
    private readonly int _mainThreadId = Environment.CurrentManagedThreadId;
}
