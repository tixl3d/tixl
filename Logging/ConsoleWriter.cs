namespace T3.Core.Logging;

/// <summary>
/// Write log messages to system console
/// </summary>
public class ConsoleWriter : ILogWriter
{
    public ILogEntry.EntryLevel Filter { get; set; }

    public void Dispose()
    {
    }

    // Once the console is gone (a launched process whose pipe closed, a detached start), every
    // write would fail - and reporting that failure through Log again would recurse forever
    // and take the editor down. Disable this writer after the first failure instead.
    private bool _broken;

    public void ProcessEntry(ILogEntry newEntry)
    {
        if (_broken)
            return;

        try
        {
            Console.Write("{0}: {1}\n", newEntry.Level.ToString(), newEntry.Message);
        }
        catch
        {
            _broken = true;
        }
    }
}
