#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Build.Construction;
using T3.Core.Compilation;
using T3.Core.UserData;
using Encoding = System.Text.Encoding;

// ReSharper disable SuggestBaseTypeForParameterInConstructor

namespace T3.Editor.Compilation;

/// <summary>
/// A class that assists in the creation, modification, and compilation of a csproj file.
/// Each editable project is currently represented by a csproj file, which is compiled and loaded at runtime.
/// There are several unusual properties in the csproj file that are used to accommodate T3's feature set - see <see cref="ProjectXml"/>
/// for more details. This can be considered a higher-level utility class that handles versioning, provides simple compilation methods, and provides
/// straightforward access to properties within the csproj file.
/// </summary>
internal sealed class CsProjectFile
{
    /// <summary>
    /// The path to the csproj file.
    /// </summary>
    public string FullPath => _projectRootElement.FullPath;
    
    /// <summary>
    /// The directory containing the csproj file.
    /// </summary>
    public string Directory => _projectRootElement.DirectoryPath;
    
    /// <summary>
    /// The name of the csproj file.
    /// </summary>
    public string Name => Path.GetFileNameWithoutExtension(FullPath);
    
    /// <summary>
    /// The root namespace of the project, as defined in the csproj file.
    /// </summary>
    public string RootNamespace => _projectRootElement.GetOrAddProperty(PropertyType.RootNamespace, Name);
    
    /// <summary>
    /// The version string of the project, as defined in the csproj file.
    /// </summary>
    public string VersionString => _projectRootElement.GetOrAddProperty(PropertyType.VersionPrefix, "1.0.0");
    
    /// <summary>
    /// The version of the project, as defined in the csproj file.
    /// </summary>
    public Version Version => new(VersionString);
    
    /// <summary>
    /// Returns the target dotnet framework for the project, or adds the default framework if none is found and returns that.
    /// </summary>
    private string TargetFramework => _projectRootElement.GetOrAddProperty(PropertyType.TargetFramework, ProjectXml.TargetFramework);

    public DateTime CreatedAt => _fileInfo.CreationTimeUtc;
    public DateTime ModifiedAt => _fileInfo.LastWriteTimeUtc;

    private CsProjectFile(ProjectRootElement projectRootElement) : this(projectRootElement, new FileInfo(projectRootElement.FullPath)) { }

    private CsProjectFile(ProjectRootElement projectRootElement, FileInfo fileInfo)
    {
        _projectRootElement = projectRootElement;
        _fileInfo = fileInfo;

        var targetFramework = TargetFramework;
        
        // check if the project needs its dotnet version upgraded. If so, update the project file accordingly.
        if (!ProjectXml.FrameworkIsCurrent(targetFramework))
        {
            var newFramework = ProjectXml.UpdateFramework(targetFramework);
            _projectRootElement.SetOrAddProperty(PropertyType.TargetFramework, newFramework);
        }

        var dir = Directory;
        _releaseRootDirectory = Path.Combine(dir, "bin", "Release");
        _debugRootDirectory = Path.Combine(dir, "bin", "Debug");
    }

    /// <summary>
    /// Details about a loaded csproj file, including any errors that occurred during loading.
    /// </summary>
    public readonly struct CsProjectLoadInfo
    {
        public readonly string? Error;
        public readonly CsProjectFile? CsProjectFile;
        public readonly bool NeedsUpgrade;
        public readonly bool NeedsRecompile;
        public readonly List<string> Warnings = [];

        internal CsProjectLoadInfo(CsProjectFile? file, string? error)
        {
            Error = error;
            CsProjectFile = file;

            if (file == null)
            {
                NeedsUpgrade = false;
                NeedsRecompile = false;
                return;
            }

            var targetFramework = file.TargetFramework;
            var currentFramework = RuntimeInformation.FrameworkDescription;

            // todo - additional version checks
            var needsUpgrade = !targetFramework.Contains(currentFramework);
            NeedsUpgrade = needsUpgrade; // is this necessary? :shrugs:

            var csProjContents = file._projectRootElement;
            // check items for correctness
            foreach(var group in csProjContents.ItemGroups)
            {
                foreach(var item in group.Items)
                {
                    #region Hotfix June-2-2025
                    // This will correct an issue with earlier project files that had an attribute set that would copy unnecessary dlls to the output directory.
                    // That would cause issues with custom Uis and whatnot.
                    // This is just an automated way to revert the issue in pre-existing projects.
                    // This can be safely removed at some point in the future when we all forget this ever happened :)
                    if (item.ItemType == "Reference" && item.Include.Contains(ProjectSetup.EnvironmentVariableName))
                    {
                        foreach (var metadata in item.Metadata)
                        {
                            if (!metadata.ExpressedAsAttribute || metadata.Name != "Private") continue;
                            if (metadata.Value == "false")
                                continue;

                            metadata.Value = "false"; // ensure that the T3 assemblies are not copied to the output directory
                            var warning = $"Modified {item} ({item.Include}) to set {metadata.Name} to {metadata.Value}";
                            Warnings.Add(warning);
                            Log.Warning(warning);
                        }
                    }
                    #endregion
                }
            }
            
            // Check properties for correctness
            var outputPathPropertyName = PropertyType.OutputPath.GetItemName();
            foreach (var group in csProjContents.PropertyGroups)
            {
                foreach (var property in group.Properties)
                {
                    if (property == null)
                        continue;
                    
                    #region Project updates post-June-26-2025 Project file correction - OutputPath
                    // remove the OutputPath property if it exists, as it is not needed and can cause issues with the build process.
                    if(property.Name == outputPathPropertyName )
                    {
                        Log.Debug($"Removing OutputPath property from {file.FullPath}");
                        group.RemoveChild(property);
                        Warnings.Add($"Removed OutputPath property from {file.FullPath}");
                        // according to the group.RemoveChild docs, we can continue to enumerate the properties after removing one.
                    }
                    #endregion
                }
            }
            
            if(csProjContents.AddCleanBuildTarget())
            {
                Warnings.Add($"Added clean build target to {file.FullPath}");
            }

            if (csProjContents.HasUnsavedChanges)
            {
                Warnings.Add($"Saving corrections to {file.FullPath}");
                try
                {
                    csProjContents.Save();
                }
                catch (Exception e)
                {
                    Warnings.Add($"Failed to save project file {file.FullPath}: {e.Message}");
                    Log.Error($"Failed to save project file {file.FullPath}: {e}");
                }

                NeedsRecompile = true;
            }

            if (!NeedsRecompile)
            {
                // check if the project needs to be compiled due to just not being built
                // we do this by checking the csproj file's version against that of the op package json
                var versionInfoDirectory = file.GetBuildTargetDirectory();
                if(!AssemblyInformation.TryLoadReleaseInfo(versionInfoDirectory, out var releaseInfo))
                {
                    NeedsRecompile = true;
                    Warnings.Add($"{file.Name} needs to be compiled because the version info file does not exist.");
                }
                else if (releaseInfo.Version != file.Version)
                {
                    NeedsRecompile = true;
                    Warnings.Add($"{file.Name} needs to be compiled because the existing build is a different version from our project file: ({releaseInfo.Version}) vs ({file.Version}).");
                }
            }
        }
    }

    /// <summary>
    /// Loads the csproj file at the given path, returning a <see cref="CsProjectLoadInfo"/> struct that contains the loaded file and any errors.
    /// Does not actually handle any assemblies or type loading - it's just a way to load the xml file.
    /// </summary>
    /// <returns>True if successful</returns>
    public static bool TryLoad(FileInfo fileInfo, out CsProjectLoadInfo loadInfo)
    {
        bool success;
        var filePath = fileInfo.FullName;
        try
        {
            var fileContents = ProjectRootElement.Open(filePath);
            if (fileContents == null)
            {
                loadInfo = new CsProjectLoadInfo(null, $"Failed to open project file at \"{filePath}\"");
                success = false;
            }
            else
            {
                loadInfo = new CsProjectLoadInfo(new CsProjectFile(fileContents, fileInfo), null);
                success = true;
            }
        }
        catch (Exception e)
        {
            var error = $"Failed to open project file at \"{filePath}\":\n{e}";
            loadInfo = new CsProjectLoadInfo(null, error);
            success = false;
        }
        
        // log any warnings that were generated during the load
        if (loadInfo.Warnings.Count > 0)
        {
            var name = loadInfo.CsProjectFile?.Name ?? Path.GetFileName(filePath);
            foreach (var warning in loadInfo.Warnings)
            {
                Log.Warning($"{name} {warning}");
            }
        }

        return success;
    }

    /// <summary>
    /// Returns the directory where the primary dll for this project is built. This directory may or may not exist, as this is simply a "functional"
    /// way to generate the directory path.
    /// </summary>
    public string GetBuildTargetDirectory(Compiler.BuildMode buildMode = EditorBuildMode)
    {
        // this functionality should mirror the way that <OutputPath> is defined in the csproj files
        return Path.Combine(GetRootDirectory(buildMode), TargetFramework);
    }

    /// <summary>
    /// Returns the debug & release build directories for this project.
    /// </summary>
    /// <param name="buildMode"></param>
    /// <returns></returns>
    private string GetRootDirectory(Compiler.BuildMode buildMode) => buildMode == Compiler.BuildMode.Debug ? _debugRootDirectory : _releaseRootDirectory;

    // todo - rate limit recompiles for when multiple files change
    /// <summary>
    /// Compiles/recompiles this project in debug mode for runtime use in the Editor.
    /// </summary>
    /// <param name="nugetRestore">True if NuGet packages should be restored</param>
    /// <param name="output">The output of the compilation process</param>
    /// <returns>True if successful</returns>
    public bool TryRecompile(bool nugetRestore, [NotNullWhen(false)] out string? output)
    {
        if (!Compiler.TryCompile(this, EditorBuildMode, nugetRestore, out output))
        {
            return false;
        }

        return true;
    }

    public void UpdateVersionForIOChange(int modifyAmount)
    {
        ModifyBuildVersion(0, Math.Clamp(modifyAmount, -1, 1), 0);
    }

    private void ModifyBuildVersion(int majorModify, int minorModify, int buildModify)
    {
        _projectRootElement.SetOrAddProperty(PropertyType.EditorVersion, Program.Version.ToBasicVersionString());
        var version = Version;
        var newVersion = new Version(version.Major + majorModify, version.Minor + minorModify, version.Build + buildModify);
        _projectRootElement.SetOrAddProperty(PropertyType.VersionPrefix, newVersion.ToBasicVersionString());
        try
        {
            _projectRootElement.Save();
            _fileInfo.Refresh();
        }
        catch (Exception e)
        {
            Log.Error($"{Name}: Failed to save project file {_projectRootElement.FullPath} after modifying version: {e}");
        }
    }

    /// <summary>
    /// For building release-mode assemblies for use in the Player. All other runtime-compilation is done in debug mode.
    /// </summary>
    /// <param name="nugetRestore">True if NuGet packages should be restored</param>
    /// <param name="output">Compilation process output in case of failure</param>
    /// <returns>True if successful</returns>
    public bool TryCompileRelease(bool nugetRestore, [NotNullWhen(false)] out string? output)
    {
        return Compiler.TryCompile(this, PlayerBuildMode, nugetRestore, out output);
    }

    /// <summary>
    /// Updates the last modified date of the project file by re-saving the project XML.
    /// </summary>
    public void UpdateLastModifiedDate()
    {
        // we can set the last modified date by re-saving the project xml
        using var textWriter = new StreamWriter(_projectRootElement.FullPath);
        try
        {
            _projectRootElement.Save(textWriter);
            _fileInfo.Refresh();
        }
        catch (Exception e)
        {
            Log.Error($"{Name}: Failed to save project file {_projectRootElement.FullPath}: {e}");
        }
    }

    // todo- use Microsoft.Build.Construction and Microsoft.Build.Evaluation
    /// <summary>
    /// Creates a new .csproj file and populates all required files for a new T3 project.
    /// </summary>
    /// <param name="projectName">The name of the new project</param>
    /// <param name="nameSpace">The root namespace of the new project</param>
    /// <param name="shareResources">True if the project should share its resources with other packages</param>
    /// <param name="parentDirectory">The directory inside which the new project should be created. The project will actually reside inside
    /// "parentDirectory/projectName"</param>
    /// <returns></returns>
    /// <remarks>
    /// todo - find a better home for this
    /// todo: - files copied into the new project should be generated at runtime where possible -
    /// for example, the default home canvas symbol/symbolui
    /// should probably be copied from the Examples package wholesale, with replaced guids.
    /// </remarks>
    public static CsProjectFile CreateNewProject(string projectName, string nameSpace, bool shareResources, string parentDirectory)
    {
        var defaultHomeDir = Path.Combine(FileLocations.ReadOnlySettingsPath, "default-home");
        var files = System.IO.Directory.EnumerateFiles(defaultHomeDir, "*");
        string destinationDirectory = Path.Combine(parentDirectory, projectName);
        destinationDirectory = Path.GetFullPath(destinationDirectory);
        System.IO.Directory.CreateDirectory(destinationDirectory);

        var dependenciesDirectory = Path.Combine(destinationDirectory, ProjectXml.DependenciesFolder);
        System.IO.Directory.CreateDirectory(dependenciesDirectory);

        var resourcesDirectory = Path.Combine(destinationDirectory, FileLocations.ResourcesSubfolder);
        System.IO.Directory.CreateDirectory(resourcesDirectory);

        string placeholderDependencyPath = Path.Combine(dependenciesDirectory, "PlaceNativeDllDependenciesHere.txt");
        File.Create(placeholderDependencyPath).Dispose();

        // todo - use source generation and direct type references instead of this copy and replace strategy
        const string guidPlaceholder = "{{GUID}}";
        const string nameSpacePlaceholder = "{{NAMESPACE}}";
        const string usernamePlaceholder = "{{USER}}";
        const string shareResourcesPlaceholder = "{{SHARE_RESOURCES}}";
        const string projectNamePlaceholder = "{{PROJ}}";

        var shouldShareResources = shareResources ? "true" : "false";
        var username = nameSpace.Split('.').First();

        var homeGuid = Guid.NewGuid();
        var homeGuidString = homeGuid.ToString();

        var projRoot = ProjectXml.CreateNewProjectRootElement(nameSpace, homeGuid);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file)
                           .Replace(projectNamePlaceholder, projectName)
                           .Replace(guidPlaceholder, homeGuidString)
                           .Replace(nameSpacePlaceholder, nameSpace)
                           .Replace(usernamePlaceholder, username)
                           .Replace(shareResourcesPlaceholder, shouldShareResources);

            var destinationFilePath = Path.Combine(destinationDirectory, Path.GetFileName(file))
                                          .Replace(projectNamePlaceholder, projectName)
                                          .Replace(guidPlaceholder, homeGuidString);

            File.WriteAllText(destinationFilePath, text);
        }

        projRoot.FullPath = Path.Combine(destinationDirectory, $"{projectName}.csproj");
        projRoot.Save(Encoding.UTF8);
        return new CsProjectFile(projRoot);
    }

    internal const Compiler.BuildMode EditorBuildMode = Compiler.BuildMode.Debug;
    internal const Compiler.BuildMode PlayerBuildMode = Compiler.BuildMode.Release;

    private readonly string _releaseRootDirectory;
    private readonly string _debugRootDirectory;
    private readonly ProjectRootElement _projectRootElement;
    private readonly FileInfo _fileInfo;


    public void IncrementBuildNumber(int amount)
    {
        if (amount == 0)
            return;

        ModifyBuildVersion(0, 0, amount);
    }
}