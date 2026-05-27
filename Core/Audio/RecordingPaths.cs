using System;
using System.IO;
using System.Text.RegularExpressions;
using T3.Core.Settings;

#nullable enable

namespace T3.Core.Audio;

/// <summary>
/// Resolves file-system paths and session indices for live-session recordings.
/// </summary>
/// <remarks>
/// <para>
/// Recording sessions share a single incrementing index across audio and data files:
/// </para>
/// <list type="bullet">
/// <item><c>rec-007-mic1.wav</c> — single audio source, session 7</item>
/// <item><c>rec-007.data</c>     — IO data capture (Phase 3) for the same session</item>
/// </list>
/// <para>
/// <see cref="NextSessionIndex"/> scans both audio and data directories so an audio-only
/// session still bumps the counter that a later data-only session sees. Phase 1 only
/// writes audio, so the data side of the scan is harmlessly empty until Phase 3 lands.
/// </para>
/// <para>
/// The dev-only target directory for Phase 1 is <see cref="DevRecordingsDirectory"/> under
/// the editor's settings folder — same parent as the log directory, easy for testers to
/// locate. Phase 4 will switch to the active project's <c>Assets/audio/</c>.
/// </para>
/// </remarks>
public static class RecordingPaths
{
    /// <summary>
    /// Recordings directory: <c>%APPDATA%\TiXL&lt;version&gt;\Recordings\</c>. Used when
    /// no project-specific destination is configured. A future per-project
    /// <c>Assets/audio/</c> destination is on the roadmap but doesn't change this fallback.
    /// </summary>
    public static string DevRecordingsDirectory => Path.Combine(FileLocations.SettingsDirectory, "Recordings");

    /// <summary>
    /// Returns <c>N+1</c> where <c>N</c> is the highest <c>rec-NNN</c> index found
    /// across any combination of the supplied directories. Returns 1 if nothing matches.
    /// Non-existent directories are skipped silently.
    /// </summary>
    public static int NextSessionIndex(params string[] directories)
    {
        var highest = 0;

        foreach (var directory in directories)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "rec-*"))
            {
                var match = _sessionIndexRegex.Match(Path.GetFileName(path));
                if (!match.Success)
                    continue;

                if (int.TryParse(match.Groups[1].Value, out var index) && index > highest)
                    highest = index;
            }
        }

        return highest + 1;
    }

    /// <summary>
    /// Builds a recording filename of the form <c>rec-NNN[-suffix].extension</c>.
    /// Pads the index to three digits; the suffix is omitted when null or empty.
    /// </summary>
    /// <param name="sessionIndex">Session index from <see cref="NextSessionIndex"/>.</param>
    /// <param name="extension">File extension including the leading dot (e.g. <c>.wav</c>).</param>
    /// <param name="suffix">Optional source identifier (e.g. <c>mic1</c>, <c>loopback</c>).</param>
    public static string BuildFileName(int sessionIndex, string extension, string? suffix = null)
    {
        var sanitisedSuffix = SanitiseSuffix(suffix);
        var indexPart = sessionIndex.ToString("D3");

        return string.IsNullOrEmpty(sanitisedSuffix)
                   ? $"rec-{indexPart}{extension}"
                   : $"rec-{indexPart}-{sanitisedSuffix}{extension}";
    }

    private static string SanitiseSuffix(string? suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            return string.Empty;

        // Keep the suffix terse and filesystem-safe. Replace anything that isn't a letter,
        // digit, dash or underscore with a dash. Avoid LINQ to stay allocation-light here
        // (called once per recording, not hot path, but matches surrounding style).
        Span<char> buffer = stackalloc char[suffix.Length];
        var write = 0;
        var lastWasDash = false;

        for (var i = 0; i < suffix.Length; i++)
        {
            var c = suffix[i];
            var isSafe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                                                || (c >= '0' && c <= '9') || c == '_';

            if (isSafe)
            {
                buffer[write++] = c;
                lastWasDash = false;
            }
            else if (!lastWasDash && write > 0)
            {
                buffer[write++] = '-';
                lastWasDash = true;
            }
        }

        // Trim trailing dash if any.
        if (write > 0 && buffer[write - 1] == '-')
            write--;

        return new string(buffer.Slice(0, write));
    }

    private static readonly Regex _sessionIndexRegex = new(@"^rec-(\d{3,})(?:-|\.)", RegexOptions.Compiled);
}
