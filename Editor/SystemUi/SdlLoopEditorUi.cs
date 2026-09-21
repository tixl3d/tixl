using T3.Editor.App;
using T3.MsForms;
using T3.SystemUi;

namespace T3.Editor.SystemUi;

/// <summary>
/// The WinForms services, except for exiting: the editor's loop is SDL's now, and WinForms' <c>Application.Exit</c>
/// would not end it.
/// </summary>
internal sealed class SdlLoopEditorUi : MsFormsEditor, ICoreSystemUiService
{
    void ICoreSystemUiService.ExitApplication()
    {
        if (AppWindow.IsLoopRunning)
        {
            // Ends the loop after this frame, so shutdown still saves and releases everything.
            AppWindow.RequestExit();
            return;
        }

        Environment.Exit(1);
    }
}
