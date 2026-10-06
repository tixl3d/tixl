namespace T3.Core.Logging;

/// <summary>
/// Write Debug-Log messages to log files
/// </summary>
public sealed class FileWriter : ILogWriter
{
    public ILogEntry.EntryLevel Filter { get; set; }

    private FileWriter(string directory, string filename)
    {
        LogDirectory = Path.Combine(directory, LogSubDirectory);
        _logPath = Path.Combine(LogDirectory, filename);

        Directory.CreateDirectory(LogDirectory);
        PruneOldLogs(LogDirectory);

        try
        {
            _streamWriter = new StreamWriter(_logPath);
        }
        catch (Exception e)
        {
            Log.Error("Failed to create log file: " + e.Message);
            return;
        }
        //#if DEBUG
        _streamWriter.AutoFlush = true;
        //#endif
    }

    public void Dispose()
    {
        if (_streamWriter == null)
            return;
        
        _streamWriter.Flush();
        _streamWriter.Close();
        _streamWriter.Dispose();
    }

    public static void Flush()
    {
        if (Instance?._streamWriter == null)
            return;

        lock (Instance._streamWriter)
        {
            Instance._streamWriter.Flush();
        }
    }

    public void ProcessEntry(ILogEntry entry)
    {
        if (_streamWriter == null)
            return;
        
        lock (_streamWriter)
        {
            try
            {
                _streamWriter.Write("{0:HH:mm:ss.fff} ({1}): {2}", entry.TimeStamp, entry.Level.ToString(), entry.Message + "\n");
            }
            catch (Exception)
            {
                // skip encoder exception
            }
        }
    }

    /// <summary>
    /// Keeps the most recent log files and deletes the rest.
    /// </summary>
    /// <remarks>
    /// One file is written per launch and nothing used to remove them, so the folder grew without limit -
    /// which matters because the crash reports live in it too, and they are what someone is looking for.
    /// Only this writer's own "*.log" files are considered; crash reports and anything else stay.
    /// </remarks>
    private static void PruneOldLogs(string directory)
    {
        try
        {
            var logFiles = new DirectoryInfo(directory).GetFiles("*.log");
            if (logFiles.Length <= MaxKeptLogFiles)
                return;

            Array.Sort(logFiles, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            for (var index = MaxKeptLogFiles; index < logFiles.Length; index++)
            {
                logFiles[index].Delete();
            }
        }
        catch (Exception)
        {
            // A log folder we cannot tidy is not a reason to start without logging.
        }
    }

    public static ILogWriter CreateDefault(string settingsFolder, out string path)
    {
        if (Instance != null)
        {   
            path = Instance._logPath;
            return Instance;
        }

        var fileName = $"{DateTime.Now:yyyy_MM_dd_HH_mm_ss_fff}.log";
        Instance = new FileWriter(settingsFolder, fileName)
                       {
                           Filter = ILogEntry.EntryLevel.All
                       };

        path = Instance._logPath;
        return Instance;
    }

    private readonly StreamWriter? _streamWriter;
    private readonly string _logPath;
    public readonly string LogDirectory;
    public static FileWriter? Instance { get; private set; }
    private const string LogSubDirectory = "Log";

    /** Enough to cover a week of ordinary use, without burying the crash reports in the same folder. */
    private const int MaxKeptLogFiles = 20;
}