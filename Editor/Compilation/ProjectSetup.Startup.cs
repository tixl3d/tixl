#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using T3.Core.Compilation;
using T3.Core.IO;
using T3.Core.Model;
using T3.Core.Resource;
using T3.Core.UserData;
using T3.Editor.External;
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
        LoadProjects(csProjFiles, forceRecompile, failedProjects: out _);

        // Register UI types
        UiRegistration.RegisterUiTypes();

        var allPackages = ActivePackages.ToArray();
        // Update all symbol packages
        UpdateSymbolPackages(allPackages);
        
        // Initialize resources and shader linting
        foreach (var package in allPackages)
        {
            InitializePackageResources(package);
        }
        

        ShaderLinter.AddPackage(SharedResources.ResourcePackage, ResourceManager.SharedShaderPackages);

        // Initialize custom UIs

        if (ProjectSettings.Config.LogAssemblyLoadingDetails)
        {
            foreach (var package in SymbolPackage.AllPackages)
            {
                Log.Debug($"Completed loading {package.DisplayName}");
            }
        }

        #if DEBUG
        totalStopwatch.Stop();
        Log.Info($"Total load time: {totalStopwatch.ElapsedMilliseconds}ms");
        #endif
    }

    private static void LoadBuiltInPackages()
    {
        var directory = Directory.CreateDirectory(CoreOperatorDirectory);

        directory
           .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
           .Where(folder => !folder.Name.EndsWith(FileLocations.ExportFolderName, StringComparison.OrdinalIgnoreCase)) // ignore "player" project directory
           .ToList()
           .ForEach(directoryInfo =>
                    {
                        AddToLoadedPackages((new EditorSymbolPackage(new AssemblyInformation(directoryInfo.FullName), null)));
                    });
    }
    
    private readonly record struct ProjectLoadInfo(FileInfo fileInfo, CsProjectFile? csProjFile, bool success);

    /// <summary>
    /// Load each project file and its associated assembly
    /// </summary>
    [SuppressMessage("ReSharper", "OutParameterValueIsAlwaysDiscarded.Local")]
    private static void LoadProjects(FileInfo[] csProjFiles, bool forceRecompile, out List<ProjectLoadInfo> failedProjects)
    {
        // Load each project file and its associated assembly
        var projectResults = csProjFiles
                      .AsParallel()
                      .Select(fileInfo =>
                              {
                                  if (!CsProjectFile.TryLoad(fileInfo, out var loadInfo))
                                  {
                                      Log.Error($"Failed to load project at \"{fileInfo.FullName}\":\n{loadInfo.Error}");
                                      return new ProjectLoadInfo(fileInfo, null, false);
                                  }
                                  
                                  var csProjFile = loadInfo.CsProjectFile!;

                                  var needsCompile = forceRecompile || loadInfo.NeedsRecompile || !Directory.Exists(csProjFile.GetBuildTargetDirectory());

                                  if (needsCompile && !csProjFile.TryRecompile(true, out var failureLog))
                                  {
                                      Log.Error($"Failed to recompile project '{csProjFile.Name}:\n{failureLog}'");
                                      return new ProjectLoadInfo(fileInfo, csProjFile, false);
                                  }

                                  return new ProjectLoadInfo(fileInfo, csProjFile, true);
                              })
                      .ToArray();

        failedProjects = [];
        foreach (var projectInfo in projectResults)
        {
            if (projectInfo is { csProjFile: not null, success: true })
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
            string[] topDirectories = [UserSettings.Config.ProjectsFolder];

            var projectSearchDirectories = topDirectories
                                          .Where(Directory.Exists)
                                          .SelectMany(Directory.EnumerateDirectories)
                                              .Where(dirName => !dirName.Contains(FileLocations.ExportFolderName, StringComparison.OrdinalIgnoreCase));

            // Add Built-in packages as projects
            if (includeBuiltInAsProjects)
            {
                projectSearchDirectories = projectSearchDirectories.Concat(Directory.EnumerateDirectories(Path.Combine(T3ParentDirectory, "Operators"))
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


    private static readonly string CoreOperatorDirectory = Path.Combine(FileLocations.StartFolder, "Operators");
    private static readonly string T3ParentDirectory = Path.Combine(FileLocations.StartFolder, "..", "..", "..", "..");
}