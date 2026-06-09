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
/// Recording sessions share a single incrementing index across audio and data files; the
/// prefix distinguishes the kind so a clip's name reads as its type at a glance:
/// </para>
/// <list type="bullet">
/// <item><c>AudioRec-007-mic1.wav</c> — single audio source, session 7</item>
/// <item><c>DataRec-007.data</c>      — IO data capture for the same session</item>
/// </list>
/// <para>
/// <see cref="NextSessionIndex"/> scans for both prefixes (and the legacy <c>rec-</c> name,
/// so indices don't collide with recordings made before the rename) so an audio-only
/// session still bumps the counter that a later data-only session sees.
/// </para>
/// <para>
/// Recorders write to <see cref="TempRecordingsDirectory"/> during capture; on stop the finalised file is
/// imported into the project's <c>Assets/</c> (the canonical location the clip references) and the staging
/// copy is removed.
/// </para>
/// </remarks>
public static class RecordingPaths
{
    /// <summary>
    /// Temp staging directory for in-progress recordings: <c>%APPDATA%\TiXL&lt;version&gt;\Tmp\Recordings\</c>.
    /// Transient — the finalised file is imported into the project's <c>Assets/</c> on stop and the staging
    /// copy deleted, so nothing here is meant to persist.
    /// </summary>
    public static string TempRecordingsDirectory => Path.Combine(FileLocations.TempFolder, "Recordings");

    /// <summary>Filename prefix for IO-data recordings (<c>DataRec-NNN.data</c>).</summary>
    public const string DataRecordingPrefix = "DataRec";

    /// <summary>Filename prefix for audio recordings (<c>AudioRec-NNN.wav</c>).</summary>
    public const string AudioRecordingPrefix = "AudioRec";

    /// <summary>
    /// Returns <c>N+1</c> where <c>N</c> is the highest session index found across any
    /// combination of the supplied directories, recognising both current prefixes and the
    /// legacy <c>rec-</c> name. Returns 1 if nothing matches. Non-existent directories are
    /// skipped silently.
    /// </summary>
    public static int NextSessionIndex(params string[] directories)
    {
        var highest = 0;

        foreach (var directory in directories)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory))
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
    /// Builds a recording filename of the form <c>{prefix}-NNN[-suffix].extension</c>.
    /// Pads the index to three digits; the suffix is omitted when null or empty.
    /// </summary>
    /// <param name="prefix">Recording-kind prefix — <see cref="DataRecordingPrefix"/> or <see cref="AudioRecordingPrefix"/>.</param>
    /// <param name="sessionIndex">Session index from <see cref="NextSessionIndex"/>.</param>
    /// <param name="extension">File extension including the leading dot (e.g. <c>.wav</c>).</param>
    /// <param name="suffix">Optional source identifier (e.g. <c>mic1</c>, <c>loopback</c>).</param>
    public static string BuildFileName(string prefix, int sessionIndex, string extension, string? suffix = null)
    {
        var sanitisedSuffix = SanitiseSuffix(suffix);
        var indexPart = sessionIndex.ToString("D3");

        return string.IsNullOrEmpty(sanitisedSuffix)
                   ? $"{prefix}-{indexPart}{extension}"
                   : $"{prefix}-{indexPart}-{sanitisedSuffix}{extension}";
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

    // Matches the index in current (DataRec-/AudioRec-) and legacy (rec-) recording names,
    // so a session counter computed after the rename still clears pre-rename files.
    private static readonly Regex _sessionIndexRegex = new(@"^(?:DataRec|AudioRec|rec)-(\d{3,})(?:-|\.)", RegexOptions.Compiled);
}
