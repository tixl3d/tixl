namespace T3.Editor.App;

/// <summary>A device that reads the raw Win32 messages sent to the editor's windows. Windows only.</summary>
public interface IWin32MessageHandler
{
    public void ProcessMessage(int message, IntPtr wParam, IntPtr lParam);
}
