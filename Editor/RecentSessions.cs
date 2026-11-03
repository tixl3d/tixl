#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using T3.Core.Utils;

namespace T3.Editor;

/// <summary>
/// The editor sessions of the last days as far as the log folder remembers them, and whether each one ended well.
/// </summary>
/// <remarks>
/// A clean exit ends its log with <see cref="Program.ShutdownCompleteMessage"/>. A log without it belongs to a
/// session that died - often a native crash that never reached the crash handler and so left no report.
/// Scanned once, on first use: whatever ends the running session ends this process too, so nothing new can
/// appear while it runs.
/// </remarks>
internal static class RecentSessions
{
    internal sealed class Session(DateTime startTime, DateTime endTime, string? logPath)
    {
        internal readonly DateTime StartTime = startTime;
        internal readonly DateTime EndTime = endTime;
        internal readonly string? LogPath = logPath;

        internal string? CrashReportPath { get; set; }
        internal string? CrashTitle { get; set; }
        internal bool EndedUnexpectedly { get; set; }
        internal bool IsShared { get; set; }

        /** Relative end time plus what went wrong; rebuilt each time the list opens. */
        internal string Label { get; set; } = string.Empty;

        internal string Tooltip { get; set; } = string.Empty;

        internal bool HasProblem => CrashReportPath != null || EndedUnexpectedly;
        internal string ProblemTitle => CrashTitle ?? "Ended Unexpectedly";

        /** The file next to which sharing leaves its marker, so a session is offered for sharing only once. */
        internal string MainPath => CrashReportPath ?? LogPath ?? string.Empty;
    }

    /// <param name="refreshLabels">
    /// Pass true only when the list is about to be shown: the labels are part of the menu ids, so changing them
    /// while a menu is open closes it.
    /// </param>
    internal static IReadOnlyList<Session> GetSessions(bool refreshLabels)
    {
        if (!_isScanned)
        {
            _isScanned = true;
            Scan();
            refreshLabels = true;
        }

        if (refreshLabels)
        {
            UpdateLabels();
        }

        return _sessions;
    }

    /// <summary>Sends the session's crash report and log to Sentry and remembers that it was shared.</summary>
    internal static void Share(Session session, string userComment)
    {
        var attachments = new List<string>(2);
        if (session.CrashReportPath != null)
            attachments.Add(session.CrashReportPath);

        if (session.LogPath != null)
            attachments.Add(session.LogPath);

        var title = session.CrashTitle ?? "Session ended unexpectedly";
        PendingCrashReports.Share(title, session.EndTime.ToUniversalTime(), attachments, userComment);

        // Sharing answers the startup question about this crash as well.
        if (session.CrashReportPath != null)
            PendingCrashReports.Forget(session.CrashReportPath);

        try
        {
            File.WriteAllText(session.MainPath + SharedMarkerExtension, DateTime.UtcNow.ToString("u"));
        }
        catch (Exception e)
        {
            Log.Debug($"Could not mark the session as shared: {e.Message}");
        }

        session.IsShared = true;
    }

    private static void Scan()
    {
        _sessions.Clear();

        var directory = FileWriter.Instance?.LogDirectory;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        var oldestEndTime = DateTime.Now - _maxSessionAge;
        var currentLogPath = CrashReporting.LogPath;
        var currentSessionStart = TryParseLogStartTime(Path.GetFileName(currentLogPath), out var start) ? start : DateTime.MaxValue;

        try
        {
            var directoryInfo = new DirectoryInfo(directory);

            foreach (var logFile in directoryInfo.GetFiles("*" + LogExtension))
            {
                if (logFile.LastWriteTime < oldestEndTime
                    || string.Equals(logFile.FullName, currentLogPath, StringComparison.Ordinal)
                    || !TryParseLogStartTime(logFile.Name, out var startTime))
                {
                    continue;
                }

                var endedCleanly = EndsWithCleanShutdown(logFile.FullName);

                // Another editor that is still running looks exactly like one that died.
                if (!endedCleanly && IsHeldByRunningEditor(logFile.FullName))
                    continue;

                _sessions.Add(new Session(startTime, logFile.LastWriteTime, logFile.FullName)
                                  {
                                      EndedUnexpectedly = !endedCleanly,
                                  });
            }

            foreach (var reportFile in directoryInfo.GetFiles(CrashReportPattern))
            {
                var crashTime = reportFile.LastWriteTime;
                if (crashTime < oldestEndTime || crashTime >= currentSessionStart)
                    continue;

                var session = FindSessionRunningAt(crashTime);
                if (session == null)
                {
                    // Its log was pruned or never written; the report alone still describes the session.
                    session = new Session(crashTime, crashTime, null);
                    _sessions.Add(session);
                }
                else if (session.CrashReportPath != null)
                {
                    // Several threads can fail in the same crash; the first report is the one to show.
                    continue;
                }

                session.CrashReportPath = reportFile.FullName;
                session.CrashTitle = ReadCrashTitle(reportFile.Name);
            }

            foreach (var session in _sessions)
            {
                session.IsShared = File.Exists(session.MainPath + SharedMarkerExtension);
                session.Tooltip = BuildTooltip(session);
            }

            _sessions.Sort((a, b) => b.EndTime.CompareTo(a.EndTime));
            DeleteOrphanedSharedMarkers(directoryInfo);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not read the recent sessions from the log folder: {e.Message}");
        }
    }

    private static void UpdateLabels()
    {
        foreach (var session in _sessions)
        {
            var relativeTime = ((DateTime?)session.EndTime).GetReadableRelativeTime();
            session.Label = session.HasProblem
                                ? $"{relativeTime}   {session.ProblemTitle}"
                                : relativeTime;
        }
    }

    private static string BuildTooltip(Session session)
    {
        var duration = session.EndTime - session.StartTime;
        var durationText = duration.TotalMinutes < 1
                               ? "less than a minute"
                               : duration.TotalHours < 2
                                   ? $"{duration.TotalMinutes:0} min"
                                   : $"{duration.TotalHours:0.0} h";

        var file = Path.GetFileName(session.CrashReportPath ?? session.LogPath);
        var description = session.CrashReportPath != null
                              ? "Crashed and wrote a report."
                              : session.EndedUnexpectedly
                                  ? "The log stops without a clean shutdown - TiXL was killed or crashed natively."
                                  : "Closed normally.";

        return $"""
                Started {session.StartTime:yyyy-MM-dd HH:mm}, ran {durationText}.
                {description}
                {file}
                """;
    }

    /** Picks the latest session started before the crash, if the crash falls inside it. */
    private static Session? FindSessionRunningAt(DateTime time)
    {
        Session? best = null;
        foreach (var session in _sessions)
        {
            if (session.LogPath == null || session.StartTime > time)
                continue;

            if (best == null || session.StartTime > best.StartTime)
                best = session;
        }

        // The log keeps being written after the report, but not by much: a few seconds of shutdown at most.
        if (best != null && time > best.EndTime + _crashAfterLastLogTolerance)
            return null;

        return best;
    }

    private static bool EndsWithCleanShutdown(string logPath)
    {
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(stream.Length, LogTailBytes);
            stream.Seek(-length, SeekOrigin.End);

            var buffer = new byte[length];
            stream.ReadExactly(buffer);
            return Encoding.UTF8.GetString(buffer).Contains(Program.ShutdownCompleteMessage, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // An unreadable log tells us nothing either way; don't raise an alarm over it.
            return true;
        }
    }

    /// <summary>
    /// A running editor keeps its log open for writing, which denies an exclusive open - on Windows through
    /// the share mode, on Linux and macOS through the advisory lock .NET takes on every file it opens.
    /// </summary>
    private static bool IsHeldByRunningEditor(string logPath)
    {
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /** Log files are named after their start time: 2026_10_06_19_49_00_484.log */
    private static bool TryParseLogStartTime(string? fileName, out DateTime startTime)
    {
        startTime = default;
        if (fileName == null || !fileName.EndsWith(LogExtension, StringComparison.Ordinal))
            return false;

        var stem = fileName[..^LogExtension.Length];
        return DateTime.TryParseExact(stem, "yyyy_MM_dd_HH_mm_ss_fff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out startTime);
    }

    /** Crash reports are named "crash {time} - {exception type} - {event id}.txt". */
    private static string ReadCrashTitle(string fileName)
    {
        var parts = fileName.Split(" - ");
        return parts.Length >= 3 ? parts[1] : "Crash";
    }

    private static void DeleteOrphanedSharedMarkers(DirectoryInfo directoryInfo)
    {
        foreach (var marker in directoryInfo.GetFiles("*" + SharedMarkerExtension))
        {
            var sharedFile = marker.FullName[..^SharedMarkerExtension.Length];
            if (File.Exists(sharedFile))
                continue;

            try
            {
                marker.Delete();
            }
            catch (Exception)
            {
                // A leftover marker is a few bytes; not worth a message.
            }
        }
    }

    private const string LogExtension = ".log";
    private const string CrashReportPattern = "crash *.txt";
    private const string SharedMarkerExtension = ".shared";

    /** Enough to cover the shutdown message plus whatever is logged while the last resources are released. */
    private const int LogTailBytes = 2048;

    private static readonly TimeSpan _maxSessionAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan _crashAfterLastLogTolerance = TimeSpan.FromMinutes(1);

    private static readonly List<Session> _sessions = [];
    private static bool _isScanned;
}
