#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;

namespace T3.Editor.UiModel.ProjectHandling;

internal sealed class OpenedProject
{
    public readonly EditorSymbolPackage Package;
    public readonly Structure Structure;

    /// <summary>Empty for a project rooted at its package's home symbol.</summary>
    private readonly Guid _explicitHomeSymbolId;

    private Guid RootSymbolId => _explicitHomeSymbolId != Guid.Empty ? _explicitHomeSymbolId : Package.HomeSymbolId;

    public static readonly Dictionary<IResourcePackage, OpenedProject> OpenedProjects = new();

    /// <summary>
    /// Projects opened on one of their symbols (examples, skill levels, missing-operator lookups). Kept apart
    /// from <see cref="OpenedProjects"/>, which holds one project per package.
    /// </summary>
    private static readonly List<OpenedProject> _explicitHomeProjects = [];

    /// <summary>
    /// Most recently shown first. A project keeps its instances, and with them its GPU resources, until it is
    /// unloaded, so without a limit every project visited in a session would stay in VRAM.
    /// </summary>
    private static readonly List<OpenedProject> _recentlyShown = [];

    public static bool TryCreate(EditorSymbolPackage project,
                                 [NotNullWhen(true)] out OpenedProject? openedProject,
                                 [NotNullWhen(false)] out string? failureLog)
    {
        if (OpenedProjects.TryGetValue(project, out openedProject))
        {
            failureLog = null;
            return true;
        }

        if (!project.HasHomeSymbol(out failureLog))
        {
            failureLog ??= "project has no home?";
            openedProject = null;
            return false;
        }

        openedProject
            = new OpenedProject(project,
                                () =>
                                {
                                    var symbol = project.Symbols[project.HomeSymbolId];
                                    if (symbol.TryGetParentlessInstance(out var instance))
                                        return instance.SymbolChild;

                                    Log.Error("Root instance could not be created?");
                                    return null!;
                                }
                               );

        OpenedProjects[openedProject.Package] = openedProject;
        return true;
    }

    public static bool TryCreateWithExplicitHome(EditorSymbolPackage project,
                                                 Guid implicitHomeOpId,
                                                 [NotNullWhen(true)] out OpenedProject? openedProject,
                                                 [NotNullWhen(false)] out string? failureLog)
    {
        failureLog = null;
        project.OverrideHomeGuid = implicitHomeOpId;

        // Reused: a second project for the same home would share its root instance, and unloading either one
        // would destroy the instances the other still shows.
        foreach (var existing in _explicitHomeProjects)
        {
            if (existing.Package == project && existing._explicitHomeSymbolId == implicitHomeOpId)
            {
                openedProject = existing;
                return true;
            }
        }

        openedProject
            = new OpenedProject(project,
                                () =>
                                {
                                    var homeSymbol = project.Symbols[implicitHomeOpId];
                                    if (homeSymbol.TryGetParentlessInstance(out var instance))
                                        return instance.SymbolChild;

                                    Log.Error("Root instance could not be created?");
                                    return null!;
                                },
                                implicitHomeOpId
                               );

        _explicitHomeProjects.Add(openedProject);
        return true;
    }

    private OpenedProject(EditorSymbolPackage project, Func<Symbol.Child> rootAction, Guid explicitHomeSymbolId = default)
    {
        Package = project;
        Structure = new Structure(rootAction);
        _explicitHomeSymbolId = explicitHomeSymbolId;
    }

    /// <summary>
    /// Called when a graph window switches to a project. Unloads the projects shown longest ago beyond
    /// <see cref="UserSettings.ConfigData.KeepLoadedProjectCount"/>, unless a graph window still shows them.
    /// </summary>
    internal static void MarkShownAndUnloadStale(OpenedProject project)
    {
        _recentlyShown.Remove(project);
        _recentlyShown.Insert(0, project);

        var keepCount = Math.Max(1, UserSettings.Config.KeepLoadedProjectCount);
        for (var index = _recentlyShown.Count - 1; index >= keepCount; index--)
        {
            var stale = _recentlyShown[index];
            if (stale.IsShownInGraphWindow())
                continue;

            var rootName = stale.Package.Symbols.TryGetValue(stale.RootSymbolId, out var rootSymbol) ? rootSymbol.Name : "?";
            Log.Debug($"Unloading {rootName} in {stale.Package.DisplayName} to free its resources; it was not shown recently.");
            stale.Unload();
        }
    }

    /// <summary>
    /// Unloads a project by closing all views that reference it and disposing resources.
    /// </summary>
    /// <returns>True if the project was unloaded successfully, false if it wasn't loaded.</returns>
    public static bool TryUnload(EditorSymbolPackage package)
    {
        if (!OpenedProjects.TryGetValue(package, out var openedProject))
        {
            return false;
        }

        // Unloading a package from the hub means all of it, including symbols opened on their own.
        for (var index = _explicitHomeProjects.Count - 1; index >= 0; index--)
        {
            if (index < _explicitHomeProjects.Count && _explicitHomeProjects[index].Package == package)
                _explicitHomeProjects[index].Unload();
        }

        openedProject.Unload();
        return true;
    }

    private void Unload()
    {
        // Close all ProjectViews that reference this opened project
        var projectViewsToClose = new List<ProjectView>();
        foreach (var graphWindow in GraphWindow.GraphWindowInstances)
        {
            if (graphWindow.ProjectView?.OpenedProject == this)
            {
                projectViewsToClose.Add(graphWindow.ProjectView);
            }
        }

        foreach (var projectView in projectViewsToClose)
        {
            projectView.Close();
        }

        if (OpenedProjects.TryGetValue(Package, out var registered) && registered == this)
            OpenedProjects.Remove(Package);

        _explicitHomeProjects.Remove(this);
        _recentlyShown.Remove(this);

        // Dispose the root instance and all children (releases GPU resources etc.), unless another loaded
        // project is rooted at the same symbol and so shows the very same instances.
        if (!IsRootSharedWithLoadedProject())
        {
            var rootInstance = Structure.GetRootInstance();
            rootInstance?.SymbolChild.DestroyAllInstances();
        }

        // Views and pins can outlive this; their lookups must not recreate what was just destroyed.
        Structure.MarkUnloaded();

        EditorSymbolPackage.NotifySymbolStructureChange();
    }

    private bool IsShownInGraphWindow()
    {
        foreach (var graphWindow in GraphWindow.GraphWindowInstances)
        {
            if (graphWindow.ProjectView?.OpenedProject == this)
                return true;
        }

        return false;
    }

    private bool IsRootSharedWithLoadedProject()
    {
        var rootSymbolId = RootSymbolId;
        foreach (var other in OpenedProjects.Values)
        {
            if (other != this && other.Package == Package && other.RootSymbolId == rootSymbolId)
                return true;
        }

        foreach (var other in _explicitHomeProjects)
        {
            if (other != this && other.Package == Package && other.RootSymbolId == rootSymbolId)
                return true;
        }

        return false;
    }
}