#nullable enable
using System.Diagnostics.CodeAnalysis;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Editor.Gui.Window;

namespace T3.Editor.UiModel.ProjectHandling;

internal sealed class OpenedProject
{
    public readonly EditorSymbolPackage Package;
    public readonly Structure Structure;

    public static readonly Dictionary<IResourcePackage, OpenedProject> OpenedProjects = new();

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
        
        // if (OpenedProjects.TryGetValue(project, out openedProject))
        // {
        //     failureLog = null;
        //     return true;
        // }

        openedProject
            = new OpenedProject(project,
                                () =>
                                {
                                    var homeSymbol = project.Symbols[implicitHomeOpId];
                                    if (homeSymbol.TryGetParentlessInstance(out var instance))
                                        return instance.SymbolChild;

                                    Log.Error("Root instance could not be created?");
                                    return null!;
                                }
                               );

        //OpenedProjects[openedProject.Package] = openedProject;
        return true;
    }

    private OpenedProject(EditorSymbolPackage project, Func<Symbol.Child> rootAction)
    {
        Package = project;
        Structure = new Structure(rootAction);
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
        
        // Close all ProjectViews that reference this opened project
        var projectViewsToClose = new List<ProjectView>();
        foreach (var graphWindow in GraphWindow.GraphWindowInstances)
        {
            if (graphWindow.ProjectView?.OpenedProject == openedProject)
            {
                projectViewsToClose.Add(graphWindow.ProjectView);
            }
        }

        foreach (var projectView in projectViewsToClose)
        {
            projectView.Close();
        }

        // Dispose the root instance and all children (releases GPU resources etc.)
        var rootInstance = openedProject.Structure.GetRootInstance();
        rootInstance?.SymbolChild.DestroyAllInstances();

        // Remove from opened projects
        OpenedProjects.Remove(package);

        EditorSymbolPackage.NotifySymbolStructureChange();
        return true;
    }
}