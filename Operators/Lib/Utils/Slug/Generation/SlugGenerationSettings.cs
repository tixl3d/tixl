#nullable enable
using SlugSharp.Core;

namespace Lib.Utils.Slug;

/// <summary>
/// High-level generation settings used by <see cref="SlugFontManager"/>.
/// </summary>
public sealed class SlugGenerationSettings
{
    /// <summary>
    /// Controls band partitioning granularity. Must be >= 1.
    /// </summary>
    public int BandCount { get; init; } = 16;

    /// <summary>
    /// When true, emits all glyphs from the font charmap and ignores whitelist-based filtering.
    /// </summary>
    public bool FullRange { get; init; }

    /// <summary>
    /// Optional explicit code points to include when <see cref="FullRange"/> is false.
    /// </summary>
    public HashSet<int>? Whitelist { get; init; }

    /// <summary>
    /// Optional path to a charset file parsed via <see cref="CharsetFileParser"/>.
    /// Merged with <see cref="Whitelist"/> when both are provided.
    /// </summary>
    public string? CharsetFilePath { get; init; }

    /// <summary>
    /// Runs strict charset validation before parsing the charset file.
    /// </summary>
    public bool ValidateCharsetFile { get; init; }

    /// <summary>
    /// Runs SluggishParser structural validation after serialization.
    /// </summary>
    public bool ValidateOutput { get; init; } = true;

    /// <summary>
    /// Optional progress callback from SlugSharp.Core.
    /// </summary>
    public Action<SlugProgressUpdate>? ProgressCallback { get; init; }

    /// <summary>
    /// Progress event interval used by SlugSharp.Core.
    /// </summary>
    public int ProgressInterval { get; init; } = 100;
}
