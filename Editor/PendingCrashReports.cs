#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sentry;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor;

/// <summary>
/// Crashes that were never offered to the user, kept so the next launch can offer them.
/// </summary>
/// <remarks>
/// <see cref="CrashReporting"/> asks about a crash from inside the crash itself, which only works while the
/// process survives long enough to draw a dialog. A hard crash, or one before the window exists, takes that
/// chance away - and those are the ones worth having. So every crash leaves a marker beside its report, and
/// whoever sends or declines it removes the marker again.
/// </remarks>
internal static class PendingCrashReports
{
    internal sealed record Report(string MarkerPath, string ReportPath, string Title, DateTime TimeUtc);

    /// <summary>Tags the deferred event so the crash handler lets it past instead of treating it as a new crash.</summary>
    internal const string DeferredTag = "DeferredReport";

    internal static void Remember(SentryEvent sentryEvent, string reportPath)
    {
        try
        {
            var marker = new MarkerFile(DateTime.UtcNow,
                                        sentryEvent.Exception?.GetType().Name ?? "Crash",
                                        sentryEvent.Exception?.Message ?? string.Empty,
                                        Path.GetFileName(reportPath));

            File.WriteAllText(MarkerPathFor(reportPath), JsonSerializer.Serialize(marker));
        }
        catch (Exception e)
        {
            // Never let bookkeeping mask the crash being reported.
            Log.Debug($"Could not remember the crash report: {e.Message}");
        }
    }

    /// <summary>Drops the marker once the user has been asked, whatever they answered.</summary>
    internal static void Forget(string reportPath)
    {
        try
        {
            var marker = MarkerPathFor(reportPath);
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Could not clear the crash marker: {e.Message}");
        }
    }

    /// <summary>
    /// Notes any unreported crash in the log. The dialog only appears once the startup windows are out of
    /// the way, while a pasted log is often all we get - so the log says it regardless.
    /// </summary>
    internal static void LogPending()
    {
        var reports = FindUnsent();
        if (reports.Count == 0)
        {
            return;
        }

        Log.Debug($"{reports.Count} crash report(s) from earlier sessions have not been sent:");
        foreach (var report in reports)
        {
            Log.Debug($"  {report.TimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {report.Title}");
        }
    }

    internal static IReadOnlyList<Report> FindUnsent()
    {
        var reports = new List<Report>();
        var directory = FileWriter.Instance?.LogDirectory;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return reports;
        }

        foreach (var markerPath in Directory.EnumerateFiles(directory, "*" + MarkerExtension))
        {
            try
            {
                var marker = JsonSerializer.Deserialize<MarkerFile>(File.ReadAllText(markerPath));
                if (marker == null)
                {
                    continue;
                }

                var reportPath = Path.Combine(directory, marker.ReportFileName);
                var title = string.IsNullOrEmpty(marker.Message) ? marker.ExceptionType : $"{marker.ExceptionType}: {marker.Message}";
                reports.Add(new Report(markerPath, reportPath, title, marker.TimeUtc));
            }
            catch (Exception e)
            {
                // A marker we cannot read is of no use to anyone; drop it rather than ask forever.
                Log.Debug($"Discarding an unreadable crash marker: {e.Message}");
                TryDelete(markerPath);
            }
        }

        reports.Sort((a, b) => b.TimeUtc.CompareTo(a.TimeUtc));
        return reports;
    }

    /// <summary>
    /// Sends each report as a fresh event rebuilt from the one saved in the report, so it arrives with the
    /// crash's own stack and context. The report itself is attached as well.
    /// </summary>
    internal static void Send(IReadOnlyList<Report> reports)
    {
        foreach (var report in reports)
        {
            Capture(report.Title, report.TimeUtc, report.ReportPath, [report.ReportPath], userComment: null);
            TryDelete(report.MarkerPath);
        }

        SentrySdk.Flush(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Sends a session the user chose to share later, with whatever files describe it - its crash report, its
    /// log, or both - and an optional note on what they were doing.
    /// </summary>
    internal static void Share(string title, DateTime timeUtc, string? crashReportPath, IReadOnlyList<string> attachmentPaths,
                               string userComment)
    {
        Capture(title, timeUtc, crashReportPath, attachmentPaths, userComment);
        SentrySdk.Flush(TimeSpan.FromSeconds(5));
    }

    /// <summary>Leaves the reports on disk but stops asking about them.</summary>
    internal static void Discard(IReadOnlyList<Report> reports)
    {
        foreach (var report in reports)
        {
            TryDelete(report.MarkerPath);
        }
    }

    private static void Capture(string title, DateTime timeUtc, string? crashReportPath, IReadOnlyList<string> attachmentPaths,
                                string? userComment)
    {
        try
        {
            var sentryEvent = CreateEvent(title, crashReportPath);

            sentryEvent.SetTag(DeferredTag, "true");
            sentryEvent.SetTag("Nickname", UserSettings.Config.UserName);
            sentryEvent.SetExtra("CrashTimeUtc", timeUtc.ToString("u"));
            if (!string.IsNullOrWhiteSpace(userComment))
            {
                sentryEvent.SetExtra("UserComment", userComment);
            }

            SentrySdk.CaptureEvent(sentryEvent, scope =>
                                                {
                                                    foreach (var path in attachmentPaths)
                                                    {
                                                        if (TryReadForAttachment(path, out var data))
                                                            scope.AddAttachment(data, Path.GetFileName(path));
                                                    }
                                                });
        }
        catch (Exception e)
        {
            Log.Warning($"Could not send a crash report from an earlier session: {e.Message}");
        }
    }

    /// <summary>
    /// A new event carrying the exception, contexts and tags of the crash saved in the report. It gets a new id
    /// because Sentry drops an event whose id it has already received, and the crash may have been sent before.
    /// Without a readable report, the title is all there is.
    /// </summary>
    private static SentryEvent CreateEvent(string title, string? crashReportPath)
    {
        var sentryEvent = new SentryEvent { Level = SentryLevel.Error };

        var original = crashReportPath != null ? TryReadSavedEvent(crashReportPath) : null;
        if (original?.SentryExceptions == null)
        {
            sentryEvent.Message = title;
            return sentryEvent;
        }

        sentryEvent.SentryExceptions = original.SentryExceptions;
        sentryEvent.Release = original.Release;
        foreach (var (key, value) in original.Contexts)
        {
            sentryEvent.Contexts[key] = value;
        }

        foreach (var (key, value) in original.Tags)
        {
            sentryEvent.SetTag(key, value);
        }

        return sentryEvent;
    }

    /** The report holds the exception text followed by the event as written by <see cref="CrashReporting"/>. */
    private static SentryEvent? TryReadSavedEvent(string crashReportPath)
    {
        try
        {
            var text = File.ReadAllText(crashReportPath);
            var jsonStart = text.IndexOf("\n{", StringComparison.Ordinal);
            if (jsonStart < 0)
                return null;

            using var document = JsonDocument.Parse(text.AsMemory(jsonStart + 1));
            return SentryEvent.FromJson(document.RootElement);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read the crash from '{Path.GetFileName(crashReportPath)}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads a file for attaching, cutting out its middle when it is too large: Sentry drops oversized attachments
    /// entirely, while the start of a log tells how the session was set up and its end how it died.
    /// </summary>
    private static bool TryReadForAttachment(string path, out byte[] data)
    {
        data = [];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= MaxAttachmentBytes)
            {
                data = new byte[stream.Length];
                stream.ReadExactly(data);
                return true;
            }

            var head = new byte[AttachmentHeadBytes];
            stream.ReadExactly(head);

            var tail = new byte[MaxAttachmentBytes - AttachmentHeadBytes];
            stream.Seek(-tail.Length, SeekOrigin.End);
            stream.ReadExactly(tail);

            // Cut at line breaks so neither half starts or ends with a broken line.
            var headLength = Array.LastIndexOf(head, (byte)'\n') + 1;
            var tailStart = Array.IndexOf(tail, (byte)'\n') + 1;
            var skippedBytes = stream.Length - headLength - (tail.Length - tailStart);
            var marker = System.Text.Encoding.UTF8.GetBytes($"\n[... {skippedBytes / 1024} KB cut from the middle to fit the size limit ...]\n\n");

            data = new byte[headLength + marker.Length + tail.Length - tailStart];
            head.AsSpan(0, headLength).CopyTo(data);
            marker.CopyTo(data, headLength);
            tail.AsSpan(tailStart).CopyTo(data.AsSpan(headLength + marker.Length));
            return true;
        }
        catch (Exception e)
        {
            Log.Debug($"Could not attach '{Path.GetFileName(path)}': {e.Message}");
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not remove '{Path.GetFileName(path)}': {e.Message}");
        }
    }

    private static string MarkerPathFor(string reportPath)
    {
        return reportPath + MarkerExtension;
    }

    /** Sits beside the human-readable crash report and marks it as never offered to the user. */
    private const string MarkerExtension = ".unsent.json";

    /** Well below Sentry's 20 MB attachment limit, so a report with both a crash file and a log still fits. */
    private const int MaxAttachmentBytes = 4 * 1024 * 1024;

    private const int AttachmentHeadBytes = 1024 * 1024;

    private sealed record MarkerFile(DateTime TimeUtc, string ExceptionType, string Message, string ReportFileName);
}
