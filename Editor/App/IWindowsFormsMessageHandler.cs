#if PLATFORM_WINDOWS
namespace T3.Editor.App;

public interface IWindowsFormsMessageHandler
{
    public void ProcessMessage(System.Windows.Forms.Message message);
}
#endif // PLATFORM_WINDOWS