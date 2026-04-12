using T3.Core.Compilation;
using T3.Core.Logging;

namespace T3.Core.Model;

/// <summary>
/// Tracks the file format version for .t3 and .t3ui symbol files.
/// Written into each file so older editors can detect and warn about newer formats.
/// </summary>
public static class SymbolFormatVersion
{
    /// <summary>Current format version written by this editor.</summary>
    public const int Current = 1;

    /// <summary>The TiXL editor version string, written alongside the format version.</summary>
    public static string TixlVersion => RuntimeAssemblies.Version.ToString();

    /// <summary>
    /// Change log for this editor's own migrations (used when reading older formats).
    /// </summary>
    public static readonly FormatChange[] Changes =
    [
        new(1, "Added per-symbol ProjectSettings with PlaybackConfig; RenderExport in .t3ui Settings section"),
    ];

    public record FormatChange(int Version, string Description);

    /// <summary>
    /// Checks a file's format version and logs a warning if it was written by a newer editor.
    /// The older editor can't know what changed — it just reports the version mismatch.
    /// </summary>
    public static void WarnIfNewer(int fileVersion, string? fileTixlVersion, string symbolName)
    {
        if (fileVersion <= Current)
            return;

        var savedWith = !string.IsNullOrEmpty(fileTixlVersion)
                            ? $" (saved with TiXL {fileTixlVersion})"
                            : "";

        Log.Warning($"'{symbolName}': file format version {fileVersion}{savedWith} is newer than supported version {Current} (TiXL {TixlVersion}). Opening this file may cause data loss.");
    }
}
