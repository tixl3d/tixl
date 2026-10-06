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
    /// Sends each report as a fresh event carrying the saved report as an attachment. The original event
    /// cannot be revived - Sentry does not read its own serialised form back - so the attachment is what
    /// carries the stack, the context and the graph snapshot.
    /// </summary>
    internal static void Send(IReadOnlyList<Report> reports)
    {
        foreach (var report in reports)
        {
            try
            {
                var sentryEvent = new SentryEvent
                                      {
                                          Message = report.Title,
                                          Level = SentryLevel.Error,
                                      };

                sentryEvent.SetTag(DeferredTag, "true");
                sentryEvent.SetTag("Nickname", UserSettings.Config.UserName);
                sentryEvent.SetExtra("CrashTimeUtc", report.TimeUtc.ToString("u"));

                if (File.Exists(report.ReportPath))
                {
                    SentrySdk.CaptureEvent(sentryEvent, scope => scope.AddAttachment(report.ReportPath));
                }
                else
                {
                    SentrySdk.CaptureEvent(sentryEvent);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Could not send a crash report from an earlier session: {e.Message}");
            }

            TryDelete(report.MarkerPath);
        }

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

    private sealed record MarkerFile(DateTime TimeUtc, string ExceptionType, string Message, string ReportFileName);
}
