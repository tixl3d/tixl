using System;
using System.Collections.Generic;
using System.IO;
using T3.Core.Compilation;

#nullable enable

namespace T3.Core.Settings;

/// <summary>
/// A collection of critical files and directors.
/// All classes in core and Editor should use these if possible. 
/// </summary>
public static class FileLocations
{
    public const string AppSubFolder = "TiXL";
    public const string ThemeSubFolder = "Themes";
    public const string RenderSubFolder = "RenderOutput";
    public const string ExportSubFolder = "Export";
    public const string KeyBindingSubFolder = "KeyBindings";
    private const string TestsSubFolder = "Tests";

    public const string LibPackageName = "Lib";
    public const string ExamplesPackageName = "Examples";
    public const string TypesPackageName = "Types";
    public const string SkillsPackageName = "Skills";
    
    public static string TempFolder => Path.Combine(SettingsDirectory, "Tmp");

#if RELEASE
    public static string TestReferencesFolder => Path.Combine(SettingsDirectory, TestsSubFolder);
#else
public static string TestReferencesFolder => Path.Combine(".tixl", TestsSubFolder);
#endif

    /// <summary>
    /// We extract this because this will later not be available for published versions.
    /// Providing this at this location will help refactoring later. 
    /// </summary>
    public static string StartFolder => AppContext.BaseDirectory;
    
    /// <summary>
    /// A subfolder next in the editor start folder.
    /// </summary>
    public static string ReadOnlySettingsPath => Path.Combine(StartFolder, ".tixl");

    /// <summary>
    /// Resolves where files marked as <see cref="T:T3.Core.Settings.UserData.UserDataLocation.Defaults"/>
    /// are read from and written to.
    /// In Release this is the same as <see cref="ReadOnlySettingsPath"/>.
    /// In Debug we walk up from the build output to find the repo's <c>.Defaults</c> source folder, so
    /// newly-created defaults (notably variations saved with <c>#if DEBUG</c>) land in git-tracked files
    /// instead of being orphaned in the build output and lost on the next rebuild.
    /// </summary>
    public static string DefaultsSourcePath => _defaultsSourcePath ??= ResolveDefaultsSourcePath();
    private static string? _defaultsSourcePath;

    private static string ResolveDefaultsSourcePath()
    {
#if DEBUG
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, ".Defaults");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }
#endif
        return ReadOnlySettingsPath;
    }

    public static HashSet<string> IgnoredFiles => ["shadertoolsconfig.json", ".gitattributes", ".git"];
    
    
    /// <summary>
    /// TiXL's <c>major.minor</c> version, e.g. <c>"4.2"</c>. Deliberately excludes the alpha
    /// suffix — recordings stamped with this stay diffable between stable and alpha builds of
    /// the same minor. The suffix is added only to folder names via <see cref="VersionedAppFolderName"/>.
    /// </summary>
    public static readonly string TixlVersion = $"{RuntimeAssemblies.Version.Major}.{RuntimeAssemblies.Version.Minor}";

    /// <summary>
    /// Environment variable that overrides the per-version folder suffix so two builds of the same
    /// version can run side by side with isolated settings and projects. The value replaces the
    /// prerelease suffix: <c>TIXL_OVERRIDE_VERSION_ID=skillQuest</c> → folder <c>TiXL4.2-skillQuest</c>.
    /// The Editor also accepts <c>--override-version-id=&lt;id&gt;</c>, which sets this variable at startup.
    /// </summary>
    public const string VersionIdOverrideEnvVar = "TIXL_OVERRIDE_VERSION_ID";

    /// <summary>
    /// Sanitised value of <see cref="VersionIdOverrideEnvVar"/>, or <c>null</c> when unset or blank.
    /// </summary>
    public static readonly string? VersionIdOverride = ReadVersionIdOverride();

    /// <summary>
    /// Per-version folder name (<c>"TiXL4.2"</c>, or <c>"TiXL4.2-alpha"</c> for prerelease builds)
    /// so alpha and stable installs don't clobber each other's settings, layouts, themes, and projects.
    /// A set <see cref="VersionIdOverrideEnvVar"/> replaces the suffix instead (<c>"TiXL4.2-skillQuest"</c>),
    /// keeping two builds of the same version isolated.
    /// Declared before the folders below — static initialisers run in source order, so reading it
    /// earlier would observe <c>null</c>.
    /// </summary>
    public static readonly string VersionedAppFolderName = ResolveVersionedAppFolderName();

    private static string ResolveVersionedAppFolderName()
    {
        if (VersionIdOverride != null)
            return $"{AppSubFolder}{TixlVersion}-{VersionIdOverride}";

        return RuntimeAssemblies.IsPreview
                   ? $"{AppSubFolder}{TixlVersion}-{RuntimeAssemblies.VersionSuffix}"
                   : $"{AppSubFolder}{TixlVersion}";
    }

    /// <summary>
    /// Reads <see cref="VersionIdOverrideEnvVar"/> and strips anything that isn't valid in a single
    /// folder-name segment, so a stray separator can't redirect the settings tree outside AppData.
    /// </summary>
    private static string? ReadVersionIdOverride()
    {
        var raw = Environment.GetEnvironmentVariable(VersionIdOverrideEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var sanitized = raw.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalid, '_');

        return sanitized.Length == 0 ? null : sanitized;
    }

    public static readonly string SettingsDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     VersionedAppFolderName

                     // Skip process name to avoid double nesting of TiXL
                     // This will lump together logs from player

                     //, Process.GetCurrentProcess().ProcessName
                     );

    public static readonly string DefaultProjectFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     VersionedAppFolderName);
    public const string LegacyResourcesSubfolder = "Resources";
    public const string AssetsSubfolder = "Assets";
    public const string EditorResourcesSubfolder = "EditorResources";
    public const string DependenciesFolder = "dependencies";
    
    /** This is only used in release builds */
    public const string ReleaseSymbolsSubfolder = "Symbols";
    
    public const string SymbolUiSubFolder = "SymbolUis";
    public const string SourceCodeSubFolder = "SourceCode";
    
    /** Folder with the packages both in editor and in exported projects */
    public const string OperatorsSubFolder = "Operators";

    public const string MetaSubFolder = ".meta";
}
