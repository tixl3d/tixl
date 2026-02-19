namespace T3.Core.Logging;

/// <summary>
/// A singleton that allows to log messages that are forwarded to <see cref="ILogWriter"/>s.
/// </summary>
public static class Log
{
    public static void AddWriter(ILogWriter writer)
    {
        _logWriters.Add(writer);
    }
        
    public static void RemoveWriter(ILogWriter writer)
    {
        _logWriters.Remove(writer);
    }

    #region Gated Debug Logging
    
    /// <summary>
    /// Gated debug logging - only logs when the category is enabled.
    /// Usage: Log.Gated.Audio("message"), Log.Gated.VideoRender("message")
    /// </summary>
    public static class Gated
    {
        public static bool AudioEnabled { get; set; }
        public static bool AudioRenderEnabled { get; set; }
        public static bool VideoRenderEnabled { get; set; }
        
        public static void Audio(string message)
        {
            if (AudioEnabled) Log.Debug(message);
        }
        
        public static void AudioRender(string message)
        {
            if (AudioRenderEnabled) Log.Debug(message);
        }
        
        public static void VideoRender(string message)
        {
            if (VideoRenderEnabled) Log.Debug(message);
        }
        
        /// <summary>
        /// Initializes the gated debug logging configuration by enabling or disabling specific logging categories.
        /// </summary>
        /// <param name="audio">Enable logging of audio-related debug messages.</param>
        /// <param name="audioRender">Enable logging of audio rendering debug messages.</param>
        /// <param name="videoRender">Enable logging of video rendering debug messages.</param>
        public static void Initialize(bool audio, bool audioRender, bool videoRender)
        {
            AudioEnabled = audio;
            AudioRenderEnabled = audioRender;
            VideoRenderEnabled = videoRender;
        }
    }
    
    #endregion

    #region API for logging

    public static void Debug(string message, params object[] args)
    {
        ProcessAndLog(ILogEntry.EntryLevel.Debug, message, args);
    }        
        
    public static void Info(string message, params object[] args)
    {
        ProcessAndLog(ILogEntry.EntryLevel.Info, message, args);
    }  
        
    public static void Warning(string message, params object[] args)
    {
        ProcessAndLog(ILogEntry.EntryLevel.Warning, message, args);
    }  
        
    public static void Error(string message, params object?[] args)
    {
        ProcessAndLog(ILogEntry.EntryLevel.Error, message, args);
    }
        
    public static void Assert(string message)
    {
        DoLog(new LogEntry(ILogEntry.EntryLevel.Warning, message));
    }
        
    public static void Assert(string message, Guid sourceId)
    {
        DoLog(new LogEntry(ILogEntry.EntryLevel.Warning, message, sourceId));
    }


    
    /// <summary>
    /// A helper function to unite different method API 
    /// </summary>
    private static void ProcessAndLog(ILogEntry.EntryLevel level, string message, object?[] args)
    {
        switch (args)
        {
            case [IGuidPathContainer instance]:
                DoLog(new LogEntry(level, message, instance.InstancePath));
                break;
                
            case [List<Guid> idPath]:
                DoLog(new LogEntry(level, message, idPath.ToArray()));
                break;
            
            case [Guid[] idPathArray]:
                DoLog(new LogEntry(level, message, idPathArray));
                break;
            
            default:
                var messageString = FormatMessageWithArguments(message, args);
                DoLog(new LogEntry(level, messageString));
                break;
        }
    } 
        

    #endregion

    private static string FormatMessageWithArguments(string messageString, object?[] args)
    {
        try
        {
            messageString = args.Length == 0 ? messageString : string.Format(messageString, args);
        }
        catch (FormatException)
        {
            DoLog(new LogEntry(ILogEntry.EntryLevel.Info, "Ignoring arguments mal-formatted debug message. Did you mess with curly braces?"));
        }
        return messageString;
    }
        
    private static void DoLog(ILogEntry entry)
    {
        _logWriters.ForEach(writer => writer.ProcessEntry(entry));
    }

    private static readonly List<ILogWriter> _logWriters = [];
}