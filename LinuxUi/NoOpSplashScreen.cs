using T3.Core.Logging;
using T3.SystemUi;

namespace T3.LinuxUi;

/// <summary>
/// No-op splash screen for Linux (WinForms splash is not available).
/// </summary>
public sealed class NoOpSplashScreen : ISplashScreen
{
    public void Show(string imagePath) { }
    public void Close() { }
    public void Dispose() { }

    public ILogEntry.EntryLevel Filter { get; set; }
    public void ProcessEntry(ILogEntry entry) { }
}
