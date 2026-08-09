#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Compilation;
using T3.Core.IO;
using T3.Core.Model;
using T3.Core.Resource.Assets;
using T3.Core.Settings;
using T3.Editor.Gui.Interaction.StartupCheck;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Compilation;

internal static partial class ProjectSetup
{
    internal static bool TryLoadAll(bool forceRecompile, [NotNullWhen(false)] out Exception? exception)
    {
        try
        {
            LoadAll(forceRecompile);

            exception = null;
            return true;
        }
        catch (Exception e)
        {
            exception = e;
            return false;
        }
    }

    /// <summary>
    /// Loads all projects and packages - for use only at the start of the editor
    /// </summary>
    /// <param name="forceRecompile">all symbol projects will be recompiled if possible</param>
    /// <exception cref="Exception">Unknown exceptions may be raised - if you want to handle them, wrap this in a try/catch</exception>
    private static void LoadAll(bool forceRecompile)
    {
        // ReSharper disable once RedundantAssignment
        bool isDebugBuild = false;
            
        #if DEBUG
        isDebugBuild = true;
        System.Diagnostics.Stopwatch totalStopwatch = new();
        totalStopwatch.Start();
        #endif

        if (!isDebugBuild)
        {
            // Load pre-built built-in packages as read-only
            LoadBuiltInPackages();
        }

        // Find project files
        var csProjFiles = FindCsProjFiles(isDebugBuild);

        // Load projects
        LoadProjects(csProjFiles, forceRecompile, out var failedProjects);

        // Keep projects that failed to load visible and recoverable instead of silently dropping them
        // — a single broken project shouldn't just vanish from the Hub.
        BrokenProjects.Clear();
        foreach (var failed in failedProjects)
        {
            // Archived projects also land in failedProjects (success, but intentionally not loaded);
            // only genuine failures are "broken".
            if (failed.success)
                continue;

            BrokenProjects.Add(new BrokenProjectInfo(failed.fileInfo, failed.csProjFile,
                                                     failed.failureReason ?? "The project could not be loaded.",
                                                     failed.hint,
                                                     failed.likelySyncConflict));
        }

        // Phase 1: Initial Startup Migration
        // This happens only once here and not in subsequent UpdateSymbolPackages calls
        
        foreach (var package in _activePackages)
        {
            if (ConformAssetPaths.RenameResourcesToAssets(package))
            {
                Log.Debug($"Rescanning {package.Name} assets after migration...");
                AssetRegistry.RegisterAssetsFromPackage(package);
            }
        }
        
        // Register UI types
        UiRegistration.RegisterUiTypes();

        var allPackages = _activePackages.ToArray();
        // Update all symbol packages
        UpdateSymbolPackages(allPackages);

        WarnAboutUnresolvedChildren(allPackages);
        WarnAboutCorruptedSymbolFiles(allPackages);

        // Needs registered symbols to resolve which project owns each variation file
        VariationsMigration.MigrateLegacyVariationsToPackageMeta();

        // Needs the [AudioClip] symbol registered (Lib). In-memory only — persists via the regular
        // save machinery once the user saves the flagged symbols.
        LegacyAudioClipMigration.MigrateLegacyClipsToOps();

        // Initialize resources and shader linting
        Log.Info("Initializing package resources...");
        foreach (var package in allPackages)
        {
            InitializePackageResources(package);
        }
        
        // FIXME: This needs to be properly handled.
        //ShaderLinter.AddPackage(SharedResources.ResourcePackage, ResourceManager.SharedShaderPackages);

        // Initialize custom UIs

        if (CoreSettings.Config.LogAssemblyLoadingDetails)
        {
            foreach (var package in SymbolPackage.AllPackages)
            {
                Log.Debug($"Completed loading {package.DisplayName}");
            }
        }

        #if DEBUG
        totalStopwatch.Stop();
        Log.Debug($">> Total load time: {totalStopwatch.ElapsedMilliseconds/1000:0.0}s");
        #endif
    }

    /// <summary>
    /// Summarises symbols that lost children on load (missing package) into one clear warning, since
    /// the per-child console warnings are just raw Guids and easy to miss. These symbols are protected
    /// from being saved over (see <see cref="Symbol.HasUnresolvedChildren"/>), so nothing is lost.
    /// </summary>
    private static void WarnAboutUnresolvedChildren(EditorSymbolPackage[] packages)
    {
        var affectedSymbols = 0;
        var totalChildren = 0;
        foreach (var package in packages)
        {
            foreach (var symbol in package.Symbols.Values)
            {
                if (!symbol.HasUnresolvedChildren)
                    continue;

                affectedSymbols++;
                totalChildren += symbol.UnresolvedChildCount;
            }
        }

        if (affectedSymbols == 0)
            return;

        Log.Warning($"{totalChildren} operator(s) across {affectedSymbols} symbol(s) could not be loaded — "
                    + "most likely a missing package. Those symbols are protected: the editor will not save over "
                    + "them, so nothing is lost. Install the missing package(s) and reload to restore them.");
    }

    /// <summary>
    /// Summarises symbol files that were corrupt and skipped during load (so one bad .t3 no longer
    /// aborts the whole editor). The affected packages refuse to save until reloaded, so the corrupt —
    /// but backup-recoverable — files aren't overwritten.
    /// </summary>
    private static void WarnAboutCorruptedSymbolFiles(EditorSymbolPackage[] packages)
    {
        var corrupted = new List<string>();
        foreach (var package in packages)
            corrupted.AddRange(package.CorruptedSymbolFilePaths);

        if (corrupted.Count == 0)
            return;

        Log.Warning($"{corrupted.Count} operator file(s) are corrupt and were skipped so the rest of the editor "
                    + "could load. The affected project(s) will not save until you restore an earlier backup:\n  "
                    + string.Join("\n  ", corrupted));
    }

    private static void LoadBuiltInPackages()
    {
        var directory = Directory.CreateDirectory(_coreOperatorDirectory);

        directory
           .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
           .Where(folder => !folder.Name.EndsWith(FileLocations.ExportSubFolder, StringComparison.OrdinalIgnoreCase)) // ignore "player" project directory
           .ToList()
           .ForEach(directoryInfo =>
                    {
                        AddToLoadedPackages((new EditorSymbolPackage(new AssemblyInformation(directoryInfo.FullName), null)));
                    });
    }
    
    private readonly record struct ProjectLoadInfo(
        FileInfo fileInfo,
        CsProjectFile? csProjFile,
        bool success,
        string? failureReason = null,
        string? hint = null,
        bool likelySyncConflict = false);

    /// <summary>
    /// Load each project file and its associated assembly
    /// </summary>
    private static void LoadProjects(FileInfo[] csProjFiles, bool forceRecompile, out List<ProjectLoadInfo> failedProjects)
    {
        Log.Info("Loading projects...");
        // Load each project file and its associated assembly
        var projectResults = csProjFiles
                      .AsParallel()
                      .Select(fileInfo =>
                              {
                                  if (!CsProjectFile.TryLoad(fileInfo, out var loadInfo))
                                  {
                                      Log.Error($"Failed to load project at \"{fileInfo.FullName}\":\n{loadInfo.Error}");
                                      var likelySyncConflict = SyncToolConflicts.IsLikelySyncConflict(loadInfo.Exception, fileInfo.DirectoryName);
                                      return new ProjectLoadInfo(fileInfo, null, false,
                                                                 likelySyncConflict
                                                                     ? "TiXL was not allowed to read the project file."
                                                                     : "The project file could not be read.",
                                                                 loadInfo.Error,
                                                                 likelySyncConflict);
                                  }
                                  
                                  var csProjFile = loadInfo.CsProjectFile!;

                                  // Check if archived before doing anything else
                                  if (csProjFile.IsArchived)
                                  {
                                      lock (ArchivedProjects)
                                      {
                                          ArchivedProjects.Add(new ArchivedProjectInfo(csProjFile));
                                      }
                                      return new ProjectLoadInfo(fileInfo, csProjFile, true); // Mark as success but don't process further
                                  }
                                  
                                  var needsCompile = forceRecompile || loadInfo.NeedsRecompile || !Directory.Exists(csProjFile.GetBuildTargetDirectory());

                                  if (needsCompile && !csProjFile.TryRecompile(true, out var failureLog))
                                  {
                                      Log.Error($"Failed to recompile project '{csProjFile.Name}:\n{failureLog}'");
                                      var explanation = Compiler.ExplainBuildFailure(failureLog);
                                      if (explanation != null)
                                          Log.Warning($"Likely cause for '{csProjFile.Name}' compile failure:\n{explanation}");
                                      return new ProjectLoadInfo(fileInfo, csProjFile, false,
                                                                 "The project failed to compile.",
                                                                 explanation ?? "See the log for details.");
                                  }

                                  return new ProjectLoadInfo(fileInfo, csProjFile, true);
                              })
                      .ToArray();

        failedProjects = [];
        foreach (var projectInfo in projectResults)
        {
            if (projectInfo is { csProjFile: not null, success: true }&& !projectInfo.csProjFile.IsArchived)
            {
                var project = new EditableSymbolProject(projectInfo.csProjFile);
                AddToLoadedPackages(project);
            }
            else
            {
                failedProjects.Add(projectInfo);
            }
        }
    }

    private static FileInfo[] FindCsProjFiles(bool includeBuiltInAsProjects)
    {
        return GetProjectDirectories(includeBuiltInAsProjects)
              .SelectMany(dir => Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories))
              .Select(x => new FileInfo(x))
              .ToArray();
        

        static IEnumerable<string> GetProjectDirectories(bool includeBuiltInAsProjects)
        {
            // ReSharper disable once JoinDeclarationAndInitializer
            string[] topDirectories = [];

            if (UserSettings.Config.EnableUsbProjectDetection)
            {
                var usbs = DriveInfo.GetDrives()
                                    .Where(drive => drive is { DriveType: DriveType.Removable, IsReady: true });
                foreach (var usb in usbs)
                {
                    var usbT3ProjectsPath = Path.Combine(usb.RootDirectory!.FullName, "TiXLProjects");
                    if (Directory.Exists(usbT3ProjectsPath))
                    {
                        topDirectories = topDirectories.Append(usbT3ProjectsPath).ToArray();
                    }
                }

            }
            
            foreach (var projectPath in UserSettings.Config.ProjectDirectories)
            {
                if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
                {
                    topDirectories = topDirectories.Append(projectPath).ToArray();
                }
            }

            var projectSearchDirectories = topDirectories
                                          .Where(Directory.Exists)
                                          .SelectMany(Directory.EnumerateDirectories)
                                              .Where(dirName => !dirName.Contains(FileLocations.ExportSubFolder, StringComparison.OrdinalIgnoreCase));

            // Add Built-in packages as projects
            if (includeBuiltInAsProjects)
            {
                projectSearchDirectories = projectSearchDirectories.Concat(Directory.EnumerateDirectories(BuiltInOperatorDirectory)
                                                                                    .Where(path =>
                                                                                           {
                                                                                               var subDir = Path.GetFileName(path);
                                                                                               return
                                                                                                   !subDir
                                                                                                      .StartsWith('.'); // ignore things like .git and file sync folders 
                                                                                           }));
            }
            return projectSearchDirectories;
        }
    }


    /// <summary>
    /// Source folder of the operator packages shipped with TiXL. Release builds load these as read-only
    /// packages; debug builds compile them as regular projects, so this is the only way to tell them apart.
    /// </summary>
    internal static string BuiltInOperatorDirectory => Path.GetFullPath(Path.Combine(_t3ParentDirectory, FileLocations.OperatorsSubFolder));

    private static readonly string _coreOperatorDirectory = Path.Combine(FileLocations.StartFolder, FileLocations.OperatorsSubFolder);
    private static readonly string _t3ParentDirectory = Path.Combine(FileLocations.StartFolder, "..", "..", "..", "..");
}