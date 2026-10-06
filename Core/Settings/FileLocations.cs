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
    /// <summary>Precompiled shader bytecode shipped with an export and read-only seeded into the player cache.</summary>
    public const string ShaderCacheSubFolder = "ShaderCache";
    public const string KeyBindingSubFolder = "KeyBindings";
    private const string TestsSubFolder = "Tests";

    public const string LibPackageName = "Lib";
    public const string ExamplesPackageName = "Examples";
    public const string TypesPackageName = "Types";
    public const string SkillsPackageName = "Skills";
    
    /// <summary>
    /// Scratch for the current session: staging files, working directories, diagnostics. Lands in the
    /// system temp directory, so it may be cleared on reboot - nothing here is allowed to matter by then.
    /// </summary>
    public static string TempFolder => Path.Combine(Path.GetTempPath(), VersionedAppFolderName);

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

    /// <summary>
    /// Expensive to rebuild, safe to delete, and must survive a reboot: compiled shaders, shadow-copied
    /// assemblies, thumbnails.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SettingsDirectory"/> because this is not configuration and users are right
    /// to object to it sitting in a config folder, and separate from <see cref="TempFolder"/> because losing
    /// it on every reboot would mean recompiling every shader.
    /// </remarks>
    public static readonly string CacheDirectory = ResolveCacheDirectory();

    /// <summary>
    /// Creates the cache folder and marks it as a cache, so it is skipped by backup tools.
    /// </summary>
    /// <remarks>
    /// A CACHEDIR.TAG makes "this is disposable" a property of the directory rather than of its location,
    /// which is what tar --exclude-caches, borg, restic and rsnapshot look for. Cheap insurance for a folder
    /// that holds compiled shaders and shadow-copied assemblies and can run to hundreds of megabytes.
    /// </remarks>
    public static void EnsureCacheDirectory()
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);

            var tag = Path.Combine(CacheDirectory, "CACHEDIR.TAG");
            if (File.Exists(tag))
                return;

            // The first line is the signature the specification requires, byte for byte.
            File.WriteAllText(tag,
                              """
                              Signature: 8a477f597d28d172789f06886806bc55
                              # This file is a cache directory tag created by TiXL.
                              # For information about cache directory tags, see https://bford.info/cachedir/

                              """);
        }
        catch (Exception)
        {
            // Not being able to tag the cache is never worth failing over.
        }
    }

    private static string ResolveCacheDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // Local rather than roaming: a shader cache has no business following a user between machines.
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                VersionedAppFolderName);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Caches", VersionedAppFolderName);
        }

        // The XDG base directory spec: $XDG_CACHE_HOME, or ~/.cache when it is unset or not absolute.
        var configured = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheRoot = !string.IsNullOrWhiteSpace(configured) && Path.IsPathRooted(configured)
                            ? configured
                            : Path.Combine(home, ".cache");

        return Path.Combine(cacheRoot, VersionedAppFolderName);
    }

    public static readonly string DefaultProjectFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     VersionedAppFolderName);
    public const string LegacyResourcesSubfolder = "Resources";
    public const string AssetsSubfolder = "Assets";
    public const string EditorResourcesSubfolder = "EditorResources";
    public const string DependenciesFolder = "dependencies";
    
    /// <summary>
    /// Folder holding a package's operator files (.cs/.t3/.t3ui) - both in editable project sources and
    /// in release package output. Editable-project discovery is limited to this folder by design.
    /// </summary>
    public const string SymbolsSubfolder = "Symbols";
    
    public const string SymbolUiSubFolder = "SymbolUis";
    public const string SourceCodeSubFolder = "SourceCode";

    /** Per-project transient state: backups, and (since project format V3) the bin/obj build output */
    public const string TempSubfolder = ".temp";
    
    /** Folder with the packages both in editor and in exported projects */
    public const string OperatorsSubFolder = "Operators";

    public const string MetaSubFolder = ".meta";
}
