#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using T3.Core.Model;

namespace T3.Editor.Compilation;

/// <summary>
/// A source build loads every package in Operators/ as a project, compiling it when its sources changed. Optional
/// packages (TixlDistribution=Optional, installed as extensions in releases) are skipped unless named in
/// TIXL_DEBUG_EXTENSIONS, so they cost no startup time for work that doesn't touch them.
/// </summary>
internal static partial class OptionalPackagesInSourceBuilds
{
    /** Semicolon- or comma-separated package folder names, or "all". */
    public const string EnvironmentVariableName = "TIXL_DEBUG_EXTENSIONS";

    /// <summary>
    /// False for an optional package that wasn't asked for; its operator ids are then collected, so projects and
    /// examples that use them don't report them as missing.
    /// </summary>
    public static bool ShouldLoad(string packageDirectory)
    {
        var csproj = Directory.EnumerateFiles(packageDirectory, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (csproj == null || !OptionalDistribution().IsMatch(File.ReadAllText(csproj)))
            return true;

        var name = Path.GetFileName(packageDirectory);
        if (_requested.Contains("all") || _requested.Contains(name))
            return true;

        _skippedNames.Add(name);
        var symbolsDirectory = Path.Combine(packageDirectory, "Symbols");
        if (Directory.Exists(symbolsDirectory))
        {
            foreach (var symbolFile in Directory.EnumerateFiles(symbolsDirectory, "*.t3", SearchOption.AllDirectories))
            {
                var match = SymbolId().Match(File.ReadAllText(symbolFile));
                if (match.Success && Guid.TryParse(match.Groups[1].Value, out var id))
                    _skippedSymbolIds.Add(id);
            }
        }

        return false;
    }

    /** Call after the project folders were scanned and before packages load. */
    public static void PublishSkipped()
    {
        if (_skippedNames.Count == 0)
            return;

        SymbolPackage.ExpectedMissingSymbolIds = _skippedSymbolIds;
        Log.Info($"Not loading optional packages {string.Join(", ", _skippedNames)} in this source build. "
                 + $"Set {EnvironmentVariableName}={string.Join(";", _skippedNames)} (or all) to load them.");
    }

    [GeneratedRegex(@"<TixlDistribution>\s*Optional\s*</TixlDistribution>")]
    private static partial Regex OptionalDistribution();

    /** A symbol file starts with its own id, before any child's. */
    [GeneratedRegex(@"""Id""\s*:\s*""([0-9a-fA-F-]{36})""")]
    private static partial Regex SymbolId();

    private static readonly HashSet<string> _requested =
        new((Environment.GetEnvironmentVariable(EnvironmentVariableName) ?? "")
           .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    private static readonly List<string> _skippedNames = [];
    private static readonly HashSet<Guid> _skippedSymbolIds = [];
}
