#nullable enable
using System;
using System.IO;
using System.Reflection;

namespace T3.Core.Compilation;

/// <summary>
/// Common info container for package loading and versioning, and the source of truth for
/// whether this build is a prerelease — see <see cref="IsPreview"/>.
/// </summary>
public static class RuntimeAssemblies
{
    public const string NetVersion = "10.0";

    // Static field initialisers run in source order — every public field below depends on
    // _coreAssembly, so it must be declared first.
    private static readonly Assembly _coreAssembly = typeof(RuntimeAssemblies).Assembly;

    public static readonly string CoreDirectory = Path.GetDirectoryName(_coreAssembly.Location)!;

    /// <summary>4-part numeric assembly version (e.g. <c>4.2.0.2</c>). Excludes any semver prerelease segment.</summary>
    public static readonly Version Version = _coreAssembly.GetName().Version!;

    /// <summary>
    /// Prerelease segment of the semver — empty for stable builds, <c>"alpha"</c> (or a future
    /// <c>"beta.2"</c>, <c>"rc.1"</c>) for prerelease builds.
    /// </summary>
    public static readonly string VersionSuffix = ParseVersionSuffix();

    /// <summary>
    /// True for any prerelease build. Permissive on purpose so future <c>beta</c> / <c>rc</c>
    /// cycles get the same isolation without a code change.
    /// </summary>
    public static readonly bool IsPreview = !string.IsNullOrEmpty(VersionSuffix);
    
    public static readonly bool IsAlpha = !string.IsNullOrEmpty(VersionSuffix) && string.Equals(VersionSuffix, "alpha", StringComparison.Ordinal);

    /// <summary>
    /// Version formatted as semver without commit SHA — e.g. <c>"4.2.0.2-alpha"</c> or <c>"4.2.0.2"</c>.
    /// </summary>
    public static readonly string FormattedVersion = string.IsNullOrEmpty(VersionSuffix)
                                                         ? Version.ToString()
                                                         : $"{Version}-{VersionSuffix}";

    /// <summary>
    /// Reads the prerelease segment from <see cref="AssemblyInformationalVersionAttribute"/>.
    /// The informational version is <c>"&lt;numeric&gt;[-&lt;suffix&gt;][+&lt;sha&gt;]"</c>; we want
    /// everything between the first <c>-</c> and the optional <c>+</c>.
    /// </summary>
    private static string ParseVersionSuffix()
    {
        var informational = _coreAssembly
                           .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
            return string.Empty;

        var plusIndex = informational.IndexOf('+');
        var withoutSha = plusIndex >= 0 ? informational[..plusIndex] : informational;

        var dashIndex = withoutSha.IndexOf('-');
        if (dashIndex < 0 || dashIndex == withoutSha.Length - 1)
            return string.Empty;

        return withoutSha[(dashIndex + 1)..];
    }
}
