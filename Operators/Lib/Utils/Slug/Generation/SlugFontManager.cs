#nullable enable
using SlugSharp.Core;

namespace Lib.Utils.Slug;

/// <summary>
/// High-level manager to compile font files into SLUGGISH bytes, with cache support.
/// Intended for operators that need on-the-fly .slug generation.
/// </summary>
public sealed class SlugFontManager
{
    public bool TryCompile(string fontPath,
                           SlugGenerationSettings? settings,
                           [NotNullWhen(true)] out SlugGenerationResult? result,
                           [NotNullWhen(false)] out string? failureReason)
    {
        result = null;
        failureReason = null;

        settings ??= new SlugGenerationSettings();

        if (string.IsNullOrWhiteSpace(fontPath))
        {
            failureReason = "Font path is null or empty.";
            return false;
        }

        if (settings.BandCount < 1)
        {
            failureReason = $"BandCount must be >= 1, got {settings.BandCount}.";
            return false;
        }

        string absoluteFontPath;
        DateTime lastWriteTimeUtc;
        try
        {
            absoluteFontPath = Path.GetFullPath(fontPath);
            if (!File.Exists(absoluteFontPath))
            {
                failureReason = $"Font file does not exist: {absoluteFontPath}";
                return false;
            }

            lastWriteTimeUtc = File.GetLastWriteTimeUtc(absoluteFontPath);
        }
        catch (Exception e)
        {
            failureReason = $"Failed to resolve font path '{fontPath}': {e.Message}";
            return false;
        }

        if (!TryResolveEffectiveWhitelist(settings, out var whitelist, out failureReason))
            return false;

        var key = CacheKey.Create(absoluteFontPath, lastWriteTimeUtc, settings.BandCount, settings.FullRange, whitelist);

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                result = CloneResult(cached);
                return true;
            }
        }

        try
        {
            var options = new GeneratorOptions
                              {
                                  BandCount = settings.BandCount,
                                  FullRange = settings.FullRange,
                                  Whitelist = settings.FullRange ? null : whitelist,
                                  ProgressCallback = settings.ProgressCallback,
                                  ProgressInterval = settings.ProgressInterval,
                              };

            var compilation = SlugCompiler.CompileWithReport(absoluteFontPath, options, settings.ValidateOutput);

            var compiled = new SlugGenerationResult
                               {
                                   FontPath = absoluteFontPath,
                                   FontLastWriteTimeUtc = lastWriteTimeUtc,
                                   GeneratedAtUtc = DateTime.UtcNow,
                                   Bytes = compilation.Bytes,
                                   EffectiveSettings = CloneSettings(settings),
                                   EffectiveWhitelist = whitelist == null ? null : new HashSet<int>(whitelist),
                                   Report = compilation.Report,
                               };

            lock (_cacheLock)
            {
                _cache[key] = compiled;
            }

            result = CloneResult(compiled);
            return true;
        }
        catch (Exception e)
        {
            failureReason = $"Failed to compile slug from '{absoluteFontPath}': {e.Message}";
            return false;
        }
    }

    public bool TryCompileToFile(string fontPath,
                                 string outputSlugPath,
                                 SlugGenerationSettings? settings,
                                 [NotNullWhen(true)] out SlugGenerationResult? result,
                                 [NotNullWhen(false)] out string? failureReason)
    {
        result = null;

        if (!TryCompile(fontPath, settings, out var compiled, out failureReason))
            return false;

        try
        {
            var absoluteOutputPath = Path.GetFullPath(outputSlugPath);
            var outputDir = Path.GetDirectoryName(absoluteOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            File.WriteAllBytes(absoluteOutputPath, compiled.Bytes);
            result = compiled;
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            failureReason = $"Failed to write slug file '{outputSlugPath}': {e.Message}";
            return false;
        }
    }

    public void InvalidateCacheForFont(string fontPath)
    {
        if (string.IsNullOrWhiteSpace(fontPath))
            return;

        var absoluteFontPath = Path.GetFullPath(fontPath);

        lock (_cacheLock)
        {
            var keys = _cache.Keys.Where(k => k.FontPath.Equals(absoluteFontPath, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in keys)
            {
                _cache.Remove(key);
            }
        }
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    public int CacheEntryCount
    {
        get
        {
            lock (_cacheLock)
            {
                return _cache.Count;
            }
        }
    }

    private static bool TryResolveEffectiveWhitelist(SlugGenerationSettings settings,
                                                     out HashSet<int>? whitelist,
                                                     [NotNullWhen(false)] out string? failureReason)
    {
        failureReason = null;

        if (settings.FullRange)
        {
            whitelist = null;
            return true;
        }

        HashSet<int>? fromFile = null;
        if (!string.IsNullOrWhiteSpace(settings.CharsetFilePath))
        {
            if (!SlugCharsetUtils.TryLoadCodePointsFromFile(settings.CharsetFilePath,
                                                            settings.ValidateCharsetFile,
                                                            out fromFile,
                                                            out failureReason,
                                                            out _))
            {
                whitelist = null;
                return false;
            }
        }

        if (settings.Whitelist == null && fromFile == null)
        {
            whitelist = null;
            return true;
        }

        whitelist = new HashSet<int>();

        if (settings.Whitelist != null)
            whitelist.UnionWith(settings.Whitelist);

        if (fromFile != null)
            whitelist.UnionWith(fromFile);

        return true;
    }

    private static SlugGenerationResult CloneResult(SlugGenerationResult source)
    {
        return new SlugGenerationResult
                   {
                       FontPath = source.FontPath,
                       FontLastWriteTimeUtc = source.FontLastWriteTimeUtc,
                       GeneratedAtUtc = source.GeneratedAtUtc,
                       Bytes = source.Bytes.ToArray(),
                       EffectiveSettings = CloneSettings(source.EffectiveSettings),
                       EffectiveWhitelist = source.EffectiveWhitelist == null ? null : new HashSet<int>(source.EffectiveWhitelist),
                       Report = source.Report == null
                                    ? null
                                    : new SlugCompilationReport
                                          {
                                              CodePointRecordCount = source.Report.CodePointRecordCount,
                                              GenerationMilliseconds = source.Report.GenerationMilliseconds,
                                              SerializationMilliseconds = source.Report.SerializationMilliseconds,
                                              ValidationMilliseconds = source.Report.ValidationMilliseconds,
                                              TotalMilliseconds = source.Report.TotalMilliseconds,
                                          },
                   };
    }

    private static SlugGenerationSettings CloneSettings(SlugGenerationSettings source)
    {
        return new SlugGenerationSettings
                   {
                       BandCount = source.BandCount,
                       FullRange = source.FullRange,
                       Whitelist = source.Whitelist == null ? null : new HashSet<int>(source.Whitelist),
                       CharsetFilePath = source.CharsetFilePath,
                       ValidateCharsetFile = source.ValidateCharsetFile,
                       ValidateOutput = source.ValidateOutput,
                       ProgressCallback = source.ProgressCallback,
                       ProgressInterval = source.ProgressInterval,
                   };
    }

    private readonly object _cacheLock = new();
    private readonly Dictionary<CacheKey, SlugGenerationResult> _cache = new();

    private readonly record struct CacheKey(string FontPath,
                                            DateTime FontLastWriteTimeUtc,
                                            int BandCount,
                                            bool FullRange,
                                            string? WhitelistSignature)
    {
        public static CacheKey Create(string fontPath,
                                      DateTime fontLastWriteTimeUtc,
                                      int bandCount,
                                      bool fullRange,
                                      HashSet<int>? whitelist)
        {
            var signature = BuildWhitelistSignature(fullRange, whitelist);
            return new CacheKey(fontPath, fontLastWriteTimeUtc, bandCount, fullRange, signature);
        }

        private static string? BuildWhitelistSignature(bool fullRange, HashSet<int>? whitelist)
        {
            if (fullRange)
                return "full-range";

            if (whitelist == null || whitelist.Count == 0)
                return "none";

            return string.Join(",", whitelist.OrderBy(c => c));
        }
    }
}
