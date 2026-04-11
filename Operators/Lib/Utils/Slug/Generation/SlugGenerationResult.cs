#nullable enable
using SlugSharp.Core;

namespace Lib.Utils.Slug;

/// <summary>
/// Captures one successful .slug generation run.
/// </summary>
public sealed class SlugGenerationResult
{
    public required string FontPath { get; init; }
    public required DateTime FontLastWriteTimeUtc { get; init; }
    public required DateTime GeneratedAtUtc { get; init; }

    /// <summary>
    /// Serialized SLUGGISH bytes.
    /// </summary>
    public required byte[] Bytes { get; init; }

    /// <summary>
    /// Generation settings effectively used for this result.
    /// </summary>
    public required SlugGenerationSettings EffectiveSettings { get; init; }

    /// <summary>
    /// Sanitized whitelist after merging explicit whitelist + charset file.
    /// Null when full-range generation is active.
    /// </summary>
    public HashSet<int>? EffectiveWhitelist { get; init; }

    /// <summary>
    /// Timing report returned by SlugSharp.Core.
    /// </summary>
    public SlugCompilationReport? Report { get; init; }

    public int ByteCount => Bytes.Length;
}
